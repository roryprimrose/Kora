using Kora.Application.UnitTests.Configuration;
using Kora.Core.Auditing;
using Kora.Core.Authorization;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Core.Storage;

using System.Collections.Immutable;

namespace Kora.Application.UnitTests.Interaction;

internal sealed class ExactGrantTestStore(AudioControlTestStore intents, ExactGrantInspection initial) : IExactGrantStore, IHostInteractionStore
{
    internal ExactGrantInspection? Current { get; set; } = initial;
    internal ExactGrantCursor? Next { get; set; }
    internal Action? BeforePage { get; set; }
    internal Action? BeforeRead { get; set; }
    internal Action? BeforeRevoke { get; set; }
    internal Action? AfterRevoke { get; set; }
    internal Exception? RevokeFailure { get; set; }
    internal Exception? ReadFailure { get; set; }
    internal Task<ExactGrantPage>? PendingPage { get; set; }
    internal Task<ExactGrantInspection?>? PendingRead { get; set; }
    internal bool Conflict { get; set; }
    internal int Revokes { get; private set; }
    internal HostRequest? RevokeRequest { get; private set; }
    internal SecurityAuditEvent? Audit { get; private set; }
    internal Func<bool>? CapturedAdmission { get; private set; }
    internal ImmutableArray<ExactGrantInspection> Additional { get; set; } = [];
    internal ExactGrantBatchDecision? BatchResult { get; set; }
    internal Action? BeforePreview { get; set; }
    internal ExactGrantBatch? PreviewResult { get; set; }
    internal Action? BeforeHistory { get; set; }
    internal ExactGrantHistoryPage? HistoryResult { get; set; }
    internal Task<ExactGrantHistoryPage>? PendingHistory { get; set; }
    internal HostRequest? HistoryViewer { get; private set; }
    internal Func<bool>? HistoryAdmission { get; private set; }
    internal ExactGrantQuery? LastQuery { get; private set; }
    internal ExactGrantCursor? LastCursor { get; private set; }
    internal CancellationToken PageToken { get; private set; }
    internal int Pages { get; private set; }
    internal int HistoryReads { get; private set; }
    internal ExactGrantHistoryQuery? HistoryQuery { get; private set; }
    internal long HistoryControlRevision { get; private set; }
    internal Guid HistoryAdmissionId { get; private set; }
    internal string? HistoryCursor { get; private set; }
    internal CancellationToken HistoryToken { get; private set; }

    public ValueTask<ExactGrantHistoryPage> ReadExactGrantHistoryAsync(ExactGrantInspection inspected, HostRequest viewer,
        ExactGrantHistoryQuery query, long controlRevision, Guid admissionId,
        string? cursor, Func<bool> admitted, CancellationToken cancellationToken)
    {
        HistoryReads++;
        HistoryQuery = query;
        HistoryControlRevision = controlRevision;
        HistoryAdmissionId = admissionId;
        HistoryCursor = cursor;
        HistoryToken = cancellationToken;
        HistoryViewer = viewer;
        HistoryAdmission = admitted;
        BeforeHistory?.Invoke();
        if (ReadFailure is { } failure) { throw failure; }
        return PendingHistory is { } pending ? new(pending)
            : ValueTask.FromResult(HistoryResult ?? new(Current, [], null, DateTimeOffset.UnixEpoch, 1, new string('a', 64), 1, 1, false));
    }

