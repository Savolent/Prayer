using Spectre.Console;
using System;
using System.IO;

public class SimpleConsoleUI
{
    private TextWriter _savedOut = Console.Out;
    private TextWriter _savedErr = Console.Error;
    private bool _muted;

    /// <summary>
    /// Re-enable normal Console output (Console.WriteLine etc).
    /// Call this BEFORE drawing if you previously muted.
    /// </summary>
    public void EnableConsole()
    {
        if (!_muted) return;

        Console.SetOut(_savedOut);
        Console.SetError(_savedErr);
        _muted = false;
    }

    /// <summary>
    /// Disable normal Console output (Console.WriteLine etc).
    /// Call this AFTER drawing to keep the screen clean between frames.
    /// </summary>
    public void DisableConsole()
    {
        if (_muted) return;

        _savedOut = Console.Out;
        _savedErr = Console.Error;

        Console.SetOut(TextWriter.Null);
        Console.SetError(TextWriter.Null);
        _muted = true;
    }

    public void Render(GameState state, SpaceMoltAgent agent)
    {
        // Enable BEFORE drawing
        EnableConsole();

        Console.Clear();

        var md = state?.ToMD() ?? "## Game State\n_(null state)_\n";
        Console.Write(md + "\n");
        Console.Write(agent.BuildMemoryBlock() + "\n");
    }
}