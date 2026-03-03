using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Diagnostics;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

class Program
{
    private const int ScriptGenerationMaxAttempts = 3;

    static async Task Main(string[] args)
    {
        AppPaths.EnsureDirectories();

        ILLMClient commandLlm = new LlamaCppClient("http://localhost:8080");

        var openAiApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        var openAiModel = Environment.GetEnvironmentVariable("OPENAI_MODEL") ?? "gpt-4o-mini";

        if (string.IsNullOrWhiteSpace(openAiApiKey))
            throw new InvalidOperationException(
                "OPENAI_API_KEY is required. Aborting startup because remote planning model is mandatory.");

        ILLMClient planningLlm = new OpenAIClient(openAiApiKey, openAiModel);
        var openAiEmbeddingModel = Environment.GetEnvironmentVariable("OPENAI_EMBEDDING_MODEL")
            ?? "text-embedding-3-small";
        var scriptExampleRag = new PromptScriptRag(openAiApiKey, openAiEmbeddingModel);
        bool hierarchicalPlanningEnabled = true;

        var ui = new BotWindow();

        var statusChannel = Channel.CreateUnbounded<string>();
        ui.SetStatusReader(statusChannel.Reader);

        var inputChannel = Channel.CreateUnbounded<string>();
        ui.SetCommandWriter(inputChannel.Writer);

        var controlInputChannel = Channel.CreateUnbounded<string>();
        ui.SetControlInputWriter(controlInputChannel.Writer);

        var generateScriptChannel = Channel.CreateUnbounded<string>();
        ui.SetGenerateScriptWriter(generateScriptChannel.Writer);

        var saveExampleChannel = Channel.CreateUnbounded<bool>();
        ui.SetSaveExampleWriter(saveExampleChannel.Writer);

        var executeScriptChannel = Channel.CreateUnbounded<bool>();
        ui.SetExecuteScriptWriter(executeScriptChannel.Writer);

        var switchBotChannel = Channel.CreateUnbounded<string>();
        ui.SetSwitchBotWriter(switchBotChannel.Writer);

        var addBotChannel = Channel.CreateUnbounded<AddBotRequest>();
        ui.SetAddBotWriter(addBotChannel.Writer);

        var uiChannel = Channel.CreateUnbounded<UiSnapshot>();
        var cts = new CancellationTokenSource();

        var botSessions = new Dictionary<string, BotSession>(StringComparer.Ordinal);
        string? activeBotId = null;
        object botLock = new();
        var savedBots = LoadSavedBots();
        string lastLoggedBotTabSignature = "";

        void LogAuth(string message)
        {
            var line = $"[{DateTime.UtcNow:O}] {message}{Environment.NewLine}";
            try
            {
                File.AppendAllText(AppPaths.AuthFlowLogFile, line);
            }
            catch
            {
                // Never crash startup because logging failed.
            }
        }

        void LogBotTabsIfChanged(string context, IReadOnlyList<BotTab>? tabs = null, string? activeId = null)
        {
            var currentTabs = tabs ?? GetBotTabs();
            var currentActiveId = activeId ?? activeBotId;
            var labels = string.Join(",", currentTabs.Select(t => t.Label));
            var signature = $"{currentTabs.Count}|{currentActiveId}|{labels}";
            if (signature == lastLoggedBotTabSignature)
                return;

            lastLoggedBotTabSignature = signature;
            LogAuth($"{context} | tabs_changed | count={currentTabs.Count} | active={currentActiveId ?? "(null)"} | labels=[{labels}]");
        }

        BotSession? GetActiveBot()
        {
            lock (botLock)
            {
                if (activeBotId == null)
                    return null;

                return botSessions.TryGetValue(activeBotId, out var session)
                    ? session
                    : null;
            }
        }

        bool IsActiveBot(BotSession bot)
        {
            lock (botLock)
            {
                return activeBotId == bot.Id;
            }
        }

        IReadOnlyList<BotTab> GetBotTabs()
        {
            lock (botLock)
            {
                return botSessions.Values
                    .Select(b => new BotTab(b.Id, b.Label))
                    .ToList();
            }
        }

        void PublishNoBotSnapshot(string? message = null)
        {
            var tabs = GetBotTabs();
            LogBotTabsIfChanged("publish_no_bot_snapshot", tabs, activeBotId);
            uiChannel.Writer.TryWrite(new UiSnapshot(
                message ?? "No bot logged in. Use + to add one.",
                null,
                Array.Empty<string>(),
                null,
                null,
                ControlModeKind.ScriptMode,
                new List<string> { "(load a bot with +)" },
                null,
                tabs,
                activeBotId));
        }

        void PublishSnapshotForBot(BotSession bot, GameState state)
        {
            var tabs = GetBotTabs();
            var uiState = bot.Agent.BuildUiState(state);
            LogBotTabsIfChanged("publish_snapshot", tabs, activeBotId);
            uiChannel.Writer.TryWrite(new UiSnapshot(
                uiState.SpaceStateMarkdown,
                uiState.TradeStateMarkdown,
                bot.Agent.GetMemoryList(),
                bot.Agent.CurrentControlInput,
                bot.Agent.CurrentScriptLine,
                bot.Agent.CurrentControlModeKind,
                bot.Agent.GetAvailableActions(state),
                bot.Agent.LastScriptGenerationPrompt,
                tabs,
                activeBotId));
        }

        void PublishActiveSnapshot(string? noStateMessage = null)
        {
            var active = GetActiveBot();
            if (active == null)
            {
                PublishNoBotSnapshot();
                return;
            }

            if (active.LatestState != null)
            {
                PublishSnapshotForBot(active, active.LatestState);
                return;
            }

            PublishNoBotSnapshot(noStateMessage ?? $"Bot '{active.Label}' loaded; initial state unavailable.");
        }

        void PublishSnapshot(BotSession bot, GameState state)
        {
            bot.LatestState = state;
            if (IsActiveBot(bot))
                PublishSnapshotForBot(bot, state);
            else
                PublishActiveSnapshot();
        }

        async Task<(BotSession Session, string Password)> CreateBotSessionAsync(
            string username,
            string flowLabel,
            Func<SpaceMoltHttpClient, Task<string>> authenticateAsync)
        {
            var normalizedUsername = username.Trim();
            var label = normalizedUsername;
            var totalTimer = Stopwatch.StartNew();
            LogAuth($"{flowLabel} | {label} | start");

            var agent = new SpaceMoltAgent(
                commandLlm,
                planningLlm,
                hierarchicalPlanningEnabled,
                scriptExampleRag);
            agent.Halt("Awaiting script input");
            agent.SetStatusWriter(statusChannel.Writer);

            var client = new SpaceMoltHttpClient();
            client.DebugContext = label;

            try
            {
                var sessionTimer = Stopwatch.StartNew();
                await client.CreateSessionAsync();
                LogAuth($"{flowLabel} | {label} | session_created | {sessionTimer.ElapsedMilliseconds}ms");

                var authTimer = Stopwatch.StartNew();
                var password = await authenticateAsync(client);
                LogAuth($"{flowLabel} | {label} | authenticated | {authTimer.ElapsedMilliseconds}ms");

                try
                {
                    var mapTimer = Stopwatch.StartNew();
                    await client.GetMapSnapshotAsync(forceRefresh: false)
                        .WaitAsync(TimeSpan.FromSeconds(15));
                    LogAuth($"{flowLabel} | {label} | map_warmup_ok | {mapTimer.ElapsedMilliseconds}ms");
                }
                catch (Exception ex)
                {
                    statusChannel.Writer.TryWrite(
                        $"Map warm-up skipped for '{label}': {ex.Message}");
                    LogAuth($"{flowLabel} | {label} | map_warmup_skipped | {ex.GetType().Name}: {ex.Message}");
                }

                LogAuth($"{flowLabel} | {label} | session_ready | {totalTimer.ElapsedMilliseconds}ms");

                return (
                    new BotSession(
                        Guid.NewGuid().ToString("N"),
                        label,
                        agent,
                        client),
                    password);
            }
            catch (Exception ex)
            {
                LogAuth($"{flowLabel} | {label} | failed | {ex.GetType().Name}: {ex.Message}");
                client.Dispose();
                throw;
            }
        }

        void UpsertSavedBot(string username, string password)
        {
            var normalizedUsername = username.Trim();

            var existing = savedBots.FindIndex(b =>
                string.Equals(b.Username, normalizedUsername, StringComparison.OrdinalIgnoreCase));

            if (existing >= 0)
            {
                savedBots[existing] = new SavedBot(normalizedUsername, password);
            }
            else
            {
                savedBots.Add(new SavedBot(normalizedUsername, password));
            }

            SaveSavedBots(savedBots);
        }

        PublishNoBotSnapshot();

        if (savedBots.Count > 0)
        {
            statusChannel.Writer.TryWrite($"Loading {savedBots.Count} saved bot(s)...");
            LogAuth($"startup | begin_autoload | count={savedBots.Count}");
            foreach (var savedBot in savedBots.ToList())
            {
                try
                {
                    statusChannel.Writer.TryWrite($"Auto-login saved bot '{savedBot.Username}'...");
                    LogAuth($"startup | {savedBot.Username} | autologin_begin");
                    var (session, _) = await CreateBotSessionAsync(
                        savedBot.Username,
                        "startup/autologin",
                        async client =>
                        {
                            await client.LoginAsync(savedBot.Username.Trim(), savedBot.Password);
                            return savedBot.Password;
                        });

                    lock (botLock)
                    {
                        botSessions[session.Id] = session;
                        if (activeBotId == null)
                            activeBotId = session.Id;
                    }
                    LogBotTabsIfChanged("startup_autologin_added");
                }
                catch (Exception ex)
                {
                    statusChannel.Writer.TryWrite(
                        $"Failed to auto-login saved bot '{savedBot.Username}': {ex.Message}");
                    LogAuth($"startup | {savedBot.Username} | autologin_failed | {ex.GetType().Name}: {ex.Message}");
                }
            }
            LogAuth("startup | end_autoload");
        }

        Task RunBotLoopAsync(BotSession bot, CancellationToken token)
        {
            var runtime = new BotRuntime(
                bot.Label,
                bot.Agent,
                bot.Client,
                bot.CommandQueue.Reader,
                bot.ControlInputQueue.Reader,
                bot.GenerateScriptQueue.Reader,
                bot.SaveExampleQueue.Reader,
                () => ui.LoopEnabled,
                () => bot.LatestState,
                state => bot.LatestState = state,
                () => bot.LastHaltedSnapshotAt,
                value => bot.LastHaltedSnapshotAt = value,
                state => PublishSnapshot(bot, state),
                message => statusChannel.Writer.TryWrite(message),
                LogAuth,
                ParseCommand,
                ScriptGenerationMaxAttempts);

            return runtime.RunAsync(token);
        }

        void StartBotWorker(BotSession session)
        {
            if (session.WorkerTask != null)
                return;

            session.WorkerTask = Task.Run(() => RunBotLoopAsync(session, session.WorkerCts.Token), session.WorkerCts.Token);
            LogAuth($"bot_worker | {session.Label} | started");
        }

        lock (botLock)
        {
            foreach (var session in botSessions.Values)
                StartBotWorker(session);
        }

        PublishActiveSnapshot();

        var botTask = Task.Run(async () =>
        {
            try
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    while (addBotChannel.Reader.TryRead(out var request))
                    {
                        var username = request.Username.Trim();
                        if (string.IsNullOrWhiteSpace(username))
                        {
                            statusChannel.Writer.TryWrite("Username is required.");
                            continue;
                        }

                        var display = username;
                        bool isLogin = request.Mode == AddBotMode.Login;
                        statusChannel.Writer.TryWrite(isLogin
                            ? $"Logging in bot '{display}'..."
                            : $"Registering bot '{display}'...");
                        LogAuth(isLogin
                            ? $"manual | {display} | login_begin"
                            : $"manual | {display} | register_begin");

                        try
                        {
                            BotSession session;
                            string passwordToSave;

                            if (request.Mode == AddBotMode.Register)
                            {
                                var registrationCode = request.RegistrationCode?.Trim();
                                var empire = request.Empire?.Trim().ToLowerInvariant();

                                if (string.IsNullOrWhiteSpace(registrationCode) ||
                                    string.IsNullOrWhiteSpace(empire))
                                {
                                    statusChannel.Writer.TryWrite(
                                        "Registration code and empire are required for register mode.");
                                    continue;
                                }

                                (session, passwordToSave) = await CreateBotSessionAsync(
                                    username,
                                    "manual/register",
                                    client => client.RegisterAsync(username, empire, registrationCode));
                            }
                            else
                            {
                                var password = request.Password ?? "";
                                if (string.IsNullOrWhiteSpace(password))
                                {
                                    statusChannel.Writer.TryWrite("Password is required for login mode.");
                                    continue;
                                }

                                (session, passwordToSave) = await CreateBotSessionAsync(
                                    username,
                                    "manual/login",
                                    async client =>
                                    {
                                        await client.LoginAsync(username, password);
                                        return password;
                                    });
                            }

                            lock (botLock)
                            {
                                botSessions[session.Id] = session;
                                if (activeBotId == null)
                                    activeBotId = session.Id;
                            }
                            LogBotTabsIfChanged("manual_add_added");
                            UpsertSavedBot(username, passwordToSave);
                            StartBotWorker(session);

                            statusChannel.Writer.TryWrite($"Bot loaded: {session.Label}");
                            PublishActiveSnapshot();
                        }
                        catch (Exception ex)
                        {
                            statusChannel.Writer.TryWrite($"Failed to load bot '{display}': {ex.Message}");
                            LogAuth($"manual | {display} | load_failed | {ex.GetType().Name}: {ex.Message}");
                        }
                    }

                    while (switchBotChannel.Reader.TryRead(out var botId))
                    {
                        BotSession? switched = null;
                        lock (botLock)
                        {
                            if (botSessions.TryGetValue(botId, out var existing))
                            {
                                activeBotId = botId;
                                switched = existing;
                            }
                        }

                        if (switched == null)
                        {
                            statusChannel.Writer.TryWrite("Selected bot no longer exists.");
                            continue;
                        }

                        statusChannel.Writer.TryWrite($"Switched to {switched.Label}");
                        PublishActiveSnapshot();
                    }

                    while (controlInputChannel.Reader.TryRead(out var newInput))
                    {
                        var active = GetActiveBot();
                        if (active == null)
                        {
                            statusChannel.Writer.TryWrite("No active bot selected.");
                            continue;
                        }

                        active.ControlInputQueue.Writer.TryWrite(newInput);
                    }

                    while (generateScriptChannel.Reader.TryRead(out var generationInput))
                    {
                        var active = GetActiveBot();
                        if (active == null)
                        {
                            statusChannel.Writer.TryWrite("No active bot selected.");
                            continue;
                        }

                        active.GenerateScriptQueue.Writer.TryWrite(generationInput);
                    }

                    while (saveExampleChannel.Reader.TryRead(out _))
                    {
                        var active = GetActiveBot();
                        if (active == null)
                        {
                            statusChannel.Writer.TryWrite("No active bot selected.");
                            continue;
                        }

                        active.SaveExampleQueue.Writer.TryWrite(true);
                    }

                    while (executeScriptChannel.Reader.TryRead(out _))
                    {
                        var active = GetActiveBot();
                        if (active == null)
                        {
                            statusChannel.Writer.TryWrite("No active bot selected.");
                            continue;
                        }

                        var script = active.Agent.CurrentControlInput;
                        if (string.IsNullOrWhiteSpace(script))
                        {
                            statusChannel.Writer.TryWrite("No script loaded.");
                            continue;
                        }

                        active.ControlInputQueue.Writer.TryWrite(script);
                        statusChannel.Writer.TryWrite($"Restarting script for {active.Label}");
                    }

                    while (inputChannel.Reader.TryRead(out var commandInput))
                    {
                        var active = GetActiveBot();
                        if (active == null)
                        {
                            statusChannel.Writer.TryWrite("No active bot selected.");
                            continue;
                        }

                        active.CommandQueue.Writer.TryWrite(commandInput);
                    }

                    await Task.Delay(50, cts.Token);
                }
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown.
            }
            catch (Exception ex)
            {
                LogAuth($"bot_coordinator | failed | {ex.GetType().Name}: {ex.Message}");
            }
        }, cts.Token);

        var uiRenderTask = Task.Run(async () =>
        {
            string lastRenderedTabSignature = "";
            try
            {
                while (await uiChannel.Reader.WaitToReadAsync(cts.Token))
                {
                    UiSnapshot snapshot = await uiChannel.Reader.ReadAsync(cts.Token);
                    while (uiChannel.Reader.TryRead(out var newer))
                        snapshot = newer;

                    var renderedLabels = string.Join(",", snapshot.Bots.Select(b => b.Label));
                    var renderedSignature = $"{snapshot.Bots.Count}|{snapshot.ActiveBotId}|{renderedLabels}";
                    if (renderedSignature != lastRenderedTabSignature)
                    {
                        lastRenderedTabSignature = renderedSignature;
                        LogAuth(
                            $"ui_render_dispatch | tabs_changed | count={snapshot.Bots.Count} | active={snapshot.ActiveBotId ?? "(null)"} | labels=[{renderedLabels}]");
                    }

                    ui.Render(
                        snapshot.SpaceStateMarkdown,
                        snapshot.TradeStateMarkdown,
                        snapshot.Memory,
                        snapshot.ControlInput,
                        snapshot.CurrentScriptLine,
                        snapshot.Mode,
                        snapshot.Actions,
                        snapshot.LastGenerationPrompt,
                        snapshot.Bots,
                        snapshot.ActiveBotId
                    );
                }
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown.
            }
        }, cts.Token);

        ui.Run();

        cts.Cancel();
        uiChannel.Writer.TryComplete();

        List<BotSession> sessionsToDispose;
        List<Task> workerTasks;
        lock (botLock)
        {
            sessionsToDispose = botSessions.Values.ToList();
            workerTasks = sessionsToDispose
                .Where(s => s.WorkerTask != null)
                .Select(s => s.WorkerTask!)
                .ToList();
        }

        foreach (var session in sessionsToDispose)
            session.WorkerCts.Cancel();

        foreach (var session in sessionsToDispose)
            session.Client.Dispose();

        await Task.WhenAll(
            botTask.ContinueWith(_ => { }),
            Task.WhenAll(workerTasks).ContinueWith(_ => { }),
            uiRenderTask.ContinueWith(_ => { })
        );
    }

    private static CommandResult ParseCommand(string input)
    {
        var parts = input
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);

        return new CommandResult
        {
            Action = parts.Length > 0 ? parts[0] : "",
            Arg1 = parts.Length > 1 ? parts[1] : null,
            Quantity = parts.Length > 2 && int.TryParse(parts[2], out var qty)
                ? qty
                : null
        };
    }

    private static List<SavedBot> LoadSavedBots()
    {
        try
        {
            if (!File.Exists(AppPaths.SavedBotsFile))
                return new List<SavedBot>();

            var raw = File.ReadAllText(AppPaths.SavedBotsFile);
            var loaded = JsonSerializer.Deserialize<List<SavedBot>>(raw);
            return loaded?
                .Where(b =>
                    !string.IsNullOrWhiteSpace(b.Username) &&
                    !string.IsNullOrWhiteSpace(b.Password))
                .ToList() ?? new List<SavedBot>();
        }
        catch
        {
            return new List<SavedBot>();
        }
    }

    private static void SaveSavedBots(List<SavedBot> bots)
    {
        var cleaned = bots
            .Where(b =>
                !string.IsNullOrWhiteSpace(b.Username) &&
                !string.IsNullOrWhiteSpace(b.Password))
            .Select(b => new SavedBot(
                b.Username.Trim(),
                b.Password))
            .ToList();

        var json = JsonSerializer.Serialize(cleaned, new JsonSerializerOptions
        {
            WriteIndented = true
        });
        File.WriteAllText(AppPaths.SavedBotsFile, json);
    }

    private sealed class BotSession
    {
        public BotSession(
            string id,
            string label,
            SpaceMoltAgent agent,
            SpaceMoltHttpClient client)
        {
            Id = id;
            Label = label;
            Agent = agent;
            Client = client;
        }

        public string Id { get; }
        public string Label { get; }
        public SpaceMoltAgent Agent { get; }
        public SpaceMoltHttpClient Client { get; }
        public GameState? LatestState { get; set; }
        public DateTime LastHaltedSnapshotAt { get; set; } = DateTime.MinValue;
        public Channel<string> CommandQueue { get; } = Channel.CreateUnbounded<string>();
        public Channel<string> ControlInputQueue { get; } = Channel.CreateUnbounded<string>();
        public Channel<string> GenerateScriptQueue { get; } = Channel.CreateUnbounded<string>();
        public Channel<bool> SaveExampleQueue { get; } = Channel.CreateUnbounded<bool>();
        public CancellationTokenSource WorkerCts { get; } = new();
        public Task? WorkerTask { get; set; }
    }

    private sealed record SavedBot(string Username, string Password);

    private sealed record UiSnapshot(
        string SpaceStateMarkdown,
        string? TradeStateMarkdown,
        IReadOnlyList<string> Memory,
        string? ControlInput,
        int? CurrentScriptLine,
        ControlModeKind Mode,
        IReadOnlyList<string> Actions,
        string? LastGenerationPrompt,
        IReadOnlyList<BotTab> Bots,
        string? ActiveBotId
    );
}
