namespace Kora.Windows.Storage;

internal sealed partial class RestrictedSqliteDatabase
{
    internal string ReadIdentity()
    {
        VerifyFiles();
        using var handle = File.OpenHandle(databasePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return WindowsFileIdentity.Read(handle);
    }

}
