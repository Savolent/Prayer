using System;
using System.Text.Json;
using System.Threading.Tasks;

public class BuyShipCommand : ISingleTurnCommand
{
    public string Name => "buy_ship";

    public bool IsAvailable(GameState state)
        => state.Mode.Kind == GameContextKind.Shipyard && state.Docked && state.CurrentPOI.IsStation;
    public string BuildHelp(GameState state)
        => "- buy_ship <shipClassId> → buy a showroom ship";

    public async Task<CommandExecutionResult?> ExecuteAsync(
        SpaceMoltHttpClient client,
        CommandResult cmd,
        GameState state)
    {
        var shipClass = cmd.Arg1?.Trim();
        if (string.IsNullOrWhiteSpace(shipClass))
            return new CommandExecutionResult { ResultMessage = "Usage: buy_ship <shipClassId>." };

        JsonElement response = await client.ExecuteAsync("buy_ship", new { ship_class = shipClass });
        return new CommandExecutionResult
        {
            ResultMessage = CommandJson.TryGetResultMessage(response) ?? $"Bought ship class {shipClass}."
        };
    }
}
