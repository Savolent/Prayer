using System;
using System.Text.Json;
using System.Threading.Tasks;

public class UninstallModCommand : ISingleTurnCommand
{
    public string Name => "uninstall_mod";

    public bool IsAvailable(GameState state)
        => state.Mode.Kind == GameContextKind.Hangar && state.Docked && state.CurrentPOI.IsStation;
    public string BuildHelp(GameState state)
        => "- uninstall_mod <moduleId> → uninstall a module from your active ship";

    public async Task<CommandExecutionResult?> ExecuteAsync(
        SpaceMoltHttpClient client,
        CommandResult cmd,
        GameState state)
    {
        var moduleId = cmd.Arg1?.Trim();
        if (string.IsNullOrWhiteSpace(moduleId))
            return new CommandExecutionResult { ResultMessage = "Usage: uninstall_mod <moduleId>." };

        JsonElement response = await client.ExecuteAsync("uninstall_mod", new { module_id = moduleId });
        return new CommandExecutionResult
        {
            ResultMessage = CommandJson.TryGetResultMessage(response) ?? $"Uninstalled module {moduleId}."
        };
    }
}