    public ValueTask<ExactGrantPage> ReadExactGrantPageAsync(ExactGrantCursor? cursor, CancellationToken cancellationToken)
        => ReadExactGrantPageAsync(ExactGrantQuery.Unfiltered, cursor, cancellationToken);
    public ValueTask<ExactGrantPage> ReadExactGrantPageAsync(ExactGrantQuery query, ExactGrantCursor? cursor, CancellationToken cancellationToken)
    {
        LastQuery = query;
        LastCursor = cursor;
        PageToken = cancellationToken;
        Pages++;
        BeforePage?.Invoke();
        return PendingPage is { } pending ? new(pending)
            : ValueTask.FromResult(new ExactGrantPage(Current is { } current ? [current, .. Additional] : Additional, Next,
                Additional.Length + (Current is null ? 0 : 1), ScannedThrough: Current?.Grant.Id.Value));
    }
    public ValueTask<ExactGrantInspection?> InspectExactGrantAsync(HostId<ApprovalIdentity> id, CancellationToken cancellationToken)
    {
        BeforeRead?.Invoke();
        if (ReadFailure is { } failure) { throw failure; }
        return PendingRead is { } pending ? new(pending)
            : ValueTask.FromResult(Current?.Grant.Id == id ? Current : Additional.FirstOrDefault(item => item.Grant.Id == id));
    }
    public ValueTask<WorkSessionAuthorization> CreateExactGrantControlSessionAsync(
        HostRequest request, Func<bool> admitted, CancellationToken cancellationToken) =>
        intents.CreateAudioControlSessionAsync(request, admitted, cancellationToken);
    public ValueTask<HostInteractionDecision> RevokeExactGrantAsync(HostRequest request, HostRevision controlGeneration,
        ExactGrantInspection preview, SecurityAuditEvent requested, Func<bool> admitted, CancellationToken cancellationToken)
    {
        RevokeRequest = request;
        Audit = requested;
        CapturedAdmission = admitted;
        BeforeRevoke?.Invoke();
        cancellationToken.ThrowIfCancellationRequested();
        if (!admitted()) { throw new InvalidOperationException("Admission changed."); }
        if (RevokeFailure is { } failure) { throw failure; }
        if (Conflict) { return ValueTask.FromResult(new HostInteractionDecision(HostInteractionOutcome.Conflict, "preview-changed")); }
        Revokes++;
        Current = preview with { Grant = preview.Grant.Revoke("explicit-user-revocation") };
        var committed = Current.Grant;
        AfterRevoke?.Invoke();
        return ValueTask.FromResult(new HostInteractionDecision(HostInteractionOutcome.Revoked, "explicit-user-revocation", Grant: committed));
    }
    public ValueTask<HostInteractionDecision> TransactAsync(HostRequest request,
        Func<HostInteractionSnapshot, HostInteractionCommit> transition, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Exact-grant test reads/revocation must never call an operation proposal or effect route.");

    public ValueTask<ExactGrantBatch> PreviewExactGrantBatchAsync(ExactGrantBatch selection, CancellationToken cancellationToken)
    {
        BeforePreview?.Invoke();
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(PreviewResult ?? selection with { PreviewId = Guid.NewGuid() });
    }

    public ValueTask<ExactGrantBatchDecision> RevokeExactGrantBatchAsync(HostRequest request, HostRevision controlGeneration,
        ExactGrantBatch preview, SecurityAuditEvent requested, Func<bool> admitted, CancellationToken cancellationToken)
    {
        RevokeRequest = request;
        Audit = requested;
        CapturedAdmission = admitted;
        BeforeRevoke?.Invoke();
        cancellationToken.ThrowIfCancellationRequested();
        if (!admitted()) { throw new InvalidOperationException("Admission changed."); }
        if (RevokeFailure is { } failure) { throw failure; }
        if (BatchResult is { } result) { return ValueTask.FromResult(result); }
        if (Conflict) { return ValueTask.FromResult(new ExactGrantBatchDecision(HostInteractionOutcome.Conflict, "preview-changed", [])); }
        var changed = preview.Records.Select(item => item with { Grant = item.Grant.Revoke("explicit-user-revocation") }).ToImmutableArray();
        Current = changed[0];
        Additional = changed.RemoveAt(0);
        Revokes++;
        AfterRevoke?.Invoke();
        return ValueTask.FromResult(new ExactGrantBatchDecision(HostInteractionOutcome.Revoked, "explicit-user-revocation",
            changed.Select(item => item.Grant).ToImmutableArray()));
    }
}