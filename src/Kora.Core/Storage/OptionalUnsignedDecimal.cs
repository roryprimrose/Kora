using System.Globalization;

namespace Kora.Core.Storage;

/// <summary>Strict invariant decimal input for optional metadata bounds; empty means Any.</summary>
public static class OptionalUnsignedDecimal
{
    public static long? Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length == 0) { return null; }
        if (text.Any(character => character is < '0' or > '9')
            || !long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value))
        { throw new InvalidDataException("Enter invariant decimal integers only; no signs, whitespace, separators or overflow. Empty means Any."); }
        return value;
    }
}
