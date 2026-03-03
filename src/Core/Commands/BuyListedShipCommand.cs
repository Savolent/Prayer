using System;
using System.Text.Json;
using System.Threading.Tasks;

public class BuyListedShipCommand : ISingleTurnCommand
{
    public string Name => "buy_listed_ship";

    public bool IsAvailable(GameState state)
        => state.Mode.Kind == GameContextKind.Shipyard && state.Docked && state.CurrentPOI.IsStation;
    public string BuildHelp(GameState state)
        => "- buy_listed_ship <listingId> → buy a player-listed ship";

    public async Task<CommandExecutionResult?> ExecuteAsync(
        SpaceMoltHttpClient client,
        CommandResult cmd,
        GameState state)
    {
        var listingId = cmd.Arg1?.Trim();
        if (string.IsNullOrWhiteSpace(listingId))
            return new CommandExecutionResult { ResultMessage = "Usage: buy_listed_ship <listingId>." };

        JsonElement response = await client.ExecuteAsync("buy_listed_ship", new { listing_id = listingId });
        return new CommandExecutionResult
        {
            ResultMessage = CommandJson.TryGetResultMessage(response) ?? $"Bought listed ship {listingId}."
        };
    }
}
