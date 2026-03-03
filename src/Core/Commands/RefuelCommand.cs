using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

public class RefuelCommand : ISingleTurnCommand
{
    public string Name => "refuel";

    public bool IsAvailable(GameState state)
        => state.Docked && state.Credits > 0;
    public string BuildHelp(GameState state)
        => "- refuel";

    public async Task<CommandExecutionResult?> ExecuteAsync(
        SpaceMoltHttpClient client,
        CommandResult cmd,
        GameState state)
    {
        JsonElement response = await client.ExecuteAsync("refuel", new { });

        return new CommandExecutionResult
        {
            ResultMessage = CommandJson.TryGetResultMessage(response)
        };
    }
}

// =====================================================
// TRAVEL
// =====================================================

