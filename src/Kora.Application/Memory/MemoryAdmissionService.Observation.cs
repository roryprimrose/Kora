using System.Collections.Immutable;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Memory;

namespace Kora.Application.Memory;

internal sealed partial class MemoryAdmissionService
{
    internal void ClearSessionCache(HostId<SessionIdentity> session)
    {
        lock (stateGate)
        {
            lifecycleRevision++;
            foreach (var id in records.Values.Where(row => row.Scope.Kind == MemoryScopeKind.Session
                && row.Scope.Identity == session.Value).Select(row => row.Id).ToArray()) { records.Remove(id); }
        }
    }

    internal async Task<ImmutableArray<MemoryRecord>> ObserveSessionAsync(CancellationToken token)
    {
        var issuer = HostActivity.RequireCurrent();
        using var activity = HostActivity.BeginChild(HostActivityLayer.Application, HostOperation.Storage);
        try
        {
            var control = access.ControlRevision;
            long lifecycle;
            lock (stateGate) { lifecycle = lifecycleRevision; }
            var boundary = await scopes.ResolveAsync(issuer.Request, token).ConfigureAwait(false);
            if (MemoryPolicy.CheckBoundary(MemoryScope.Session(issuer.Request.SessionId), boundary) != MemoryReason.None)
            {
                throw new InvalidOperationException("The private session memory boundary is closed.");
            }
            var session = await workspace.ReadMetadataAsync(issuer.Request.SessionId, token).ConfigureAwait(false);
            bool Eligible() => !token.IsCancellationRequested && lifecycle == lifecycleRevision
                && HostEligible(control, issuer) && scopes.Observe(issuer.Request) == boundary
                && session.Authority.SessionId == boundary!.Session && session.Authority.IsActive
                && session.Authority.Generation == boundary.Generation;
            ImmutableArray<MemoryRecord> snapshot = [];
            var observed = false;
            void Observe(ImmutableArray<MemoryRecord> rows)
            {
                lock (stateGate)
                {
                    token.ThrowIfCancellationRequested();
                    if (!Eligible()) { throw new InvalidOperationException("Memory inspection admission changed."); }
                    foreach (var row in rows)
                    {
                        if (row.Scope.Kind != MemoryScopeKind.Session || row.Scope.Identity != boundary!.Session.Value)
                        {
                            throw new InvalidDataException("Memory storage returned a foreign scope.");
                        }
                        if (!records.TryGetValue(row.Id, out var cached) || DurableProjection(cached) != row)
                        {
                            records[row.Id] = row;
                        }
                    }
                    snapshot = [.. records.Values.Where(row => row.Scope.Kind == MemoryScopeKind.Session
                        && row.Scope.Identity == boundary!.Session.Value
                        && (row.Retention == MemoryRetentionState.Forgotten
                            || MemoryPolicy.CheckLineage(row, boundary) == MemoryReason.None))
                        .OrderBy(row => row.Id.Value)];
                    observed = true;
                }
            }
            if (storage is not null)
            {
                var result = await storage.TransactMemoryAsync(issuer.Request, boundary!, rows =>
                {
                    Observe(rows);
                    return new(new(MemoryOutcome.Succeeded, MemoryReason.None), null, null);
                }, Eligible, token).ConfigureAwait(false);
                if (!observed || result.Outcome != MemoryOutcome.Succeeded)
                {
                    throw new InvalidOperationException("The authoritative memory observation was not confirmed.");
                }
            }
            else { Observe([]); }
            lock (stateGate)
            {
                token.ThrowIfCancellationRequested();
                if (!Eligible()) { throw new InvalidOperationException("Memory inspection admission changed before publication."); }
            }
            activity.Complete(HostOperationOutcome.Completed);
            return snapshot;
        }
        catch (OperationCanceledException)
        {
            activity.Complete(HostOperationOutcome.Cancelled);
            throw;
        }
        catch (Exception exception)
        {
            Failure(logger, exception.GetType().Name);
            activity.Complete(HostOperationOutcome.Failed);
            throw;
        }
    }
}
