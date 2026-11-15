using System.Runtime.ExceptionServices;
using System.Text.Json;
using Kora.Core.Mcp;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Kora.Windows.Mcp;

internal sealed partial class McpHttpConnection
{
    private McpSdkRequestHandler? sdkHandler;
    private HttpClient? sdkHttp;
    private McpClient? sdk;
    private bool sdkFaulted;

    public Task<McpConnectionReply<McpServerCapabilities>> InitializeAsync(CancellationToken token) => RunAsync(async current =>
    {
        if (sdkHandler is not null) { throw new InvalidOperationException("MCP initialization is single-use."); }
        sdkHandler = new(Endpoint, (wire, limit, notification, cancellation) => PostAsync(wire, limit, notification, cancellation));
        sdkHttp = new(sdkHandler) { Timeout = Timeout.InfiniteTimeSpan };
        McpServerCapabilities? admitted = null;
        sdkHandler.Expect("initialize", JsonSerializer.SerializeToElement(new
        {
            protocolVersion = McpProtocol.Version,
            capabilities = new Dictionary<string, string>(StringComparer.Ordinal),
            clientInfo = new { name = "Kora", version = "1" },
        }), McpProtocol.MaximumDiscoveryUtf8Bytes, (bytes, id) =>
        {
            if (id != 1) { throw new InvalidDataException("The SDK initialization correlation is unsupported."); }
            admitted = McpCapabilityProtocol.ReadInitialization(bytes);
        });
        await using var transport = new HttpClientTransport(new()
        {
            Endpoint = Endpoint.Address,
            Name = "kora-reviewed-mcp",
            TransportMode = HttpTransportMode.StreamableHttp,
            EnableStandaloneGetStream = false,
            OwnsSession = false,
            MaxReconnectionAttempts = 0,
        }, sdkHttp);
        try
        {
            sdk = await McpClient.CreateAsync(transport, new()
            {
                ProtocolVersion = McpProtocol.Version,
                ClientInfo = new() { Name = "Kora", Version = "1" },
                Capabilities = new(),
                InitializationTimeout = TimeSpan.FromSeconds(30),
                TimeProvider = time,
            }, cancellationToken: current).ConfigureAwait(false);
            current.ThrowIfCancellationRequested();
            if (sdk.Completion.IsCompleted || admitted is null || !sdkHandler.Initialized
                || !string.Equals(sdk.NegotiatedProtocolVersion, McpProtocol.Version, StringComparison.Ordinal)
                || admitted.Tools != (sdk.ServerCapabilities.Tools is not null)
                || admitted.Resources != (sdk.ServerCapabilities.Resources is not null)
                || admitted.Prompts != (sdk.ServerCapabilities.Prompts is not null))
            { throw new InvalidDataException("SDK initialization differs from the admitted server capabilities."); }
            await WindowsMcpConnectionBroker.RequireCurrentAsync(lease, Account, current).ConfigureAwait(false);
            return new McpConnectionReply<McpServerCapabilities>(admitted, sdkHandler.ResponseBytes);
        }
        catch
        {
            sdkFaulted = true;
            if (sdkHandler.Failure is { } failure) { ExceptionDispatchInfo.Capture(failure).Throw(); }
            throw;
        }
    }, token);

