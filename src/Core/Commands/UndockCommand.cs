using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

public class UndockCommand : ISingleTurnCommand
{
    public string Name => "undock";

    public bool IsAvailable(GameState state)
        => state.Docked;
    public string BuildHelp(GameState state)
        => "- undock → leave station";

    public async Task<CommandExecutionResult?> ExecuteAsync(
        SpaceMoltHttpClient client,
        CommandResult cmd,
        GameState state)
    {
        JsonElement response = await client.ExecuteAsync("undock");

        return new CommandExecutionResult
        {
            ResultMessage = CommandJson.TryGetResultMessage(response)
        };
    }
}

// =====================================================
// REPAIR
// =====================================================

