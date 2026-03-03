using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Threading.Channels;

#region Agent

public class SpaceMoltAgent
{
    private const string BaseSystemPrompt =
        "You are an autonomous agent playing the online game SpaceMolt. " +
        "Your objective is to pursue the active objective. " +
        "Make rational, goal-directed decisions based on the current game state. ";
    private static bool PlanningEnabled => false;
    private const string DefaultScriptGenerationExamples =
        "Go, sell, then mine ->\n" +
        "go system_a;\n" +
        "dock {\n" +
        "  sell cargo;\n" +
        "}\n" +
        "mine asteroid_belt;\n\n" +
        "Go to Sol ->\n" +
        "go sol;\n\n" +
        "Sell your cargo ->\n" +
        "dock {\n" +
        "  sell cargo;\n" +
        "}\n\n" +
        "Go to system, mine, and sell at other system ->\n" +
        "go system_a;\n" +
        "mine asteroid_belt;\n" +
        "go system_b;\n" +
        "dock {\n" +
        "  sell cargo;\n" +
        "}";

    private readonly ILLMClient _plannerLlm;
    private readonly PromptScriptRag? _scriptExampleRag;
    private readonly bool _hierarchicalPlanningEnabled;
    private ControlMode _controlMode = ScriptMode.Instance;

    private string? _plan;
    private string? _objective;
    private string? _script;
    private string? _lastScriptGenerationPrompt;
    private string? _lastGeneratedScript;
    private int? _currentScriptLine;
    private Queue<CommandResult> _scriptQueue = new();
    private bool _isHalted;
    private readonly List<ScriptGenerationExample> _scriptGenerationExamples = new();

    public string? CurrentObjective
        => string.IsNullOrWhiteSpace(_objective)
            ? null
            : _objective;
    public bool IsHalted => _isHalted;
    public ControlModeKind CurrentControlModeKind => _controlMode.Kind;

    public string CurrentControlModeName => _controlMode.Name;
    public string? LastScriptGenerationPrompt => _lastScriptGenerationPrompt;
    public int? CurrentScriptLine => _currentScriptLine;

    public string? CurrentControlInput
        => string.IsNullOrWhiteSpace(_script) ? null : _script;

    private ChannelWriter<string>? _statusWriter;

    public void SetStatusWriter(ChannelWriter<string> writer)
    {
        _statusWriter = writer;
    }

    private void SetStatus(string status)
    {
        _statusWriter?.TryWrite(status);
    }

    public void SetObjective(string objective, bool clearCurrentPlan = true)
    {
        _objective = ParseObjective(objective);
        _isHalted = false;

        if (clearCurrentPlan)
            _plan = string.Empty;

        SetStatus("Objective set");
    }

    public void SetScript(
        string script,
        GameState? state = null,
        bool preserveAssociatedPrompt = false)
    {
        _script = script ?? string.Empty;
        _currentScriptLine = null;
        var steps = state == null
            ? DslInterpreter.Translate(_script)
            : DslInterpreter.Translate(_script, state);

        if (!preserveAssociatedPrompt &&
            !string.Equals(_script, _lastGeneratedScript, StringComparison.Ordinal))
        {
            _lastScriptGenerationPrompt = null;
            _lastGeneratedScript = null;
        }
        else if (preserveAssociatedPrompt && !string.IsNullOrWhiteSpace(_lastScriptGenerationPrompt))
        {
            // Keep prompt association in sync with the edited script.
            _lastGeneratedScript = _script;
        }

        _scriptQueue = new Queue<CommandResult>(steps);
        _isHalted = false;
        _plan = string.Empty;

        SetStatus(_scriptQueue.Count == 0
            ? "Script loaded (empty)"
            : $"Script loaded ({_scriptQueue.Count} steps)");
    }

