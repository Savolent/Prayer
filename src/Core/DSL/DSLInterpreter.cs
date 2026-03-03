using System;
using System.Collections.Generic;
using System.Linq;

public static class DslInterpreter
{
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
        InterpretNodes(tree.Statements, DslParser.RootGroup, state, result);
        return result;
    }

    private static void InterpretNodes(
        IReadOnlyList<DslAstNode> nodes,
        DslCommandGroup currentGroup,
        GameState? state,
        List<CommandResult> output)
    {
        foreach (var node in nodes)
        {
            switch (node)
            {
                case DslCommandAstNode commandNode:
                {
                    var command = new DslCommand(commandNode.Name, commandNode.Args);
                    var result = command.ToValidCommand(state, command);
                    result.SourceLine = commandNode.SourceLine;
                    output.Add(result);
                    break;
                }
                case DslBlockAstNode blockNode:
                {
                    var block = DslBlockFactory.Create(blockNode);
                    var enterSteps = block.Enter(state, block).ToList();
                    foreach (var step in enterSteps)
                        step.SourceLine = blockNode.SourceLine;
                    output.AddRange(enterSteps);

                    var bodyGroup = DslParser.ResolveBlockBodyGroup(block.Name, currentGroup);
                    InterpretNodes(block.Body, bodyGroup, state, output);

                    var leaveSteps = block.Leave(state, block).ToList();
                    foreach (var step in leaveSteps)
                        step.SourceLine = blockNode.SourceLine;
                    output.AddRange(leaveSteps);
                    break;
                }
                default:
                    throw new FormatException("Unknown DSL AST node.");
            }
        }
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
