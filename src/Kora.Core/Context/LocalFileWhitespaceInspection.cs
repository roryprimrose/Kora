namespace Kora.Core.Context;

public static class LocalFileWhitespaceInspection
{
    public static LocalFileWhitespaceCounts Count(LocalFileRevision file, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);
        cancellationToken.ThrowIfCancellationRequested();
        // LocalFileRevision already owns admission and the original-byte budget. This
        // character scan neither decodes again nor interprets chunks as logical lines.
        var text = file.Text;
        var pairs = 0;
        var lf = 0;
        var cr = 0;
        var tabs = 0;
        var spaces = 0;
        checked
        {
            for (var index = 0; index < text.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var character = text[index];
                if (character == '\r')
                {
                    if (index + 1 < text.Length && text[index + 1] == '\n')
                    {
                        pairs++;
                        index++;
                    }
                    else { cr++; }
                }
                else if (character == '\n') { lf++; }
                else if (character == '\t') { tabs++; }
                else if (character == ' ') { spaces++; }
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        return new(pairs, lf, cr, tabs, spaces);
    }
}
