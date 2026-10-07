using System.Collections.ObjectModel;
using System.Globalization;
using Kora.Core.Diagnostics;

namespace Kora.Application.Diagnostics;

internal static class EvidenceProperties
{
    private const int MaximumProperties = 32;
    private const int MaximumStringLength = 1024;

    internal static IReadOnlyDictionary<string, EvidenceValue> Capture(object? state)
    {
        if (state is not IEnumerable<KeyValuePair<string, object?>> values)
        {
            throw new InvalidDataException("Structured named logging state is required.");
        }
        var properties = new Dictionary<string, EvidenceValue>(StringComparer.Ordinal);
        foreach (var pair in values)
        {
            if (string.Equals(pair.Key, "{OriginalFormat}", StringComparison.Ordinal))
            {
                continue;
            }
            if (properties.Count >= MaximumProperties || pair.Key.Length > 128)
            {
                throw new InvalidDataException("Structured logging state exceeds its bounds.");
            }
            if (!properties.TryAdd(pair.Key, EvidenceFieldPolicy.IsSensitive(pair.Key)
                ? new EvidenceValue(EvidenceValueKind.Text, "[redacted]")
                : CaptureValue(pair.Value)))
            {
                throw new InvalidDataException("Structured logging state contains duplicate names.");
            }
        }
        return new ReadOnlyDictionary<string, EvidenceValue>(properties);
    }

    internal static string Template(object? state)
    {
        if (state is IEnumerable<KeyValuePair<string, object?>> values)
        {
            foreach (var pair in values)
            {
                if (string.Equals(pair.Key, "{OriginalFormat}", StringComparison.Ordinal) && pair.Value is string template
                    && template.Length <= 4096)
                {
                    return template;
                }
            }
        }
        throw new InvalidDataException("A bounded original logging template is required.");
    }

    private static EvidenceValue CaptureValue(object? value) => value switch
    {
        null => new(EvidenceValueKind.Null, null),
        bool boolean => new(EvidenceValueKind.Boolean, boolean ? "true" : "false"),
        byte or sbyte or short or ushort or int or uint or long or ulong =>
            new(EvidenceValueKind.WholeNumber, ((IFormattable)value).ToString(null, CultureInfo.InvariantCulture)),
        decimal number => new(EvidenceValueKind.Real, number.ToString(CultureInfo.InvariantCulture)),
        float number when float.IsFinite(number) =>
            new(EvidenceValueKind.Real, number.ToString("R", CultureInfo.InvariantCulture)),
        double number when double.IsFinite(number) =>
            new(EvidenceValueKind.Real, number.ToString("R", CultureInfo.InvariantCulture)),
        string text when text.Length <= MaximumStringLength => new(EvidenceValueKind.Text, text),
        Guid guid => new(EvidenceValueKind.Identifier, guid.ToString("D")),
        DateTimeOffset timestamp => new(EvidenceValueKind.Timestamp, timestamp.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)),
        Enum enumeration => new(EvidenceValueKind.Text, enumeration.ToString()),
        _ => throw new InvalidDataException("The property has no admitted safe projection."),
    };
}
