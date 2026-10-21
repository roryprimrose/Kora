using System.Text;

namespace Kora.Core.Context;

public sealed record ClipboardTextStatistics(
    int Utf8Bytes, int Utf16CodeUnits, int UnicodeScalars, int WhitespaceDelimitedWords, int LogicalLines)
{
    public static ClipboardTextStatistics Measure(ClipboardSnapshot snapshot, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        cancellationToken.ThrowIfCancellationRequested();
        var scalars = 0;
        var words = 0;
        var lines = 1;
        var inWord = false;
        var previousWasCarriageReturn = false;
        foreach (var rune in snapshot.Text.EnumerateRunes())
        {
            cancellationToken.ThrowIfCancellationRequested();
            scalars++;
            var whitespace = Rune.IsWhiteSpace(rune);
            if (!whitespace && !inWord) { words++; }
            inWord = !whitespace;
            if (rune.Value is '\r' or 0x85 or 0x2028 or 0x2029
                || rune.Value == '\n' && !previousWasCarriageReturn)
            {
                lines++;
            }
            previousWasCarriageReturn = rune.Value == '\r';
        }
        cancellationToken.ThrowIfCancellationRequested();
        return new(snapshot.Utf8Bytes, snapshot.Text.Length, scalars, words, lines);
    }
}
