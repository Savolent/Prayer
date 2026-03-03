using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

public class HaltCommand : ISingleTurnCommand
{
    public string Name => "halt";

    public bool IsAvailable(GameState state)
        => true;
    public string BuildHelp(GameState state)
        => "- halt → pause and wait for user input";

    public Task<CommandExecutionResult?> ExecuteAsync(
        SpaceMoltHttpClient client,
        CommandResult cmd,
        GameState state)
    {
        return Task.FromResult<CommandExecutionResult?>(new CommandExecutionResult
        {
            ResultMessage = "Halting autonomous execution. Waiting for user input."
        });
    }
}

// =====================================================
// SELL (SELL ALL STACK)
// =====================================================

