namespace Kora.Core.Configuration;

public sealed record WorkbenchPlacement(WorkbenchBounds? Compact, WorkbenchBounds? Expanded, bool ExpandedMaximized)
{
    public string Serialize() => string.Join("\n", "1", Compact?.Serialize() ?? "-", Expanded?.Serialize() ?? "-",
        ExpandedMaximized ? "Maximized" : "Normal");

    public static WorkbenchPlacement Parse(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var lines = value.Split('\n');
        if (lines.Length != 4 || !string.Equals(lines[0], "1", StringComparison.Ordinal)
            || lines[3] is not ("Normal" or "Maximized"))
        { throw new InvalidDataException("The saved workbench placement format is unknown or malformed."); }
        return new(string.Equals(lines[1], "-", StringComparison.Ordinal) ? null : WorkbenchBounds.Parse(lines[1]),
            string.Equals(lines[2], "-", StringComparison.Ordinal) ? null : WorkbenchBounds.Parse(lines[2]),
            string.Equals(lines[3], "Maximized", StringComparison.Ordinal));
    }
}
