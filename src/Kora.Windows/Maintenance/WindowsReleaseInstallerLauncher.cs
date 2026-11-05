using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.Principal;
using Kora.Core.Maintenance;

namespace Kora.Windows.Maintenance;

public sealed class WindowsReleaseInstallerLauncher : IReleaseInstallerLauncher
{
    private readonly Func<bool> isInteractive;
    private readonly Action<ProcessStartInfo> start;

    public WindowsReleaseInstallerLauncher(Func<bool>? isInteractive = null, Action<ProcessStartInfo>? start = null)
    {
        this.isInteractive = isInteractive ?? IsInteractive;
        this.start = start ?? StartInstaller;
    }

    public async Task LaunchAsync(string path, ReleaseAsset installer, Func<bool> admitted, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Path.IsPathFullyQualified(path) || !string.Equals(Path.GetFileName(path), installer.Name, StringComparison.Ordinal)
            || !installer.Name.StartsWith("Kora-Setup-", StringComparison.Ordinal) || !installer.Name.EndsWith("-win-x64.exe", StringComparison.Ordinal))
        { throw new InvalidDataException("Only an exact canonical Kora installer file can be launched."); }
        if (!isInteractive())
        { throw new InvalidOperationException("Run the installer from a non-elevated interactive Kora session."); }
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var digest = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        if (stream.Length != installer.Bytes || !Convert.ToHexString(digest).Equals(installer.Sha256, StringComparison.OrdinalIgnoreCase))
        { throw new InvalidDataException("Installer identity changed before execution."); }
        cancellationToken.ThrowIfCancellationRequested();
        if (!admitted() || !isInteractive())
        { throw new InvalidOperationException("Upgrade approval or host admission changed before installer startup."); }
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            start(new ProcessStartInfo(path)
            {
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(path)!,
            });
        }
        catch (System.ComponentModel.Win32Exception exception)
        { throw new IOException("Windows could not start the reviewed installer.", exception); }
    }

    private static bool IsInteractive()
    {
        using var identity = WindowsIdentity.GetCurrent();
        using var process = Process.GetCurrentProcess();
        return Environment.UserInteractive && process.SessionId != 0
            && !new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static void StartInstaller(ProcessStartInfo info)
    {
        using var process = Process.Start(info)
            ?? throw new InvalidOperationException("Windows did not acknowledge the installer startup request.");
    }
}
