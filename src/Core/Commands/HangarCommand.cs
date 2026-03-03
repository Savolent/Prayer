using System.Threading.Tasks;

public class HangarCommand : ISingleTurnCommand
{
    public string Name => "hangar";

    public bool IsAvailable(GameState state)
        => state.Docked &&
           state.CurrentPOI.IsStation &&
           state.Mode.Kind == GameContextKind.Space;
    public string BuildHelp(GameState state)
        => "- hangar → enter HangarState (manage owned ships)";

    public Task<CommandExecutionResult?> ExecuteAsync(
        SpaceMoltHttpClient client,
        CommandResult cmd,
        GameState state)
    {
        if (!state.Docked || !state.CurrentPOI.IsStation)
        {
            return Task.FromResult<CommandExecutionResult?>(new CommandExecutionResult
            {
                ResultMessage = "Hangar is only available while docked at a station."
            });
        }

        client.SetMode(GameContextKind.Hangar);
        state.Mode = HangarContextMode.Instance;

        return Task.FromResult<CommandExecutionResult?>(new CommandExecutionResult
        {
            ResultMessage = "Entered HangarState."
        });
    }
}
