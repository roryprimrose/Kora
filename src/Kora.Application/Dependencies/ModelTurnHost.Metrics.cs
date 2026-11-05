using Kora.Core.Diagnostics;
using System.Runtime.ExceptionServices;

namespace Kora.Application.Dependencies;

public sealed partial class ModelTurnHost
{
    private async Task<ModelProviderReply> RunMeasuredAdapterAsync(ModelTurn turn, CancellationToken token)
    {
        var started = time.GetTimestamp();
        var storage = HostActivity.CaptureContinuation(HostActivityLayer.Application, HostOperation.Storage);
        ModelProviderReply reply = null!;
        Exception? providerFailure = null;
        try
        {
            reply = await RunAdapterAsync(turn, token).ConfigureAwait(false);
        }
        catch (Exception exception) { providerFailure = exception; }
        try { await RecordUsageAsync(turn, reply, started, storage).ConfigureAwait(false); }
        catch (Exception storageFailure) when (providerFailure is not null)
        { throw new AggregateException(providerFailure, storageFailure); }
        if (providerFailure is not null) { ExceptionDispatchInfo.Throw(providerFailure); }
        return reply;
    }

    private async Task RecordUsageAsync(ModelTurn turn, ModelProviderReply? reply, long started, Func<HostActivity> storage)
    {
        using var observation = HostActivity.Current is { Activity.IsStopped: false, Outcome: HostOperationOutcome.Unknown }
            ? HostActivity.BeginChild(HostActivityLayer.Application, HostOperation.Storage) : storage();
        try
        {
            await metrics.RecordAsync(turn.Provenance, turn.Profile, reply, time.GetElapsedTime(started)).ConfigureAwait(false);
            observation.Complete(HostOperationOutcome.Completed);
        }
        catch
        {
            observation.Complete(HostOperationOutcome.Unknown);
            throw;
        }
    }
}
