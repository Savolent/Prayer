using System;
using System.Text.Json;
using System.Threading.Tasks;

public class CommissionQuoteCommand : ISingleTurnCommand
{
    public string Name => "commission_quote";

    public bool IsAvailable(GameState state)
        => state.Mode.Kind == GameContextKind.Shipyard && state.Docked && state.CurrentPOI.IsStation;
    public string BuildHelp(GameState state)
        => "- commission_quote <shipClassId> → quote ship commission cost";

    public async Task<CommandExecutionResult?> ExecuteAsync(
        SpaceMoltHttpClient client,
        CommandResult cmd,
        GameState state)
    {
        var shipClass = cmd.Arg1?.Trim();
        if (string.IsNullOrWhiteSpace(shipClass))
            return new CommandExecutionResult { ResultMessage = "Usage: commission_quote <shipClassId>." };

        JsonElement response = await client.ExecuteAsync("commission_quote", new { ship_class = shipClass });
        return new CommandExecutionResult
        {
            ResultMessage = CommandJson.TryGetResultMessage(response) ?? $"Fetched commission quote for {shipClass}."
        };
    }
}
