using System.ComponentModel;
using Microsoft.Extensions.Logging;

namespace Kora.Setup;

public sealed class InstallerPreflightState(
    IInstallerPreflight probe, ILogger<InstallerPreflightState> logger) : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public bool IsChecking { get; private set; }
    public bool IsComplete { get; private set; }
    public InstallerPreflightResult Result { get; private set; } = InstallerPreflightResult.Checking;

    public async Task CheckAsync(CancellationToken cancellationToken)
    {
        if (IsChecking)
        {
            throw new InvalidOperationException("A dependency check is already running.");
        }
        IsChecking = true;
        IsComplete = false;
        Result = InstallerPreflightResult.Checking;
        Notify();
        try
        {
            var result = await Task.Run(() => probe.ProbeAsync(cancellationToken), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (result is null) { throw new InvalidDataException("Dependency detection returned no result."); }
            result.ValidateCompleted();
            Result = result;
            IsComplete = true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or InvalidOperationException
            or UnauthorizedAccessException or HttpRequestException or TimeoutException)
        {
            InstallerLog.PreflightFailed(logger, exception);
            var failure = new InstallerDependencyStatus(InstallerDependencyState.Failed, exception.Message);
            var startup = new InstallerStartupStatus(InstallerStartupState.Failed, exception.Message);
            Result = new(failure, failure, failure, startup, startup);
            IsComplete = true;
        }
        finally
        {
            IsChecking = false;
            Notify();
        }
    }

    public void ValidateSelection(OptionalComponents selection, InstallScope scope = InstallScope.CurrentUser,
        bool startAtLogin = false)
    {
        if (!IsComplete)
        {
            throw new InvalidOperationException("Wait for dependency detection before approving installation.");
        }
        Result.ValidateSelection(selection, scope, startAtLogin);
    }

    private void Notify() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
}
