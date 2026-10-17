using System.Globalization;

namespace Kora.Core.Storage;

/// <summary>Strict round-trip UTC metadata input; empty means Any, never a repaired timestamp.</summary>
public static class OptionalCanonicalUtc
{
    public static DateTimeOffset? Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length == 0) { return null; }
        if (!DateTimeOffset.TryParseExact(text, "O", CultureInfo.InvariantCulture, DateTimeStyles.None, out var value)
            || value.Offset != TimeSpan.Zero
            || !text.Equals(value.ToString("O", CultureInfo.InvariantCulture), StringComparison.Ordinal))
        { throw new InvalidDataException("Enter canonical UTC time: yyyy-MM-ddTHH:mm:ss.fffffff+00:00. Empty means Any."); }
        return value;
    }
}
