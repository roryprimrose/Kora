using System.Diagnostics;
using Kora.Core.Context;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.Hosting;

/// <summary>One private, expiring source disclosure review; disposal revokes it and drops private bodies.</summary>
public sealed class SessionAttachmentAnswerReview : IDisposable
{
    private readonly CancellationTokenSource revocation = new();
    private SessionFileAttachment? source;
    private LocalFileSearchResult? search;
    private string? question;
    private int consumed;

    internal SessionAttachmentAnswerReview(Guid owner, SessionWorkspaceEntry target, SessionFileAttachment source,
        LocalFileSearchResult search, string question, RequestOrigin origin, long controlRevision,
        DateTimeOffset expiresAt, Func<bool> admitted, ActivityContext cause = default)
    {
        Owner = owner;
        Target = target;
        this.source = source;
        this.search = search;
        this.question = question;
        Origin = origin;
        ControlRevision = controlRevision;
        ExpiresAt = expiresAt;
        Admitted = admitted;
        Cause = cause;
    }

    internal Guid Owner { get; }
    internal SessionWorkspaceEntry Target { get; }
    internal long ControlRevision { get; }
    internal Func<bool> Admitted { get; }
    internal ActivityContext Cause { get; }
    internal RequestOrigin Origin { get; }
    internal CancellationToken Revocation => revocation.Token;
    internal bool IsRevoked => revocation.IsCancellationRequested;
    internal bool TryConsume() => Interlocked.Exchange(ref consumed, 1) == 0 && !IsRevoked;
    public DateTimeOffset ExpiresAt { get; }
    public SessionFileAttachment Source => source ?? throw new InvalidOperationException("The attachment review was retired.");
    public LocalFileSearchResult Evidence => search ?? throw new InvalidOperationException("The attachment review was retired.");
    public string Question => question ?? throw new InvalidOperationException("The attachment review was retired.");

    public void Dispose()
    {
        if (IsRevoked) { return; }
        revocation.Cancel();
        source = null;
        search = null;
        question = null;
        // The token remains readable by an in-flight consumer until its deterministic join.
        revocation.Dispose();
    }
}
