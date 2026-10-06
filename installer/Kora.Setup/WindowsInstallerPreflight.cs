using System.ComponentModel;
using System.Runtime.InteropServices;
using Kora.Core.Dependencies;
using Kora.Windows.Audio;
using Kora.Windows.Dependencies;
using Microsoft.Extensions.Logging;

namespace Kora.Setup;

internal sealed class WindowsInstallerPreflight(
    ILogger<WindowsInstallerPreflight> logger,
    ILogger<KokoroTextToSpeechProvider> kokoroLogger, string productVersion) : IInstallerPreflight
{
    public async Task<InstallerPreflightResult> ProbeAsync(CancellationToken cancellationToken)
    {
        var powerShell = InspectAsync("PowerShell", async () =>
        {
            var result = await new WindowsPowerShellSetupService().ProbeAsync(cancellationToken);
            return new InstallerDependencyStatus(result.Readiness switch
            {
                DependencyReadiness.Ready => InstallerDependencyState.Installed,
                DependencyReadiness.Missing => InstallerDependencyState.Missing,
                DependencyReadiness.Incompatible => InstallerDependencyState.UpdateRequired,
                _ => InstallerDependencyState.Failed,
            }, result.Detail);
        }, cancellationToken);
        var ollama = InspectAsync("Ollama", async () =>
        {
            using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false });
            using var setup = new WindowsOllamaSetupService(client);
            var result = await setup.InspectAsync(cancellationToken);
            return new InstallerDependencyStatus(result.State switch
            {
                OllamaInstallationState.Missing => InstallerDependencyState.Missing,
                OllamaInstallationState.NotRunning => InstallerDependencyState.NotRunning,
                OllamaInstallationState.ModelMissing => InstallerDependencyState.NeedsPreparation,
                OllamaInstallationState.ModelDetected => InstallerDependencyState.Detected,
                OllamaInstallationState.Incompatible => InstallerDependencyState.Incompatible,
                _ => InstallerDependencyState.Failed,
            }, result.Detail);
        }, cancellationToken);
        var kokoro = InspectAsync("Kokoro", () => Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var client = new HttpClient();
            using var provider = new KokoroTextToSpeechProvider(
                new ApplicationDataPaths(developmentBuild: false), client, kokoroLogger);
            var result = provider.IsInstalled
                ? new InstallerDependencyStatus(InstallerDependencyState.Detected,
                    "Pinned model/voice installation records and assets were found. Hash records are not a fresh content/loader check; loading and verification require approval.")
                : provider.HasLocalAssets
                    ? new InstallerDependencyStatus(InstallerDependencyState.NeedsPreparation,
                        "Existing app-managed assets are incomplete or do not match the pinned installation records. Consented repair/preparation is required.")
                    : new InstallerDependencyStatus(InstallerDependencyState.Missing,
                        "No Kokoro assets were found in this user's production Kora speech store.");
            cancellationToken.ThrowIfCancellationRequested();
            return result;
        }, cancellationToken), cancellationToken);
        var currentUserStartup = InspectStartupAsync(InstallScope.CurrentUser, cancellationToken);
        var allUsersStartup = InspectStartupAsync(InstallScope.AllUsers, cancellationToken);
        await Task.WhenAll(powerShell, ollama, kokoro, currentUserStartup, allUsersStartup);
        return new(await powerShell, await ollama, await kokoro, await currentUserStartup, await allUsersStartup);
    }

    private async Task<InstallerStartupStatus> InspectStartupAsync(InstallScope scope, CancellationToken cancellationToken)
    {
        try
        {
            var result = await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return WindowsStartupRegistrationProbe.Inspect(scope, productVersion);
            }, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!result.CanConfigure)
            {
                InstallerLog.StartupReported(logger, scope, result.State, result.Detail);
            }
            return result;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException
            or UnauthorizedAccessException or System.Security.SecurityException or COMException)
        {
            InstallerLog.ComponentFailed(logger, "Startup registration", exception);
            return new(InstallerStartupState.Failed, exception.Message);
        }
    }

    private async Task<InstallerDependencyStatus> InspectAsync(string component,
        Func<Task<InstallerDependencyStatus>> inspect, CancellationToken cancellationToken)
    {
        try
        {
            var result = await inspect();
            if (result.State is InstallerDependencyState.Failed or InstallerDependencyState.Incompatible)
            {
                InstallerLog.Reported(logger, component, result.State, result.Detail);
            }
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException
            or InvalidOperationException or HttpRequestException or Win32Exception or TimeoutException)
        {
            InstallerLog.ComponentFailed(logger, component, exception);
            return new(InstallerDependencyState.Failed, exception.Message);
        }
    }
}
