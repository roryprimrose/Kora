using System.ComponentModel;
using System.Diagnostics;
using Kora.Core.Maintenance;

namespace Kora.Windows.Maintenance;

public sealed class WindowsReleasePageOpener : ICanonicalReleasePageOpener
{
    private readonly Action<ProcessStartInfo> start;
    public WindowsReleasePageOpener(Action<ProcessStartInfo>? start = null) =>
        this.start = start ?? StartBrowser;

    public Task OpenAsync(ReleaseVersion version, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(version);
        cancellationToken.ThrowIfCancellationRequested();
        var canonicalVersion = ReleaseVersion.Parse(version.ToString());
        try
        {
            start(new ProcessStartInfo(CanonicalRelease.Page(canonicalVersion).AbsoluteUri)
            {
                UseShellExecute = true,
                Verb = "open",
            });
        }
        catch (Win32Exception exception)
        {
            throw new InvalidOperationException("Windows could not request the canonical release page in the browser.", exception);
        }
        return Task.CompletedTask;
    }

    private static void StartBrowser(ProcessStartInfo info)
    {
        using var process = Process.Start(info);
        if (process is null) { throw new InvalidOperationException("Windows did not accept the browser navigation request."); }
    }
}
