using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.Memory;

internal sealed class SessionMemoryContextUse : IDisposable
{
    private readonly SessionMemoryContextReview review;
    private readonly Func<bool> admitted;
    private readonly CancellationTokenSource cancellation;

    internal SessionMemoryContextUse(SessionMemoryContextReview review,
        Func<bool> admitted, CancellationToken token)
    {
        this.review = review;
        this.admitted = admitted;
        cancellation = CancellationTokenSource.CreateLinkedTokenSource(token, review.Revocation);
    }
    internal CancellationToken Token => cancellation.Token;
    internal SessionWorkspaceEntry Target => review.Target;
    internal RequestOrigin Origin => review.Origin;
    internal string Body => review.ReadCurrent().Body;
    internal bool Current() => !Token.IsCancellationRequested && review.Current && admitted();

    internal ModelContextEnvelope CreateEnvelope(HostRequest request, DateTimeOffset now)
    {
        var content = review.ReadCurrent();
        return SessionMemoryContext.CreateEnvelope(request, content.Body, content.Snapshot, now);
    }

    internal async Task ValidateAsync(CancellationToken token)
    {
        try
        {
            token.ThrowIfCancellationRequested();
            if (!Current()) { throw new InvalidOperationException("The reviewed memory use was revoked."); }
            await review.ValidateAsync(Current, token).ConfigureAwait(false);
        }
        catch
        {
            review.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        review.Dispose();
        cancellation.Dispose();
    }
}
