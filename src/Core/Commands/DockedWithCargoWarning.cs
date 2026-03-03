using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

public class DockedWithCargoWarning : IWarning
{
    public bool ShouldWarn(GameState state)
        => state.Docked;

    public string BuildWarning(GameState state)
        => "HINT: You are docked at a station!";
}