    public Task<McpConnectionReply<McpCapabilityPage>> ListAsync(McpCapabilityKind kind, string? cursor, CancellationToken token)
    {
        var method = McpCapabilityProtocol.ListMethod(kind);
        McpCapabilityProtocol.ValidateCursor(cursor);
        var parameters = cursor is null ? JsonSerializer.SerializeToElement(new Dictionary<string, string>(StringComparer.Ordinal))
            : JsonSerializer.SerializeToElement(new { cursor });
        return kind switch
        {
            McpCapabilityKind.Tool => PerformAsync(method, parameters, McpProtocol.MaximumDiscoveryUtf8Bytes,
                (client, current) => client.ListToolsAsync(new ListToolsRequestParams { Cursor = cursor }, current),
                (bytes, id) => McpCapabilityProtocol.ReadPage(bytes, kind, id),
                static (raw, result) => PageAgrees(raw, result.NextCursor,
                    result.Tools.Select(item => (item.Name, item.Name, (JsonElement?)item.InputSchema))), token),
            McpCapabilityKind.Resource => PerformAsync(method, parameters, McpProtocol.MaximumDiscoveryUtf8Bytes,
                (client, current) => client.ListResourcesAsync(new ListResourcesRequestParams { Cursor = cursor }, current),
                (bytes, id) => McpCapabilityProtocol.ReadPage(bytes, kind, id),
                static (raw, result) => PageAgrees(raw, result.NextCursor,
                    result.Resources.Select(item => (item.Uri, item.Name, (JsonElement?)null))), token),
            McpCapabilityKind.ResourceTemplate => PerformAsync(method, parameters, McpProtocol.MaximumDiscoveryUtf8Bytes,
                (client, current) => client.ListResourceTemplatesAsync(new ListResourceTemplatesRequestParams { Cursor = cursor }, current),
                (bytes, id) => McpCapabilityProtocol.ReadPage(bytes, kind, id),
                static (raw, result) => PageAgrees(raw, result.NextCursor,
                    result.ResourceTemplates.Select(item => (item.UriTemplate, item.Name, (JsonElement?)null))), token),
            _ => PerformAsync(method, parameters, McpProtocol.MaximumDiscoveryUtf8Bytes,
                (client, current) => client.ListPromptsAsync(new ListPromptsRequestParams { Cursor = cursor }, current),
                (bytes, id) => McpCapabilityProtocol.ReadPage(bytes, kind, id),
                static (raw, result) => PageAgrees(raw, result.NextCursor,
                    result.Prompts.Select(item => (item.Name, item.Name, (JsonElement?)null))), token),
        };
    }

    public Task<McpConnectionReply<McpRemoteResult>> InvokeAsync(string name, McpToolSchema schema, string arguments, CancellationToken token)
    {
        schema.ValidateArguments(arguments);
        var input = ReadArguments(arguments);
        return PerformAsync("tools/call", JsonSerializer.SerializeToElement(new { name, arguments = input }),
            McpProtocol.MaximumResultUtf8Bytes,
            (client, current) => client.CallToolAsync(new CallToolRequestParams { Name = name, Arguments = input }, current),
            (bytes, id) => McpProtocol.ReadInvocationResult(bytes, id),
            static (raw, result) => raw.IsError == (result.IsError == true)
                && result.Content.All(block => block is TextContentBlock)
                && string.Equals(raw.Text, string.Join('\n', result.Content.Cast<TextContentBlock>().Select(block => block.Text)), StringComparison.Ordinal)
                && JsonAgrees(raw.StructuredJson, result.StructuredContent), token);
    }

    public Task<McpConnectionReply<McpTextDocument>> ReadResourceAsync(string uri, CancellationToken token)
    {
        McpCapabilityProtocol.ValidateUri(uri);
        return PerformAsync("resources/read", JsonSerializer.SerializeToElement(new { uri }), McpCapabilityProtocol.MaximumContentBytes,
            (client, current) => client.ReadResourceAsync(new ReadResourceRequestParams { Uri = uri }, current),
            (bytes, id) => McpCapabilityProtocol.ReadResource(bytes, id, uri),
            static (raw, result) => raw.Blocks.SequenceEqual(result.Contents.Select(item => ResourceBlock(item, null))), token);
    }

