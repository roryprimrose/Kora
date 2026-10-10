using System.Diagnostics;
using System.Net;
using System.Text;
using AwesomeAssertions;
using Kora.Core.Auditing;
using Kora.Core.Diagnostics;
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

        var unicode = new Transport(PublicAddress);
        unicode.Responses.Enqueue(Response(
            "https://example.com/",
            new string('é', WebPageCapability.MaximumTextUtf8Bytes),
            mediaType: "text/plain"));
        var unicodeResult = await new WebPageGet(unicode, new Audit()).ExecuteAsync(
            new(new("https://example.com/")),
            SecurityAuditInitiator.LocalUser,
            static (_, _) => ValueTask.FromResult(true),
            CancellationToken.None);
        unicodeResult.Truncated.Should().BeTrue();
        Encoding.UTF8.GetByteCount(unicodeResult.Text!).Should()
            .Be(WebPageCapability.MaximumTextUtf8Bytes);
    }

    [Fact]
    public async Task ValidatesArgumentsAndRejectsEmptyDnsResults()
    {
        var action = new WebPageGet(new Transport(), new Audit());

        await FluentActions.Awaiting(() => action.ExecuteAsync(
                null!,
                SecurityAuditInitiator.LocalUser,
                static (_, _) => ValueTask.FromResult(true),
                CancellationToken.None))
            .Should().ThrowAsync<ArgumentNullException>();
        await FluentActions.Awaiting(() => action.ExecuteAsync(
                new(new("https://example.com/")),
                SecurityAuditInitiator.LocalUser,
                null!,
                CancellationToken.None))
            .Should().ThrowAsync<ArgumentNullException>();

        var result = await action.ExecuteAsync(
            new(new("https://example.com/")),
            SecurityAuditInitiator.LocalUser,
            static (_, _) => ValueTask.FromResult(true),
            CancellationToken.None);

        result.Outcome.Should().Be(WebPageGetOutcome.Denied);
        result.Reason.Should().Be("non-public-network-destination");
    }

    [Fact]
    public async Task SelectsTheDeterministicPublicEndpoint()
    {
        var ipv6 = IPAddress.Parse("2606:4700:4700::1111");
        var ipv4 = IPAddress.Parse("8.8.8.8");
        var transport = new Transport(ipv6, ipv4);
        transport.Responses.Enqueue(Response("https://example.com/", "ok", mediaType: "text/plain"));

        var result = await new WebPageGet(transport, new Audit()).ExecuteAsync(
            new(new("https://example.com/")),
            SecurityAuditInitiator.LocalUser,
            static (_, _) => ValueTask.FromResult(true),
            CancellationToken.None);

        result.Outcome.Should().Be(WebPageGetOutcome.Succeeded);
        transport.Endpoints.Should().Equal(ipv4);
    }

    [Theory]
    [InlineData(200, "https://other.example/", null, "transport-address-mismatch")]
    [InlineData(302, "https://example.com/", null, "redirect-location-missing")]
    [InlineData(404, "https://example.com/", null, "http-status-404")]
    public async Task RejectsInvalidTransportResponses(
        int status,
        string responseAddress,
        string? redirect,
        string reason)
    {
        var transport = new Transport(PublicAddress);
        transport.Responses.Enqueue(new(
            status,
            new(responseAddress),
            "text/plain",
            "utf-8",
            0,
            redirect is null ? null : new Uri(redirect),
            [],
            new MemoryStream()));

        var result = await new WebPageGet(transport, new Audit()).ExecuteAsync(
            new(new("https://example.com/")),
            SecurityAuditInitiator.LocalUser,
            static (_, _) => ValueTask.FromResult(true),
            CancellationToken.None);

        result.Outcome.Should().Be(WebPageGetOutcome.Failed);
        result.Reason.Should().Be(reason);
    }

    [Fact]
    public async Task ResolvesRelativeRedirectsAndEnforcesTheRedirectLimit()
    {
        var relative = new Transport(PublicAddress);
        relative.Responses.Enqueue(Response(
            "https://example.com/start",
            string.Empty,
            302,
            new Uri("/final", UriKind.Relative)));
        relative.Responses.Enqueue(Response(
            "https://example.com/final",
            "done",
            mediaType: "text/plain"));

        var followed = await new WebPageGet(relative, new Audit()).ExecuteAsync(
            new(new("https://example.com/start")),
            SecurityAuditInitiator.LocalUser,
            static (_, _) => ValueTask.FromResult(true),
            CancellationToken.None);
        followed.Outcome.Should().Be(WebPageGetOutcome.Succeeded);
        followed.FinalAddress.Should().Be(new Uri("https://example.com/final"));

        var looping = new Transport(PublicAddress);
        for (var index = 0; index <= WebPageCapability.MaximumRedirects; index++)
        {
            var address = $"https://example.com/{index}";
            looping.Responses.Enqueue(Response(
                address,
                string.Empty,
                302,
                new Uri($"https://example.com/{index + 1}")));
        }
        var limited = await new WebPageGet(looping, new Audit()).ExecuteAsync(
            new(new("https://example.com/0")),
            SecurityAuditInitiator.LocalUser,
            static (_, _) => ValueTask.FromResult(true),
            CancellationToken.None);
        limited.Outcome.Should().Be(WebPageGetOutcome.Failed);
        limited.Reason.Should().Be("redirect-limit-exceeded");
        limited.RedirectCount.Should().Be(WebPageCapability.MaximumRedirects);
    }

    [Theory]
    [InlineData("text/plain", "latin1", 4, "unsupported-character-set", WebPageGetOutcome.UnsupportedContent)]
    [InlineData("text/plain", null, 262145, "content-length-exceeded", WebPageGetOutcome.TooLarge)]
    [InlineData(null, null, 0, "unsupported-content-type", WebPageGetOutcome.UnsupportedContent)]
    public async Task RejectsUnsupportedResponseMetadata(
        string? mediaType,
        string? characterSet,
        long contentLength,
        string reason,
        WebPageGetOutcome outcome)
    {
        var transport = new Transport(PublicAddress);
        transport.Responses.Enqueue(new(
            200,
            new("https://example.com/"),
            mediaType,
            characterSet,
            contentLength,
            null,
            [],
            new MemoryStream()));

        var result = await new WebPageGet(transport, new Audit()).ExecuteAsync(
            new(new("https://example.com/")),
            SecurityAuditInitiator.LocalUser,
            static (_, _) => ValueTask.FromResult(true),
            CancellationToken.None);

        result.Outcome.Should().Be(outcome);
        result.Reason.Should().Be(reason);
    }

    [Fact]
    public async Task RejectsInvalidUtf8AndAcceptsUtf8Alias()
    {
        var invalid = new Transport(PublicAddress);
        invalid.Responses.Enqueue(new(
            200,
            new("https://example.com/"),
            "text/plain",
            "utf8",
            1,
            null,
            [],
            new MemoryStream([0xff])));

        var result = await new WebPageGet(invalid, new Audit()).ExecuteAsync(
            new(new("https://example.com/")),
            SecurityAuditInitiator.LocalUser,
            static (_, _) => ValueTask.FromResult(true),
            CancellationToken.None);

        result.Outcome.Should().Be(WebPageGetOutcome.UnsupportedContent);
        result.Reason.Should().Be("invalid-utf8");
    }

    [Fact]
    public async Task NormalizesPlainTextAndExercisesHtmlEdgeCases()
    {
        var plain = new Transport(PublicAddress);
        plain.Responses.Enqueue(Response(
            "https://example.com/",
            "  cafe\u0301 \t value\r\nnext  ",
            mediaType: "text/plain"));
        var plainResult = await new WebPageGet(plain, new Audit()).ExecuteAsync(
            new(new("https://example.com/")),
            SecurityAuditInitiator.LocalUser,
            static (_, _) => ValueTask.FromResult(true),
            CancellationToken.None);
        plainResult.Text.Should().Be("café value\nnext");

        var html = new Transport(PublicAddress);
        html.Responses.Enqueue(Response(
            "https://example.com/",
            "<div><script><script>hidden</script></script><br><li> item </li>"
            + "<h2>heading</h2><h3>subheading</h3><style>hidden</style>"
            + "<abcdefghijklmnopqrstuvwxyz123456789>tail</abcdefghijklmnopqrstuvwxyz123456789></div>"));
        var htmlResult = await new WebPageGet(html, new Audit()).ExecuteAsync(
            new(new("https://example.com/")),
            SecurityAuditInitiator.LocalUser,
            static (_, _) => ValueTask.FromResult(true),
            CancellationToken.None);
        htmlResult.Text.Should().Be("item\nheading\nsubheading\ntail");
    }

    [Fact]
    public async Task AuditsCancellationDeadlineAndUnhandledFailure()
    {
        var ambientFreeCancellation = new Audit();
        using (var ambientFreeToken = new CancellationTokenSource())
        {
            ambientFreeToken.Cancel();
            await FluentActions.Awaiting(() => new WebPageGet(
                    new Transport(PublicAddress),
                    ambientFreeCancellation).ExecuteAsync(
                    new(new("https://example.com/")),
                    SecurityAuditInitiator.LocalUser,
                    static (_, _) => ValueTask.FromResult(true),
                    ambientFreeToken.Token))
                .Should().ThrowAsync<OperationCanceledException>();
        }
        var ambientFreeFailure = new Audit();
        await FluentActions.Awaiting(() => new WebPageGet(
                new ThrowingTransport(new HttpRequestException("failed")),
                ambientFreeFailure).ExecuteAsync(
                new(new("https://example.com/")),
                SecurityAuditInitiator.LocalUser,
                static (_, _) => ValueTask.FromResult(true),
                CancellationToken.None))
            .Should().ThrowAsync<HttpRequestException>();
        await FluentActions.Awaiting(() => new WebPageGet(
                new ThrowingSendTransport(),
                new Audit()).ExecuteAsync(
                new(new("https://example.com/")),
                SecurityAuditInitiator.LocalUser,
                static (_, _) => ValueTask.FromResult(true),
                CancellationToken.None))
            .Should().ThrowAsync<HttpRequestException>();
        await FluentActions.Awaiting(() => new WebPageGet(
                new NullResponseTransport(),
                new Audit()).ExecuteAsync(
                new(new("https://example.com/")),
                SecurityAuditInitiator.LocalUser,
                static (_, _) => ValueTask.FromResult(true),
                CancellationToken.None))
            .Should().ThrowAsync<NullReferenceException>();
        var disposalFailure = new Transport(PublicAddress);
        disposalFailure.Responses.Enqueue(new(
            200,
            new("https://example.com/"),
            "text/plain",
            "utf-8",
            0,
            null,
            [],
            new ThrowingDisposeStream()));
        await FluentActions.Awaiting(() => new WebPageGet(
                disposalFailure,
                new Audit()).ExecuteAsync(
                new(new("https://example.com/")),
                SecurityAuditInitiator.LocalUser,
                static (_, _) => ValueTask.FromResult(true),
                CancellationToken.None))
            .Should().ThrowAsync<InvalidOperationException>();

        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };
        ActivitySource.AddActivityListener(listener);
        using var activity = HostActivity.BeginOperation(
            HostActivityLayer.Application,
            HostOperation.Tool);
        var successTransport = new Transport(PublicAddress);
        successTransport.Responses.Enqueue(Response(
            "https://example.com/",
            "ok",
            mediaType: "text/plain"));
        (await new WebPageGet(successTransport, new Audit()).ExecuteAsync(
            new(new("https://example.com/")),
            SecurityAuditInitiator.LocalUser,
            static (_, _) => ValueTask.FromResult(true),
            CancellationToken.None)).Outcome.Should().Be(WebPageGetOutcome.Succeeded);
        var cancelledAudit = new Audit();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await FluentActions.Awaiting(() => new WebPageGet(
                new Transport(PublicAddress),
                cancelledAudit).ExecuteAsync(
                new(new("https://example.com/")),
                SecurityAuditInitiator.LocalUser,
                static (_, _) => ValueTask.FromResult(true),
                cancellation.Token))
            .Should().ThrowAsync<OperationCanceledException>();
        cancelledAudit.Events[^1].Outcome.Should().Be(SecurityAuditOutcome.Cancelled);

        var deadlineAudit = new Audit();
        var deadline = await new WebPageGet(
            new ThrowingTransport(new OperationCanceledException()),
            deadlineAudit).ExecuteAsync(
            new(new("https://example.com/")),
            SecurityAuditInitiator.LocalUser,
            static (_, _) => ValueTask.FromResult(true),
            CancellationToken.None);
        deadline.Reason.Should().Be("deadline-exceeded");

        var failedAudit = new Audit();
        await FluentActions.Awaiting(() => new WebPageGet(
                new ThrowingTransport(new HttpRequestException("failed")),
                failedAudit).ExecuteAsync(
                new(new("https://example.com/")),
                SecurityAuditInitiator.LocalUser,
                static (_, _) => ValueTask.FromResult(true),
                CancellationToken.None))
            .Should().ThrowAsync<HttpRequestException>();
        failedAudit.Events[^1].Outcome.Should().Be(SecurityAuditOutcome.Failed);
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

    private sealed class ThrowingTransport(Exception exception) : IWebPageTransport
    {
        public Task<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken cancellationToken) =>
            Task.FromException<IReadOnlyList<IPAddress>>(exception);

        public Task<WebPageResponse> SendAsync(
            Uri address,
            IPAddress endpoint,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class ThrowingSendTransport : IWebPageTransport
    {
        public Task<IReadOnlyList<IPAddress>> ResolveAsync(
            string host,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<IPAddress>>([PublicAddress]);

        public Task<WebPageResponse> SendAsync(
            Uri address,
            IPAddress endpoint,
            CancellationToken cancellationToken) =>
            Task.FromException<WebPageResponse>(new HttpRequestException("send failed"));
    }

    private sealed class NullResponseTransport : IWebPageTransport
    {
        public Task<IReadOnlyList<IPAddress>> ResolveAsync(
            string host,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<IPAddress>>([PublicAddress]);

        public Task<WebPageResponse> SendAsync(
            Uri address,
            IPAddress endpoint,
            CancellationToken cancellationToken) =>
            Task.FromResult<WebPageResponse>(null!);
    }

    private sealed class ThrowingDisposeStream : MemoryStream
    {
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            throw new InvalidOperationException("dispose failed");
        }
    }
}
