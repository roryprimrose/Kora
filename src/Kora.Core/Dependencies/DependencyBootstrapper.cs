using Kora.Core.Diagnostics;

using Microsoft.Extensions.Logging;
namespace Kora.Core.Dependencies;

public sealed class DependencyBootstrapper(
    IEnumerable<IDependencyProbe> probes,
    ILogger<DependencyBootstrapper> logger) : IDisposable
{
    private readonly IDependencyProbe[] probes = probes.ToArray();
    private readonly SemaphoreSlim probeGate = new(1, 1);

    public SetupTaskLedger Tasks { get; } = new();

    public void Dispose() => probeGate.Dispose();

    public async Task<IReadOnlyList<DependencyStatus>> ProbeAsync(CancellationToken cancellationToken = default)
    {
        await probeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await ProbeCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            probeGate.Release();
        }
    }

    private async Task<IReadOnlyList<DependencyStatus>> ProbeCoreAsync(CancellationToken cancellationToken)
    {
        CoreLog.DependencyProbesStarting(logger, probes.Length);
        var results = new List<DependencyStatus>(probes.Length);

        foreach (var probe in probes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (probe is ISetupDependencyProbe setup)
            {
                Tasks.Start(setup.TaskId, setup.TaskName, $"Checking {setup.TaskName}.");
            }

            DependencyStatus result;
            try
            {
                result = await probe.ProbeAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                if (probe is ISetupDependencyProbe cancelled)
                {
                    Tasks.Update(cancelled.TaskId, SetupTaskState.Cancelled, "Setup check was cancelled.");
                }

                throw;
            }
            catch (Exception exception)
            {
                if (probe is ISetupDependencyProbe failed)
                {
                    Tasks.Update(failed.TaskId, SetupTaskState.Failed, exception.Message);
                }

                throw;
            }
            CoreLog.DependencyProbeCompleted(
                logger,
                result.Id,
                result.Readiness);
            results.Add(result);
            if (probe is ISetupDependencyProbe tracked)
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