    public async Task<string> GenerateScriptFromUserInputAsync(
        string userInput,
        GameState state,
        int maxAttempts = 3)
    {
        var attempts = Math.Max(1, maxAttempts);
        var generationInput = userInput.Trim();
        var examplesBlock = await BuildScriptGenerationExamplesBlockAsync(generationInput);
        string? previousScript = null;
        string? previousError = null;

        for (int attempt = 1; attempt <= attempts; attempt++)
        {
            var prompt = AgentPrompts.BuildScriptFromUserInputPrompt(
                baseSystemPrompt: BaseSystemPrompt,
                userInput: generationInput,
                examplesBlock: examplesBlock,
                attemptNumber: attempt,
                previousScript: previousScript,
                previousError: previousError);

            await LogPlannerPromptAsync($"script_generation_attempt_{attempt}", prompt);

            var result = await _plannerLlm.CompleteAsync(
                prompt,
                maxTokens: 320,
                temperature: 0.2f,
                topP: 0.9f);

            var script = ExtractScript(result);

            try
            {
                _ = DslInterpreter.Translate(script, state);
                // Persist only the raw user request for prompt->script examples.
                _lastScriptGenerationPrompt = generationInput;
                _lastGeneratedScript = script;
                return script;
            }
            catch (FormatException ex)
            {
                previousScript = script;
                previousError = ex.Message;
                SetStatus($"Script generation retry {attempt}/{attempts}");
            }
        }

        throw new FormatException(
            "Failed to generate a valid script after retries. Last error: " +
            (previousError ?? "Unknown script error."));
    }

    public void SetControlMode(ControlMode mode, bool clearCurrentPlan = true)
    {
        _controlMode = mode ?? ScriptMode.Instance;
        _isHalted = false;

        if (clearCurrentPlan)
            _plan = string.Empty;

        SetStatus($"Mode: {_controlMode.Name}");
    }

    public bool InterruptActiveCommand(string reason = "Interrupted")
    {
        if (_activeCommand == null)
            return false;

        _activeCommand = null;
        _activeCommandResult = null;
        SetStatus(reason);
        return true;
    }

    public void Halt(string reason = "Halted")
    {
        _isHalted = true;
        _currentScriptLine = null;
        SetStatus(reason);
    }

    public void ResumeFromHalt(bool clearCurrentPlan = true, string reason = "Resumed")
    {
        _isHalted = false;
        if (clearCurrentPlan)
            _plan = string.Empty;
        SetStatus(reason);
    }

    private readonly List<ICommand> _commands;
    private readonly Dictionary<string, ICommand> _commandMap;

    private Queue<ActionMemory> _memory = new();
    private const int MaxMemory = 12;

    private IMultiTurnCommand? _activeCommand;
    private CommandResult? _activeCommandResult;

    public bool HasActiveCommand => _activeCommand != null;

    private record ActionMemory(
        string Action,
        string? Arg1,
        int? Quantity,
        string? ResultMessage
    );

    public SpaceMoltAgent(
        ILLMClient llm,
        ILLMClient? plannerLlm = null,
        bool hierarchicalPlanningEnabled = true,
        PromptScriptRag? scriptExampleRag = null)
    {
        _plannerLlm = plannerLlm ?? llm;
        _scriptExampleRag = scriptExampleRag;
        _hierarchicalPlanningEnabled = hierarchicalPlanningEnabled;

        _commands = SpaceContextMode.Instance.GetCommands()
            .Concat(TradeContextMode.Instance.GetCommands())
            .Concat(HangarContextMode.Instance.GetCommands())
            .Concat(ShipyardContextMode.Instance.GetCommands())
            .Concat(ShipCatalogContextMode.Instance.GetCommands())
            .GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

        _commandMap = _commands.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
        LoadScriptGenerationExamples();
        SyncRagExamples();
    }

