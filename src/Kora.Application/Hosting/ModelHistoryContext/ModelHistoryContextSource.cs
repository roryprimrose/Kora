using Kora.Core.Conversation;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.Hosting.ModelHistoryContext;

/// <summary>Host-bound advisory lineage, never evidence or renewable history consent.</summary>
public sealed class ModelHistoryContextSource : IDisposable
{
    private readonly SessionConversationService owner;
    private readonly Func<bool> current;
    private readonly TimeProvider time;
    private readonly DateTimeOffset createdAt;
    private int disposed;
    private int consumed;

    internal ModelHistoryContextSource(SessionConversationService owner, SessionWorkspaceEntry target,
        ConversationMessage message, Func<bool> current, TimeProvider time)
    {
        this.owner = owner;
        Target = target;
        Message = message;
        this.current = current;
        this.time = time;
        createdAt = time.GetUtcNow();
        ExpiresAt = createdAt.AddSeconds(60);
    }

    internal SessionWorkspaceEntry Target { get; }
    internal ConversationMessage Message { get; }
    public string OriginalQuestion => IsCurrent ? Message.Body : string.Empty;
    public RequestOrigin Origin => Message.Request.Origin;
    internal DateTimeOffset ExpiresAt { get; }
    public bool IsCurrent
    {
        get
        {
            if (Volatile.Read(ref disposed) != 0) { return false; }
            if (!current() || time.GetUtcNow() < createdAt || time.GetUtcNow() >= ExpiresAt)
            { Dispose(); return false; }
            return true;
        }
    }

    internal void RequireOwner(SessionConversationService service, bool reviewing = false)
    {
        if (!ReferenceEquals(owner, service) || !IsCurrent || reviewing && Volatile.Read(ref consumed) != 0)
        { throw new InvalidOperationException("The exact model history advisory is expired, revoked, consumed or owned elsewhere."); }
    }

    internal void Consume(SessionConversationService service)
    {
        RequireOwner(service);
        if (Interlocked.Exchange(ref consumed, 1) != 0)
        { throw new InvalidOperationException("This model history advisory already admitted its one confirmation."); }
    }

    public void Dispose() => Interlocked.Exchange(ref disposed, 1);
}
