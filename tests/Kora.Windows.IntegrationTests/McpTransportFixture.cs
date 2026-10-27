using System.Collections.Immutable;
using System.Net;
using System.Text;
using System.Text.Json;
using Kora.Core.Mcp;
using Kora.Windows.Mcp;

namespace Kora.Windows.IntegrationTests;

internal sealed class McpTransportFixture : IDisposable
{
    internal const string Schema = """{"type":"object","additionalProperties":false,"properties":{"query":{"type":"string","maxLength":32}},"required":["query"]}""";
    internal const string Initialization = """{"jsonrpc":"2.0","id":1,"result":{"protocolVersion":"2025-06-18","capabilities":{"tools":{}},"serverInfo":{"name":"fake","version":"1"}}}""";
    internal const string Result = """{"jsonrpc":"2.0","id":3,"result":{"content":[{"type":"text","text":"reference 42"}]}}""";
    internal McpAccountReference Account => Lease.Account;
    internal TestLease Lease { get; } = new();
    internal List<SentRequest> Requests { get; } = [];
    internal List<string> Methods { get; } = [];
    internal int Calls => Methods.Count(method => string.Equals(method, "tools/call", StringComparison.Ordinal));
    internal bool Connected { get; private set; }
    internal bool Disposed => Lease.Disposed;
    internal bool HandlerDisposed { get; private set; }
    internal int Resolutions { get; private set; }
    internal int Acquisitions { get; private set; }
    internal IReadOnlyList<IPAddress>? Pinned { get; private set; }
    internal Func<string, CancellationToken, Task<IPAddress[]>> Resolve { get; set; } =
        (_, _) => Task.FromResult(new[] { IPAddress.Parse("8.8.8.8") });
    internal Func<SentRequest, CancellationToken, Task<HttpResponseMessage>>? Send { get; set; }
    internal bool Qualified { get; set; } = true;
    internal WindowsMcpConnectionBroker Broker { get; }

    internal McpTransportFixture()
    {
        Broker = new(new Accounts(this), async (host, token) =>
        {
            Resolutions++;
            return await Resolve(host, token);
        }, (_, addresses) =>
        {
            Pinned = addresses;
            Connected = true;
            return new Handler(this);
        });
    }

    internal static ImmutableArray<byte> Wire(byte[] bytes) => ImmutableArray.CreateRange(bytes);
    internal IMcpConnection CreateConnection(TimeProvider time) =>
        new McpHttpConnection(Account, Lease, [IPAddress.Parse("8.8.8.8")], new Handler(this), time);
    internal static HttpResponseMessage Json(string text, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(text, Encoding.UTF8, "application/json") };

    public void Dispose() => Lease.Withdrawn.Dispose();

    internal sealed record SentRequest(Uri Address, HttpMethod Method, Version Version,
        HttpVersionPolicy VersionPolicy, string? Authorization, string? ContentType, string Accept,
        string Protocol, byte[] Bytes);

    private sealed class Accounts(McpTransportFixture owner) : IQualifiedMcpAccountSource
    {
        public Task<IReadOnlyList<McpAccountChoice>> ReadAccountsAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (!owner.Qualified) { throw new InvalidOperationException("Fake profile is unqualified."); }
            return Task.FromResult<IReadOnlyList<McpAccountChoice>>([new(owner.Account, "Native delegated test account")]);
        }

        public Task<McpAccountLease> AcquireAsync(McpAccountReference account, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            owner.Acquisitions++;
            if (!owner.Qualified) { throw new InvalidOperationException("Fake profile is unqualified."); }
            return Task.FromResult<McpAccountLease>(owner.Lease);
        }
    }

    internal sealed class TestLease : McpAccountLease
    {
        public override McpAccountReference Account { get; } =
            new(Guid.NewGuid(), new(1), Guid.NewGuid(), new(new("https://example.com/mcp")));
        internal string Credential { get; set; } = "fake-delegated-token";
        public override string AccessToken => Credential;
        internal CancellationTokenSource Withdrawn { get; } = new();
        public override CancellationToken AdmissionWithdrawn => Withdrawn.Token;
        internal bool Current { get; set; } = true;
        internal bool Disposed { get; private set; }
        internal bool FailDisposal { get; set; }
        internal Action? OnValidation { get; set; }
        public override Task<bool> IsCurrentAsync(CancellationToken token)
        {
            OnValidation?.Invoke();
            token.ThrowIfCancellationRequested();
            return Task.FromResult(Current);
        }
        public override void Dispose()
        {
            Disposed = true;
            if (FailDisposal) { throw new InvalidOperationException("Fake credential lease disposal failed."); }
        }
    }

    private sealed class Handler(McpTransportFixture owner) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var bytes = await request.Content!.ReadAsByteArrayAsync(cancellationToken);
            using var json = JsonDocument.Parse(bytes);
            var method = json.RootElement.GetProperty("method").GetString()!;
            owner.Methods.Add(method);
            var sent = new SentRequest(request.RequestUri!, request.Method, request.Version, request.VersionPolicy,
                request.Headers.Authorization?.ToString(), request.Content.Headers.ContentType?.ToString(),
                request.Headers.Accept.ToString(), request.Headers.GetValues("MCP-Protocol-Version").Single(), bytes);
            owner.Requests.Add(sent);
            if (owner.Send is { } send) { return await send(sent, cancellationToken); }
            return method switch
            {
                "initialize" => Json(Initialization),
                "notifications/initialized" => new(HttpStatusCode.Accepted) { Content = new ByteArrayContent([]) },
                "tools/list" => Json("{\"jsonrpc\":\"2.0\",\"id\":2,\"result\":{\"tools\":[{\"name\":\"search\",\"inputSchema\":" + Schema + "}]}}"),
                _ => Json(Result),
            };
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { owner.HandlerDisposed = true; }
            base.Dispose(disposing);
        }
    }
}
