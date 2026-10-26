using System.Runtime.InteropServices;
using Kora.Core.Network.ExternalBrowser;
using Windows.System;

namespace Kora.Windows.Network.ExternalBrowser;

public sealed class WindowsExternalBrowserLauncher : IExternalBrowserLauncher
{
    private readonly Func<Uri, Task<bool>> launch;

    public WindowsExternalBrowserLauncher() : this(LaunchRegisteredBrowserAsync) { }

    internal WindowsExternalBrowserLauncher(Func<Uri, Task<bool>> launch) => this.launch = launch;

    public async Task<ExternalBrowserLaunchOutcome> LaunchAsync(
        ExternalBrowserDestination destination, Func<bool> admitted, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(admitted);
        if (cancellationToken.IsCancellationRequested) { return ExternalBrowserLaunchOutcome.Cancelled; }
        if (!admitted()) { return ExternalBrowserLaunchOutcome.Denied; }
        try
        {
            return await launch(destination.Address).ConfigureAwait(false)
                ? ExternalBrowserLaunchOutcome.Accepted : ExternalBrowserLaunchOutcome.Failed;
        }
        catch (Exception exception) when (exception is COMException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            return ExternalBrowserLaunchOutcome.Unknown;
        }
    }

    private static async Task<bool> LaunchRegisteredBrowserAsync(Uri address) =>
        await Launcher.LaunchUriAsync(address);
}
