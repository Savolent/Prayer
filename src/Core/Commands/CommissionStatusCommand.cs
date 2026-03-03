using System.Text.Json;
using System.Threading.Tasks;

public class CommissionStatusCommand : ISingleTurnCommand
{
    public string Name => "commission_status";

    public bool IsAvailable(GameState state)
        => state.Mode.Kind == GameContextKind.Shipyard && state.Docked && state.CurrentPOI.IsStation;
    public string BuildHelp(GameState state)
        => "- commission_status → view ship commission progress";

    public async Task<CommandExecutionResult?> ExecuteAsync(
        SpaceMoltHttpClient client,
        CommandResult cmd,
        GameState state)
    {
        JsonElement response = await client.ExecuteAsync("commission_status");

        int count = 0;
        if (response.ValueKind == JsonValueKind.Object &&
            response.TryGetProperty("commissions", out var commissions) &&
            commissions.ValueKind == JsonValueKind.Array)
        {
            count = commissions.GetArrayLength();
        }

        return new CommandExecutionResult
        {
            ResultMessage = count > 0
                ? $"You have {count} active commission(s)."
                : (CommandJson.TryGetResultMessage(response) ?? "No active commissions.")
        };
    }
}
