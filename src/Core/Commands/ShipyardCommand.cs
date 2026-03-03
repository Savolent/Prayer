using System.Threading.Tasks;

public class ShipyardCommand : ISingleTurnCommand
{
    public string Name => "shipyard";

    public bool IsAvailable(GameState state)
        => state.Docked &&
           state.CurrentPOI.IsStation &&
           state.Mode.Kind == GameContextKind.Space;
    public string BuildHelp(GameState state)
        => "- shipyard → enter ShipYardState";

    public Task<CommandExecutionResult?> ExecuteAsync(
        SpaceMoltHttpClient client,
        CommandResult cmd,
        GameState state)
    {
        if (!state.Docked || !state.CurrentPOI.IsStation)
        {
            return Task.FromResult<CommandExecutionResult?>(new CommandExecutionResult
            {
                ResultMessage = "Shipyard is only available while docked at a station."
            });
        }

        client.SetMode(GameContextKind.Shipyard);
        state.Mode = ShipyardContextMode.Instance;

        return Task.FromResult<CommandExecutionResult?>(new CommandExecutionResult
        {
            ResultMessage = "Entered ShipYardState."
        });
    }
}
