using System.Collections.Immutable;
using Kora.Core.Commands;
using Kora.Core.Hosting;
using Kora.Core.Memory;

namespace Kora.Application.Memory;

/// <summary>A bounded, one-shot original-user review followed by sequential authoritative single-record actions.</summary>
internal sealed class MemoryBatchDisableWorkflow(IMemoryBatchDisableActions actions) : IDisposable
{
    private readonly Lock stateGate = new();
    private long revision;
    private MemoryBatchDisableReview? current;
    private Func<bool>? reviewAdmission;
    private bool disposed;
    private bool applying;

    internal void Retire()
    {
        lock (stateGate) { revision++; current = null; reviewAdmission = null; }
    }

    internal async Task<MemoryBatchDisableReview> ReviewAsync(HostId<SessionIdentity> session,
        IReadOnlyList<MemorySummary> selected, Func<bool> admitted, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(selected);
        ArgumentNullException.ThrowIfNull(admitted);
        session.Validate();
        long epoch;
        lock (stateGate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (applying) { throw new InvalidOperationException("A disable sequence is already applying."); }
            current = null;
            reviewAdmission = null;
            epoch = ++revision;
        }
        bool Eligible() => IsCurrent(epoch) && admitted() && !token.IsCancellationRequested;
        // Validate every selected identity before even the first inspection, never just the first row.
        var selection = new MemoryMetadataFilter().Apply(selected).ToImmutableArray();
        if (selection.IsEmpty || selection.Any(row => row.Scope != MemoryScope.Session(session)
            || row.Retention != MemoryRetentionState.Enabled || row.Review != MemoryReviewState.Admitted))
        {
            throw new InvalidDataException("Select a finite nonempty subset of enabled memories in one exact active session.");
        }
        var records = ImmutableArray.CreateBuilder<MemoryRecord>(selection.Length);
        foreach (var row in selection)
        {
            RequireEligible(Eligible, token);
            var record = await actions.InspectAsync(session, row, Eligible, token).ConfigureAwait(false);
            ValidateRecord(session, row, record);
            records.Add(record);
        }
        RequireEligible(Eligible, token);
        var review = new MemoryBatchDisableReview(session, records.ToImmutable());
        lock (stateGate)
        {
            RequireEligible(Eligible, token);
            current = review;
            reviewAdmission = () => admitted() && !token.IsCancellationRequested;
        }
        return review;
    }

    internal async Task<MemoryBatchDisableOutcome> ConfirmAsync(MemoryBatchDisableReview review,
        Func<bool> admitted, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(review);
        ArgumentNullException.ThrowIfNull(admitted);
        long epoch;
        Func<bool> originalAdmission;
        lock (stateGate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (applying || !ReferenceEquals(current, review))
            {
                throw new InvalidOperationException("The one-shot review was retired or consumed. Select and review again.");
            }
            current = null;
            originalAdmission = reviewAdmission
                ?? throw new InvalidOperationException("The original review admission is unavailable.");
            reviewAdmission = null;
            applying = true;
            epoch = revision;
        }
        bool Eligible() => IsCurrent(epoch) && originalAdmission() && admitted() && !token.IsCancellationRequested;
        var outcomes = review.Records.Select(record => new MemoryBatchDisableItem(
            record.Id.Value, record.Revision.Value, MemoryBatchDisableState.NotAttempted)).ToArray();
        var index = 0;
        MemoryResult? completed = null;
        var attempted = false;
        MemoryBatchDisableState? stoppedBy = null;
        try
        {
            // Full exact-record preflight prevents an invalid later row from causing any early disable.
            foreach (var record in review.Records)
            {
                RequireEligible(Eligible, token);
                var observed = await actions.InspectAsync(review.Session, Summary(record), Eligible, token).ConfigureAwait(false);
                if (observed != record)
                {
                    outcomes[index] = outcomes[index] with { State = MemoryBatchDisableState.Conflict };
                    return new([.. outcomes], MemoryBatchDisableState.Conflict);
                }
                index++;
            }
            index = 0;
            foreach (var record in review.Records)
            {
                completed = null;
                attempted = false;
                RequireEligible(Eligible, token);
                attempted = true;
                await actions.DisableAsync(review.Session, record, Eligible, result => completed = result, token)
                    .ConfigureAwait(false);
                outcomes[index] = Item(record, completed);
                if (outcomes[index].State != MemoryBatchDisableState.Succeeded)
                {
                    stoppedBy = outcomes[index].State;
                    break;
                }
                index++;
            }
        }
        catch (MemoryBatchDisableConflictException)
        {
            RecordFailure(MemoryBatchDisableState.Conflict);
        }
        catch (OperationCanceledException)
        {
            RecordFailure(attempted && completed is null
                ? MemoryBatchDisableState.Unavailable : MemoryBatchDisableState.Cancelled);
        }
        catch (Exception exception) when (exception is InvalidOperationException or InvalidDataException
            or IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException)
        {
            RecordFailure(MemoryBatchDisableState.Unavailable);
        }
        finally
        {
            lock (stateGate) { applying = false; }
        }
        return new([.. outcomes], stoppedBy);

        void RecordFailure(MemoryBatchDisableState failure)
        {
            stoppedBy = failure;
            if (index >= outcomes.Length) { return; }
            outcomes[index] = completed is null
                ? outcomes[index] with { State = failure }
                : Item(review.Records[index], completed);
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

    private static MemorySummary Summary(MemoryRecord record) =>
        new(record.Id.Value, record.Revision.Value, record.Scope, record.Review, record.Retention, record.CreatedAt);

    private static void ValidateRecord(HostId<SessionIdentity> session, MemorySummary row, MemoryRecord record)
    {
        if (record is null || Summary(record) != row || record.Revision.Value == long.MaxValue || record.Lineage is null
            || record.Lineage.Request is null || record.Lineage.Request.SessionId != session
            || record.Receipt is null
            || MemoryPolicy.Eligible(record, record.Receipt.Boundary, MemoryDestination.Local) != MemoryReason.None)
        {
            throw new InvalidDataException("The complete selected content, classification or reviewed lineage is invalid.");
        }
    }

    private static MemoryBatchDisableItem Item(MemoryRecord record, MemoryResult? result) =>
        new(record.Id.Value, record.Revision.Value, result switch
        {
            { Outcome: MemoryOutcome.Succeeded, Record: { } updated } when updated == (record with
            {
                Revision = new(checked(record.Revision.Value + 1)),
                Retention = MemoryRetentionState.Disabled,
            }) => MemoryBatchDisableState.Succeeded,
            { Outcome: MemoryOutcome.RevisionConflict or MemoryOutcome.InvalidTransition or MemoryOutcome.NotFound }
                => MemoryBatchDisableState.Conflict,
            { Outcome: MemoryOutcome.Cancelled } => MemoryBatchDisableState.Cancelled,
            _ => MemoryBatchDisableState.Unavailable,
        }, result);

    public void Dispose()
    {
        lock (stateGate) { disposed = true; revision++; current = null; reviewAdmission = null; }
        GC.SuppressFinalize(this);
    }
}
