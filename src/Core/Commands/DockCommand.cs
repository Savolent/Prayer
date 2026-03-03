using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

public class DockCommand : ISingleTurnCommand, IDslCommandGrammar
{
    public string Name => "dock";
    public DslCommandSyntax GetDslSyntax() => new(
        AllowsBlock: true,
        BlockBodyGroup: DslCommandGroup.Space);

    public bool IsAvailable(GameState state)
        => !state.Docked &&
           state.CurrentPOI?.HasBase == true;
    public string BuildHelp(GameState state)
        => "- dock → enter station";

    public async Task<CommandExecutionResult?> ExecuteAsync(
        SpaceMoltHttpClient client,
        CommandResult cmd,
        GameState state)
    {
        JsonElement response = await client.ExecuteAsync("dock");

        return new CommandExecutionResult
        {
            ResultMessage = CommandJson.TryGetResultMessage(response)
        };
    }
}

// =====================================================
// UNDOCK
// =====================================================
