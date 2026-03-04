using System.Text.Json;
using System.Threading.Tasks;

public class SurveyCommand : ISingleTurnCommand
{
    public string Name => "survey";

    public bool IsAvailable(GameState state)
        => true;

    public string BuildHelp(GameState state)
        => "- survey → survey the current system for hidden deep core deposits";

    public async Task<CommandExecutionResult?> ExecuteAsync(
        SpaceMoltHttpClient client,
        CommandResult cmd,
        GameState state)
    {
        JsonElement response = await client.ExecuteAsync("survey_system");
        return new CommandExecutionResult
        {
            ResultMessage = CommandJson.TryGetResultMessage(response)
        };
    }
}
