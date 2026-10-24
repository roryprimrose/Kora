using System.Collections.Immutable;
using Kora.Core.Commands;
using Kora.Core.Hosting;
using Kora.Core.Memory;
using Kora.Core.Storage;

namespace Kora.Application.Memory;

internal sealed class SessionMemoryContextReview : IDisposable
{
    private readonly ISessionMemoryContextSource source;
    private readonly Func<bool> admitted;
    private readonly TimeProvider time;
    private readonly DateTimeOffset createdAt;
    private readonly DateTimeOffset expiresAt;
    private readonly CancellationTokenRegistration cancellation;
    private ITimer? expiry;
    private readonly CancellationTokenSource revocation = new();
    private readonly CancellationToken revocationToken;
    private readonly Lock stateGate = new();
    private SessionMemoryContextSnapshot? snapshot;
    private string body;
    private bool consumed;
    private bool retired;

    internal SessionMemoryContextReview(SessionWorkspaceEntry target, ImmutableArray<MemorySummary> selection,
        SessionMemoryContextSnapshot snapshot, string body, RequestOrigin origin, Func<bool> admitted,
        ISessionMemoryContextSource source, TimeProvider time, CancellationToken token)
    {
        Target = target;
        Selection = selection;
        Origin = origin;
        this.snapshot = snapshot;
        this.body = body;
        this.source = source;
        this.admitted = admitted;
        this.time = time;
        createdAt = time.GetUtcNow();
        expiresAt = createdAt.AddMinutes(2);
        revocationToken = revocation.Token;
        source.Invalidated += Dispose;
        try
        {
            cancellation = token.Register(Dispose);
            expiry = time.CreateTimer(_ => Dispose(), null, TimeSpan.FromMinutes(2), Timeout.InfiniteTimeSpan);
            if (retired) { expiry.Dispose(); expiry = null; }
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    internal SessionWorkspaceEntry Target { get; }
    internal ImmutableArray<MemorySummary> Selection { get; }
    internal RequestOrigin Origin { get; }
    internal event Action? Retired;
    internal CancellationToken Revocation => revocationToken;
    internal bool Current
    {
        get
        {
            lock (stateGate)
            {
                var now = time.GetUtcNow();
                if (!retired && now >= createdAt && now < expiresAt && admitted()) { return true; }
                Dispose();
                return false;
            }
        }
    }
    internal ImmutableArray<MemoryRecord> Records
    {
        get { lock (stateGate) { return !consumed && Current ? snapshot!.Records : []; } }
    }
    internal string UserRequest
    {
        get { lock (stateGate) { return !consumed && Current ? body : string.Empty; } }
    }
    internal (SessionMemoryContextSnapshot Snapshot, string Body) ReadCurrent()
    {
        lock (stateGate)
        {
            if (!Current) { throw new InvalidOperationException("The private memory review is no longer current."); }
            return (snapshot!, body);
        }
    }
    internal bool TryConsume()
    {
        lock (stateGate)
        {
            if (consumed || !Current) { return false; }
            consumed = true;
            return true;
        }
    }

    public void Dispose()
    {
        lock (stateGate)
        {
            if (retired) { return; }
            retired = true;
            snapshot = null;
            body = string.Empty;
        }
        source.Invalidated -= Dispose;
        expiry?.Dispose();
        expiry = null;
        cancellation.Unregister();
        // Cancel callbacks without retaining private source bodies. The execution owner joins its work.
        revocation.Cancel();
        revocation.Dispose();
        var notify = Retired;
        Retired = null;
        notify?.Invoke();
    }
}
