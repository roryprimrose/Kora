using Kora.Core.Hosting;
using Kora.Core.Providers;
using Kora.Core.Storage;

namespace Kora.Application.Dependencies;

/// <summary>Volatile host-issued authority for one exact original request, never saved policy or egress approval.</summary>
internal sealed class LocalRequestProfileReview(
    Guid owner, SessionWorkspaceEntry target, string body, RequestOrigin origin,
    ProviderProfileTuple profile, Func<bool> current, Func<CancellationToken, Task<bool>> validate)
{
    private int consumed;
    private HostRequest? boundRequest;
    internal Guid Owner { get; } = owner;
    internal SessionWorkspaceEntry Target { get; } = target;
    internal string Body { get; } = body;
    internal RequestOrigin Origin { get; } = origin;
    internal ProviderProfileTuple Profile { get; } = profile;
    internal bool IsCurrent => current();
    internal bool IsConsumed => Volatile.Read(ref consumed) != 0;
    internal Task<bool> IsCurrentAsync(CancellationToken token) => validate(token);
    internal async Task ValidateAsync(CancellationToken token)
    {
        if (!await IsCurrentAsync(token).ConfigureAwait(false))
        { throw new InvalidOperationException("The exact request, session, profile or current authority changed; review again."); }
    }
    internal void Bind(HostRequest request)
    {
        if (request.SessionId != Target.Authority.SessionId || request.Origin != Origin
            || Interlocked.CompareExchange(ref boundRequest, request, null) is not null)
        { throw new InvalidOperationException("The request-only capability is already bound or addresses another original request."); }
    }
    internal bool TryConsume(HostRequest request) => ReferenceEquals(Volatile.Read(ref boundRequest), request)
        && Interlocked.CompareExchange(ref consumed, 1, 0) == 0;
}
