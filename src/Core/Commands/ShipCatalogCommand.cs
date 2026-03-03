using System.Threading.Tasks;

public class ShipCatalogCommand : ISingleTurnCommand
{
    public string Name => "ship_catalog";

    public bool IsAvailable(GameState state)
        => state.Docked &&
           state.CurrentPOI.IsStation &&
           state.Mode.Kind == GameContextKind.Shipyard;
    public string BuildHelp(GameState state)
        => "- ship_catalog → enter ShipCatalogState";

    public Task<CommandExecutionResult?> ExecuteAsync(
        SpaceMoltHttpClient client,
        CommandResult cmd,
        GameState state)
    {
        if (!state.Docked || !state.CurrentPOI.IsStation)
        {
            return Task.FromResult<CommandExecutionResult?>(new CommandExecutionResult
            {
                ResultMessage = "Ship catalog is only available while docked at a station."
            });
        }

        client.EnterShipCatalogMode();
        state.Mode = ShipCatalogContextMode.Instance;

        return Task.FromResult<CommandExecutionResult?>(new CommandExecutionResult
        {
            ResultMessage = "Entered ShipCatalogState."
        });
    }
}
