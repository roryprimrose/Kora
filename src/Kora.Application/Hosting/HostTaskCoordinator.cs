using Kora.Core.Hosting;
using Kora.Core.Diagnostics;
using Kora.Core.Storage;

namespace Kora.Application.Hosting;

public sealed class HostTaskCoordinator(IHostTaskStore store)
{
    public async ValueTask<HostTaskRecord> RecordIntentAsync(
        HostRequest request, CancellationToken cancellationToken)
    {
        var record = new HostTaskRecord(request, new HostRevision(1), HostTaskState.IntentRecorded);
        await CommitAsync(record, 0, cancellationToken).ConfigureAwait(false);
        return record;
    }

    public async ValueTask<HostTaskRecord> RecordDispatchAsync(
        HostTaskRecord intent, CancellationToken cancellationToken)
    {
        var record = intent.Next(HostTaskState.DispatchRecorded);
        await CommitAsync(record, intent.Revision.Value, cancellationToken).ConfigureAwait(false);
        return record;
    }

    public async ValueTask<HostTaskRecord> RecordOutcomeAsync(
        HostTaskRecord prior, HostTaskState outcome, CancellationToken cancellationToken)
    {
        var record = prior.Next(outcome);
        if (!record.IsTerminal)
        {
            throw new InvalidOperationException("A receipt must describe a terminal outcome.");
        }
        await CommitAsync(record, prior.Revision.Value, cancellationToken).ConfigureAwait(false);
        return record;
    }

    public async ValueTask<IReadOnlyList<HostTaskRecord>> RecoverAsync(
        int limit, CancellationToken cancellationToken)
    {
        if (limit is <= 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(limit));
        }
        var incomplete = await store.ReadIncompleteAsync(limit, cancellationToken).ConfigureAwait(false);
        if (incomplete.Count > limit)
        {
            throw new InvalidDataException("The store exceeded the recovery batch limit.");
        }
        var recovered = new List<HostTaskRecord>(incomplete.Count);
        foreach (var prior in incomplete)
        {
            if (prior.IsTerminal)
            {
                throw new InvalidDataException("The incomplete-work query returned a terminal task.");
            }
            var record = prior.Recover();
            using var recovery = HostActivity.BeginRoot(record.Request, HostActivityLayer.Application, HostOperation.Recovery);
            await CommitAsync(record, prior.Revision.Value, cancellationToken).ConfigureAwait(false);
            recovery.Complete(HostOperationOutcome.Completed);
            recovered.Add(record);
        }
        return recovered;
    }

    private async ValueTask CommitAsync(HostTaskRecord record, long expectedRevision, CancellationToken cancellationToken)
    {
        if (HostActivity.RequireCurrent().Request != record.Request)
        {
            throw new InvalidOperationException("The host activity does not own this request.");
        }
        using var boundary = HostActivity.BeginChild(HostActivityLayer.Application, HostOperation.Storage);
        try
        {
            await store.CommitAsync(record, expectedRevision, cancellationToken).ConfigureAwait(false);
            boundary.Complete(HostOperationOutcome.Completed);
        }
        catch (OperationCanceledException)
        {
            boundary.Complete(HostOperationOutcome.Cancelled);
            throw;
        }
        catch
        {
            boundary.Complete(HostOperationOutcome.Failed);
            throw;
        }
    }
}
