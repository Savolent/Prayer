using System.Threading.Channels;

public static class Log
{
    private static readonly Channel<string> _channel = Channel.CreateUnbounded<string>();
    private static readonly Task _worker;

    static Log()
    {
        AppPaths.EnsureDirectories();

        _worker = Task.Run(async () =>
        {
            await using var stream = new FileStream(
                AppPaths.LlmLogFile,
                FileMode.Append,
                FileAccess.Write,
                FileShare.ReadWrite);

            await using var writer = new StreamWriter(stream)
            {
                AutoFlush = true
            };

            await foreach (var msg in _channel.Reader.ReadAllAsync())
            {
                await writer.WriteLineAsync(msg);
            }
        });
    }

    public static void Write(string message)
    {
        var line = $"[{DateTime.UtcNow:HH:mm:ss.fff}] {message}";
        _channel.Writer.TryWrite(line);
    }
}
