using Kora.Core.Agents;
using Kora.Core.Hosting;
using Kora.Core.Storage;
using System.Security.Cryptography;
using System.Text;

namespace Kora.Application.Agents;

/// <summary>One immutable exact-session input review; separate consent is required to consume it.</summary>
public sealed class BoundedAgentReview : IDisposable
{
    private int consumed;
    private int revoked;
    private readonly Lock gate = new();
    private readonly CancellationTokenSource revocation = new();
    private Task cancellation = Task.CompletedTask;
    internal CancellationToken Token => revocation.Token;
    internal BoundedAgentReview(BoundedAgentWorkflow owner, IAgentProfileSelection source,
        BoundedAgentProfile profile, SessionWorkspaceEntry target, string input, RequestOrigin origin,
        Func<bool> current, DateTimeOffset expiresAt)
    {
        Owner = owner;
        Source = source;
        Profile = profile;
        Target = target;
        Input = input;
        Origin = origin;
        Current = current;
        ExpiresAt = expiresAt;
        CreatedAt = expiresAt.AddMinutes(-2);
        SourceRevision = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            source.Metadata.CanonicalPath + "\0" + source.Metadata.FileIdentity + "\0" + profile.Document.Definition.Digest)));
    }
    internal BoundedAgentWorkflow Owner { get; }
    internal IAgentProfileSelection Source { get; }
    internal Func<bool> Current { get; }
    internal bool IsRevoked => Volatile.Read(ref revoked) != 0;
    public BoundedAgentProfile Profile { get; }
    public AgentInstructionReferenceReview? References { get; internal set; }
    public bool IsInvocationReady => !IsRevoked && (References is null || References.Bundle is not null);
    public SessionWorkspaceEntry Target { get; }
    public string Input { get; }
    public RequestOrigin Origin { get; }
    public DateTimeOffset ExpiresAt { get; }
    internal DateTimeOffset CreatedAt { get; }
    public string SourceRevision { get; private set; }
    internal void PinBundle(AgentInstructionBundle bundle) =>
        SourceRevision = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            SourceRevision + "\0" + string.Join("\0", bundle.Instructions.Select(item => item.Revision)))));
    internal void Consume(BoundedAgentWorkflow owner)
    {
        if (!ReferenceEquals(Owner, owner) || !IsInvocationReady || Interlocked.Exchange(ref consumed, 1) != 0)
        { throw new InvalidOperationException("An agent review is exact-owner, single-use and cannot be replayed."); }
    }
    /// <summary>Immediately closes new dispatch/publication; the workflow retains the pinned resource until joined.</summary>
    public void Dispose()
    {
        lock (gate)
        {
            if (Interlocked.Exchange(ref revoked, 1) == 0)
            {
                References?.Dispose();
                cancellation = revocation.CancelAsync();
            }
        }
    }
    internal async Task ReleaseSourceAsync()
    {
        Task pending;
        lock (gate) { pending = cancellation; }
        try { await pending.ConfigureAwait(false); }
        finally { Source.Dispose(); revocation.Dispose(); }
    }
}
