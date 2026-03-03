using System;
using System.Text.Json;
using System.Threading.Tasks;

public class CommissionShipCommand : ISingleTurnCommand
{
    public string Name => "commission_ship";

    public bool IsAvailable(GameState state)
        => state.Mode.Kind == GameContextKind.Shipyard && state.Docked && state.CurrentPOI.IsStation;
    public string BuildHelp(GameState state)
        => "- commission_ship <shipClassId> → start ship commission";

    public async Task<CommandExecutionResult?> ExecuteAsync(
        SpaceMoltHttpClient client,
        CommandResult cmd,
        GameState state)
    {
        var shipClass = cmd.Arg1?.Trim();
        if (string.IsNullOrWhiteSpace(shipClass))
            return new CommandExecutionResult { ResultMessage = "Usage: commission_ship <shipClassId>." };

        JsonElement response = await client.ExecuteAsync("commission_ship", new { ship_class = shipClass });
        return new CommandExecutionResult
        {
            ResultMessage = CommandJson.TryGetResultMessage(response) ?? $"Commission started for {shipClass}."
        };
    }
}
