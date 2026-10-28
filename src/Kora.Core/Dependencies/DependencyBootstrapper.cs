using Kora.Core.Diagnostics;

using Microsoft.Extensions.Logging;
namespace Kora.Core.Dependencies;

public sealed class DependencyBootstrapper(
    IEnumerable<IDependencyProbe> probes,
    ILogger<DependencyBootstrapper> logger,
    TimeProvider? timeProvider = null) : IDisposable
{
    private readonly IDependencyProbe[] probes = probes.ToArray();
    private readonly SemaphoreSlim probeGate = new(1, 1);
    private readonly Lock observationLock = new();
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;
    private IReadOnlyList<DependencyObservation> observations = Array.Empty<DependencyObservation>();

    public IReadOnlyList<DependencyObservation> Observations => Volatile.Read(ref observations);

    public void RecordObservation(DependencyStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        if (!Enum.IsDefined(status.Readiness))
        {
            throw new ArgumentOutOfRangeException(nameof(status));
        }
        lock (observationLock)
        {
            Volatile.Write(ref observations, Array.AsReadOnly(
                observations.Where(item => !string.Equals(item.Status.Id, status.Id, StringComparison.Ordinal))
                    .Append(new DependencyObservation(status, clock.GetUtcNow())).ToArray()));
        }
    }

    public SetupTaskLedger Tasks { get; } = new();

    public void Dispose() => probeGate.Dispose();

    public Task<IReadOnlyList<DependencyStatus>> ProbeAsync(CancellationToken cancellationToken = default) =>
        ProbeAsync(null, cancellationToken);

    public Task<IReadOnlyList<DependencyStatus>> ProbeWithinSetupAsync(
        SetupTask setupTask,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(setupTask);
        if (!ReferenceEquals(Tasks.ActiveTask, setupTask))
        {
            throw new InvalidOperationException("Verification requires the current running setup task.");
        }

        return ProbeAsync(setupTask, cancellationToken);
    }

    private async Task<IReadOnlyList<DependencyStatus>> ProbeAsync(
        SetupTask? setupTask,
        CancellationToken cancellationToken)
    {
        await probeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ValidateSetupTask(setupTask);
            return await ProbeCoreAsync(setupTask, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            probeGate.Release();
        }
    }

    private void ValidateSetupTask(SetupTask? setupTask)
    {
        if (setupTask is not null
            && !string.Equals(Tasks.ActiveTask?.Id, setupTask.Id, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The setup task no longer owns verification.");
        }
    }

    private async Task<IReadOnlyList<DependencyStatus>> ProbeCoreAsync(
        SetupTask? setupTask,
        CancellationToken cancellationToken)
    {
        // A new check invalidates old health; interrupted checks must not leave a ready runtime.
        lock (observationLock)
        {
            Volatile.Write(ref observations, Array.Empty<DependencyObservation>());
        }
        CoreLog.DependencyProbesStarting(logger, probes.Length);
        var results = new List<DependencyStatus>(probes.Length);

        foreach (var probe in probes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidateSetupTask(setupTask);
            var tracked = setupTask is null ? probe as ISetupDependencyProbe : null;
            if (tracked is not null)
            {
                Tasks.Start(tracked.TaskId, tracked.TaskName, $"Checking {tracked.TaskName}.");
            }

            DependencyStatus result;
            try
            {
                result = await probe.ProbeAsync(cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                ValidateSetupTask(setupTask);
                CoreLog.DependencyProbeCompleted(
                    logger,
                    result.Id,
                    result.Readiness);
                cancellationToken.ThrowIfCancellationRequested();
                ValidateSetupTask(setupTask);
            }
            catch (OperationCanceledException)
            {
                if (tracked is not null)
                {
                    Tasks.Update(tracked.TaskId, SetupTaskState.Cancelled, "Setup check was cancelled.");
                }

                throw;
            }
            catch (Exception exception)
            {
                if (tracked is not null)
                {
                    Tasks.Update(tracked.TaskId, SetupTaskState.Failed, exception.Message);
                }

                throw;
            }
            results.Add(result);
            RecordObservation(result);
            if (tracked is not null)
            {
                Tasks.Update(tracked.TaskId, result.Readiness switch
                {
                    DependencyReadiness.Ready => SetupTaskState.Completed,
                    DependencyReadiness.Failed => SetupTaskState.Failed,
                    _ => SetupTaskState.NeedsAction,
                }, result.Detail);
            }
        }

        CoreLog.DependencyProbesCompleted(logger);
        return results;
    }
}