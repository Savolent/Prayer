using System;
using System.Text.Json;
using System.Threading.Tasks;

public class SwitchShipCommand : ISingleTurnCommand
{
    public string Name => "switch_ship";

    public bool IsAvailable(GameState state)
        => state.Mode.Kind == GameContextKind.Hangar && state.Docked && state.CurrentPOI.IsStation;
    public string BuildHelp(GameState state)
        => "- switch_ship <shipId> → switch active ship at this station";

    public async Task<CommandExecutionResult?> ExecuteAsync(
        SpaceMoltHttpClient client,
        CommandResult cmd,
        GameState state)
    {
        var shipId = cmd.Arg1?.Trim();
        if (string.IsNullOrWhiteSpace(shipId))
        {
            return new CommandExecutionResult
            {
                ResultMessage = "Usage: switch_ship <shipId>."
            };
        }

        JsonElement response = await client.ExecuteAsync("switch_ship", new { ship_id = shipId });
        string? message = CommandJson.TryGetResultMessage(response);

        if (string.IsNullOrWhiteSpace(message))
            message = $"Switched to ship {shipId}.";

        return new CommandExecutionResult
        {
            ResultMessage = message
        };
    }
}
