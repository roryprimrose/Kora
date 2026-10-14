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
