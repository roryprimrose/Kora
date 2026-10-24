using System.Net;
using System.Text.Json;
using GitHub.Copilot;
using Kora.Core.Dependencies;
using Kora.Core.Providers;

namespace Kora.Windows.Dependencies;

#pragma warning disable GHCP001 // The pinned SDK's only final-request boundary; no hook-only or native transport fallback is permitted.
internal sealed class CopilotFinalRequestHandler : CopilotRequestHandler
{
    private readonly HttpClient http;
    private readonly ModelTurnProvenance provenance;
    private readonly ProviderProfileTuple profile;
    private readonly string sessionId;
    private readonly Func<bool> live;
    private readonly Func<CopilotTransmission, CancellationToken, ValueTask<bool>> admit;
    private readonly CancellationToken turnToken;
    private int denied;
    private int successfulTransmission;
    internal bool Denied => Volatile.Read(ref denied) != 0;
    internal bool HasSuccessfulTransmission => Volatile.Read(ref successfulTransmission) != 0;

    internal CopilotFinalRequestHandler(HttpClient http, ModelTurnProvenance provenance, ProviderProfileTuple profile,
        string sessionId, Func<bool> live, Func<CopilotTransmission, CancellationToken, ValueTask<bool>> admit,
        CancellationToken turnToken) : base(http)
    {
        this.http = http;
        this.provenance = provenance;
        this.profile = profile;
        this.sessionId = sessionId;
        this.live = live;
        this.admit = admit;
        this.turnToken = turnToken;
    }

    protected override Task<HttpResponseMessage> SendRequestAsync(HttpRequestMessage request, CopilotRequestContext ctx) =>
        SendFinalAsync(request, ctx.SessionId, ctx.ParentAgentId, ctx.Url, ctx.CancellationToken);

    internal async Task<HttpResponseMessage> SendFinalAsync(HttpRequestMessage request, string? sdkSessionId,
        string? parentAgentId, string url, CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(turnToken, cancellationToken);
        var token = linked.Token;
        token.ThrowIfCancellationRequested();
        if (!live() || !string.Equals(sdkSessionId, sessionId, StringComparison.Ordinal)
            || parentAgentId is not null || request.Method != HttpMethod.Post || request.RequestUri is not { } uri
            || !string.Equals(url, uri.AbsoluteUri, StringComparison.Ordinal)
            || !AllowedDestination(uri) || request.Content is null || request.Headers.Contains("Cookie")
            || request.Headers.Contains("Proxy-Authorization") || request.Headers.Contains("X-Api-Key")
            || !string.Equals(request.Headers.Authorization?.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(request.Headers.Authorization?.Parameter)
            || request.Headers.Host is { } host && !string.Equals(host, uri.Host, StringComparison.OrdinalIgnoreCase))
        {
            throw Reject();
        }
        // Buffer with a hard bound before review. Never forward streams, attachments, credentials, redirects or a changed body.
        if (request.Content.Headers.ContentLength > CopilotTransmission.MaximumBodyBytes) { throw Reject(); }
        try { await request.Content.LoadIntoBufferAsync(CopilotTransmission.MaximumBodyBytes, token).ConfigureAwait(false); }
        catch (HttpRequestException exception) { throw Reject(exception); }
        var bytes = await request.Content.ReadAsByteArrayAsync(token).ConfigureAwait(false);
        using var document = ParseBody(bytes);
        if (document.RootElement.ValueKind != JsonValueKind.Object
            || document.RootElement.EnumerateObject().Count(item => string.Equals(item.Name, "model", StringComparison.Ordinal)) != 1
            || !document.RootElement.TryGetProperty("model", out var model)
            || model.ValueKind != JsonValueKind.String || !string.Equals(model.GetString(), profile.Model.Value, StringComparison.Ordinal))
        {
            throw Reject();
        }
        using var outgoing = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Version = HttpVersion.Version11,
            VersionPolicy = HttpVersionPolicy.RequestVersionExact,
            Content = new ByteArrayContent(bytes),
        };
        foreach (var header in request.Headers) { outgoing.Headers.TryAddWithoutValidation(header.Key, header.Value); }
        foreach (var header in request.Content.Headers) { outgoing.Content.Headers.TryAddWithoutValidation(header.Key, header.Value); }
        outgoing.Headers.ConnectionClose = true;
        outgoing.Content.Headers.ContentLength = bytes.Length;
        var headers = outgoing.Headers.Where(header => !string.Equals(header.Key, "Authorization", StringComparison.OrdinalIgnoreCase))
            .Concat(outgoing.Content.Headers).Select(header => KeyValuePair.Create(header.Key, string.Join(", ", header.Value)));
        var transmission = new CopilotTransmission(provenance, profile, uri, bytes, headers);
        if (!await admit(transmission, token).ConfigureAwait(false) || !live())
        {
            throw Reject();
        }
        token.ThrowIfCancellationRequested();
        var response = await http.SendAsync(outgoing, HttpCompletionOption.ResponseContentRead, token).ConfigureAwait(false);
        if (response.IsSuccessStatusCode) { Interlocked.Exchange(ref successfulTransmission, 1); }
        return response;
    }

    protected override Task<CopilotWebSocketHandler> OpenWebSocketAsync(CopilotRequestContext ctx) =>
        Task.FromException<CopilotWebSocketHandler>(Reject());

    private JsonDocument ParseBody(byte[] bytes)
    {
        try { return JsonDocument.Parse(bytes); }
        catch (JsonException exception) { throw Reject(exception); }
    }

    private CopilotEgressDeniedException Reject(Exception? exception = null)
    {
        Interlocked.Exchange(ref denied, 1);
        return new(exception);
    }

    internal static bool AllowedDestination(Uri uri) =>
        uri.IsAbsoluteUri && string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal) && uri.IsDefaultPort && uri.UserInfo.Length == 0
        && uri.Query.Length == 0 && uri.Fragment.Length == 0
        && string.Equals(uri.Host, "api.githubcopilot.com", StringComparison.OrdinalIgnoreCase)
        && uri.AbsolutePath is "/chat/completions" or "/responses";
}
#pragma warning restore GHCP001
