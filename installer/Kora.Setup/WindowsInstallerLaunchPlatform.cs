using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.Principal;

namespace Kora.Setup;

internal sealed class WindowsInstallerLaunchPlatform : IInstallerLaunchPlatform
{
    public bool IsInteractiveNonElevated
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            using var process = Process.GetCurrentProcess();
            return identity.User is not null && Environment.UserInteractive && process.SessionId != 0
                && !new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    public string ExecutablePath(InstallScope scope, string productVersion) =>
        WindowsInstalledApplicationPaths.Executable(scope, productVersion);

    public async Task<string> Sha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
    }

    public void Start(string executable)
    {
        if (!IsInteractiveNonElevated)
        {
            throw new InvalidOperationException("Cannot launch Kora from an elevated or non-interactive installer.");
        }
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(executable)
                ?? throw new InvalidDataException("Installed application directory is unknown."),
        };
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Windows did not create the Kora process.");
    }
}
