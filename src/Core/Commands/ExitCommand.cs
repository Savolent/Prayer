using System.Threading.Tasks;

public class ExitCommand : ISingleTurnCommand
{
    public string Name => "exit";

    public bool IsAvailable(GameState state)
        => state.Mode.Kind != GameContextKind.Space;
    public string BuildHelp(GameState state)
        => "- exit → back to your ship";

    public Task<CommandExecutionResult?> ExecuteAsync(
        SpaceMoltHttpClient client,
        CommandResult cmd,
        GameState state)
    {
        bool fromShipyard = state.Mode.Kind == GameContextKind.Shipyard;
        bool fromHangar = state.Mode.Kind == GameContextKind.Hangar;
        bool fromShipCatalog = state.Mode.Kind == GameContextKind.ShipCatalog;
        client.SetMode(GameContextKind.Space);
        state.Mode = SpaceContextMode.Instance;

        return Task.FromResult<CommandExecutionResult?>(new CommandExecutionResult
        {
            ResultMessage = fromShipyard
                ? "Exited shipyard. Back to your ship."
                : (fromShipCatalog
                    ? "Exited ship catalog. Back to your ship."
                    : (fromHangar
                    ? "Exited hangar. Back to your ship."
                : "Back to your ship.")
                )
        });
    }
}
