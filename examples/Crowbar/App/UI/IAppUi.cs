using System.Collections.Generic;
using System.Threading.Channels;

public interface IAppUi
{
    void SetRuntimeCommandWriter(ChannelWriter<RuntimeCommandRequest> writer);
    void SetSwitchBotWriter(ChannelWriter<string> writer);
    void SetAddBotWriter(ChannelWriter<AddBotRequest> writer);
    void SetLlmSelectionWriter(ChannelWriter<LlmProviderSelection> writer);

    void ConfigureInitialLlmSelection(string provider, string model);
    void SetAvailableProviders(IReadOnlyList<string> providers);
    void SetProviderModels(string provider, IReadOnlyList<string> models);

    void Render(
        string spaceStateMarkdown,
        string? tradeStateMarkdown,
        string? shipyardStateMarkdown,
        string? cantinaStateMarkdown,
        string? catalogStateMarkdown,
        IReadOnlyList<MissionPromptOption> activeMissionPrompts,
        IReadOnlyList<string> memory,
        IReadOnlyList<string> executionStatusLines,
        string? controlInput,
        int? currentScriptLine,
        string? lastGenerationPrompt,
        IReadOnlyList<BotTab> bots,
        string? activeBotId);

    void Run();
}
