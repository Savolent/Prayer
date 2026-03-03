using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

public class RepairCommand : ISingleTurnCommand
{
    public string Name => "repair";

    public bool IsAvailable(GameState state)
        => state.Docked &&
           state.Hull < state.MaxHull;
    public string BuildHelp(GameState state)
        => "- repair → restore hull";

    public async Task<CommandExecutionResult?> ExecuteAsync(
        SpaceMoltHttpClient client,
        CommandResult cmd,
        GameState state)
    {
        JsonElement response = await client.ExecuteAsync("repair");

        return new CommandExecutionResult
        {
            ResultMessage = CommandJson.TryGetResultMessage(response)
        };
    }
}

