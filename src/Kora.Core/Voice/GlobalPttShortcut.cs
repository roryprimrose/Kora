namespace Kora.Core.Voice;

public sealed record GlobalPttShortcut
{
    private GlobalPttShortcut(string label) => Label = label;

    public string Label { get; }
    public static GlobalPttShortcut Unassigned { get; } = new("Unassigned");
    public static GlobalPttShortcut ControlShiftSpace { get; } = new("Control Shift Space");
    public static GlobalPttShortcut ControlShiftF9 { get; } = new("Control Shift F9");
    public static IReadOnlyList<GlobalPttShortcut> Choices { get; } =
        Array.AsReadOnly(new[] { Unassigned, ControlShiftSpace, ControlShiftF9 });

    public static GlobalPttShortcut Parse(string text) =>
        Choices.SingleOrDefault(choice => string.Equals(choice.Label, text, StringComparison.Ordinal))
        ?? throw new InvalidDataException("Unsupported global PTT shortcut. Choose Unassigned, Control Shift Space or Control Shift F9. Other editing, navigation and system chords are reserved.");
}
