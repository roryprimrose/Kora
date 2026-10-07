using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Kora.Application.Maintenance;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Maintenance;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.Maintenance;

internal sealed class ReleaseFixture : IDisposable
{
    internal const string Source = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private readonly ActivityListener listener = new()
    {
        ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
        Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
    };
    internal readonly Clock Time = new();
    internal readonly List<Uri> Requests = [];
    internal readonly List<string?> Conditionals = [];
    internal readonly HttpClient Http;
    internal readonly GitHubReleaseMetadataClient Client;
    internal JsonObject Release;
    internal JsonObject Manifest;
    internal Func<HttpRequestMessage, HttpResponseMessage?>? Override;
    internal Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? AsyncOverride;
    internal Func<int, JsonArray>? Pages;
    internal bool Conditional;
    internal string Version;
    internal int Readbacks;

    internal ReleaseFixture(string version = "1.2.3")
    {
        ActivitySource.AddActivityListener(listener);
        Version = version;
        var names = CanonicalRelease.AssetNames(ReleaseVersion.Parse(version));
        Manifest = new()
        {
            ["version"] = version, ["sourceRevision"] = Source, ["unsigned"] = true, ["productionAccepted"] = false,
            ["workflowRun"] = CanonicalRelease.RepositoryUrl + "/actions/runs/1",
            ["assets"] = new JsonArray(names.Where(name => name is not ("release-manifest.json" or "SHA256SUMS.txt"))
                .Select(name => (JsonNode)new JsonObject { ["name"] = name, ["sha256"] = new string('b', 64) }).ToArray()),
        };
        Release = new()
        {
            ["id"] = 1, ["url"] = CanonicalRelease.ApiRoot + "/releases/1",
            ["html_url"] = CanonicalRelease.RepositoryUrl + "/releases/tag/v" + version,
            ["tag_name"] = "v" + version, ["target_commitish"] = Source, ["draft"] = false,
            ["prerelease"] = version.Contains("-beta", StringComparison.Ordinal),
            ["published_at"] = "2026-10-07T00:00:00Z", ["body"] = "<!-- kora-source: " + Source + " -->",
            ["assets"] = new JsonArray(names.Select((name, index) => (JsonNode)new JsonObject
            {
                ["id"] = index + 10, ["name"] = name, ["size"] = 123,
                ["digest"] = "sha256:" + new string('b', 64), ["state"] = "uploaded",
                ["url"] = CanonicalRelease.ApiRoot + "/releases/assets/" + (index + 10),
                ["browser_download_url"] = CanonicalRelease.RepositoryUrl + "/releases/download/v" + version + "/" + name,
            }).ToArray()),
        };
        BindManifest();
        Http = new(new Handler(this)) { Timeout = Timeout.InfiniteTimeSpan };
        Client = new(Http, Time, NullLogger<GitHubReleaseMetadataClient>.Instance);
    }

    internal JsonObject Asset(int index = 0) => Release["assets"]!.AsArray()[index]!.AsObject();
    internal byte[] ManifestBytes => Encoding.UTF8.GetBytes(Manifest.ToJsonString());
    internal void BindManifest()
    {
        var asset = Release["assets"]!.AsArray().Single(node => string.Equals(node!["name"]!.GetValue<string>(),
            "release-manifest.json", StringComparison.Ordinal))!;
        asset["size"] = ManifestBytes.Length;
        asset["digest"] = "sha256:" + Convert.ToHexString(SHA256.HashData(ManifestBytes)).ToLowerInvariant();
    }

    internal async Task<ReleaseCheck> CheckAsync(ReleaseChannel channel = ReleaseChannel.Production,
        string current = "1.0.0", ReleaseArchitecture architecture = ReleaseArchitecture.X64,
        CancellationToken? token = null)
    {
        using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Application, HostOperation.Request);
        var result = await Client.CheckAsync(channel, current, architecture, token ?? TestContext.Current.CancellationToken);
        root.Complete(HostOperationOutcome.Completed);
        return result;
    }

    internal HttpResponseMessage Respond(HttpRequestMessage request, HttpStatusCode status, byte[]? bytes = null)
    {
        var response = new HttpResponseMessage(status) { RequestMessage = request, Content = new ByteArrayContent(bytes ?? []) };
        return response;
    }

    private HttpResponseMessage Default(HttpRequestMessage request)
    {
        var uri = request.RequestUri!;
        var path = uri.PathAndQuery;
        byte[] bytes;
        if (path.Contains("/releases/assets/", StringComparison.Ordinal)) { bytes = ManifestBytes; }
        else if (path.Contains("/git/ref/", StringComparison.Ordinal))
        {
            bytes = Encoding.UTF8.GetBytes(new JsonObject
            {
                ["ref"] = "refs/tags/v" + Version, ["object"] = new JsonObject { ["type"] = "commit", ["sha"] = Source },
            }.ToJsonString());
        }
        else if (path.Contains("?per_page=", StringComparison.Ordinal))
        {
            var page = int.Parse(uri.Query.Split("&page=", StringSplitOptions.None)[1], System.Globalization.CultureInfo.InvariantCulture);
            bytes = Encoding.UTF8.GetBytes((Pages?.Invoke(page) ?? new JsonArray(Release.DeepClone())).ToJsonString());
        }
        else
        {
            if (path.EndsWith("/releases/1", StringComparison.Ordinal)) { Readbacks++; }
            bytes = Encoding.UTF8.GetBytes(Release.ToJsonString());
        }
        var etag = "\"" + Convert.ToHexString(SHA256.HashData(bytes)) + "\"";
        if (Conditional && request.Headers.IfNoneMatch.Any(value => string.Equals(value.ToString(), etag, StringComparison.Ordinal)))
        {
            return Respond(request, HttpStatusCode.NotModified);
        }
        var response = Respond(request, HttpStatusCode.OK, bytes);
        response.Headers.ETag = EntityTagHeaderValue.Parse(etag);
        return response;
    }

    public void Dispose()
    {
        Http.Dispose();
        listener.Dispose();
    }

    private sealed class Handler(ReleaseFixture fixture) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            fixture.Requests.Add(request.RequestUri!);
            fixture.Conditionals.Add(request.Headers.IfNoneMatch.SingleOrDefault()?.ToString());
            if (fixture.AsyncOverride is { } asyncOverride) { return asyncOverride(request, cancellationToken); }
            return Task.FromResult(fixture.Override?.Invoke(request) ?? fixture.Default(request));
        }
    }

    internal sealed class Clock : TimeProvider
    {
        internal DateTimeOffset Now = new(2026, 10, 7, 0, 0, 0, TimeSpan.Zero);
        internal List<Timer> Timers = [];
        public override DateTimeOffset GetUtcNow() => Now;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new Timer(callback, state);
            timer.Change(dueTime, period);
            Timers.Add(timer);
            return timer;
        }
        internal sealed class Timer(TimerCallback callback, object? state) : ITimer
        {
            internal TimeSpan Due;
            internal bool Disposed;
            internal void Fire() => callback(state);
            public bool Change(TimeSpan dueTime, TimeSpan period) { Due = dueTime; return true; }
            public void Dispose() => Disposed = true;
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }
}
