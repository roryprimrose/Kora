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
                switch (text[index])
                {
                    case '\r':
                        if (index + 1 < text.Length && text[index + 1] == '\n')
                        {
                            pairs++;
                            index++;
                        }
                        else { cr++; }
                        break;
                    case '\n': lf++; break;
                    case '\t': tabs++; break;
                    case ' ': spaces++; break;
                }
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        return new(pairs, lf, cr, tabs, spaces);
    }
}
