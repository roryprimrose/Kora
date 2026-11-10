using System.Globalization;

namespace Kora.Core.Configuration;

public sealed record WorkbenchBounds
{
    public int X { get; }
    public int Y { get; }
    public double Width { get; }
    public double Height { get; }
    public double Scale { get; }

    public WorkbenchBounds(int x, int y, double width, double height, double scale)
    {
        if (!double.IsFinite(width) || width <= 0 || width > 32768)
        { throw new ArgumentOutOfRangeException(nameof(width)); }
        if (!double.IsFinite(height) || height <= 0 || height > 32768)
        { throw new ArgumentOutOfRangeException(nameof(height)); }
        if (!double.IsFinite(scale) || scale <= 0 || scale > 16)
        { throw new ArgumentOutOfRangeException(nameof(scale)); }
        X = x;
        Y = y;
        Width = width;
        Height = height;
        Scale = scale;
    }

    internal string Serialize() => string.Join("|",
        X.ToString(CultureInfo.InvariantCulture), Y.ToString(CultureInfo.InvariantCulture),
        Width.ToString("R", CultureInfo.InvariantCulture), Height.ToString("R", CultureInfo.InvariantCulture),
        Scale.ToString("R", CultureInfo.InvariantCulture));

    internal static WorkbenchBounds Parse(string value)
    {
        var fields = value.Split('|');
        if (fields.Length != 5
            || !int.TryParse(fields[0], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var x)
            || !int.TryParse(fields[1], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var y)
            || !double.TryParse(fields[2], NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent,
                CultureInfo.InvariantCulture, out var width)
            || !double.TryParse(fields[3], NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent,
                CultureInfo.InvariantCulture, out var height)
            || !double.TryParse(fields[4], NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent,
                CultureInfo.InvariantCulture, out var scale))
        { throw new InvalidDataException("The saved workbench bounds are malformed."); }
        try { return new(x, y, width, height, scale); }
        catch (ArgumentOutOfRangeException exception)
        { throw new InvalidDataException("The saved workbench bounds are invalid.", exception); }
    }
}
