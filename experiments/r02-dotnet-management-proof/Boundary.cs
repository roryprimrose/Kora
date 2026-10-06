#pragma warning disable GHCP001 // Reviewed public RT1 experimental boundary.
using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using GitHub.Copilot;
using Kora.Rt1;

namespace Kora.Mg1;

internal sealed class Binding(Uri destination, bool execution, bool probe)
{
    internal Uri Destination { get; } = destination;
    internal bool Execution { get; } = execution;
    internal bool Probe { get; } = probe;
    internal readonly Lock Gate = new();
    internal bool Closed;
    internal int Forwarded;
}
internal sealed record BoundaryReceipt(int Bytes, string Reason, bool SystemPresent, bool HistoryPresent, bool Multibyte);

internal sealed class Boundary(HttpClient http) : CopilotRequestHandler(http), IDisposable
{
    private readonly ConcurrentDictionary<string, Binding> bindings = new(StringComparer.Ordinal);
    private readonly ConcurrentBag<CancellationTokenRegistration> registrations = [];
    internal ConcurrentQueue<BoundaryReceipt> Receipts { get; } = new();
    internal int CancellationObserved;
    internal TaskCompletionSource SendAcknowledgement { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal bool StallSendAcknowledgement { get; set; }

    internal void Bind(string session, Uri destination, bool execution = false, bool probe = false)
    {
        if (destination.Scheme != "http" || destination.Host != "127.0.0.1" || destination.IsDefaultPort
            || !bindings.TryAdd(session, new Binding(destination, execution, probe)))
            throw new InvalidOperationException("invalid-owned-binding");
    }
    internal void Close(string session)
    {
        var binding = bindings[session];
        lock (binding.Gate) binding.Closed = true;
    }
    internal bool ToolAllowed(string session)
    {
        if (!bindings.TryGetValue(session, out var binding)) return false;
        lock (binding.Gate) return binding.Execution && !binding.Closed;
    }

    protected override async Task<HttpResponseMessage> SendRequestAsync(HttpRequestMessage request, CopilotRequestContext ctx)
    {
        var bytes = request.Content is null ? [] : await request.Content.ReadAsByteArrayAsync(ctx.CancellationToken);
        var reason = "unbound";
        using var json = JsonDocument.Parse(bytes);
        var body = json.RootElement.GetRawText();
        if (ctx.SessionId is not null && bindings.TryGetValue(ctx.SessionId, out var binding))
        {
            lock (binding.Gate)
            {
                reason = request.RequestUri is not { } uri
                    || uri.GetLeftPart(UriPartial.Authority) != binding.Destination.GetLeftPart(UriPartial.Authority)
                    || uri.AbsolutePath != "/v1/chat/completions" ? "destination"
                    : binding.Closed ? "closed"
                    : binding.Probe ? "probe"
                    : bytes.Length > Envelope.InputBytes && !binding.Execution ? "input-overflow"
                    : body.Contains(Candidate.Credential, StringComparison.Ordinal) ? "credential"
                    : binding.Forwarded != 0 ? "retry-continuation"
                    : "admitted";
                if (reason == "admitted") binding.Forwarded++;
            }
        }
        Receipts.Enqueue(new BoundaryReceipt(bytes.Length, reason,
            body.Contains("MG1_SYSTEM", StringComparison.Ordinal),
            body.Contains("MG1_HISTORY", StringComparison.Ordinal),
            bytes.Any(value => value >= 128)));
        if (reason != "admitted")
            return new HttpResponseMessage(HttpStatusCode.Forbidden)
            {
                Content = new StringContent("{\"error\":{\"message\":\"mg1-host-" + reason + "\"}}")
            };
        registrations.Add(ctx.CancellationToken.Register(() => Interlocked.Increment(ref CancellationObserved)));
        if (StallSendAcknowledgement) await SendAcknowledgement.Task.WaitAsync(TimeSpan.FromSeconds(25));
        return await base.SendRequestAsync(request, ctx);
    }
    protected override Task<CopilotWebSocketHandler> OpenWebSocketAsync(CopilotRequestContext ctx) =>
        throw new NotSupportedException("MG1 WebSockets unavailable.");
    public void Dispose()
    {
        SendAcknowledgement.TrySetResult();
        foreach (var registration in registrations) registration.Dispose();
    }
}
#pragma warning restore GHCP001
