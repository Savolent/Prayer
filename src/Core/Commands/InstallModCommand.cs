using System;
using System.Text.Json;
using System.Threading.Tasks;

public class InstallModCommand : ISingleTurnCommand
{
    public string Name => "install_mod";

    public bool IsAvailable(GameState state)
        => state.Mode.Kind == GameContextKind.Hangar && state.Docked && state.CurrentPOI.IsStation;
    public string BuildHelp(GameState state)
        => "- install_mod <moduleId> → install a module on your active ship";

    public async Task<CommandExecutionResult?> ExecuteAsync(
        SpaceMoltHttpClient client,
        CommandResult cmd,
        GameState state)
    {
        var moduleId = cmd.Arg1?.Trim();
        if (string.IsNullOrWhiteSpace(moduleId))
            return new CommandExecutionResult { ResultMessage = "Usage: install_mod <moduleId>." };

        JsonElement response = await client.ExecuteAsync("install_mod", new { module_id = moduleId });
        return new CommandExecutionResult
        {
            ResultMessage = CommandJson.TryGetResultMessage(response) ?? $"Installed module {moduleId}."
        };
    }
}
