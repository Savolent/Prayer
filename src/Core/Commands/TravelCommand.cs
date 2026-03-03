using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

public class TravelCommand : ISingleTurnCommand
{
    public string Name => "travel";

    public bool IsAvailable(GameState state)
        => !state.Docked &&
           state.POIs?.Length > 0;

    public string BuildHelp(GameState state)
        => "- travel <poiId>";

    public async Task<CommandExecutionResult?> ExecuteAsync(
        SpaceMoltHttpClient client,
        CommandResult cmd,
        GameState state)
    {
        if (cmd.Arg1 == null)
            return null;

        JsonElement response = await client.ExecuteAsync("travel",
            new { target_poi = cmd.Arg1 });

        return new CommandExecutionResult
        {
            ResultMessage = CommandJson.TryGetResultMessage(response)
        };
    }
}

// =====================================================
// GO (AUTO TRAVEL/JUMP WITH PATHFIND)
// =====================================================

