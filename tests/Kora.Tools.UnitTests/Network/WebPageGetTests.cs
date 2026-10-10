using System.Net;
using System.Text;
using AwesomeAssertions;
using Kora.Core.Auditing;
using Kora.Core.Network;
using Kora.Tools.Network;

namespace Kora.Tools.UnitTests.Network;

public sealed class WebPageGetTests
{
    private static readonly IPAddress PublicAddress = IPAddress.Parse("93.184.216.34");

    [Fact]
    public async Task RetrievesBoundedUtf8HtmlWithoutActiveContent()
    {
        var transport = new Transport(PublicAddress);
        transport.Responses.Enqueue(Response(
            "https://example.com/",
            "<html><style>secret</style><h1>Hello &amp; welcome</h1><script>alert(1)</script><p>Body</p></html>"));
        var audit = new Audit();
        var action = new WebPageGet(transport, audit);

        var result = await action.ExecuteAsync(
            new(new("https://example.com/")),
            SecurityAuditInitiator.TypedCommand,
            static (_, _) => ValueTask.FromResult(true),
            CancellationToken.None);

        result.Outcome.Should().Be(WebPageGetOutcome.Succeeded);
        result.Text.Should().Be("Hello & welcome\nBody");
        result.MediaType.Should().Be("text/html");
        result.Truncated.Should().BeFalse();
        transport.Endpoints.Should().Equal(PublicAddress);
        audit.Events.Select(item => item.Outcome).Should().Equal(
            SecurityAuditOutcome.Requested,
            SecurityAuditOutcome.Succeeded);
    }

    [Fact]
    public async Task ReauthorizesAndReresolvesEveryRedirect()
    {
        var transport = new Transport(PublicAddress);
        transport.Responses.Enqueue(Response(
            "https://example.com/start",
            string.Empty,
            302,
            new("https://other.example/final")));
        transport.Responses.Enqueue(Response("https://other.example/final", "done", mediaType: "text/plain"));
        var authorized = new List<Uri>();
        var action = new WebPageGet(transport, new Audit());

        var result = await action.ExecuteAsync(
            new(new("https://example.com/start")),
            SecurityAuditInitiator.LocalUser,
            (address, _) =>
            {
                authorized.Add(address);
                return ValueTask.FromResult(true);
            },
            CancellationToken.None);

        result.Outcome.Should().Be(WebPageGetOutcome.Succeeded);
        result.RedirectCount.Should().Be(1);
        authorized.Should().Equal(
            new Uri("https://example.com/start"),
            new Uri("https://other.example/final"));
        transport.ResolvedHosts.Should().Equal("example.com", "other.example");
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("10.0.0.1")]
    [InlineData("169.254.169.254")]
    public async Task DeniesNonPublicOrMixedDnsBeforeConnecting(string address)
    {
        var transport = new Transport(IPAddress.Parse(address), PublicAddress);
        var action = new WebPageGet(transport, new Audit());

        var result = await action.ExecuteAsync(
            new(new("https://example.com/")),
            SecurityAuditInitiator.TypedCommand,
            static (_, _) => ValueTask.FromResult(true),
            CancellationToken.None);

        result.Outcome.Should().Be(WebPageGetOutcome.Denied);
        result.Reason.Should().Be("non-public-network-destination");
        transport.Endpoints.Should().BeEmpty();
    }

    [Fact]
    public async Task AuthorizationDenialPrecedesDnsAndNetworkAccess()
    {
        var transport = new Transport(PublicAddress);
        var action = new WebPageGet(transport, new Audit());

        var result = await action.ExecuteAsync(
            new(new("https://example.com/")),
            SecurityAuditInitiator.TypedCommand,
            static (_, _) => ValueTask.FromResult(false),
            CancellationToken.None);

        result.Outcome.Should().Be(WebPageGetOutcome.Denied);
        transport.ResolvedHosts.Should().BeEmpty();
        transport.Endpoints.Should().BeEmpty();
    }

