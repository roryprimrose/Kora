using Kora.Core.Authorization;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Core.Storage;

namespace Kora.Application.UnitTests.Configuration;

internal sealed class AudioControlTestStore : ISessionWorkspaceStore, IAudioControlSessionStore, IMaintenanceControlSessionStore,
    IDiagnosticRetentionSessionStore, IHostTaskStore
{
    public ValueTask<HostTaskObservation?> ReadTaskAsync(HostId<SessionIdentity> session, HostId<TaskIdentity> task,
        CancellationToken cancellationToken) => throw new NotSupportedException();
    public ValueTask<HostTaskObservation> CancelWaitingTaskAsync(HostRequest control, HostTaskCancellationTarget target,
        Func<bool> canControl, CancellationToken cancellationToken) => throw new NotSupportedException();
    internal WorkSessionAuthorization? Authority { get; set; }
    internal List<HostTaskRecord> Tasks { get; } = [];
    internal Action? BeforeOperation { get; set; }
    internal Action? AfterOperation { get; set; }
    internal Exception? CreateFailure { get; set; }
    internal Task<WorkSessionAuthorization>? PendingCreation { get; set; }
    internal bool FailTerminal { get; set; }
    internal Action<HostTaskRecord>? BeforeCommit { get; set; }
    internal HostRequest? LastRequest { get; private set; }
    public ValueTask<WorkSessionAuthorization> CreateDiagnosticRetentionSessionAsync(HostRequest request,
        Func<bool> admitted, CancellationToken cancellationToken) => CreateAudioControlSessionAsync(request, admitted, cancellationToken);
    public ValueTask<T> WithDiagnosticRetentionSessionAsync<T>(HostRequest request, HostRevision generation,
        Func<T> operation, CancellationToken cancellationToken) => WithAudioControlSessionAsync(request, generation, operation, cancellationToken);

    public ValueTask<WorkSessionAuthorization> CreateMaintenanceControlSessionAsync(HostRequest request,
        Func<bool> admitted, CancellationToken cancellationToken) => CreateAudioControlSessionAsync(request, admitted, cancellationToken);
    public ValueTask<T> WithMaintenanceControlSessionAsync<T>(HostRequest request, HostRevision generation,
        Func<T> operation, CancellationToken cancellationToken) => WithAudioControlSessionAsync(request, generation, operation, cancellationToken);

    public ValueTask<HostTaskRecord> RecordControlIntentAsync(HostRequest request, CancellationToken cancellationToken)
    {
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