    public Task<McpConnectionReply<McpTextDocument>> ReadPromptAsync(McpCapability prompt, string arguments, CancellationToken token)
    {
        var input = McpCapabilityProtocol.ReadPromptArguments(prompt, arguments).EnumerateObject()
            .ToDictionary(item => item.Name, item => item.Value.Clone(), StringComparer.Ordinal);
        return PerformAsync("prompts/get", JsonSerializer.SerializeToElement(new { name = prompt.Id, arguments = input }),
            McpCapabilityProtocol.MaximumContentBytes,
            (client, current) => client.GetPromptAsync(new GetPromptRequestParams { Name = prompt.Id, Arguments = input }, current),
            (bytes, id) => McpCapabilityProtocol.ReadPrompt(bytes, id),
            static (raw, result) => raw.Blocks.SequenceEqual(result.Messages.Select(message =>
            {
                var role = message.Role switch { Role.User => "user", Role.Assistant => "assistant", _ => null };
                return message.Content switch
                {
                    TextContentBlock text => new McpTextBlock(text.Text, null, null, role),
                    EmbeddedResourceBlock resource => ResourceBlock(resource.Resource, role),
                    _ => throw new InvalidDataException("The SDK prompt projection is not admitted text."),
                };
            })), token);
    }

    private Task<McpConnectionReply<T>> PerformAsync<TSdk, T>(string method, JsonElement parameters, int limit,
        Func<McpClient, CancellationToken, ValueTask<TSdk>> invoke, Func<byte[], int, T> parse, Func<T, TSdk, bool> agrees,
        CancellationToken token) => RunAsync(async current =>
    {
        if (sdk is null || sdkHandler is null || sdkFaulted || sdk.Completion.IsCompleted)
        { throw new InvalidOperationException("A fresh initialized SDK MCP connection is required."); }
        T? admitted = default;
        var observed = false;
        sdkHandler.Expect(method, parameters, limit, (bytes, id) => { admitted = parse(bytes, id); observed = true; });
        try
        {
            var result = await invoke(sdk, current).ConfigureAwait(false);
            current.ThrowIfCancellationRequested();
            if (!observed) { throw new InvalidDataException("The SDK MCP result lacks raw host admission."); }
            if (sdk.Completion.IsCompleted || !agrees(admitted!, result))
            { throw new InvalidDataException("The SDK MCP projection differs from the admitted result."); }
            await WindowsMcpConnectionBroker.RequireCurrentAsync(lease, Account, current).ConfigureAwait(false);
            return new McpConnectionReply<T>(admitted!, sdkHandler.ResponseBytes);
        }
        catch
        {
            sdkFaulted = true;
            if (sdkHandler.Failure is { } failure) { ExceptionDispatchInfo.Capture(failure).Throw(); }
            throw;
        }
    }, token);

    private static Dictionary<string, JsonElement> ReadArguments(string arguments)
    {
        using var document = JsonDocument.Parse(arguments);
        return document.RootElement.EnumerateObject().ToDictionary(item => item.Name, item => item.Value.Clone(), StringComparer.Ordinal);
    }

    private static McpTextBlock ResourceBlock(ResourceContents content, string? role) => content is TextResourceContents text
        ? new(text.Text, text.Uri, text.MimeType, role)
        : throw new InvalidDataException("The SDK resource projection is not admitted text.");

    private static bool PageAgrees(McpCapabilityPage raw, string? cursor,
        IEnumerable<(string Id, string Name, JsonElement? Schema)> projected)
    {
        var items = projected.ToArray();
        return string.Equals(raw.NextCursor, cursor, StringComparison.Ordinal) && raw.Items.Length == items.Length
            && raw.Items.Zip(items).All(pair => string.Equals(pair.First.Id, pair.Second.Id, StringComparison.Ordinal)
                && string.Equals(pair.First.Name, pair.Second.Name, StringComparison.Ordinal)
                && (pair.First.Kind != McpCapabilityKind.Tool || ToolSchemaAgrees(pair.First, pair.Second.Schema)));
    }

    private static bool ToolSchemaAgrees(McpCapability raw, JsonElement? projected)
    {
        using var first = JsonDocument.Parse(raw.DefinitionJson);
        return first.RootElement.TryGetProperty("inputSchema", out var left)
            ? projected is { } right && JsonElement.DeepEquals(left, right)
            : projected is null || projected.Value.ValueKind == JsonValueKind.Undefined;
    }

    private static bool JsonAgrees(string? left, JsonElement? right)
    {
        if (left is null || right is null) { return left is null && right is null; }
        using var first = JsonDocument.Parse(left);
        return JsonElement.DeepEquals(first.RootElement, right.Value);
    }
}
