using System.Buffers;
using System.Globalization;
using System.Text;

namespace Kora.Core.Hosting;

/// <summary>User content only; never an authority key or diagnostic label.</summary>
public sealed record SessionName
{
    public const int MaximumScalars = 120;
    public const int MaximumUtf8Bytes = 480;

    public SessionName(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (string.IsNullOrWhiteSpace(value) || !string.Equals(value, value.Trim(), StringComparison.Ordinal))
        {
            throw new InvalidDataException("A session name must be nonblank with no surrounding whitespace.");
        }
        var remaining = value.AsSpan();
        var scalars = 0;
        while (!remaining.IsEmpty)
        {
            if (Rune.DecodeFromUtf16(remaining, out var rune, out var consumed) != OperationStatus.Done
                || Rune.GetUnicodeCategory(rune) is UnicodeCategory.Control or UnicodeCategory.Format
                    or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator)
            {
                throw new InvalidDataException("A session name must contain valid single-line Unicode without control or format characters.");
            }
            scalars++;
            remaining = remaining[consumed..];
        }
        if (Encoding.UTF8.GetByteCount(value) > MaximumUtf8Bytes || scalars > MaximumScalars
            || !value.IsNormalized(NormalizationForm.FormC))
        {
            throw new InvalidDataException("A session name must be NFC Unicode within 120 scalars and 480 UTF-8 bytes.");
        }
        Value = value;
    }

    public string Value { get; }
}
