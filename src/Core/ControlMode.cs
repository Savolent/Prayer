public enum ControlModeKind
{
    ScriptMode
}

public interface ControlMode
{
    ControlModeKind Kind { get; }
    string Name { get; }
}

public sealed class ScriptMode : ControlMode
{
    public static ScriptMode Instance { get; } = new();

    private ScriptMode()
    {
    }

    public ControlModeKind Kind => ControlModeKind.ScriptMode;
    public string Name => "ScriptMode";
}

public static class ControlModes
{
    public static ControlMode FromKind(ControlModeKind kind)
    {
        return ScriptMode.Instance;
    }
}
