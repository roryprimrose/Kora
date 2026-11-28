using System.Security.Cryptography;

namespace Kora.Windows.Storage;

internal sealed partial class RestrictedSqliteDatabase
{
    internal string ReadIdentity()
    {
        VerifyFiles();
        using var handle = File.OpenHandle(databasePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return WindowsFileIdentity.Read(handle);
    }

    // Binds the verified file identity to the exact database and journal bytes. Equal receipts mean
    // an unchanged file whose deterministic open/validation outcome is already known.
    internal string ReadContentReceipt()
    {
        VerifyFiles();
        using var database = new FileStream(databasePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var journal = new FileStream(journalPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return string.Join('|', WindowsFileIdentity.Read(database.SafeFileHandle),
            Convert.ToHexString(SHA256.HashData(database)), Convert.ToHexString(SHA256.HashData(journal)));
    }

    internal byte[] ComputeDatabaseDigest()
    {
        VerifyFiles();
        using var stream = new FileStream(
            databasePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite,
            bufferSize: 1024 * 1024,
            FileOptions.SequentialScan);
        var digest = SHA256.HashData(stream);
        VerifyFiles();
        return digest;
    }
}
