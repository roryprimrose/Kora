using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Kora.Windows.Storage;

internal sealed partial class RestrictedSqliteDatabase
{
    internal string ReadPreviewStamp()
    {
        var identity = ReadIdentity();
        var file = new FileInfo(databasePath);
        var journal = new FileInfo(journalPath);
        using var stream = new FileStream(databasePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        Span<byte> header = stackalloc byte[100];
        stream.ReadExactly(header);
        if (!header[..16].SequenceEqual("SQLite format 3\0"u8))
        {
            throw new InvalidDataException("The preview store has an unknown SQLite header.");
        }
        // Includes SQLite's committed change counter/version-valid-for pair, not only filesystem timestamps.
        var stamp = string.Join(':', identity, file.Length.ToString(CultureInfo.InvariantCulture),
            file.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture),
            journal.Length.ToString(CultureInfo.InvariantCulture),
            journal.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture), Convert.ToHexString(header));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(stamp)));
    }
}
