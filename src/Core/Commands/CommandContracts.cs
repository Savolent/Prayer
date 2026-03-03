using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

public class CommandExecutionResult
{
    public string? ResultMessage { get; set; }
}

public interface ICommand
{
    string Name { get; }

    bool IsAvailable(GameState state);

    string BuildHelp(GameState state);
}

public enum DslArgKind
{
    None,
    Identifier,
    Integer,
    String
}

public enum DslCommandGroup
{
    Space,
    Trade,
    Hangar,
    Shipyard,
    ShipCatalog
}

public sealed record DslArgumentSpec(
    DslArgKind Kind,
    bool Required = true,
    string? DefaultValue = null);

public sealed record DslCommandSyntax(
    DslArgKind ArgKind = DslArgKind.None,
    bool ArgRequired = false,
    string? DefaultArg = null,
    bool AllowsBlock = false,
    DslCommandGroup? BlockBodyGroup = null,
    IReadOnlyList<DslArgumentSpec>? ArgSpecs = null);

public interface IDslCommandGrammar
{
    DslCommandSyntax GetDslSyntax();
}

public interface ISingleTurnCommand : ICommand
{
    Task<CommandExecutionResult?> ExecuteAsync(
        SpaceMoltHttpClient client,
        CommandResult result,
        GameState state);
}

public interface IMultiTurnCommand : ICommand
{
    Task<CommandExecutionResult?> StartAsync(
        SpaceMoltHttpClient client,
        CommandResult result,
        GameState state);

    Task<(bool finished, CommandExecutionResult? result)> ContinueAsync(
        SpaceMoltHttpClient client,
        GameState state);
}

public interface IWarning
{
    bool ShouldWarn(GameState state);

    string BuildWarning(GameState state);
}
