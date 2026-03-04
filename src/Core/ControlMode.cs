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
