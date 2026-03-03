using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

public class LowFuelWarning : IWarning
{
    private readonly int _threshold;

    public LowFuelWarning(int threshold = 10)
    {
        _threshold = threshold;
    }

    public bool ShouldWarn(GameState state)
        => state.Fuel <= _threshold;

    public string BuildWarning(GameState state)
        => $"WARNING: Fuel is low ({state.Fuel}). Consider refueling soon.";
}

