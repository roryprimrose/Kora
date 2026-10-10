using Kora.Application.UnitTests.Configuration;
using Kora.Core.Auditing;
using Kora.Core.Authorization;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Core.Storage;

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

    public ValueTask<ExactGrantPage> ReadExactGrantPageAsync(ExactGrantCursor? cursor, CancellationToken cancellationToken)
    {
        BeforePage?.Invoke();
        return PendingPage is { } pending ? new(pending)
            : ValueTask.FromResult(new ExactGrantPage(Current is { } current ? [current] : [], Next));
    }
    public ValueTask<ExactGrantInspection?> InspectExactGrantAsync(HostId<ApprovalIdentity> id, CancellationToken cancellationToken)
    {
        BeforeRead?.Invoke();
        if (ReadFailure is { } failure) { throw failure; }
        return PendingRead is { } pending ? new(pending) : ValueTask.FromResult(Current);
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
}
