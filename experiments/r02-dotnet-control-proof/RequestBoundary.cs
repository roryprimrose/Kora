// GHCP001 is the reviewed public experimental request-handler API, not private SDK access.
#pragma warning disable GHCP001
using System.Collections.Concurrent;
using System.Net;
using System.Text;
using GitHub.Copilot;

namespace Kora.Rt1;

internal sealed record RequestReceipt(int Bytes, bool Blocked, bool DeniedMarker, bool CredentialInBody, string Reason);
internal sealed record LaneBinding(Uri Destination, string Lane, string HostIdentity, int MaximumRequests);

internal sealed class RequestBoundary(HttpClient http) : CopilotRequestHandler(http), IDisposable
{
    private readonly ConcurrentDictionary<string, LaneBinding> bindings = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, int> forwarded = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> cancelled = new(StringComparer.Ordinal);
    private readonly ConcurrentBag<CancellationTokenRegistration> registrations = [];
    internal ConcurrentQueue<RequestReceipt> Receipts { get; } = new();
    internal int CancellationObserved;
    internal int WebSocketsDenied;
    internal bool RejectAll { get; set; }

    internal void Bind(string session, Uri destination, string lane, string hostIdentity, int maximumRequests = 8)
    {
        if (destination.Scheme != "http" || destination.Host != "127.0.0.1" || destination.IsDefaultPort)
            throw new InvalidOperationException("Fixture destinations must be owned ephemeral loopback listeners.");
        if (!bindings.TryAdd(session, new LaneBinding(destination, lane, hostIdentity, maximumRequests)))
            throw new InvalidOperationException("Runtime session already bound.");
    }

    internal void Cancel(string session) => cancelled.TryAdd(session, 0);
    internal bool CanInvokeTool(string session) =>
        bindings.TryGetValue(session, out var binding) && binding.Lane == "execution" && !cancelled.ContainsKey(session);

    internal async Task WaitForReceiptsAsync(int count)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (Receipts.Count < count) await Task.Delay(TimeSpan.FromMilliseconds(10), timeout.Token);
    }

    protected override async Task<HttpResponseMessage> SendRequestAsync(HttpRequestMessage request, CopilotRequestContext ctx)
    {
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ctx.CancellationToken);
        var denied = body.Contains(Candidate.Denied, StringComparison.Ordinal)
            || body.Contains(Candidate.Collected, StringComparison.Ordinal);
        var credential = body.Contains(Candidate.Credential, StringComparison.Ordinal);
        var bytes = Encoding.UTF8.GetByteCount(body);
        var reason = "admitted";
        if (ctx.SessionId is null || !bindings.TryGetValue(ctx.SessionId, out var binding)) reason = "unbound";
        else if (request.RequestUri is not { } uri
            || uri.GetLeftPart(UriPartial.Authority) != binding.Destination.GetLeftPart(UriPartial.Authority)
            || uri.AbsolutePath != "/v1/chat/completions") reason = "destination";
        else if (cancelled.ContainsKey(ctx.SessionId)) reason = "cancelled";
        else if (RejectAll) reason = "trial-denial";
        else if (denied || credential) reason = "content";
        else if (bytes > 262144) reason = "fixture-bound";
        else if (forwarded.AddOrUpdate(ctx.SessionId, 1, (_, count) => count + 1) > binding.MaximumRequests) reason = "retry-bound";
        var blocked = reason != "admitted";
        Receipts.Enqueue(new RequestReceipt(bytes, blocked, denied, credential, reason));
        if (blocked)
        {
            return new HttpResponseMessage(HttpStatusCode.Forbidden)
            {
                Content = new StringContent("{\"error\":{\"message\":\"synthetic-host-denial\"}}")
            };
        }
        registrations.Add(ctx.CancellationToken.Register(() => Interlocked.Increment(ref CancellationObserved)));
        return await base.SendRequestAsync(request, ctx);
    }

    protected override Task<CopilotWebSocketHandler> OpenWebSocketAsync(CopilotRequestContext ctx)
    {
        Interlocked.Increment(ref WebSocketsDenied);
        throw new NotSupportedException("WebSockets are not admitted by RT1.");
    }

    public void Dispose()
    {
        foreach (var registration in registrations) registration.Dispose();
    }
}
#pragma warning restore GHCP001