    public async Task<(bool Added, string Message)> AddCurrentScriptAsExampleAsync()
    {
        if (string.IsNullOrWhiteSpace(_script))
            return (false, "No script loaded to save as an example.");

        if (string.IsNullOrWhiteSpace(_lastScriptGenerationPrompt))
            return (false, "No tracked generation prompt for this script.");

        var candidateScript = _script.Trim();
        var candidatePrompt = _lastScriptGenerationPrompt.Trim();
        bool alreadyExists = _scriptGenerationExamples.Any(e =>
            string.Equals(e.Script, candidateScript, StringComparison.Ordinal) &&
            string.Equals(e.Prompt, candidatePrompt, StringComparison.Ordinal));

        if (alreadyExists)
            return (false, "That script example is already saved.");

        _scriptGenerationExamples.Add(new ScriptGenerationExample
        {
            Prompt = candidatePrompt,
            Script = candidateScript,
            CreatedUtc = DateTime.UtcNow
        });
        _scriptExampleRag?.AddExample(new PromptScriptExample(candidatePrompt, candidateScript));

        await SaveScriptGenerationExamplesAsync();
        return (true, $"Saved script example #{_scriptGenerationExamples.Count}.");
    }

    public IReadOnlyList<string> GetMemoryList()
    {
        return _memory.Select(m =>
        {
            var action = FormatAction(m);
            var msg = m.ResultMessage;

            return string.IsNullOrWhiteSpace(msg)
                ? action
                : $"{action} → {msg}";
        }).ToList();
    }

    public IReadOnlyList<string> GetAvailableActions(GameState state)
    {
        return GetCandidateCommands(state, onlyAvailable: true)
            .Where(c => c.IsAvailable(state))
            .Select(c => c.BuildHelp(state))
            .ToList();
    }

    public (string SpaceStateMarkdown, string? TradeStateMarkdown) BuildUiState(GameState state)
    {
        var spaceState = SpaceContextMode.Instance.ToDisplayText(state);
        var tradeState = state.Docked
            ? TradeContextMode.Instance.ToDisplayText(state)
            : null;

        return (spaceState, tradeState);
    }

    public async Task ExecuteAsync(
        SpaceMoltHttpClient client,
        CommandResult result,
        GameState state)
    {
        string? message = null;
        bool shouldAddMemory = false;

        if (_activeCommand != null)
        {
            string activeCommandText = _activeCommandResult == null
                ? "multi-step command"
                : FormatCommand(_activeCommandResult);

            await LogCommandExecutionAsync(
                commandText: activeCommandText,
                state: state,
                phase: "continue-start");

            SetStatus($"Executing: continuing {activeCommandText}");

            var continuation = await _activeCommand.ContinueAsync(client, state);

            bool finished = continuation.Item1;
            var response = continuation.Item2;

            if (finished)
            {
                message = response?.ResultMessage;
                shouldAddMemory = _activeCommandResult != null;

                if (shouldAddMemory && _activeCommandResult != null)
                    AddMemory(_activeCommandResult, message);

                _activeCommand = null;
                _activeCommandResult = null;
                SetStatus("Waiting");
                if (PlanningEnabled)
                    await RefreshPlanAfterExecutionAsync(client, state);
            }

            await LogCommandExecutionAsync(
                commandText: activeCommandText,
                state: state,
                phase: "continue-end",
                details: message ?? (finished ? "completed" : "in-progress"));

            return;
        }

        await LogCommandExecutionAsync(
            commandText: FormatCommand(result),
            state: state,
            phase: "start");

        if (_commandMap.TryGetValue(result.Action, out var command))
        {
            if (command is IMultiTurnCommand multiTurnCommand)
            {
                SetStatus($"Executing: start {FormatCommand(result)}");
                _activeCommand = multiTurnCommand;
                _activeCommandResult = result;

                await multiTurnCommand.StartAsync(client, result, state);
            }
            else if (command is ISingleTurnCommand singleTurnCommand)
            {
                SetStatus($"Executing: run {FormatCommand(result)}");
                var response = await singleTurnCommand.ExecuteAsync(client, result, state);
                message = response?.ResultMessage;
                shouldAddMemory = true;

                if (string.Equals(result.Action, "halt", StringComparison.Ordinal))
                {
                    Halt("Halted: waiting for user input");
                }
                else
                {
                    SetStatus("Waiting");
                    if (PlanningEnabled)
                        await RefreshPlanAfterExecutionAsync(client, state);
                }
            }
        }

        if (shouldAddMemory)
            AddMemory(result, message);

        await LogCommandExecutionAsync(
            commandText: FormatCommand(result),
            state: state,
            phase: "end",
            details: message ?? "(no result message)");
    }

