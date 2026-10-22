using System.Collections.Immutable;
using Kora.Application.Hosting;
using Kora.Core.Auditing;
using Kora.Core.Commands;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Memory;
using Kora.Core.Storage;
using Kora.Core.Tools;

namespace Kora.Application.Memory;

internal sealed class SessionMemoryContextSource(
    ISessionWorkspaceStore workspace, ISessionWorkspaceAccess access, ICapabilityHostAccess host,
    IMemoryStore store, ISecurityAuditLog audit) : ISessionMemoryContextSource
{
    private long revision;
    private bool closed;
    public long Revision => Interlocked.Read(ref revision);
    public event Action? Invalidated;
    internal void Invalidate()
    {
        Interlocked.Increment(ref revision);
        Invalidated?.Invoke();
    }
    internal void Close()
    {
        Volatile.Write(ref closed, true);
        Invalidate();
    }

    public async Task<SessionMemoryContextSnapshot> ReadAsync(SessionWorkspaceEntry target,
        ImmutableArray<MemorySummary> selection, Func<bool> admitted, CancellationToken token)
    {
        SessionMemoryContext.ValidateSelection(target, selection);
        var issuer = HostActivity.RequireCurrent();
        SessionConversationService.RequireOriginalSource(issuer.Request.Origin);
        if (issuer.Request.SessionId != target.Authority.SessionId)
        { throw new InvalidOperationException("Memory context requires the exact original session activity."); }
        var control = access.ControlRevision;
        bool Live() => !Volatile.Read(ref closed) && access.CanInspect && access.CanControl && host.IsCurrentHost
            && access.ControlRevision == control && !token.IsCancellationRequested && admitted()
            && !issuer.Activity!.IsStopped && issuer.Outcome == HostOperationOutcome.Unknown;
        var requested = new SecurityAuditEvent(Guid.NewGuid(), SecurityAuditCategory.ProtectedOperation,
            "memory.context.read", SecurityAuditOutcome.Requested, SecurityAuditInitiator.System,
            issuer.Request.TaskId.Value.ToString("D"));
        using var activity = HostActivity.BeginAudit(issuer.Request, requested);
        try
        {
            audit.Write(requested);
            if (!Live()) { throw new InvalidOperationException("Private memory context access is closed."); }
            var scopes = new SessionMemoryScopeAccess(store, access, host);
            var boundary = await scopes.ResolveAsync(issuer.Request, token).ConfigureAwait(false);
            if (MemoryPolicy.CheckBoundary(MemoryScope.Session(target.Authority.SessionId), boundary) != MemoryReason.None
                || boundary!.Generation != target.Authority.Generation
                || await workspace.ReadMetadataAsync(target.Authority.SessionId, token).ConfigureAwait(false) != target)
            { throw new InvalidOperationException("The exact memory session boundary changed."); }
            bool Current() => Live() && scopes.Observe(issuer.Request) == boundary;
            ImmutableArray<MemoryRecord> records = [];
            var observed = false;
            var expected = new MemoryResult(MemoryOutcome.Succeeded, MemoryReason.None);
            var result = await store.TransactMemoryAsync(issuer.Request, boundary, rows =>
            {
                if (!Current()) { throw new InvalidOperationException("Memory context source admission changed."); }
                if (rows.IsDefault || rows.Any(row => row is null))
                { throw new InvalidDataException("The authoritative memory snapshot is malformed."); }
                var selected = ImmutableArray.CreateBuilder<MemoryRecord>(selection.Length);
                foreach (var summary in selection)
                {
                    var matches = rows.Where(row => row.Id.Value == summary.Id).ToArray();
                    if (matches.Length != 1)
                    { throw new InvalidDataException("The exact selected retained memory is missing or duplicated."); }
                    var record = matches[0];
                    if (record.Scope != summary.Scope || record.Revision.Value != summary.Revision
                        || record.Review != summary.Review || record.Retention != summary.Retention
                        || record.CreatedAt != summary.CreatedAt || record.Lineage is null
                        || record.Lineage.Request is null || record.Receipt?.Boundary is null
                        || record.Lineage.Request.SessionId != target.Authority.SessionId
                        || MemoryPolicy.Eligible(record, record.Receipt.Boundary, MemoryDestination.Local) != MemoryReason.None
                        || MemoryPolicy.Eligible(record, boundary, MemoryDestination.Local) != MemoryReason.None)
                    { throw new InvalidDataException("The selected memory is stale, foreign or no longer eligible."); }
                    selected.Add(record);
                }
                records = selected.ToImmutable();
                observed = true;
                return new(expected, null, null);
            }, Current, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (!observed || result != expected || !Current()
                || await workspace.ReadMetadataAsync(target.Authority.SessionId, token).ConfigureAwait(false) != target
                || !Current())
            { throw new IOException("The authoritative memory context read is unconfirmed."); }
            audit.Write(requested.WithOutcome(SecurityAuditOutcome.Succeeded));
            if (!Current()) { throw new InvalidOperationException("Memory disclosure admission changed."); }
            activity.Complete(HostOperationOutcome.Completed);
            return new(boundary, records);
        }
        catch (OperationCanceledException)
        {
            audit.Write(requested.WithOutcome(SecurityAuditOutcome.Cancelled));
            activity.Complete(HostOperationOutcome.Cancelled);
            throw;
        }
        catch
        {
            audit.Write(requested.WithOutcome(SecurityAuditOutcome.Unknown));
            activity.Complete(HostOperationOutcome.Failed);
            throw;
        }
    }
}
