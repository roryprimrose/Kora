using System.Globalization;

namespace Kora.Core.Diagnostics;

public static class EvidenceFieldPolicy
{
    public static bool IsSensitive(string name) =>
        name.Contains("password", StringComparison.OrdinalIgnoreCase)
        || name.Contains("secret", StringComparison.OrdinalIgnoreCase)
        || name.Contains("token", StringComparison.OrdinalIgnoreCase)
        || name.Contains("path", StringComparison.OrdinalIgnoreCase)
        || name.Contains("transcript", StringComparison.OrdinalIgnoreCase)
        || name.Contains("content", StringComparison.OrdinalIgnoreCase)
        || name.Contains("arguments", StringComparison.OrdinalIgnoreCase)
        || name.Contains("response", StringComparison.OrdinalIgnoreCase)
        || name.Contains("speech", StringComparison.OrdinalIgnoreCase);

    public static void ValidateValue(EvidenceValue value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (!Enum.IsDefined(value.Kind) || (value.Kind == EvidenceValueKind.Null) != (value.CanonicalValue is null)
            || value.CanonicalValue?.Length > 1024)
        {
            throw new InvalidDataException("The structured property kind or bound is invalid.");
        }
        var canonical = value.CanonicalValue;
        var valid = value.Kind switch
        {
            EvidenceValueKind.Boolean => canonical is "true" or "false",
            EvidenceValueKind.WholeNumber => decimal.TryParse(canonical, NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture, out _),
            EvidenceValueKind.Real => double.TryParse(canonical, NumberStyles.Float,
                CultureInfo.InvariantCulture, out var number) && double.IsFinite(number),
            EvidenceValueKind.Identifier => Guid.TryParseExact(canonical, "D", out _),
            EvidenceValueKind.Timestamp => DateTimeOffset.TryParseExact(canonical, "O",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out _),
            _ => true,
        };
        if (!valid)
        {
            throw new InvalidDataException("A structured property has an invalid canonical representation.");
        }
    }
}
