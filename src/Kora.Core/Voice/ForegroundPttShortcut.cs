namespace Kora.Core.Voice;

public sealed record ForegroundPttShortcut
{
    private ForegroundPttShortcut(string label) => Label = label;

    public string Label { get; }
    public static ForegroundPttShortcut Unassigned { get; } = new("Unassigned");
    public static ForegroundPttShortcut ControlShiftSpace { get; } = new("Control Shift Space");
    public static ForegroundPttShortcut ControlShiftF9 { get; } = new("Control Shift F9");
    public static IReadOnlyList<ForegroundPttShortcut> Choices { get; } =
        Array.AsReadOnly(new[] { Unassigned, ControlShiftSpace, ControlShiftF9 });

    public static ForegroundPttShortcut Parse(string text) =>
        Choices.SingleOrDefault(choice => string.Equals(choice.Label, text, StringComparison.Ordinal))
        ?? throw new InvalidDataException("Unsupported foreground PTT shortcut. Choose Unassigned, Control Shift Space or Control Shift F9. Other editing, navigation and system chords are reserved.");
}
