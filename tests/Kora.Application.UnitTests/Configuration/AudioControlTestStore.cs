using Kora.Core.Authorization;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Core.Storage;
using Kora.Core.Communication;

namespace Kora.Application.UnitTests.Configuration;

internal sealed class AudioControlTestStore : ISessionWorkspaceStore, IAudioControlSessionStore, IMaintenanceControlSessionStore,
    IDiagnosticRetentionSessionStore, IAuditRetentionSessionStore, ISharedSkillSessionStore, IManualCallControlStore, IHostTaskStore
{
    public ValueTask<HostTaskObservation?> ReadTaskAsync(HostId<SessionIdentity> session, HostId<TaskIdentity> task,
        CancellationToken cancellationToken) => throw new NotSupportedException();
    public ValueTask<HostTaskObservation> CancelWaitingTaskAsync(HostRequest control, HostTaskCancellationTarget target,
        Func<bool> canControl, CancellationToken cancellationToken) => throw new NotSupportedException();
    internal WorkSessionAuthorization? Authority { get; set; }
    public ValueTask<WorkSessionAuthorization> CreateSharedSkillSessionAsync(HostRequest request,
        Func<bool> admitted, CancellationToken cancellationToken) => CreateAudioControlSessionAsync(request, admitted, cancellationToken);
    public ValueTask<T> WithSharedSkillSessionAsync<T>(HostRequest request, HostRevision generation,
        Func<T> operation, CancellationToken cancellationToken) => WithAudioControlSessionAsync(request, generation, operation, cancellationToken);
    internal List<HostTaskRecord> Tasks { get; } = [];
    internal Action? BeforeOperation { get; set; }
    internal Action? AfterOperation { get; set; }
    internal Exception? CreateFailure { get; set; }
    internal Task<WorkSessionAuthorization>? PendingCreation { get; set; }
    internal bool FailTerminal { get; set; }
    internal Action<HostTaskRecord>? BeforeCommit { get; set; }
    internal Action<HostRequest>? BeforeControlIntent { get; set; }
    internal HostRequest? LastRequest { get; private set; }
    public ValueTask<WorkSessionAuthorization> CreateDiagnosticRetentionSessionAsync(HostRequest request,
        Func<bool> admitted, CancellationToken cancellationToken) => CreateAudioControlSessionAsync(request, admitted, cancellationToken);
    public ValueTask<T> WithDiagnosticRetentionSessionAsync<T>(HostRequest request, HostRevision generation,
        Func<T> operation, CancellationToken cancellationToken) => WithAudioControlSessionAsync(request, generation, operation, cancellationToken);
    public ValueTask<WorkSessionAuthorization> CreateAuditRetentionSessionAsync(HostRequest request,
        Func<bool> admitted, CancellationToken cancellationToken) => CreateAudioControlSessionAsync(request, admitted, cancellationToken);
    public ValueTask<T> WithAuditRetentionSessionAsync<T>(HostRequest request, HostRevision generation,
        Func<T> operation, CancellationToken cancellationToken) => WithAudioControlSessionAsync(request, generation, operation, cancellationToken);
    internal bool FailRequestedAudit { get; set; }
    internal bool FailOutcomeAudit { get; set; }
    internal bool ForeignManualContext { get; set; }

    public ValueTask<WorkSessionAuthorization> CreateManualCallControlSessionAsync(HostRequest request,
        Func<bool> admitted, CancellationToken cancellationToken) => CreateAudioControlSessionAsync(request, admitted, cancellationToken);

    public async ValueTask<CallMutationOutcome> ApplyManualCallAsync(HostRequest request, HostRevision generation,
        bool active, Func<bool> admitted, Func<Task<CallMutationOutcome>> transition, CancellationToken cancellationToken)
    {
        var operation = await WithAudioControlSessionAsync(request, generation, async () =>
        {
            if (!admitted()) { throw new InvalidOperationException("Manual admission changed."); }
            if (FailRequestedAudit) { throw new IOException("required requested audit failed"); }
            using var foreign = ForeignManualContext
                ? HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.HostSystem), HostActivityLayer.Application, HostOperation.Request) : null;
            var outcome = await transition();
            if (!ReferenceEquals(HostActivity.RequireCurrent().Request, request) || HostActivity.RequireCurrent().Activity!.IsStopped)
            {
                throw new InvalidOperationException("Lost manual context.");
            }
            if (FailOutcomeAudit) { throw new IOException("required outcome audit failed"); }
            return outcome;
        }, cancellationToken);
        return await operation;
    }

    public ValueTask<WorkSessionAuthorization> CreateMaintenanceControlSessionAsync(HostRequest request,
        Func<bool> admitted, CancellationToken cancellationToken) => CreateAudioControlSessionAsync(request, admitted, cancellationToken);
    public ValueTask<T> WithMaintenanceControlSessionAsync<T>(HostRequest request, HostRevision generation,
        Func<T> operation, CancellationToken cancellationToken) => WithAudioControlSessionAsync(request, generation, operation, cancellationToken);

    public ValueTask<HostTaskRecord> RecordControlIntentAsync(HostRequest request, CancellationToken cancellationToken)
    {
        BeforeControlIntent?.Invoke(request);
        var record = new HostTaskRecord(request, new(1), HostTaskState.IntentRecorded);
        Tasks.Add(record);
        return ValueTask.FromResult(record);
    }

    public ValueTask<WorkSessionAuthorization> CreateAudioControlSessionAsync(HostRequest request, Func<bool> admitted,
        CancellationToken cancellationToken)
    {
        if (CreateFailure is { } failure) { throw failure; }
        if (!ReferenceEquals(HostActivity.RequireCurrent().Request, request) || !admitted())
        {
            throw new InvalidOperationException("No admitted input.");
        }
        Authority = new(request.SessionId, new(1), true);
        return PendingCreation is { } pending ? new(pending.WaitAsync(cancellationToken)) : ValueTask.FromResult(Authority);
    }

    public ValueTask<T> WithAudioControlSessionAsync<T>(HostRequest request, HostRevision generation, Func<T> operation,
        CancellationToken cancellationToken)
    {
        LastRequest = request;
        BeforeOperation?.Invoke();
        if (!ReferenceEquals(HostActivity.RequireCurrent().Request, request) || Authority is null
            || Authority.SessionId != request.SessionId || Authority.Generation != generation || !Authority.IsActive)
        {
            throw new InvalidOperationException("No current durable session/generation.");
        }
        var result = operation();
        AfterOperation?.Invoke();
        return ValueTask.FromResult(result);
    }

    public ValueTask CommitAsync(HostTaskRecord record, long expectedRevision, CancellationToken cancellationToken)
    {
        BeforeCommit?.Invoke(record);
        if (FailTerminal && record.IsTerminal) { throw new IOException("terminal receipt failed"); }
        Tasks.Add(record);
        return ValueTask.CompletedTask;
    }

    public ValueTask<IReadOnlyList<HostTaskRecord>> ReadIncompleteAsync(int limit, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<HostTaskRecord>>(Tasks.Where(record => !record.IsTerminal).ToArray());
    public ValueTask<SessionPage<WorkSessionAuthorization>> ReadSessionsAsync(Guid? after, int limit, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
    public ValueTask<SessionPage<SessionWorkspaceEntry>> ReadMetadataPageAsync(Guid? after, int limit, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
    public ValueTask<SessionWorkspaceEntry> ReadMetadataAsync(HostId<SessionIdentity> session, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
    public ValueTask<SessionWorkspaceEntry> CreateNamedSessionAsync(HostRequest request, SessionName name, Func<bool> canControl, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
    public ValueTask<SessionWorkspaceEntry> RenameSessionAsync(HostRequest request, HostRevision expectedGeneration, long expectedMetadataRevision, SessionName name, Func<bool> canControl, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
    public ValueTask<SessionPage<HostQuestionRecord>> ReadQuestionPageAsync(HostId<SessionIdentity> session, Guid? after, int limit, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
    public ValueTask<SessionPage<HostTaskRecord>> ReadTaskPageAsync(HostId<SessionIdentity> session, Guid? after, int limit, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
    public ValueTask<WorkSessionAuthorization> ChangeIdleLifecycleAsync(HostRequest request, HostRevision expectedGeneration, bool active, Func<bool> canControl, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
}
