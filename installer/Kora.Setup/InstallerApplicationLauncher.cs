using Microsoft.Extensions.Logging;

namespace Kora.Setup;

public sealed class InstallerApplicationLauncher(
    IInstallerLaunchPlatform platform, string productVersion, string executableHash, string assemblyHash,
    ILogger<InstallerApplicationLauncher> logger) : IInstallerApplicationLauncher
{
    public async Task LaunchAsync(InstallScope scope, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Enum.IsDefined(scope)) { throw new ArgumentOutOfRangeException(nameof(scope)); }
        if (!platform.IsInteractiveNonElevated)
        {
            throw new InvalidOperationException("Kora must be launched by a non-elevated interactive user. Launch it normally from the Start menu.");
        }
        var executable = platform.ExecutablePath(scope, productVersion);
        var directory = Path.GetDirectoryName(executable) ?? throw new InvalidDataException("Installed application directory is unknown.");
        await VerifyAsync(executable, executableHash, cancellationToken);
        await VerifyAsync(Path.Combine(directory, "Kora.dll"), assemblyHash, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (!platform.IsInteractiveNonElevated)
        {
            throw new InvalidOperationException("The interactive launch context is no longer eligible.");
        }
        platform.Start(executable);
        InstallerLog.ApplicationLaunchRequested(logger, scope);
    }

    private async Task VerifyAsync(string path, string expected, CancellationToken cancellationToken)
    {
        if (expected.Length != 64 || expected.Any(character => !char.IsAsciiHexDigit(character)))
        {
            throw new InvalidDataException("Installer application identity is invalid.");
        }
        var actual = await platform.Sha256Async(path, cancellationToken);
        if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The installed application does not match this installer. Use Repair before launching Kora.");
        }
    }
}
