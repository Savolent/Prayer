using System;
using System.Text.Json;
using System.Threading.Tasks;

public class SellShipCommand : ISingleTurnCommand
{
    public string Name => "sell_ship";

    public bool IsAvailable(GameState state)
        => state.Mode.Kind == GameContextKind.Shipyard && state.Docked && state.CurrentPOI.IsStation;
    public string BuildHelp(GameState state)
        => "- sell_ship <shipId> → sell a stored ship to the station";

    public async Task<CommandExecutionResult?> ExecuteAsync(
        SpaceMoltHttpClient client,
        CommandResult cmd,
        GameState state)
    {
        var shipId = cmd.Arg1?.Trim();
        if (string.IsNullOrWhiteSpace(shipId))
            return new CommandExecutionResult { ResultMessage = "Usage: sell_ship <shipId>." };

        JsonElement response = await client.ExecuteAsync("sell_ship", new { ship_id = shipId });
        return new CommandExecutionResult
        {
            ResultMessage = CommandJson.TryGetResultMessage(response) ?? $"Sold ship {shipId}."
        };
    }
}
