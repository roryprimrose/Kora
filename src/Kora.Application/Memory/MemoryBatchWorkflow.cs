using System.Collections.Immutable;
using Kora.Core.Commands;
using Kora.Core.Hosting;
using Kora.Core.Memory;

namespace Kora.Application.Memory;

/// <summary>A bounded, one-shot original-user review followed by sequential authoritative single-record actions.</summary>
internal sealed class MemoryBatchWorkflow(IMemoryBatchActions actions) : IDisposable
{
    private readonly Lock stateGate = new();
    private long revision;
    private MemoryBatchReview? current;
    private Func<bool> reviewAdmission = static () => false;
    private bool disposed;
    private bool applying;

    internal void Retire()
    {
        lock (stateGate)
        {
            revision++;
            current?.Retire();
            current = null;
            reviewAdmission = static () => false;
        }
    }

    internal Task<MemoryBatchReview> ReviewAsync(HostId<SessionIdentity> session,
        IReadOnlyList<MemorySummary> selected, Func<bool> admitted, CancellationToken token) =>
        ReviewAsync(MemoryBatchOperation.Disable, session, selected, admitted, token);

    internal async Task<MemoryBatchReview> ReviewAsync(MemoryBatchOperation operation, HostId<SessionIdentity> session,
        IReadOnlyList<MemorySummary> selected, Func<bool> admitted, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(selected);
        ArgumentNullException.ThrowIfNull(admitted);
        session.Validate();
        long epoch;
        lock (stateGate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (applying) { throw new InvalidOperationException("A memory sequence is already applying."); }
            current?.Retire();
            current = null;
            reviewAdmission = static () => false;
            epoch = ++revision;
        }
        MemoryBatchPolicy.ValidateOperation(operation);
        bool Eligible() => IsCurrent(epoch) && admitted() && !token.IsCancellationRequested;
        // Validate the entire bounded identity set before even the first inspection.
        var selection = new MemoryMetadataFilter().Apply(selected).ToImmutableArray();
        if (selection.IsEmpty || selection.Any(row => row.Scope != MemoryScope.Session(session)
            || !MemoryBatchPolicy.Eligible(operation, row)))
        {
            throw new InvalidDataException("Select a finite nonempty subset of admitted session memories: "
                + "enabled for disable, enabled or disabled for forget. No default select-all.");
        }
        var records = ImmutableArray.CreateBuilder<MemoryRecord>(selection.Length);
        foreach (var row in selection)
        {
            RequireEligible(Eligible, token);
            var record = await actions.InspectAsync(session, row, Eligible, token).ConfigureAwait(false);
            MemoryBatchPolicy.ValidateRecord(operation, session, row, record);
            records.Add(record);
        }
        RequireEligible(Eligible, token);
        var review = new MemoryBatchReview(operation, session, records.ToImmutable(), Eligible);
        lock (stateGate)
        {
            RequireEligible(Eligible, token);
            current = review;
            reviewAdmission = () => admitted() && !token.IsCancellationRequested;
        }
        return review;
    }

    internal async Task<MemoryBatchOutcome> ConfirmAsync(MemoryBatchReview review,
        Func<bool> admitted, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(review);
        ArgumentNullException.ThrowIfNull(admitted);
        long epoch;
        Func<bool> originalAdmission;
        ImmutableArray<MemoryRecord> records;
        lock (stateGate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (applying || !ReferenceEquals(current, review))
            {
                throw new InvalidOperationException("The one-shot review was retired or consumed. Select and review again.");
            }
            current = null;
            originalAdmission = reviewAdmission;
            reviewAdmission = static () => false;
            // The public receipt must no longer retain bodies once confirmation starts.
            records = review.Consume();
            if (records.IsEmpty)
            {
                throw new InvalidOperationException("The private review content was retired. Select and review again.");
            }
            applying = true;
            epoch = revision;
        }
        bool Eligible() => IsCurrent(epoch) && originalAdmission() && admitted() && !token.IsCancellationRequested;
        var outcomes = records.Select(record => new MemoryBatchItem(
            record.Id.Value, record.Revision.Value, MemoryBatchState.NotAttempted)).ToArray();
        var index = 0;
        MemoryResult? completed = null;
        var attempted = false;
        MemoryBatchState? stoppedBy = null;
        try
        {
            // Complete exact-record preflight prevents an invalid later row from causing any early mutation.
            foreach (var record in records)
            {
                RequireEligible(Eligible, token);
                var observed = await actions.InspectAsync(review.Session, MemoryBatchPolicy.Summary(record), Eligible, token)
                    .ConfigureAwait(false);
                if (observed != record)
                {
                    outcomes[index] = outcomes[index] with { State = MemoryBatchState.Conflict };
                    return new([.. outcomes], MemoryBatchState.Conflict);
                }
                index++;
            }
            index = 0;
            foreach (var record in records)
            {
                completed = null;
                attempted = false;
                RequireEligible(Eligible, token);
                attempted = true;
                await actions.ApplyAsync(review.Operation, review.Session, record, Eligible, result => completed = result, token)
                    .ConfigureAwait(false);
                outcomes[index] = Item(review.Operation, record, completed);
                if (outcomes[index].State != MemoryBatchState.Succeeded)
                {
                    stoppedBy = outcomes[index].State;
                    break;
                }
                index++;
            }
        }
        catch (MemoryBatchConflictException) { RecordFailure(MemoryBatchState.Conflict); }
        catch (OperationCanceledException)
        {
            RecordFailure(attempted && completed is null ? MemoryBatchState.Unavailable : MemoryBatchState.Cancelled);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            RecordFailure(MemoryBatchState.Unavailable);
        }
        finally
        {
            records = [];
            completed = null;
            lock (stateGate) { applying = false; }
        }
        return new([.. outcomes], stoppedBy);

        void RecordFailure(MemoryBatchState failure)
        {
            stoppedBy = failure;
            outcomes[index] = completed is null
                ? outcomes[index] with { State = failure }
                : Item(review.Operation, records[index], completed);
        }
    }

    private bool IsCurrent(long epoch)
    {
        lock (stateGate) { return !disposed && revision == epoch; }
    }

    private static void RequireEligible(Func<bool> eligible, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!eligible()) { throw new InvalidOperationException("Private exact-session review admission changed."); }
    }

    private static MemoryBatchItem Item(MemoryBatchOperation operation, MemoryRecord record, MemoryResult? result)
    {
        var state = result switch
        {
            { Outcome: MemoryOutcome.Succeeded, Record: { } updated }
                when updated == MemoryBatchPolicy.Expected(operation, record) => MemoryBatchState.Succeeded,
            { Outcome: MemoryOutcome.RevisionConflict or MemoryOutcome.InvalidTransition or MemoryOutcome.NotFound }
                => MemoryBatchState.Conflict,
            { Outcome: MemoryOutcome.Cancelled } => MemoryBatchState.Cancelled,
            _ => MemoryBatchState.Unavailable,
        };
        // Even malformed or uncertain readback cannot expose a removed body's prior value.
        if (operation == MemoryBatchOperation.Forget && result is not null)
        {
            result = result with { Record = state == MemoryBatchState.Succeeded ? result.Record : null, Use = null };
        }
        return new(record.Id.Value, record.Revision.Value, state, result);
    }

    public void Dispose()
    {
        lock (stateGate)
        {
            disposed = true;
            Retire();
        }
        GC.SuppressFinalize(this);
    }
}
