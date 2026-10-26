namespace Kora.Core.Network.ExternalBrowser;

/// <summary>Accepted means Windows accepted a launch request, not that a page loaded.</summary>
public interface IExternalBrowserLauncher
{
    Task<ExternalBrowserLaunchOutcome> LaunchAsync(
        ExternalBrowserDestination destination, Func<bool> admitted, CancellationToken cancellationToken);
}
