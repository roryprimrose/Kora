using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using Kora.Application.Maintenance;
using Kora.Core.Maintenance;

namespace Kora.Application.UnitTests.Maintenance;

[Collection("Host tracing")]
public sealed class GitHubReleaseMetadataClientTests
{
    [Theory]
    [InlineData("1.0.0", ReleaseAvailability.Available, ReleaseArchitecture.X64)]
    [InlineData("1.2.3", ReleaseAvailability.UpToDate, ReleaseArchitecture.X86)]
    [InlineData("2.0.0-beta1", ReleaseAvailability.UpToDate, ReleaseArchitecture.X64)]
    public async Task Verifies_exact_published_metadata_without_obtaining_any_code(string current, ReleaseAvailability expected, ReleaseArchitecture architecture)
    {
        using var fixture = new ReleaseFixture();
        var result = await fixture.CheckAsync(current: current, architecture: architecture);
        result.Status.Should().Be(expected);
        result.VerifiedAt.Should().Be(fixture.Time.Now);
        result.Release!.SourceRevision.Should().Be(ReleaseFixture.Source);
        result.Release.Assets.Should().HaveCount(9);
        fixture.Readbacks.Should().Be(1);
        fixture.Requests.Should().HaveCount(5);
        fixture.Requests.Should().OnlyContain(uri => uri.Host == "api.github.com" && !uri.AbsolutePath.EndsWith(".zip", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Preview_enumerates_all_bounded_pages_and_orders_beta_numerically_not_by_date()
    {
        using var fixture = new ReleaseFixture("1.2.3-beta10");
        var older = fixture.Release.DeepClone();
        older["tag_name"] = "v1.2.3-beta9";
        older["published_at"] = "2099-01-01T00:00:00Z";
        var draft = fixture.Release.DeepClone();
        draft["draft"] = true;
        var stable = fixture.Release.DeepClone();
        stable["prerelease"] = false;
        fixture.Pages = page => page == 1
            ? new JsonArray(Enumerable.Range(1, 19).Select(_ => draft.DeepClone()).Append(older).ToArray())
            : new JsonArray(stable, fixture.Release.DeepClone());
        var result = await fixture.CheckAsync(ReleaseChannel.Preview, "1.2.3-beta9");
        result.Status.Should().Be(ReleaseAvailability.Available);
        result.Release!.Version.ToString().Should().Be("1.2.3-beta10");
        fixture.Requests.Count(uri => uri.Query.Length != 0).Should().Be(2);
    }

    [Fact]
    public async Task Empty_and_bounded_or_ambiguous_enumeration_never_claims_current()
    {
        using var fixture = new ReleaseFixture("1.2.3-beta10");
        fixture.Pages = _ => [];
        (await fixture.CheckAsync(ReleaseChannel.Preview)).Status.Should().Be(ReleaseAvailability.Unavailable);
        var draft = fixture.Release.DeepClone();
        draft["draft"] = true;
        fixture.Pages = _ => new JsonArray(Enumerable.Range(0, 20).Select(_ => draft.DeepClone()).ToArray());
        (await fixture.CheckAsync(ReleaseChannel.Preview)).Reason.Should().Contain("page bound");
        fixture.Pages = _ => new JsonArray(fixture.Release.DeepClone(), fixture.Release.DeepClone());
        (await fixture.CheckAsync(ReleaseChannel.Preview)).Reason.Should().Contain("duplicate");
        fixture.Release["tag_name"] = "v1.2.3";
        fixture.Pages = _ => new JsonArray(fixture.Release.DeepClone());
        (await fixture.CheckAsync(ReleaseChannel.Preview)).Reason.Should().Contain("not a canonical beta");
        fixture.Override = request => fixture.Respond(request, HttpStatusCode.OK, "null"u8.ToArray());
        (await fixture.CheckAsync(ReleaseChannel.Preview)).Status.Should().Be(ReleaseAvailability.Unknown);
        fixture.Override = null;
        fixture.Pages = _ => new JsonArray(Enumerable.Range(0, 21).Select(_ => draft.DeepClone()).ToArray());
        (await fixture.CheckAsync(ReleaseChannel.Preview)).Status.Should().Be(ReleaseAvailability.Unknown);
    }

    [Theory]
    [InlineData(403, ReleaseAvailability.RateLimited)]
    [InlineData(404, ReleaseAvailability.Unavailable)]
    [InlineData(429, ReleaseAvailability.RateLimited)]
    [InlineData(503, ReleaseAvailability.Unknown)]
    [InlineData(302, ReleaseAvailability.Unknown)]
    public async Task Http_failures_are_explicit_and_not_success(int code, ReleaseAvailability expected)
    {
        using var fixture = new ReleaseFixture();
        fixture.Override = request => fixture.Respond(request, (HttpStatusCode)code);
        var result = await fixture.CheckAsync();
        result.Status.Should().Be(expected);
        result.VerifiedAt.Should().BeNull();
        result.Release.Should().BeNull();
        result.Reason.Should().Contain(code.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task Conditional_bytes_are_reverified_and_failed_checks_do_not_acquire_fresh_timestamps()
    {
        using var fixture = new ReleaseFixture { Conditional = true };
        (await fixture.CheckAsync()).VerifiedAt.Should().Be(fixture.Time.Now);
        fixture.Time.Now = fixture.Time.Now.AddHours(6);
        (await fixture.CheckAsync()).VerifiedAt.Should().Be(fixture.Time.Now);
        fixture.Conditionals.Should().Contain(value => value != null);
        fixture.Override = request => fixture.Respond(request, HttpStatusCode.ServiceUnavailable);
        (await fixture.CheckAsync()).VerifiedAt.Should().BeNull();
        using var empty = new ReleaseFixture();
        empty.Override = request => empty.Respond(request, HttpStatusCode.NotModified);
        (await empty.CheckAsync()).Reason.Should().Contain("no verified cached");
    }

    [Fact]
    public async Task Rejects_each_hostile_release_and_asset_field_before_any_artifact_transfer()
    {
        var changes = new Action<ReleaseFixture>[]
        {
            f => f.Release["id"] = 0, f => f.Release["id"] = "1", f => f.Release["id"] = 1.5,
            f => f.Release.Remove("id"), f => f.Release["draft"] = true, f => f.Release["draft"] = "false",
            f => f.Release["prerelease"] = true, f => f.Release["tag_name"] = "1.2.3",
            f => f.Release["tag_name"] = "v1.2.3-rc1", f => f.Release["url"] = "https://evil.example/releases/1",
            f => f.Release["html_url"] = "https://evil.example/", f => f.Release["target_commitish"] = "main",
            f => f.Release["body"] = "", f => f.Release["body"] = "<!-- kora-source: " + new string('b', 40) + " -->",
            f => f.Release["body"] = f.Release["body"]!.GetValue<string>() + f.Release["body"]!.GetValue<string>(),
            f => f.Release["body"] = new string('x', 32_769), f => f.Release["body"] = null,
            f => f.Release["published_at"] = "bad", f => f.Release["assets"] = new JsonObject(),
            f => f.Release["assets"]!.AsArray().RemoveAt(0),
            f => f.Asset()["name"] = "evil.exe", f => f.Asset(1)["name"] = f.Asset()["name"]!.DeepClone(),
            f => f.Asset(1)["id"] = f.Asset()["id"]!.DeepClone(), f => f.Asset()["size"] = 0,
            f => f.Asset()["state"] = "new", f => f.Asset()["digest"] = "sha256:no",
            f => f.Asset()["url"] = "https://evil.example/", f => f.Asset()["browser_download_url"] = "https://evil.example/",
            f => f.Asset()["size"] = 2_147_483_649L, f => f.Asset(8)["size"] = 16_777_217,
            f => f.Asset(7)["size"] = 131_073,
        };
        foreach (var change in changes)
        {
            using var fixture = new ReleaseFixture();
            change(fixture);
            var result = await fixture.CheckAsync();
            result.Status.Should().Be(ReleaseAvailability.Unknown);
            result.VerifiedAt.Should().BeNull();
        }
        using var preview = new ReleaseFixture("1.2.3-beta1");
        (await preview.CheckAsync()).Status.Should().Be(ReleaseAvailability.Unknown);
    }

    [Fact]
    public async Task Manifest_rejects_each_conflict_and_digest_or_size_change()
    {
        var changes = new Action<ReleaseFixture>[]
        {
            f => f.Manifest["version"] = "1.2.4", f => f.Manifest["sourceRevision"] = new string('b', 40),
            f => f.Manifest["unsigned"] = false, f => f.Manifest["productionAccepted"] = true,
            f => f.Manifest["workflowRun"] = "https://evil.example/run/1",
            f => f.Manifest["assets"] = new JsonObject(), f => f.Manifest["assets"]!.AsArray().RemoveAt(0),
            f => f.Manifest["assets"]![1]!["name"] = f.Manifest["assets"]![0]!["name"]!.DeepClone(),
            f => f.Manifest["assets"]![0]!["name"] = "release-manifest.json",
            f => f.Manifest["assets"]![0]!["name"] = "SHA256SUMS.txt",
            f => f.Manifest["assets"]![0]!["name"] = "evil.zip",
            f => f.Manifest["assets"]![0]!["sha256"] = new string('c', 64),
        };
        foreach (var change in changes)
        {
            using var fixture = new ReleaseFixture();
            change(fixture);
            fixture.BindManifest();
            (await fixture.CheckAsync()).Status.Should().Be(ReleaseAvailability.Unknown);
        }
        using var changed = new ReleaseFixture();
        changed.Manifest["unsigned"] = false;
        (await changed.CheckAsync()).Reason.Should().Contain("size/digest changed");
        changed.BindManifest();
        changed.Asset(7)["digest"] = "sha256:" + new string('c', 64);
        (await changed.CheckAsync()).Reason.Should().Contain("size/digest changed");
    }

    [Fact]
    public async Task Malformed_oversized_implicit_redirect_and_cancelled_checks_are_unknown()
    {
        using var fixture = new ReleaseFixture();
        foreach (var body in new[] { "{", "{\"id\":1,\"id\":2}", "[]", "{\"id\":null}" })
        {
            fixture.Override = request => fixture.Respond(request, HttpStatusCode.OK, Encoding.UTF8.GetBytes(body));
            (await fixture.CheckAsync()).Status.Should().Be(ReleaseAvailability.Unknown);
        }
        fixture.Override = request => fixture.Respond(request, HttpStatusCode.OK, new byte[1_048_577]);
        (await fixture.CheckAsync()).Reason.Should().Contain("Content-Length");
        fixture.Override = request =>
        {
            var response = fixture.Respond(request, HttpStatusCode.OK);
            response.RequestMessage = new(HttpMethod.Get, "https://evil.example/");
            return response;
        };
        (await fixture.CheckAsync()).Reason.Should().Contain("implicit");
        fixture.Override = null;
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        fixture.AsyncOverride = (_, token) => Task.FromCanceled<HttpResponseMessage>(token);
        (await fixture.CheckAsync(token: cancellation.Token)).Reason.Should().Contain("cancelled");
        fixture.AsyncOverride = (_, _) => throw new HttpRequestException("fixture");
        (await fixture.CheckAsync()).Reason.Should().Contain("HttpRequestException");
        fixture.AsyncOverride = (_, _) => throw new IOException("fixture");
        (await fixture.CheckAsync()).Reason.Should().Contain("IOException");
        fixture.AsyncOverride = (_, _) => throw new OperationCanceledException("timeout fixture");
        (await fixture.CheckAsync()).Reason.Should().Contain("timeout");
        (await fixture.CheckAsync((ReleaseChannel)99)).Status.Should().Be(ReleaseAvailability.Unknown);
        (await fixture.CheckAsync(current: "development")).Status.Should().Be(ReleaseAvailability.Unknown);
        (await fixture.CheckAsync(architecture: ReleaseArchitecture.Unsupported)).Status.Should().Be(ReleaseAvailability.Unknown);
    }

    [Fact]
    public async Task Rate_limit_deadlines_are_bounded_and_explicit()
    {
        using var fixture = new ReleaseFixture();
        foreach (var seconds in new[] { 0, 600, 100_000 })
        {
            fixture.Override = request =>
            {
                var response = fixture.Respond(request, HttpStatusCode.TooManyRequests);
                response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(seconds));
                return response;
            };
            var retry = (await fixture.CheckAsync()).RetryAt;
            retry.Should().BeOnOrAfter(fixture.Time.Now.AddMinutes(1)).And.BeOnOrBefore(fixture.Time.Now.AddHours(24));
        }
        fixture.Override = request =>
        {
            var response = fixture.Respond(request, HttpStatusCode.Forbidden);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(fixture.Time.Now.AddHours(2));
            response.Headers.Add("X-RateLimit-Reset", fixture.Time.Now.AddHours(3).ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture));
            return response;
        };
        (await fixture.CheckAsync()).RetryAt.Should().Be(fixture.Time.Now.AddHours(3));
        foreach (var reset in new[] { "invalid", "-1", "253402300800" })
        {
            fixture.Override = request =>
            {
                var response = fixture.Respond(request, HttpStatusCode.Forbidden);
                response.Headers.Add("X-RateLimit-Reset", reset);
                return response;
            };
            (await fixture.CheckAsync()).RetryAt.Should().Be(fixture.Time.Now.AddHours(1));
        }
    }

    [Fact]
    public async Task Tags_are_bounded_exact_and_rechecked_including_annotated_identity()
    {
        using var fixture = new ReleaseFixture();
        JsonObject reference = new()
        {
            ["ref"] = "refs/tags/v1.2.3", ["object"] = new JsonObject { ["type"] = "tag", ["sha"] = ReleaseFixture.Source },
        };
        JsonObject annotated = new()
        {
            ["sha"] = ReleaseFixture.Source, ["object"] = new JsonObject { ["type"] = "commit", ["sha"] = ReleaseFixture.Source },
        };
        fixture.Override = request => request.RequestUri!.AbsolutePath.Contains("/git/", StringComparison.Ordinal)
            ? fixture.Respond(request, HttpStatusCode.OK, Encoding.UTF8.GetBytes(
                (request.RequestUri.AbsolutePath.Contains("/git/ref/", StringComparison.Ordinal) ? reference : annotated).ToJsonString()))
            : null;
        (await fixture.CheckAsync()).Status.Should().Be(ReleaseAvailability.Available);
        reference["ref"] = "refs/tags/evil";
        (await fixture.CheckAsync()).Reason.Should().Contain("Tag identity");
        reference["ref"] = "refs/tags/v1.2.3";
        reference["object"]!["sha"] = "main";
        (await fixture.CheckAsync()).Reason.Should().Contain("malformed");
        reference["object"]!["sha"] = ReleaseFixture.Source;
        annotated["sha"] = new string('c', 40);
        (await fixture.CheckAsync()).Reason.Should().Contain("Annotated tag identity");
        annotated["sha"] = ReleaseFixture.Source;
        annotated["object"]!["type"] = "tree";
        (await fixture.CheckAsync()).Reason.Should().Contain("exact release source");
        annotated["object"]!["type"] = "commit";
        annotated["object"]!["sha"] = new string('c', 40);
        (await fixture.CheckAsync()).Reason.Should().Contain("exact release source");
        annotated["object"]!["sha"] = ReleaseFixture.Source;
        annotated["object"]!["type"] = "tag";
        (await fixture.CheckAsync()).Status.Should().Be(ReleaseAvailability.Unknown);
        fixture.Requests.Count(uri => uri.AbsolutePath.Contains("/git/tags/", StringComparison.Ordinal)).Should().BeGreaterThanOrEqualTo(8);
    }

    [Fact]
    public async Task Changed_readback_id_version_source_and_assets_never_establish_availability()
    {
        var changes = new Action<JsonObject>[]
        {
            node => { node["id"] = 2; node["url"] = CanonicalRelease.ApiRoot + "/releases/2"; },
            node => { node["target_commitish"] = new string('c', 40); node["body"] = "<!-- kora-source: " + new string('c', 40) + " -->"; },
            node => node["assets"]![0]!["size"] = 124,
        };
        foreach (var change in changes)
        {
            using var fixture = new ReleaseFixture();
            fixture.Override = request =>
            {
                if (!request.RequestUri!.AbsolutePath.EndsWith("/releases/1", StringComparison.Ordinal)) { return null; }
                var readback = fixture.Release.DeepClone().AsObject();
                change(readback);
                return fixture.Respond(request, HttpStatusCode.OK, Encoding.UTF8.GetBytes(readback.ToJsonString()));
            };
            (await fixture.CheckAsync()).Reason.Should().Contain("changed during verification");
        }
        using var version = new ReleaseFixture();
        using var newer = new ReleaseFixture("1.2.4");
        version.Override = request => request.RequestUri!.AbsolutePath.EndsWith("/releases/1", StringComparison.Ordinal)
            ? version.Respond(request, HttpStatusCode.OK, Encoding.UTF8.GetBytes(newer.Release.ToJsonString())) : null;
        (await version.CheckAsync()).Reason.Should().Contain("changed during verification");
    }

    [Theory]
    [InlineData(302)]
    [InlineData(307)]
    public async Task Only_one_manifest_CDN_redirect_is_admitted_without_credentials(int code)
    {
        using var fixture = new ReleaseFixture();
        var location = new Uri("https://release-assets.githubusercontent.com/github-production-release-asset/1/manifest");
        fixture.Override = request =>
        {
            request.Headers.Authorization.Should().BeNull();
            if (string.Equals(request.RequestUri!.Host, "release-assets.githubusercontent.com", StringComparison.Ordinal))
            {
                return fixture.Respond(request, HttpStatusCode.OK, fixture.ManifestBytes);
            }
            if (!request.RequestUri.AbsolutePath.Contains("/releases/assets/", StringComparison.Ordinal)) { return null; }
            var response = fixture.Respond(request, (HttpStatusCode)code);
            response.Headers.Location = location;
            return response;
        };
        (await fixture.CheckAsync()).Status.Should().Be(ReleaseAvailability.Available);
        fixture.Override = request =>
        {
            if (string.Equals(request.RequestUri!.Host, "release-assets.githubusercontent.com", StringComparison.Ordinal))
            {
                var response = fixture.Respond(request, HttpStatusCode.Found);
                response.Headers.Location = new("https://evil.example/");
                return response;
            }
            if (!request.RequestUri.AbsolutePath.Contains("/releases/assets/", StringComparison.Ordinal)) { return null; }
            var redirect = fixture.Respond(request, (HttpStatusCode)code);
            redirect.Headers.Location = location;
            return redirect;
        };
        (await fixture.CheckAsync()).Status.Should().Be(ReleaseAvailability.Unknown);
    }

    [Fact]
    public async Task Hostile_manifest_redirects_and_implicit_CDN_redirects_are_denied()
    {
        var locations = new string?[]
        {
            null, "relative", "http://release-assets.githubusercontent.com/github-production-release-asset/1",
            "https://evil.example/github-production-release-asset/1", "https://release-assets.githubusercontent.com:444/github-production-release-asset/1",
            "https://user@release-assets.githubusercontent.com/github-production-release-asset/1",
            "https://release-assets.githubusercontent.com/github-production-release-asset/1#fragment",
            "https://release-assets.githubusercontent.com/arbitrary",
        };
        foreach (var location in locations)
        {
            using var fixture = new ReleaseFixture();
            fixture.Override = request =>
            {
                if (!request.RequestUri!.AbsolutePath.Contains("/releases/assets/", StringComparison.Ordinal)) { return null; }
                var response = fixture.Respond(request, HttpStatusCode.Found);
                response.Headers.Location = location is null ? null : new Uri(location, UriKind.RelativeOrAbsolute);
                return response;
            };
            (await fixture.CheckAsync()).Reason.Should().Contain("not the admitted");
        }
        foreach (var missingRequest in new[] { false, true })
        {
            using var fixture = new ReleaseFixture();
            fixture.Override = request =>
            {
                if (string.Equals(request.RequestUri!.Host, "release-assets.githubusercontent.com", StringComparison.Ordinal))
                {
                    var implicitResponse = fixture.Respond(request, HttpStatusCode.OK);
                    implicitResponse.RequestMessage = missingRequest ? null : new(HttpMethod.Get, "https://evil.example/");
                    return implicitResponse;
                }
                if (!request.RequestUri.AbsolutePath.Contains("/releases/assets/", StringComparison.Ordinal)) { return null; }
                var response = fixture.Respond(request, HttpStatusCode.Found);
                response.Headers.Location = new("https://release-assets.githubusercontent.com/github-production-release-asset/1");
                return response;
            };
            (await fixture.CheckAsync()).Reason.Should().Contain("Implicit CDN");
        }
    }

    [Fact]
    public async Task Streaming_bounds_content_length_and_uncacheable_metadata_are_checked()
    {
        using var fixture = new ReleaseFixture();
        fixture.Override = request =>
        {
            var response = fixture.Respond(request, HttpStatusCode.OK);
            response.Content = new UnboundedLengthContent(new byte[GitHubReleaseMetadataClient.MaximumResponseBytes + 1]);
            return response;
        };
        (await fixture.CheckAsync()).Reason.Should().Contain("streaming bound");
        fixture.Override = request =>
        {
            var response = fixture.Respond(request, HttpStatusCode.OK, "{}"u8.ToArray());
            response.Content.Headers.ContentLength = 1;
            return response;
        };
        (await fixture.CheckAsync()).Reason.Should().Contain("Content-Length changed");
        fixture.Override = request =>
        {
            var response = fixture.Respond(request, HttpStatusCode.OK, Encoding.UTF8.GetBytes(fixture.Release.ToJsonString()));
            response.Content = new UnboundedLengthContent(Encoding.UTF8.GetBytes(fixture.Release.ToJsonString()));
            response.Headers.ETag = new EntityTagHeaderValue("\"" + new string('x', 257) + "\"");
            return response;
        };
        (await fixture.CheckAsync()).Status.Should().Be(ReleaseAvailability.Unknown);
        fixture.Override = request => new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = null };
        (await fixture.CheckAsync()).Reason.Should().Contain("implicit");
    }

    private sealed class UnboundedLengthContent(byte[] bytes) : HttpContent
    {
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            stream.WriteAsync(bytes).AsTask();
        protected override Task<Stream> CreateContentReadStreamAsync() => Task.FromResult<Stream>(new MemoryStream(bytes));
    }

    [Fact]
    public async Task Conditional_cache_is_bounded_across_many_release_identities()
    {
        using var fixture = new ReleaseFixture();
        for (var id = 2; id < 30; id++)
        {
            fixture.Release["id"] = id;
            fixture.Release["url"] = CanonicalRelease.ApiRoot + "/releases/" + id.ToString(System.Globalization.CultureInfo.InvariantCulture);
            (await fixture.CheckAsync()).Status.Should().Be(ReleaseAvailability.Available);
        }
        fixture.Requests.Should().OnlyContain(uri => uri.Host == "api.github.com");
    }
}
