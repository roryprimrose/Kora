using System.ComponentModel;

using Microsoft.Win32.SafeHandles;

namespace Kora.Windows.Coordination;

internal sealed class InstallerShutdownPeer : IDisposable
{
    private readonly SafeProcessHandle process;
    private readonly FileStream image;

    private InstallerShutdownPeer(SafeProcessHandle process, FileStream image, string path)
    {
        this.process = process;
        this.image = image;
        Path = path;
    }

    internal string Path { get; }
    internal bool IsAlive => CoordinationNative.WaitForSingleObject(process, 0) switch
    {
        258 => true,
        0 => false,
        _ => throw new InvalidOperationException("The setup caller's lifetime cannot be verified."),
    };

    internal static InstallerShutdownPeer Open(int processId, string sid, int sessionId)
    {
        var processHandle = CoordinationNative.OpenProcess(
            CoordinationNative.QueryLimitedInformation | CoordinationNative.Synchronize, false, processId);
        FileStream? imageFile = null;
        try
        {
            if (processHandle.IsInvalid) { throw new Win32Exception(); }
            var token = CoordinationNative.ReadProcessToken(processHandle);
            if (!string.Equals(token.Sid, sid, StringComparison.Ordinal) || token.SessionId != sessionId)
            {
                throw new InvalidOperationException("Setup must run non-elevated in the owner's interactive user session.");
            }
            imageFile = new FileStream(CoordinationNative.ReadImagePath(processHandle), FileMode.Open, FileAccess.Read, FileShare.Read);
            var path = CoordinationNative.CanonicalPath(imageFile.SafeFileHandle);
            if (!System.IO.Path.GetFileName(path).Equals("Kora.Setup.exe", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("The caller is not the supported setup host.");
            }
            var peer = new InstallerShutdownPeer(processHandle, imageFile, path);
            if (!peer.IsAlive) { throw new InvalidOperationException("The setup caller exited."); }
            return peer;
        }
        catch
        {
            imageFile?.Dispose();
            processHandle.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        image.Dispose();
        process.Dispose();
    }
}
