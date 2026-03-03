public sealed record BotTab(string Id, string Label);

public enum AddBotMode
{
    Login,
    Register
}

public sealed record AddBotRequest(
    AddBotMode Mode,
    string Username,
    string? Password = null,
    string? RegistrationCode = null,
    string? Empire = null);
