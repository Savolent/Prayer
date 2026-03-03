using System;
using System.Collections.Generic;
using System.Linq;

public class DslCommand
{
    public DslCommand(string name, IReadOnlyList<string>? args = null)
    {
        Name = name ?? "";
        Args = args ?? Array.Empty<string>();
    }

    public string Name { get; }
    public IReadOnlyList<string> Args { get; }

    public virtual CommandResult ToValidCommand(GameState? state, DslCommand self)
    {
        var normalized = DslParser.NormalizeCommandStep(self.Name, self.Args, DslParser.RootGroup);
        var parts = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return new CommandResult
        {
            Action = parts.ElementAtOrDefault(0) ?? "",
            Arg1 = parts.ElementAtOrDefault(1),
            Quantity = int.TryParse(parts.ElementAtOrDefault(2), out int n) ? n : null
        };
    }
}

public abstract class DslBlock
{
    protected DslBlock(string name, IReadOnlyList<DslAstNode> body)
    {
        Name = name ?? "";
        Body = body ?? Array.Empty<DslAstNode>();
    }

    public string Name { get; }
    public IReadOnlyList<DslAstNode> Body { get; }

    public virtual IEnumerable<CommandResult> Enter(GameState? state, DslBlock self)
        => Array.Empty<CommandResult>();

    public virtual IEnumerable<CommandResult> Leave(GameState? state, DslBlock self)
        => Array.Empty<CommandResult>();
}

public sealed class DockDslBlock : DslBlock
{
    private bool _wasDockedAtEntry;
    private bool _enteredDockDuringBlock;

    public DockDslBlock(string name, IReadOnlyList<DslAstNode> body)
        : base(name, body)
    {
    }

    public override IEnumerable<CommandResult> Enter(GameState? state, DslBlock self)
    {
        _wasDockedAtEntry = state?.Docked ?? false;
        _enteredDockDuringBlock = false;

        if (_wasDockedAtEntry)
            return Array.Empty<CommandResult>();

        var commands = new List<CommandResult>();

        if (state != null && !state.CurrentPOI.HasBase)
        {
            var nearestDockable = state.POIs.FirstOrDefault(p => p.HasBase)?.Id;
            if (!string.IsNullOrWhiteSpace(nearestDockable))
            {
                commands.Add(new DslCommand("go", new[] { nearestDockable })
                    .ToValidCommand(state, new DslCommand("go", new[] { nearestDockable })));
            }
        }

        commands.Add(new DslCommand("dock").ToValidCommand(state, new DslCommand("dock")));
        _enteredDockDuringBlock = true;
        return commands;
    }

    public override IEnumerable<CommandResult> Leave(GameState? state, DslBlock self)
    {
        if (_wasDockedAtEntry || !_enteredDockDuringBlock)
            return Array.Empty<CommandResult>();

        return new[] { new DslCommand("undock").ToValidCommand(state, new DslCommand("undock")) };
    }
}

public sealed class PassthroughDslBlock : DslBlock
{
    public PassthroughDslBlock(string name, IReadOnlyList<DslAstNode> body)
        : base(name, body)
    {
    }
}

public static class DslBlockFactory
{
    public static DslBlock Create(DslBlockAstNode node)
    {
        var name = (node.Name ?? "").Trim();

        if (string.Equals(name, "dock", StringComparison.OrdinalIgnoreCase))
            return new DockDslBlock("dock", node.Body);

        return new PassthroughDslBlock(name, node.Body);
    }
}
