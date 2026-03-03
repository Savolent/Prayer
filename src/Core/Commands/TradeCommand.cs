using System.Threading.Tasks;

public class TradeCommand : ISingleTurnCommand, IDslCommandGrammar
{
    public string Name => "trade";
    public DslCommandSyntax GetDslSyntax() => new();

    public bool IsAvailable(GameState state)
        => state.Docked &&
           state.CurrentPOI.IsStation &&
           state.Mode.Kind == GameContextKind.Space;
    public string BuildHelp(GameState state)
        => "- trade → use trading terminal";

    public Task<CommandExecutionResult?> ExecuteAsync(
        SpaceMoltHttpClient client,
        CommandResult cmd,
        GameState state)
    {
        if (!state.Docked || !state.CurrentPOI.IsStation)
        {
            return Task.FromResult<CommandExecutionResult?>(new CommandExecutionResult
            {
                ResultMessage = "Trade is only available while docked at a station."
            });
        }

        client.SetMode(GameContextKind.Trade);
        state.Mode = TradeContextMode.Instance;

        return Task.FromResult<CommandExecutionResult?>(new CommandExecutionResult
        {
            ResultMessage = "Entered trading terminal."
        });
    }
}
