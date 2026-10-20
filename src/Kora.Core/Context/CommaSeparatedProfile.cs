using System.Text;

namespace Kora.Core.Context;

public static class CommaSeparatedProfile
{
    public const string Name = "Comma / double quote / CRLF or LF / literal spaces";
    private static readonly UTF8Encoding Encoding = new(false, true);

    private enum State { FieldStart, Unquoted, Quoted, Closed }

    public static CommaSeparatedInspection Inspect(string text, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(text);
        cancellationToken.ThrowIfCancellationRequested();
        if (text.Length > ClipboardSnapshot.MaximumUtf8Bytes
            || Encoding.GetByteCount(text) > ClipboardSnapshot.MaximumUtf8Bytes)
        {
            throw new InvalidDataException("The complete comma-separated source must fit the clipboard UTF-8 budget.");
        }
        if (text.Length == 0) { return new(CommaSeparatedSyntax.Empty, 0, 0, 0); }
        var state = State.FieldStart;
        var records = 0;
        var fields = 1;
        var minimum = int.MaxValue;
        var maximum = 0;
        var endedRecord = false;
        for (var offset = 0; offset < text.Length; offset++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var character = text[offset];
            endedRecord = false;
            if (state == State.Quoted)
            {
                if (character == '"') { state = State.Closed; }
                continue;
            }
            if (character == '"')
            {
                if (state is State.FieldStart or State.Closed) { state = State.Quoted; }
                else { return Invalid(CommaSeparatedSyntax.InvalidQuoting, offset, records); }
            }
            else if (character == ',')
            {
                fields = checked(fields + 1);
                state = State.FieldStart;
            }
            else if (character is '\r' or '\n')
            {
                if (character == '\r')
                {
                    if (offset + 1 == text.Length || text[offset + 1] != '\n')
                    { return Invalid(CommaSeparatedSyntax.InvalidRecordSeparator, offset, records); }
                    offset++;
                }
                CompleteRecord();
                endedRecord = true;
                state = State.FieldStart;
            }
            else
            {
                if (state == State.Closed)
                { return Invalid(CommaSeparatedSyntax.InvalidQuoting, offset, records); }
                state = State.Unquoted;
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (state == State.Quoted) { return Invalid(CommaSeparatedSyntax.InvalidQuoting, text.Length, records); }
        if (!endedRecord) { CompleteRecord(); }
        return new(CommaSeparatedSyntax.Admitted, records, minimum, maximum);

        void CompleteRecord()
        {
            records = checked(records + 1);
            minimum = Math.Min(minimum, fields);
            maximum = Math.Max(maximum, fields);
            fields = 1;
        }
    }

    private static CommaSeparatedInspection Invalid(CommaSeparatedSyntax syntax, int offset, int records) =>
        new(syntax, 0, 0, 0, offset, checked(records + 1));
}
