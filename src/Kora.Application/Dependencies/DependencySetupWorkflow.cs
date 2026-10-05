using System.ComponentModel;
using System.Text.Json;

using Kora.Core.Dependencies;

namespace Kora.Application.Dependencies;

public sealed class DependencySetupWorkflow(
    DependencyBootstrapper bootstrapper,
    ILocalModelSetup localModelSetup,
    IPowerShellSetup powerShellSetup)
{
    public const string LocalModelTaskId = "local.inference";

    public string PowerShellTaskId => powerShellSetup.TaskId;

    public async Task<LocalModelSetupResult> InstallLocalModelAsync(
        IProgress<LocalModelSetupProgress> progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(progress);
        var tasks = bootstrapper.Tasks;
        tasks.Start(
            LocalModelTaskId,
            "Local model inference (Ollama)",
            "Preparing approved Ollama and model installation.");
        try
        {
            await localModelSetup.InstallAsync(progress, cancellationToken);
            tasks.Update(
                LocalModelTaskId,
                SetupTaskState.Completed,
                "Validating local inference readiness.");
            var statuses = await bootstrapper.ProbeAsync(cancellationToken);
            var inference = statuses.Single(status =>
                string.Equals(status.Id, LocalModelTaskId, StringComparison.Ordinal));
            if (inference.Readiness != DependencyReadiness.Ready)
            {
                throw new InvalidOperationException(
                    $"Local inference did not pass the readiness check: {inference.Detail}");
            }

            tasks.Update(
                LocalModelTaskId,
                SetupTaskState.Completed,
                inference.Detail);
            return new LocalModelSetupResult(statuses, inference);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            tasks.Update(
                LocalModelTaskId,
                SetupTaskState.Cancelled,
                "Local model setup was cancelled.");
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or InvalidOperationException or HttpRequestException or JsonException or Win32Exception)
        {
            tasks.Update(
                LocalModelTaskId,
                SetupTaskState.Failed,
                exception.Message);
            throw;
        }
    }

    public async Task<DependencyStatus> InstallPowerShellAsync(
        IProgress<string> progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(progress);
        var tasks = bootstrapper.Tasks;
        tasks.Start(
            powerShellSetup.TaskId,
            powerShellSetup.TaskName,
            "Checking the approved PowerShell 7 installation.");
        try
        {
            const string installing =
                "Installing or reusing PowerShell 7 for this Windows user.";
            progress.Report(installing);
            tasks.Update(
                powerShellSetup.TaskId,
                SetupTaskState.Running,
                installing);
            await powerShellSetup.InstallAsync(cancellationToken);

            const string verifying =
                "Verifying PowerShell 7 without a user profile.";
            progress.Report(verifying);
            tasks.Update(
                powerShellSetup.TaskId,
                SetupTaskState.Running,
                verifying);
            var status = await powerShellSetup.ProbeAsync(cancellationToken);
            if (status.Readiness != DependencyReadiness.Ready)
            {
                throw new InvalidOperationException(
                    $"PowerShell setup did not pass readiness verification: {status.Detail}");
            }

            progress.Report(status.Detail);
            tasks.Update(
                powerShellSetup.TaskId,
                SetupTaskState.Completed,
                status.Detail);
            return status;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            const string cancelled = "PowerShell installation was cancelled.";
            progress.Report(cancelled);
            tasks.Update(
                powerShellSetup.TaskId,
                SetupTaskState.Cancelled,
                cancelled);
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or InvalidOperationException or TimeoutException or Win32Exception)
        {
            progress.Report(exception.Message);
            tasks.Update(
                powerShellSetup.TaskId,
                SetupTaskState.Failed,
                exception.Message);
            throw;
        }
    }
}