    [Fact]
    public async Task RejectsUnsupportedContentAndOversizedBodies()
    {
        var unsupported = new Transport(PublicAddress);
        unsupported.Responses.Enqueue(Response(
            "https://example.com/",
            "binary",
            mediaType: "application/octet-stream"));
        var unsupportedResult = await new WebPageGet(unsupported, new Audit()).ExecuteAsync(
            new(new("https://example.com/")),
            SecurityAuditInitiator.LocalUser,
            static (_, _) => ValueTask.FromResult(true),
            CancellationToken.None);
        unsupportedResult.Outcome.Should().Be(WebPageGetOutcome.UnsupportedContent);

        var oversized = new Transport(PublicAddress);
        oversized.Responses.Enqueue(Response(
            "https://example.com/",
            new string('a', WebPageCapability.MaximumContentBytes + 1),
            mediaType: "text/plain",
            contentLength: null));
        var oversizedResult = await new WebPageGet(oversized, new Audit()).ExecuteAsync(
            new(new("https://example.com/")),
            SecurityAuditInitiator.LocalUser,
            static (_, _) => ValueTask.FromResult(true),
            CancellationToken.None);
        oversizedResult.Outcome.Should().Be(WebPageGetOutcome.TooLarge);
    }

    [Fact]
    public async Task RejectsEncodedBodiesAndTruncatesTextWithinTheWireBudget()
    {
        var encoded = new Transport(PublicAddress);
        encoded.Responses.Enqueue(new(
            200,
            new("https://example.com/"),
            "text/plain",
            "utf-8",
            4,
            null,
            ["gzip"],
            new MemoryStream("test"u8.ToArray())));
        var encodedResult = await new WebPageGet(encoded, new Audit()).ExecuteAsync(
            new(new("https://example.com/")),
            SecurityAuditInitiator.LocalUser,
            static (_, _) => ValueTask.FromResult(true),
            CancellationToken.None);
        encodedResult.Outcome.Should().Be(WebPageGetOutcome.UnsupportedContent);
        encodedResult.Reason.Should().Be("encoded-content-not-supported");

        var largeText = new string('a', WebPageCapability.MaximumTextUtf8Bytes + 100);
        var truncated = new Transport(PublicAddress);
        truncated.Responses.Enqueue(Response(
            "https://example.com/",
            largeText,
            mediaType: "text/plain"));
        var truncatedResult = await new WebPageGet(truncated, new Audit()).ExecuteAsync(
            new(new("https://example.com/")),
            SecurityAuditInitiator.LocalUser,
            static (_, _) => ValueTask.FromResult(true),
            CancellationToken.None);
        truncatedResult.Outcome.Should().Be(WebPageGetOutcome.Succeeded);
        truncatedResult.Truncated.Should().BeTrue();
        Encoding.UTF8.GetByteCount(truncatedResult.Text!).Should()
            .Be(WebPageCapability.MaximumTextUtf8Bytes);
    }

    private static WebPageResponse Response(
        string address,
        string content,
        int status = 200,
        Uri? redirect = null,
        string mediaType = "text/html",
        long? contentLength = -1)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        return new(
            status,
            new(address),
            mediaType,
            "utf-8",
            contentLength == -1 ? bytes.Length : contentLength,
            redirect,
            [],
            new MemoryStream(bytes));
    }

    private sealed class Transport(params IPAddress[] addresses) : IWebPageTransport
    {
        public Queue<WebPageResponse> Responses { get; } = [];
        public List<string> ResolvedHosts { get; } = [];
        public List<IPAddress> Endpoints { get; } = [];

        public Task<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken cancellationToken)
        {
            ResolvedHosts.Add(host);
            return Task.FromResult<IReadOnlyList<IPAddress>>(addresses);
        }

        public Task<WebPageResponse> SendAsync(
            Uri address,
            IPAddress endpoint,
            CancellationToken cancellationToken)
        {
            Endpoints.Add(endpoint);
            return Task.FromResult(Responses.Dequeue());
        }
    }

    private sealed class Audit : ISecurityAuditLog
    {
        public List<SecurityAuditEvent> Events { get; } = [];

        public void Write(SecurityAuditEvent auditEvent) => Events.Add(auditEvent);
    }
}
