using Kora.Application.Hosting;
using Kora.Core.Auditing;
using Kora.Core.Commands;
using Kora.Core.Hosting;
using Kora.Core.Memory;
using Kora.Core.Storage;
using Kora.Core.Tools;
using Microsoft.Extensions.Logging;

namespace Kora.Application.Memory;

/// <summary>Original-user management only. No proposal, use, model tool or disclosure entry point.</summary>
internal sealed class MemoryManagementService : IAsyncDisposable
{
    private readonly SessionWorkspaceService sessions;
    private readonly ManagementAccess access;
    internal MemoryAdmissionService Admission { get; }
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly CancellationTokenSource lifetime = new();
    private readonly Lock stateGate = new();
    private readonly Dictionary<HostId<MemoryIdentity>, (MemoryRecord Record, long Control)> inspections = [];
    private bool disposed;
    private Task? disposal;

    public MemoryManagementService(SessionWorkspaceService sessions, ISessionWorkspaceStore workspace,
        ISessionWorkspaceAccess access, ICapabilityHostAccess host, IMemoryStore store,
        ISecurityAuditLog audit, ILogger<MemoryAdmissionService> logger, TimeProvider time)
    {
        this.sessions = sessions;
        this.access = new(access);
        Admission = new(workspace, this.access, host, new SessionMemoryScopeAccess(store, this.access, host),
            audit, logger, time, store);
        sessions.SessionRetired += ExpireSession;
        sessions.SessionLifecycleChanged += ClearSession;
    }

    internal void ClearInspection()
    {
        lock (stateGate) { inspections.Clear(); }
    }

    internal void ExpireSession(HostId<SessionIdentity> session)
    {
        Admission.ExpireSession(session);
        lock (stateGate)
        {
            foreach (var id in inspections.Where(pair => pair.Value.Record.Scope.Identity == session.Value)
                .Select(pair => pair.Key).ToArray()) { inspections.Remove(id); }
        }

        private void ClearSession(HostId<SessionIdentity> session)
        {
            Admission.ClearSessionCache(session);
            ClearInspection();
        }
    }

    internal async Task<MemoryCommandResult> ExecuteAsync(MemoryCommand command, RequestOrigin origin,
        Func<bool> admission, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(admission);
        if (Kora.Core.Diagnostics.HostActivity.Current is { } current) { origin = current.Request.Origin; }
        if (origin is not (RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice))
        {
            throw new InvalidOperationException("Memory management requires fresh original local user input.");
        }
        if (command.Operation == MemoryCommandOperation.Invalid) { throw new InvalidOperationException(command.Error); }
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token);
        token = linked.Token;
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            var control = access.ControlRevision;
            bool Eligible() => !disposed && !token.IsCancellationRequested && admission()
                && access.Underlying.CanControl && access.ControlRevision == control;
            access.Admitted = Eligible;
            if (!Eligible()) { throw new InvalidOperationException("Private memory management admission is unavailable."); }
            if (command.Operation == MemoryCommandOperation.Help)
            {
                return new("observed", MemoryCommand.Syntax);
            }
            var session = new HostId<SessionIdentity>(command.SessionId
                ?? throw new InvalidOperationException("An exact active session ID is required."));
            session.Validate();
            return await sessions.ExecuteMemoryControlAsync(session, origin, Eligible, async (_, eligible) =>
            {
                var rows = await Admission.ObserveSessionAsync(token).ConfigureAwait(false);
                MemoryCommandResult result;
                if (command.Operation == MemoryCommandOperation.List)
                {
                    result = new("observed", "Exact active-session metadata only; no recall, proposal or disclosure.")
                    { Memories = [.. rows.Select(Summary)] };
                }
                else
                {
                    var id = new HostId<MemoryIdentity>(command.MemoryId
                        ?? throw new InvalidOperationException("An exact memory ID is required."));
                    id.Validate();
                    var revision = new HostRevision(command.Revision);
                    var record = rows.FirstOrDefault(row => row.Id == id);
                    if (record is null) { result = new("NotFound", "No such memory belongs to the exact active session."); }
                    else if (record.Revision != revision) { result = new("RevisionConflict", "Inspect the exact current revision before a fresh action."); }
                    else if (command.Operation == MemoryCommandOperation.Inspect)
                    {
                        lock (stateGate) { inspections[id] = (record, control); }
                        result = new("observed", "Exact content and classification for original-user review; inspection does not review or admit.")
                        { Memories = [Summary(record)], Inspected = record };
                    }
                    else
                    {
                        if (command.Operation == MemoryCommandOperation.Review)
                        {
                            lock (stateGate)
                            {
                                if (!inspections.TryGetValue(id, out var inspected) || inspected.Record != record
                                    || inspected.Control != control)
                                {
                                    throw new InvalidOperationException("Inspect this exact candidate and classification under current private admission before review.");
                                }
                            }
                        }
                        var mutation = command.Operation switch
                        {
                            MemoryCommandOperation.Review => Admission.ReviewAsync(id, revision, command.Accept, token),
                            MemoryCommandOperation.Admit => Admission.AdmitAsync(id, revision, token),
                            MemoryCommandOperation.Edit => Admission.EditAsync(id, revision, command.Candidate, token),
                            MemoryCommandOperation.Disable => Admission.DisableAsync(id, revision, token),
                            MemoryCommandOperation.Forget => Admission.ForgetAsync(id, revision, token),
                            _ => throw new InvalidOperationException("Unsupported memory management operation."),
                        };
                        var changed = await mutation.ConfigureAwait(false);
                        lock (stateGate) { inspections.Remove(id); }
                        result = new(changed.Outcome.ToString(), changed.Reason.ToString())
                        { Memories = changed.Record is { } updated ? [Summary(updated)] : [] };
                    }
                }
                token.ThrowIfCancellationRequested();
                if (!eligible()) { throw new InvalidOperationException("Memory management admission changed before presentation. Inspect durable state before retrying; no rollback is claimed."); }
                _ = MemoryCommandResult.Serialize(result);
                return result;
            }, token).ConfigureAwait(false);
        }
        finally { access.Admitted = static () => false; gate.Release(); }
    }

    private static MemorySummary Summary(MemoryRecord record) =>
        new(record.Id.Value, record.Revision.Value, record.Scope, record.Review, record.Retention, record.CreatedAt);

    public ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        lock (stateGate) { return new(disposal ??= CloseAsync()); }
    }

    private async Task CloseAsync()
    {
        disposed = true;
        sessions.SessionRetired -= ExpireSession;
        sessions.SessionLifecycleChanged -= ClearSession;
        Admission.Dispose();
        ClearInspection();
        await lifetime.CancelAsync().ConfigureAwait(false);
        await gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        gate.Dispose();
        lifetime.Dispose();
    }

    private sealed class ManagementAccess(ISessionWorkspaceAccess underlying) : ISessionWorkspaceAccess
    {
        internal ISessionWorkspaceAccess Underlying => underlying;
        internal Func<bool> Admitted { get; set; } = static () => false;
        public bool CanInspect => underlying.CanInspect && Admitted();
        public bool CanControl => underlying.CanControl && Admitted();
        public long ControlRevision => underlying.ControlRevision;
    }
}
