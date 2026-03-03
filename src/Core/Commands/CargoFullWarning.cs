using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

public class CargoFullWarning : IWarning
{
    public bool ShouldWarn(GameState state)
        => state.CargoUsed >= state.CargoCapacity * 3/4;

    public string BuildWarning(GameState state)
        => "WARNING: Cargo is full.";
}

