using System.Collections.Immutable;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Kora.Core.Mcp;

namespace Kora.Windows.Mcp;

internal sealed class McpSdkRequestHandler(McpEndpoint? endpoint = null,
    Func<ImmutableArray<byte>, int, bool, CancellationToken, Task<ImmutableArray<byte>>>? send = null) : HttpMessageHandler
{
    private string? method;
    private JsonElement parameters;
    private int limit;
    private Action<byte[], int>? validate;
    private bool sent;
    private bool notificationAdmitted;
    internal bool Initialized { get; private set; }
    internal int ResponseBytes { get; private set; }
    internal int RequestId { get; private set; }
    internal Exception? Failure { get; private set; }
    internal int MaximumResponseBytes => limit;

    internal void Expect(string expectedMethod, JsonElement expectedParameters, int maximumBytes, Action<byte[], int> validator)
    {
        method = expectedMethod;
        parameters = expectedParameters;
        limit = maximumBytes;
        validate = validator;
        sent = false;
        ResponseBytes = 0;
        Failure = null;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        try
        {
            token.ThrowIfCancellationRequested();
            if (request.Method != HttpMethod.Post || request.RequestUri != endpoint?.Address || request.Content is null
                || request.Headers.Any(header => header.Key is not ("Accept" or "MCP-Protocol-Version" or "Mcp-Method" or "Mcp-Name"))
                || request.Content.Headers.ContentType?.MediaType is not "application/json")
            { throw new InvalidDataException("The SDK MCP request exceeds the admitted HTTPS JSON profile."); }
            var bytes = await request.Content.ReadAsByteArrayAsync(token).ConfigureAwait(false);
            if (bytes.Length is 0 or > McpProtocol.MaximumDiscoveryUtf8Bytes)
            { throw new InvalidDataException("The SDK MCP request exceeds the complete byte bound."); }
            using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 16 });
            var root = document.RootElement;
            var observedMethod = root.GetProperty("method").GetString();
            var name = observedMethod switch
            {
                "tools/call" or "prompts/get" => root.GetProperty("params").GetProperty("name").GetString(),
                "resources/read" => root.GetProperty("params").GetProperty("uri").GetString(),
                _ => null,
            };
            if (!request.Headers.TryGetValues("Mcp-Method", out var methods)
                || !methods.SequenceEqual([observedMethod], StringComparer.Ordinal)
                || (request.Headers.TryGetValues("Mcp-Name", out var names)
                    ? name is null || !names.SequenceEqual([name], StringComparer.Ordinal) : name is not null))
            { throw new InvalidDataException("SDK MCP routing headers differ from the admitted body."); }
            var notification = AdmitRequest(bytes);
            var response = await send!(ImmutableArray.CreateRange(bytes), notification ? 0 : limit, notification, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (notification) { CommitNotification(); }
            else { AdmitResponse(response.ToArray()); }
            return new(notification ? HttpStatusCode.Accepted : HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new ByteArrayContent(response.ToArray())
                {
                    Headers = { ContentType = new MediaTypeHeaderValue("application/json") },
                },
            };
        }
        catch (Exception exception) { Failure ??= exception; throw; }
    }

    internal bool AdmitRequest(byte[] bytes)
    {
        try
        {
            if (bytes.Length is 0 or > McpProtocol.MaximumDiscoveryUtf8Bytes)
            { throw new InvalidDataException("The SDK MCP request exceeds the complete byte bound."); }
            using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 16 });
            var root = document.RootElement;
            var observedMethod = root.GetProperty("method").GetString();
            var notification = observedMethod is "notifications/initialized";
            if (notification)
            {
                if (method is not "initialize" || !sent || Initialized || notificationAdmitted || ResponseBytes == 0
                    || root.EnumerateObject().Any(field => field.Name is not ("jsonrpc" or "method" or "params"))
                    || root.TryGetProperty("params", out var notificationParameters)
                        && (notificationParameters.ValueKind != JsonValueKind.Object || notificationParameters.EnumerateObject().Any())
                    || root.GetProperty("jsonrpc").GetString() is not "2.0")
                { throw new InvalidDataException("The SDK MCP notification was not admitted by initialization."); }
            }
            else if (sent || !string.Equals(observedMethod, method, StringComparison.Ordinal)
                || root.EnumerateObject().Count() != 4 || root.GetProperty("jsonrpc").GetString() is not "2.0"
                || !JsonElement.DeepEquals(root.GetProperty("params"), parameters)
                || !root.GetProperty("id").TryGetInt32(out var id) || id < 1)
            { throw new InvalidDataException("The SDK MCP method, correlation or parameters differ from host admission."); }
            else { RequestId = id; sent = true; }
            if (notification) { notificationAdmitted = true; }
            return notification;
        }

        catch (Exception exception) { Failure ??= exception; throw; }
    }

    internal void CommitNotification() => Initialized = true;

    internal void AdmitResponse(byte[] bytes)
    {
        try
        {
            if (validate is null || !sent || ResponseBytes != 0 || bytes.Length == 0 || bytes.Length > limit)
            { throw new InvalidDataException("An unrequested or oversized MCP frame is unavailable."); }
            validate(bytes, RequestId);
            ResponseBytes = bytes.Length;
        }
        catch (Exception exception) { Failure ??= exception; throw; }
    }
}
