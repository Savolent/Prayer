using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

public static class DslInterpreter
{
    internal const string RepeatStartAction = "__repeat_start";
    internal const string RepeatEndAction = "__repeat_end";

    public static string NormalizeScript(string dslScript, GameState? state = null)
    {
        if (string.IsNullOrWhiteSpace(dslScript))
            return string.Empty;

        var commands = state == null
            ? Translate(dslScript)
            : Translate(dslScript, state);

        return RenderScript(commands).TrimEnd();
    }

    public static string RenderScript(IReadOnlyList<CommandResult> commands)
    {
        if (commands == null || commands.Count == 0)
            return string.Empty;

        var sb = new StringBuilder();
        int index = 0;
        AppendRendered(commands, ref index, sb, indent: 0, closeRepeatId: null);

        return sb.ToString();
    }

    public static IReadOnlyList<CommandResult> Translate(string dslScript)
    {
        var tree = DslParser.ParseTree(dslScript);
        return Translate(tree, state: null);
    }

    public static IReadOnlyList<CommandResult> Translate(string dslScript, GameState state)
    {
        var tree = DslParser.ParseTree(dslScript);
        return Translate(tree, state);
    }

    public static IReadOnlyList<CommandResult> Translate(DslProgram program)
    {
        if (program == null)
            throw new ArgumentNullException(nameof(program));

        return program.AllSteps
            .Select(ParseStep)
            .ToList();
    }

    public static IReadOnlyList<CommandResult> Translate(DslAstProgram tree)
    {
        return Translate(tree, state: null);
    }

    public static IReadOnlyList<CommandResult> Translate(DslAstProgram tree, GameState? state)
    {
        if (tree == null)
            throw new ArgumentNullException(nameof(tree));

        var result = new List<CommandResult>();
        int nextRepeatId = 0;
        InterpretNodes(tree.Statements, state, result, ref nextRepeatId);
        return result;
    }

    private static void InterpretNodes(
        IReadOnlyList<DslAstNode> nodes,
        GameState? state,
        List<CommandResult> output,
        ref int nextRepeatId)
    {
        foreach (var node in nodes)
        {
            switch (node)
            {
                case DslCommandAstNode commandNode:
                {
                    var command = new DslCommand(commandNode.Name, commandNode.Args);
                    CommandResult result;
                    try
                    {
                        result = command.ToValidCommand(state, command);
                    }
                    catch (FormatException ex) when (commandNode.SourceLine > 0)
                    {
                        throw new FormatException($"Line {commandNode.SourceLine}: {ex.Message}", ex);
                    }

                    result.SourceLine = commandNode.SourceLine;
                    output.Add(result);
                    break;
                }
                case DslRepeatAstNode repeatNode:
                {
                    string repeatId = $"r{++nextRepeatId}";
                    output.Add(new CommandResult
                    {
                        Action = RepeatStartAction,
                        Arg1 = repeatId,
                        SourceLine = repeatNode.SourceLine
                    });
                    InterpretNodes(
                        repeatNode.Body ?? Array.Empty<DslAstNode>(),
                        state,
                        output,
                        ref nextRepeatId);
                    output.Add(new CommandResult
                    {
                        Action = RepeatEndAction,
                        Arg1 = repeatId,
                        SourceLine = repeatNode.SourceLine
                    });
                    break;
                }
                default:
                    throw new FormatException("Unknown DSL AST node.");
            }
        }
    }

    private static void AppendRendered(
        IReadOnlyList<CommandResult> commands,
        ref int index,
        StringBuilder sb,
        int indent,
        string? closeRepeatId)
    {
        while (index < commands.Count)
        {
            var cmd = commands[index++];
            if (string.IsNullOrWhiteSpace(cmd.Action))
                continue;

            if (string.Equals(cmd.Action, RepeatStartAction, StringComparison.Ordinal))
            {
                AppendIndent(sb, indent);
                sb.AppendLine("repeat {");
                AppendRendered(commands, ref index, sb, indent + 2, cmd.Arg1);
                AppendIndent(sb, indent);
                sb.AppendLine("}");
                continue;
            }

            if (string.Equals(cmd.Action, RepeatEndAction, StringComparison.Ordinal))
            {
                if (closeRepeatId != null && string.Equals(cmd.Arg1, closeRepeatId, StringComparison.Ordinal))
                    return;

                // Ignore unmatched control markers when rendering normalized scripts.
                continue;
            }

            AppendIndent(sb, indent);
            sb.Append(cmd.Action);
            if (!string.IsNullOrWhiteSpace(cmd.Arg1))
            {
                sb.Append(' ');
                sb.Append(cmd.Arg1);
            }

            if (cmd.Quantity.HasValue)
            {
                sb.Append(' ');
                sb.Append(cmd.Quantity.Value);
            }

            sb.AppendLine(";");
        }
    }

    private static void AppendIndent(StringBuilder sb, int indent)
    {
        if (indent > 0)
            sb.Append(' ', indent);
    }

    private static CommandResult ParseStep(string step)
    {
        var parts = step
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return new CommandResult
        {
            Action = parts.ElementAtOrDefault(0) ?? "",
            Arg1 = parts.ElementAtOrDefault(1),
            Quantity = int.TryParse(parts.ElementAtOrDefault(2), out int n)
                ? n
                : null,
            SourceLine = null
        };
    }
}