    private async Task LogCommandExecutionAsync(
        string commandText,
        GameState state,
        string phase,
        string? details = null)
    {
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine($"[{DateTime.UtcNow:O}] === COMMAND_EXECUTION:{phase} ===");
            sb.AppendLine($"ControlMode: {_controlMode.Name}");
            sb.AppendLine($"Command: {commandText}");

            if (!string.IsNullOrWhiteSpace(details))
                sb.AppendLine($"Details: {details}");

            sb.AppendLine("GameState:");
            sb.AppendLine(state.ToLLMMarkdown());
            sb.AppendLine();

            await File.AppendAllTextAsync(AppPaths.CommandExecutionLogFile, sb.ToString());
        }
        catch
        {
            // Never fail command execution due to logging errors.
        }
    }

    private void AddMemory(CommandResult result, string? message)
    {
        if (_memory.Count >= MaxMemory)
            _memory.Dequeue();

        _memory.Enqueue(new ActionMemory(
            result.Action,
            result.Arg1,
            result.Quantity,
            message
        ));
    }

    public async Task<CommandResult?> DecideAsync(GameState state)
    {
        if (_isHalted)
        {
            SetStatus("Halted: waiting for user input");
            return null;
        }

        if (HasActiveCommand)
            return _activeCommandResult;

        return await DecideScriptStepAsync(state);
    }

    private async Task<CommandResult?> DecidePromptStepAsync(GameState state)
    {

        if (_hierarchicalPlanningEnabled && string.IsNullOrWhiteSpace(_objective))
        {
            _objective = await GenerateObjectiveAsync(state);
        }

        if (!PlanningEnabled)
            return await ExecutePlanStepAsync(state);

        if (IsPlanEmpty())
        {
            SetStatus("Planning");
            _plan = await GeneratePlanAsync(state);

            if (IsPlanEmpty())
            {
                return null;
            }
        }

        return await ExecutePlanStepAsync(state);
    }

    private Task<CommandResult?> DecideScriptStepAsync(GameState state)
    {
        while (_scriptQueue.Count > 0)
        {
            var next = _scriptQueue.Dequeue();

            if (string.IsNullOrWhiteSpace(next.Action) || !_commandMap.ContainsKey(next.Action))
            {
                AddMemory(next, "invalid script command");
                continue;
            }

            _currentScriptLine = next.SourceLine;
            SetStatus($"Executing: script {FormatCommand(next)}");
            return Task.FromResult<CommandResult?>(next);
        }

        Halt("Script complete: waiting for input");
        return Task.FromResult<CommandResult?>(null);
    }

    public void RequeueScriptStep(CommandResult step)
    {
        if (step == null || string.IsNullOrWhiteSpace(step.Action))
            return;

        var copy = new CommandResult
        {
            Action = step.Action,
            Arg1 = step.Arg1,
            Quantity = step.Quantity,
            SourceLine = step.SourceLine
        };

        _scriptQueue = new Queue<CommandResult>(
            new[] { copy }.Concat(_scriptQueue));
    }

    private async Task RefreshPlanAfterExecutionAsync(
        SpaceMoltHttpClient client,
        GameState fallbackState)
    {
        SetStatus("Planning");

        GameState latestState = fallbackState;

        try
        {
            latestState = await client.GetGameStateAsync();
        }
        catch
        {
            // Keep using fallback state if refresh fails.
        }

        _plan = await UpdatePlanAsync(latestState);

        if (IsPlanEmpty())
        {
            _plan = await GeneratePlanAsync(latestState);
        }
    }

    private async Task<string> GeneratePlanAsync(GameState state)
    {
        SetStatus("Planning");

        return await CompletePlanAsync(
            state,
            objective:
                "Create or refresh the rationale.\n" +
                "Rationale is freeform and can include any useful ideas.\n" +
                "Keep it concise and practical.",
            includeCurrentPlan: true,
            memoryRecent: 8
        );
    }

    private async Task<CommandResult?> ExecutePlanStepAsync(GameState state)
    {
        SetStatus("Executing: selecting next move");

        if (!GetCandidateCommands(state, onlyAvailable: true).Any(c => c.IsAvailable(state)))
            return null;

        SetStatus("Executing: planner suggestion");
        string selectedCommand = await SuggestNextMoveAsync(state);
        SetStatus($"Executing: selected {selectedCommand}");

        var parsed = Parse(selectedCommand);
        if (!IsValidCommandForCurrentState(parsed, state))
        {
            AddInvalidCommandMemory(parsed);
            SetStatus("Waiting");
            return null;
        }

        return parsed;
    }

    private async Task<string> SuggestNextMoveAsync(GameState state)
    {
        string prompt = AgentPrompts.BuildSuggestNextMovePrompt(
            baseSystemPrompt: BaseSystemPrompt,
            stateMarkdown: state.ToLLMMarkdown(),
            availableActionsBlock: BuildAvailableActionsBlock(state, onlyAvailable: true),
            allActionsBlock: BuildAvailableActionsBlock(state, onlyAvailable: false),
            currentObjectiveBlock: BuildCurrentObjectiveContextBlock(),
            previousActionsBlock: BuildMemoryBlock(8)
        );

        await LogPlannerPromptAsync("suggest_next_move", prompt);

        string result = await _plannerLlm.CompleteAsync(
            prompt,
            maxTokens: 24,
            temperature: 0.2f,
            topP: 0.9f
        );

        return string.IsNullOrWhiteSpace(result)
            ? "No suggestion."
            : result.Trim();
    }

    private async Task<string> UpdatePlanAsync(GameState state)
    {
        string updated = await CompletePlanAsync(
            state,
            objective:
                "Regenerate the rationale after the most recent action.\n" +
                "Rationale is freeform and can include any useful ideas, priorities, or hypotheses.\n" +
                "Keep it concise and practical.\n" +
                "Output rationale lines only.",
            includeCurrentPlan: true,
            memoryRecent: 8,
            temperature: 0.2f,
            topP: 0.8f
        );

        return string.IsNullOrWhiteSpace(updated)
            ? (_plan ?? string.Empty)
            : updated;
    }

    private async Task<string> CompletePlanAsync(
        GameState state,
        string objective,
        string? userRequest = null,
        bool includeCurrentPlan = false,
        int memoryRecent = 8,
        float temperature = 0.3f,
        float topP = 0.85f)
    {
        string prompt = BuildPlanPrompt(state, objective, userRequest, includeCurrentPlan, memoryRecent);
        await LogPlannerPromptAsync("plan_rationale", prompt);

        string result = await _plannerLlm.CompleteAsync(
            prompt,
            maxTokens: 160,
            temperature: temperature,
            topP: topP
        );

        return (result ?? string.Empty).Trim();
    }

    private string BuildPlanPrompt(
        GameState state,
        string objective,
        string? userRequest,
        bool includeCurrentPlan,
        int memoryRecent)
    {
        string requestBlock = string.IsNullOrWhiteSpace(userRequest)
            ? ""
            : "User request:\n" + userRequest.Trim() + "\n\n";

        string currentRationaleBlock = includeCurrentPlan
            ? BuildCurrentRationaleBlock()
            : "";

        return AgentPrompts.BuildPlanPrompt(
            baseSystemPrompt: BaseSystemPrompt,
            requestBlock: requestBlock,
            stateMarkdown: state.ToLLMMarkdown(),
            currentObjectiveBlock: BuildCurrentObjectiveBlock(),
            previousActionsBlock: BuildMemoryBlock(memoryRecent),
            currentRationaleBlock: currentRationaleBlock,
            objective: objective
        );
    }

    public string BuildMemoryBlock(int? maxRecent = null)
    {
        if (_memory.Count == 0)
            return "";

        var items = maxRecent.HasValue
            ? _memory.TakeLast(maxRecent.Value)
            : _memory;

        var lines = items.Select(m =>
        {
            var action = FormatAction(m);

            var msg = m.ResultMessage;

            return string.IsNullOrWhiteSpace(msg)
                ? $"- {action}"
                : $"- {action} → {msg}";
        });

        return "Previous actions:\n" +
               string.Join("\n", lines) +
               "\n\n";
    }

    private string BuildAvailableActionsBlock(GameState state, bool onlyAvailable)
    {
        return state.Mode.BuildActionsBlock(state, onlyAvailable);
    }

    private IEnumerable<ICommand> GetCandidateCommands(GameState state, bool onlyAvailable)
    {
        var activeCommands = state.Mode.GetCommands();
        var source = onlyAvailable
            ? activeCommands.Where(c => c.IsAvailable(state))
            : activeCommands.AsEnumerable();

        return source;
    }

    private string BuildCurrentRationaleBlock()
    {
        if (string.IsNullOrWhiteSpace(_plan))
            return "Current rationale:\n- none\n\n";

        return "Current rationale:\n" +
               _plan.Trim() +
               "\n\n";
    }

    private string BuildCurrentObjectiveContextBlock()
    {
        if (string.IsNullOrWhiteSpace(_objective))
            return "";

        return "Current objective:\n- " + _objective.Trim() + "\n\n";
    }

    private string BuildCurrentObjectiveBlock()
    {
        if (!_hierarchicalPlanningEnabled)
            return "";

        if (string.IsNullOrWhiteSpace(_objective))
            return "Current objective:\n- none\n\n";

        return "Current objective:\n- " + _objective.Trim() + "\n\n";
    }

    private async Task<string> GenerateObjectiveAsync(GameState state)
    {
        string prompt = AgentPrompts.BuildObjectivePrompt(
            baseSystemPrompt: BaseSystemPrompt,
            stateMarkdown: state.ToLLMMarkdown(),
            allActionsBlock: BuildAvailableActionsBlock(state, onlyAvailable: false),
            previousActionsBlock: BuildMemoryBlock(8)
        );

        await LogPlannerPromptAsync("generate_objective", prompt);

        string result = await _plannerLlm.CompleteAsync(
            prompt,
            maxTokens: 48,
            temperature: 0.2f,
            topP: 0.9f
        );

        return ParseObjective(result);
    }

    private async Task<string> GenerateObjectiveFromUserInputAsync(string input, GameState state)
    {
        string prompt = AgentPrompts.BuildObjectiveFromUserInputPrompt(
            baseSystemPrompt: BaseSystemPrompt,
            userInput: input.Trim(),
            stateMarkdown: state.ToLLMMarkdown()
        );

        await LogPlannerPromptAsync("generate_objective_from_user_input", prompt);

        string result = await _plannerLlm.CompleteAsync(
            prompt,
            maxTokens: 48,
            temperature: 0.2f,
            topP: 0.9f
        );

        return ParseObjective(result);
    }

    private static string ParseObjective(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "";

        var lines = raw
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToArray();

        if (lines.Length == 0)
            return "";

        return string.Join('\n', lines);
    }

    private static string ExtractScript(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;

        var text = raw.Trim();
        if (!text.StartsWith("```", StringComparison.Ordinal))
            return text;

        var firstNewline = text.IndexOf('\n');
        if (firstNewline < 0)
            return text.Trim('`').Trim();

        var lastFence = text.LastIndexOf("```", StringComparison.Ordinal);
        if (lastFence <= firstNewline)
            return text[(firstNewline + 1)..].Trim();

        return text.Substring(firstNewline + 1, lastFence - firstNewline - 1).Trim();
    }

    private static async Task LogPlannerPromptAsync(string stage, string prompt)
    {
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine($"[{DateTime.UtcNow:O}] === {stage} ===");
            sb.AppendLine(prompt ?? string.Empty);
            sb.AppendLine();

            await File.AppendAllTextAsync(AppPaths.PlannerPromptLogFile, sb.ToString());
        }
        catch
        {
            // Prompt logging should never block gameplay.
        }
    }

    private bool IsValidCommandForCurrentState(CommandResult result, GameState state)
    {
        if (string.IsNullOrWhiteSpace(result.Action))
            return false;

        if (!_commandMap.TryGetValue(result.Action, out var command))
            return false;

        var activeCommands = GetCandidateCommands(state, onlyAvailable: false)
            .Select(c => c.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (!activeCommands.Contains(result.Action))
            return false;

        return command.IsAvailable(state);
    }

    private void AddInvalidCommandMemory(CommandResult result)
    {
        var memoryResult = new CommandResult
        {
            Action = string.IsNullOrWhiteSpace(result.Action) ? "<empty>" : result.Action.Trim(),
            Arg1 = result.Arg1,
            Quantity = result.Quantity
        };

        AddMemory(memoryResult, "invalid command");
    }

    private bool IsPlanEmpty()
    {
        return string.IsNullOrWhiteSpace(_plan);
    }

    private CommandResult Parse(string output)
    {
        var parts = output
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);

        return new CommandResult
        {
            Action = parts.ElementAtOrDefault(0) ?? "",
            Arg1 = parts.ElementAtOrDefault(1),
            Quantity = int.TryParse(parts.ElementAtOrDefault(2), out int n)
                ? n
                : null
        };
    }

    private static string FormatAction(ActionMemory m)
    {
        if (!string.IsNullOrWhiteSpace(m.Arg1) && m.Quantity.HasValue)
            return $"{m.Action} {m.Arg1} {m.Quantity}";

        if (!string.IsNullOrWhiteSpace(m.Arg1))
            return $"{m.Action} {m.Arg1}";

        return m.Action;
    }

    private static string FormatCommand(CommandResult cmd)
    {
        if (!string.IsNullOrWhiteSpace(cmd.Arg1) && cmd.Quantity.HasValue)
            return $"{cmd.Action} {cmd.Arg1} {cmd.Quantity}";

        if (!string.IsNullOrWhiteSpace(cmd.Arg1))
            return $"{cmd.Action} {cmd.Arg1}";

        return cmd.Action;
    }

    private async Task<string> BuildScriptGenerationExamplesBlockAsync(string generationInput)
    {
        var sb = new StringBuilder();
        sb.Append(DefaultScriptGenerationExamples.TrimEnd());
        sb.Append("\n\n");

        if (_scriptGenerationExamples.Count == 0)
            return sb.ToString().TrimEnd();

        IReadOnlyList<PromptScriptMatch> matches = Array.Empty<PromptScriptMatch>();

        if (_scriptExampleRag != null && !string.IsNullOrWhiteSpace(generationInput))
        {
            try
            {
                matches = await _scriptExampleRag.FindTopMatchesAsync(generationInput, maxMatches: 5);
            }
            catch
            {
                // Retrieval failure should never block script generation.
            }
        }

        if (matches.Count == 0)
        {
            matches = _scriptGenerationExamples
                .TakeLast(5)
                .Select(e => new PromptScriptMatch(e.Prompt, e.Script, 0d))
                .ToList();
        }

        for (int i = 0; i < matches.Count; i++)
        {
            var example = matches[i];
            sb.Append(example.Prompt.Trim());
            sb.Append(" ->\n");
            sb.Append(NormalizeScriptExampleForPrompt(example.Script));
            sb.Append("\n\n");
        }

        return sb.ToString().TrimEnd();
    }

    private void LoadScriptGenerationExamples()
    {
        try
        {
            if (!File.Exists(AppPaths.ScriptGenerationExamplesFile))
                return;

            var raw = File.ReadAllText(AppPaths.ScriptGenerationExamplesFile);
            var loaded = JsonSerializer.Deserialize<List<ScriptGenerationExample>>(raw);
            if (loaded == null || loaded.Count == 0)
                return;

            _scriptGenerationExamples.Clear();
            _scriptGenerationExamples.AddRange(
                loaded.Where(e =>
                    !string.IsNullOrWhiteSpace(e.Prompt) &&
                    !string.IsNullOrWhiteSpace(e.Script)));
        }
        catch
        {
            // Loading examples should never block startup.
        }
    }

    private async Task SaveScriptGenerationExamplesAsync()
    {
        try
        {
            var json = JsonSerializer.Serialize(_scriptGenerationExamples, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            await File.WriteAllTextAsync(AppPaths.ScriptGenerationExamplesFile, json);
        }
        catch
        {
            // Persisting examples should never block gameplay.
        }
    }

    private void SyncRagExamples()
    {
        _scriptExampleRag?.ReplaceExamples(
            _scriptGenerationExamples.Select(e => new PromptScriptExample(e.Prompt, e.Script)));
    }

    private static string NormalizeScriptExampleForPrompt(string script)
    {
        if (string.IsNullOrWhiteSpace(script))
            return string.Empty;

        var normalizedScript = FlattenLegacyTradeBlocksText(script);

        DslAstProgram tree;
        try
        {
            tree = DslParser.ParseTree(normalizedScript);
        }
        catch
        {
            return normalizedScript.Trim();
        }

        var flattened = FlattenTradeBlocks(tree.Statements);
        return RenderDslNodes(flattened, indentLevel: 0).TrimEnd();
    }

    private static string FlattenLegacyTradeBlocksText(string script)
    {
        var current = script ?? string.Empty;

        // Flatten legacy "trade { ... }" wrappers so old examples still guide the model.
        while (true)
        {
            var next = Regex.Replace(
                current,
                @"trade\s*\{\s*(.*?)\s*\}",
                "$1",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);

            if (string.Equals(next, current, StringComparison.Ordinal))
                return next;

            current = next;
        }
    }

    private static IReadOnlyList<DslAstNode> FlattenTradeBlocks(IReadOnlyList<DslAstNode> nodes)
    {
        var result = new List<DslAstNode>();

        foreach (var node in nodes)
        {
            switch (node)
            {
                case DslCommandAstNode command:
                    result.Add(command);
                    break;
                case DslBlockAstNode block:
                {
                    var flattenedChildren = FlattenTradeBlocks(block.Body);
                    if (string.Equals(block.Name, "trade", StringComparison.OrdinalIgnoreCase))
                    {
                        foreach (var child in flattenedChildren)
                            result.Add(child);
                    }
                    else
                    {
                        result.Add(new DslBlockAstNode(block.Name, flattenedChildren, block.SourceLine));
                    }

                    break;
                }
            }
        }

        return result;
    }

    private static string RenderDslNodes(IReadOnlyList<DslAstNode> nodes, int indentLevel)
    {
        var sb = new StringBuilder();
        var indent = new string(' ', indentLevel * 2);

        foreach (var node in nodes)
        {
            switch (node)
            {
                case DslCommandAstNode command:
                    sb.Append(indent);
                    sb.Append(command.Name);
                    if (command.Args.Count > 0)
                    {
                        sb.Append(' ');
                        sb.Append(string.Join(" ", command.Args));
                    }

                    sb.AppendLine(";");
                    break;
                case DslBlockAstNode block:
                    sb.Append(indent);
                    sb.Append(block.Name);
                    sb.AppendLine(" {");
                    sb.Append(RenderDslNodes(block.Body, indentLevel + 1));
                    sb.Append(indent);
                    sb.AppendLine("}");
                    break;
            }
        }

        return sb.ToString();
    }

    private sealed class ScriptGenerationExample
    {
        public string Prompt { get; set; } = string.Empty;
        public string Script { get; set; } = string.Empty;
        public DateTime CreatedUtc { get; set; }
    }
}

#endregion

public class CommandResult
{
    public string Action { get; set; } = "";
    public string? Arg1 { get; set; }
    public int? Quantity { get; set; }
    public int? SourceLine { get; set; }
}
