using System.Net;
using System.Text;
using System.Text.Json.Nodes;

using AwesomeAssertions;

using Kora.Application.Diagnostics;
using Kora.Core.Maintenance;

namespace Kora.Application.UnitTests.Diagnostics;

public sealed class GitHubIssueLookupTests
{
    private const string Fingerprint = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Fact]
    public async Task Searches_only_the_fingerprint_and_returns_open_and_closed_exact_matches()
    {
        using var handler = new Handler(_ => Response(Result(Issue(12), Issue(13, closed: true))));
        using var http = new HttpClient(handler);
        var result = await new GitHubIssueLookup(http).FindAsync(Fingerprint, static () => true, TestContext.Current.CancellationToken);
        result.Matches.Select(match => match.Number).Should().Equal(12, 13);
        result.Matches.Select(match => match.Closed).Should().Equal(false, true);
        result.Matches[0].Address.AbsoluteUri.Should().Be(CanonicalRelease.RepositoryUrl + "/issues/12");
        handler.Requests.Should().ContainSingle();
        var request = handler.Requests.Single();
        request.Method.Should().Be(HttpMethod.Get);
        request.RequestUri!.Host.Should().Be("api.github.com");
        Uri.UnescapeDataString(request.RequestUri.Query).Should().Contain("repo:roryprimrose/Kora is:issue in:body")
            .And.Contain("Kora error fingerprint: v1:" + Fingerprint);
        request.Headers.Authorization.Should().BeNull();
        request.Content.Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("gggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggg")]
    public async Task Invalid_fingerprints_never_send(string fingerprint)
    {
        using var handler = new Handler(_ => Response(Result()));
        using var http = new HttpClient(handler);
        await FluentActions.Awaiting(() => new GitHubIssueLookup(http).FindAsync(fingerprint, static () => true,
            TestContext.Current.CancellationToken)).Should().ThrowAsync<InvalidDataException>();
        handler.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task Revocation_before_send_after_headers_during_read_or_before_return_never_returns_a_result(int allowedChecks)
    {
        using var handler = new Handler(_ => Response(Result()));
        using var http = new HttpClient(handler);
        var calls = 0;
        await FluentActions.Awaiting(() => new GitHubIssueLookup(http).FindAsync(Fingerprint, () => calls++ < allowedChecks,
            TestContext.Current.CancellationToken)).Should().ThrowAsync<InvalidOperationException>();
        handler.Requests.Should().HaveCount(allowedChecks == 0 ? 0 : 1);
    }

    [Fact]
    public async Task Cancelled_lookup_never_sends_and_empty_complete_lookup_is_distinct_from_failure()
    {
        using var handler = new Handler(_ => Response(Result()));
        using var http = new HttpClient(handler);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        await FluentActions.Awaiting(() => new GitHubIssueLookup(http).FindAsync(Fingerprint, static () => true,
            cancelled.Token)).Should().ThrowAsync<OperationCanceledException>();
        handler.Requests.Should().BeEmpty();
        (await new GitHubIssueLookup(http).FindAsync(Fingerprint, static () => true,
            TestContext.Current.CancellationToken)).Matches.Should().BeEmpty();
    }

    [Theory]
    [InlineData(302)]
    [InlineData(403)]
    [InlineData(429)]
    [InlineData(503)]
    public async Task Http_failure_is_not_an_empty_success(int status)
    {
        using var handler = new Handler(_ => new HttpResponseMessage((HttpStatusCode)status));
        using var http = new HttpClient(handler);
        await FluentActions.Awaiting(() => new GitHubIssueLookup(http).FindAsync(Fingerprint, static () => true,
            TestContext.Current.CancellationToken)).Should().ThrowAsync<HttpRequestException>();
        handler.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task Incomplete_or_malformed_searches_cannot_establish_a_new_issue()
    {
        var invalid = new[]
        {
            "[]", "null", "{}", """{"incomplete_results":true,"total_count":0,"items":[]}""",
            """{"incomplete_results":false,"items":[]}""",
            """{"incomplete_results":false,"total_count":-1,"items":[]}""",
            """{"incomplete_results":false,"total_count":21,"items":[]}""",
            """{"incomplete_results":false,"total_count":0}""",
            """{"incomplete_results":false,"total_count":0,"items":{}}""",
            """{"incomplete_results":false,"total_count":1,"items":[]}""",
        };
        foreach (var json in invalid)
        {
            using var handler = new Handler(_ => Response(json));
            using var http = new HttpClient(handler);
            await FluentActions.Awaiting(() => new GitHubIssueLookup(http).FindAsync(Fingerprint, static () => true,
                TestContext.Current.CancellationToken)).Should().ThrowAsync<InvalidDataException>();
        }
    }

    [Fact]
    public async Task Unrelated_issues_pull_requests_and_hostile_fields_fail_closed()
    {
        var changes = new Action<JsonObject>[]
        {
            item => item["number"] = 0,
            item => item["title"] = "",
            item => item["title"] = null,
            item => item["title"] = new string('x', 257),
            item => item["title"] = "title\ninjection",
            item => item["state"] = "unknown",
            item => item["body"] = null,
            item => item["body"] = "prefix Kora error fingerprint: v1:" + Fingerprint,
            item => item["html_url"] = "https://evil.example/issues/12",
            item => item["repository_url"] = "https://api.github.com/repos/other/repo",
            item => item["pull_request"] = new JsonObject(),
        };
        foreach (var change in changes)
        {
            var item = Issue(12);
            change(item);
            using var handler = new Handler(_ => Response(Result(item)));
            using var http = new HttpClient(handler);
            await FluentActions.Awaiting(() => new GitHubIssueLookup(http).FindAsync(Fingerprint, static () => true,
                TestContext.Current.CancellationToken)).Should().ThrowAsync<InvalidDataException>();
        }
        using var duplicate = new Handler(_ => Response(Result(Issue(12), Issue(12))));
        using var client = new HttpClient(duplicate);
        await FluentActions.Awaiting(() => new GitHubIssueLookup(client).FindAsync(Fingerprint, static () => true,
            TestContext.Current.CancellationToken)).Should().ThrowAsync<InvalidDataException>();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Bounds_advertised_and_streamed_response_bytes(bool advertised)
    {
        var payload = new string(' ', GitHubIssueLookup.MaximumResponseBytes + 1);
        using var handler = new Handler(_ =>
        {
            var response = Response(payload);
            if (!advertised) { response.Content = new UnboundedContent(Encoding.UTF8.GetBytes(payload)); }
            return response;
        });
        using var http = new HttpClient(handler);
        await FluentActions.Awaiting(() => new GitHubIssueLookup(http).FindAsync(Fingerprint, static () => true,
            TestContext.Current.CancellationToken)).Should().ThrowAsync<InvalidDataException>();
    }

    private static JsonObject Issue(int number, bool closed = false) => new()
    {
        ["number"] = number, ["title"] = "Example failure", ["state"] = closed ? "closed" : "open",
        ["body"] = "User report\r\nKora error fingerprint: v1:" + Fingerprint + "\r\n",
        ["html_url"] = CanonicalRelease.RepositoryUrl + "/issues/" + number.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ["repository_url"] = CanonicalRelease.ApiRoot,
    };

    private static string Result(params JsonObject[] items) => new JsonObject
    {
        ["incomplete_results"] = false, ["total_count"] = items.Length,
        ["items"] = new JsonArray(items.Select(item => (JsonNode)item).ToArray()),
    }.ToJsonString();

    private static HttpResponseMessage Response(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(respond(request));
        }
    }

    private sealed class UnboundedContent(byte[] bytes) : HttpContent
    {
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => stream.WriteAsync(bytes).AsTask();
        protected override Task<Stream> CreateContentReadStreamAsync() => Task.FromResult<Stream>(new MemoryStream(bytes));
    }
}
