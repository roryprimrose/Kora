using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

using Kora.Core.Diagnostics;
using Kora.Core.Maintenance;
using Microsoft.Extensions.Logging;

namespace Kora.Application.Maintenance;

public sealed partial class GitHubReleaseMetadataClient(
    HttpClient http, TimeProvider time, ILogger<GitHubReleaseMetadataClient> logger) : IReleaseMetadataClient
{
    public const int MaximumPages = 5;
    public const int PageSize = 20;
    public const int MaximumResponseBytes = 1_048_576;
    public const int MaximumManifestBytes = 131_072;
    public static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(30);
    private readonly Dictionary<Uri, CachedResponse> cache = [];
    private readonly Lock cacheGate = new();

    public async Task<ReleaseCheck> CheckAsync(ReleaseChannel channel, string currentVersion,
        ReleaseArchitecture architecture, CancellationToken cancellationToken)
    {
        var attempted = time.GetUtcNow();
        using var activity = HostActivity.BeginChild(HostActivityLayer.Application, HostOperation.Runtime);
        using var timeout = new CancellationTokenSource(CheckTimeout, time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        ReleaseCheck result;
        try
        {
            if (!Enum.IsDefined(channel)) { throw new InvalidDataException("Unknown release channel."); }
            var current = ReleaseVersion.Parse(currentVersion);
            _ = CanonicalRelease.Rid(architecture);
            var release = await DiscoverAsync(channel, linked.Token).ConfigureAwait(false);
            if (release is null)
            {
                result = new(ReleaseAvailability.Unavailable, "No published release exists in the selected channel.", attempted);
            }
            else
            {
                var metadata = ParseRelease(release.Value, channel, architecture);
                await VerifyTagAsync(metadata, linked.Token).ConfigureAwait(false);
                var manifest = metadata.Assets.Single(asset => string.Equals(asset.Name, "release-manifest.json", StringComparison.Ordinal));
                if (manifest.Bytes > MaximumManifestBytes) { throw new InvalidDataException("Release manifest exceeds its size bound."); }
                var bytes = await GetAsync(new(CanonicalRelease.ApiRoot + "/releases/assets/" + Number(manifest.Id)),
                    MaximumManifestBytes, true, linked.Token).ConfigureAwait(false);
                if (bytes.LongLength != manifest.Bytes ||
                    !string.Equals(Convert.ToHexString(SHA256.HashData(bytes)), manifest.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("Release manifest size/digest changed.");
                }
                using var document = ParseJson(bytes);
                ValidateManifest(document.RootElement, metadata);
                // The published identity must still describe the exact manifest and nine assets.
                var readbackBytes = await GetAsync(new(CanonicalRelease.ApiRoot + "/releases/" + Number(metadata.Id)),
                    MaximumResponseBytes, false, linked.Token).ConfigureAwait(false);
                using var readback = ParseJson(readbackBytes);
                var final = ParseRelease(readback.RootElement, channel, architecture);
                if (final.Id != metadata.Id || final.Version != metadata.Version ||
                    !string.Equals(final.SourceRevision, metadata.SourceRevision, StringComparison.Ordinal) || !final.Assets.SequenceEqual(metadata.Assets))
                {
                    throw new InvalidDataException("Published release identity/assets changed during verification.");
                }
                await VerifyTagAsync(metadata, linked.Token).ConfigureAwait(false);
                linked.Token.ThrowIfCancellationRequested();
                result = new(metadata.Version.CompareTo(current) > 0 ? ReleaseAvailability.Available : ReleaseAvailability.UpToDate,
                    "Canonical published metadata is consistent; replacement remains manual and unsigned.",
                    attempted, time.GetUtcNow(), metadata);
            }
        }
        catch (MetadataRequestException exception)
        {
            result = new(exception.Status, exception.Message, attempted, RetryAt: exception.RetryAt);
        }
        catch (OperationCanceledException)
        {
            result = new(ReleaseAvailability.Unknown, cancellationToken.IsCancellationRequested
                ? "Metadata check cancelled; no current-version claim." : "Metadata check exceeded its total timeout.", attempted);
        }
        catch (Exception exception) when (exception is InvalidDataException or JsonException or HttpRequestException or IOException or RegexMatchTimeoutException)
        {
            result = new(ReleaseAvailability.Unknown, exception is InvalidDataException
                ? exception.Message : "Metadata transport/JSON failed: " + exception.GetType().Name + ".", attempted);
        }
        CheckOutcome(logger, channel, result.Status);
        activity.Complete(result.VerifiedAt is not null ? HostOperationOutcome.Completed : HostOperationOutcome.Failed);
        return result;
    }

    private async Task<JsonElement?> DiscoverAsync(ReleaseChannel channel, CancellationToken token)
    {
        if (channel == ReleaseChannel.Production)
        {
            using var document = ParseJson(await GetAsync(new(CanonicalRelease.ApiRoot + "/releases/latest"),
                MaximumResponseBytes, false, token).ConfigureAwait(false));
            return document.RootElement.Clone();
        }
        JsonElement? best = null;
        ReleaseVersion? bestVersion = null;
        var tags = new HashSet<string>(StringComparer.Ordinal);
        for (var page = 1; page <= MaximumPages; page++)
        {
            using var document = ParseJson(await GetAsync(new(CanonicalRelease.ApiRoot + "/releases?per_page="
                + Number(PageSize) + "&page=" + Number(page)), MaximumResponseBytes, false, token).ConfigureAwait(false));
            var records = document.RootElement;
            if (records.ValueKind != JsonValueKind.Array || records.GetArrayLength() > PageSize)
            {
                throw new InvalidDataException("Release enumeration is malformed or exceeds its page bound.");
            }
            foreach (var record in records.EnumerateArray())
            {
                if (Boolean(record, "draft")) { continue; }
                if (!Boolean(record, "prerelease")) { continue; }
                var tag = Text(record, "tag_name");
                var version = ParseTag(tag);
                if (version.Beta is null) { throw new InvalidDataException("Preview release is not a canonical beta."); }
                if (!tags.Add(tag)) { throw new InvalidDataException("Ambiguous duplicate published version."); }
                if (bestVersion is null || version.CompareTo(bestVersion) > 0)
                {
                    best = record.Clone();
                    bestVersion = version;
                }
            }
            if (records.GetArrayLength() < PageSize) { return best; }
        }
        throw new InvalidDataException("Preview enumeration reached its page bound; latest availability is Unknown.");
    }

    private static ReleaseMetadata ParseRelease(JsonElement record, ReleaseChannel channel, ReleaseArchitecture architecture)
    {
        var id = Positive(record, "id");
        var version = ParseTag(Text(record, "tag_name"));
        if (Boolean(record, "draft") || Boolean(record, "prerelease") != (version.Beta is not null) ||
            (channel == ReleaseChannel.Production) != (version.Beta is null))
        {
            throw new InvalidDataException("Release is not published in the requested stable/beta channel.");
        }
        if (!string.Equals(Text(record, "url"), CanonicalRelease.ApiRoot + "/releases/" + Number(id), StringComparison.Ordinal) ||
            !string.Equals(Text(record, "html_url"), CanonicalRelease.Page(version).AbsoluteUri, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Release repository/page identity is not canonical.");
        }
        var source = Text(record, "target_commitish");
        if (!ShaPattern().IsMatch(source)) { throw new InvalidDataException("Release source is not an immutable revision."); }
        var body = Text(record, "body");
        if (body.Length > 32_768) { throw new InvalidDataException("Release notes exceed the metadata bound."); }
        var markers = SourcePattern().Matches(body);
        if (markers.Count != 1 || !string.Equals(markers[0].Groups["source"].Value, source, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Release source marker is missing, ambiguous or changed.");
        }
        _ = Timestamp(record, "published_at");
        var names = CanonicalRelease.AssetNames(version);
        var assetsElement = Property(record, "assets");
        if (assetsElement.ValueKind != JsonValueKind.Array || assetsElement.GetArrayLength() != names.Count)
        {
            throw new InvalidDataException("Release must contain exactly the nine published assets.");
        }
        var assets = new List<ReleaseAsset>();
        var ids = new HashSet<long>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var asset in assetsElement.EnumerateArray())
        {
            var name = Text(asset, "name");
            var assetId = Positive(asset, "id");
            var size = Positive(asset, "size");
            var digest = Text(asset, "digest");
            if (!names.Contains(name, StringComparer.Ordinal) || !seen.Add(name) || !ids.Add(assetId) ||
                !string.Equals(Text(asset, "state"), "uploaded", StringComparison.Ordinal) || !DigestPattern().IsMatch(digest) ||
                !string.Equals(Text(asset, "url"), CanonicalRelease.ApiRoot + "/releases/assets/" + Number(assetId), StringComparison.Ordinal) ||
                !string.Equals(Text(asset, "browser_download_url"), CanonicalRelease.RepositoryUrl + "/releases/download/v" + version.ToString() + "/" + name, StringComparison.Ordinal) ||
                size > 2_147_483_648L || (name.EndsWith("-source-tools.zip", StringComparison.Ordinal) && size > 16_777_216))
            {
                throw new InvalidDataException("Release asset name/ID/origin/state/size/digest is invalid or ambiguous.");
            }
            assets.Add(new(assetId, name, size, digest[7..].ToLowerInvariant()));
        }
        return new(id, version, source, architecture, assets.OrderBy(asset => asset.Name, StringComparer.Ordinal).ToArray());
    }

    private async Task VerifyTagAsync(ReleaseMetadata metadata, CancellationToken token)
    {
        using var reference = ParseJson(await GetAsync(new(CanonicalRelease.ApiRoot + "/git/ref/tags/v" + metadata.Version.ToString()),
            MaximumManifestBytes, false, token).ConfigureAwait(false));
        if (!string.Equals(Text(reference.RootElement, "ref"), "refs/tags/v" + metadata.Version.ToString(), StringComparison.Ordinal))
        {
            throw new InvalidDataException("Tag identity changed.");
        }
        var target = Property(reference.RootElement, "object").Clone();
        for (var depth = 0; string.Equals(Text(target, "type"), "tag", StringComparison.Ordinal) && depth < 8; depth++)
        {
            var sha = Text(target, "sha");
            if (!ShaPattern().IsMatch(sha)) { throw new InvalidDataException("Annotated tag revision is malformed."); }
            using var tag = ParseJson(await GetAsync(new(CanonicalRelease.ApiRoot + "/git/tags/" + sha),
                MaximumManifestBytes, false, token).ConfigureAwait(false));
            if (!string.Equals(Text(tag.RootElement, "sha"), sha, StringComparison.Ordinal)) { throw new InvalidDataException("Annotated tag identity changed."); }
            target = Property(tag.RootElement, "object").Clone();
        }
        if (!string.Equals(Text(target, "type"), "commit", StringComparison.Ordinal) ||
            !string.Equals(Text(target, "sha"), metadata.SourceRevision, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Tag does not resolve to the exact release source revision.");
        }
    }

    private static void ValidateManifest(JsonElement manifest, ReleaseMetadata metadata)
    {
        if (!string.Equals(Text(manifest, "version"), metadata.Version.ToString(), StringComparison.Ordinal) ||
            !string.Equals(Text(manifest, "sourceRevision"), metadata.SourceRevision, StringComparison.Ordinal) ||
            !Boolean(manifest, "unsigned") || Boolean(manifest, "productionAccepted") ||
            !WorkflowPattern().IsMatch(Text(manifest, "workflowRun")))
        {
            throw new InvalidDataException("Manifest conflicts with the exact unsigned POC release/source.");
        }
        var assets = Property(manifest, "assets");
        if (assets.ValueKind != JsonValueKind.Array || assets.GetArrayLength() != 7)
        {
            throw new InvalidDataException("Manifest must describe exactly seven non-self/checksum assets.");
        }
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var record in assets.EnumerateArray())
        {
            var name = Text(record, "name");
            var expected = metadata.Assets.SingleOrDefault(asset => string.Equals(asset.Name, name, StringComparison.Ordinal));
            if (!seen.Add(name) || name is "release-manifest.json" or "SHA256SUMS.txt" || expected is null ||
                !string.Equals(Text(record, "sha256"), expected.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Manifest expected final-byte digest/name differs from release assets.");
            }
        }
    }

    private async Task<byte[]> GetAsync(Uri uri, int limit, bool manifest, CancellationToken token)
    {
        CachedResponse? cached;
        lock (cacheGate) { cache.TryGetValue(uri, out cached); }
        using var request = CreateRequest(uri, manifest, cached?.ETag);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        if (response.RequestMessage?.RequestUri != uri) { throw new InvalidDataException("An implicit metadata redirect is not admitted."); }
        if (response.StatusCode == HttpStatusCode.NotModified)
        {
            if (cached is null) { throw new InvalidDataException("Conditional response has no verified cached bytes."); }
            return cached.Bytes;
        }
        byte[] bytes;
        if (manifest && response.StatusCode is HttpStatusCode.Found or HttpStatusCode.TemporaryRedirect)
        {
            var location = response.Headers.Location;
            // GitHub's public asset API redirects only the manifest to its asset CDN.
            if (location is null || !location.IsAbsoluteUri || !string.Equals(location.Scheme, "https", StringComparison.Ordinal) ||
                !string.Equals(location.Host, "release-assets.githubusercontent.com", StringComparison.Ordinal) || !location.IsDefaultPort ||
                location.UserInfo.Length != 0 || location.Fragment.Length != 0 ||
                !location.AbsolutePath.StartsWith("/github-production-release-asset/", StringComparison.Ordinal))
            {
                throw new InvalidDataException("Manifest redirect is not the admitted GitHub asset CDN.");
            }
            using var redirectedRequest = CreateRequest(location, true, null);
            using var redirected = await http.SendAsync(redirectedRequest, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            if (redirected.RequestMessage?.RequestUri != location) { throw new InvalidDataException("Implicit CDN redirect denied."); }
            CheckStatus(redirected);
            bytes = await ReadBoundedAsync(redirected, limit, token).ConfigureAwait(false);
        }
        else
        {
            CheckStatus(response);
            bytes = await ReadBoundedAsync(response, limit, token).ConfigureAwait(false);
        }
        var etag = response.Headers.ETag;
        if (etag is not null && etag.ToString().Length <= 256)
        {
            lock (cacheGate)
            {
                if (cache.Count >= 24) { cache.Clear(); }
                cache[uri] = new(etag.ToString(), bytes);
            }
        }
        return bytes;
    }

    private void CheckStatus(HttpResponseMessage response)
    {
        if (response.StatusCode == HttpStatusCode.OK) { return; }
        var now = time.GetUtcNow();
        var status = response.StatusCode;
        if (status is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
        {
            var retry = response.Headers.RetryAfter;
            var until = retry?.Date ?? now + (retry?.Delta ?? TimeSpan.FromHours(1));
            if (response.Headers.TryGetValues("X-RateLimit-Reset", out var values) &&
                long.TryParse(string.Join("", values), NumberStyles.Integer, CultureInfo.InvariantCulture, out var epoch) &&
                epoch is >= 0 and <= 253402300799)
            {
                until = DateTimeOffset.FromUnixTimeSeconds(epoch);
            }
            until = until < now.AddMinutes(1) ? now.AddMinutes(1) : until;
            until = until > now.AddHours(24) ? now.AddHours(24) : until;
            throw new MetadataRequestException(ReleaseAvailability.RateLimited,
                "GitHub returned " + Number((int)status) + "; check deferred by rate-limit/backoff.", until);
        }
        throw new MetadataRequestException(status == HttpStatusCode.NotFound
            ? ReleaseAvailability.Unavailable : ReleaseAvailability.Unknown,
            "GitHub metadata returned HTTP " + Number((int)status) + "; no verified up-to-date claim.", null);
    }

    private static HttpRequestMessage CreateRequest(Uri uri, bool manifest, string? etag)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.UserAgent.ParseAdd("Kora-ReleaseMetadata/1.0");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(manifest ? "application/octet-stream" : "application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        if (etag is not null) { request.Headers.IfNoneMatch.ParseAdd(etag); }
        return request;
    }

    private static async Task<byte[]> ReadBoundedAsync(HttpResponseMessage response, int limit, CancellationToken token)
    {
        if (response.Content.Headers.ContentLength > limit) { throw new InvalidDataException("Metadata Content-Length exceeds its bound."); }
        using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int count;
        while ((count = await stream.ReadAsync(chunk, token).ConfigureAwait(false)) != 0)
        {
            if (buffer.Length + count > limit) { throw new InvalidDataException("Metadata body exceeds its streaming bound."); }
            await buffer.WriteAsync(chunk.AsMemory(0, count), token).ConfigureAwait(false);
        }
        if (response.Content.Headers.ContentLength is { } length && buffer.Length != length)
        {
            throw new InvalidDataException("Metadata Content-Length changed.");
        }
        return buffer.ToArray();
    }

    private static JsonDocument ParseJson(byte[] bytes)
    {
        var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 16 });
        try { RejectDuplicateProperties(document.RootElement); return document; }
        catch { document.Dispose(); throw; }
    }

    private static void RejectDuplicateProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) { throw new InvalidDataException("Duplicate JSON property is not admitted."); }
                RejectDuplicateProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in element.EnumerateArray()) { RejectDuplicateProperties(child); }
        }
    }

    private static JsonElement Property(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value)
            ? value : throw new InvalidDataException("Required metadata field is missing: " + name + ".");
    private static string Text(JsonElement element, string name) => Property(element, name) is { ValueKind: JsonValueKind.String } value
        ? value.GetString()! : throw new InvalidDataException("Metadata field must be text: " + name + ".");
    private static bool Boolean(JsonElement element, string name) => Property(element, name).ValueKind switch
    {
        JsonValueKind.True => true, JsonValueKind.False => false,
        _ => throw new InvalidDataException("Metadata field must be a boolean: " + name + "."),
    };
    private static long Positive(JsonElement element, string name) =>
        Property(element, name) is { ValueKind: JsonValueKind.Number } number && number.TryGetInt64(out var value) && value > 0
            ? value : throw new InvalidDataException("Metadata field must be a positive integer: " + name + ".");
    private static DateTimeOffset Timestamp(JsonElement element, string name) =>
        DateTimeOffset.TryParse(Text(element, name), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var value)
            ? value : throw new InvalidDataException("Publication timestamp is malformed.");
    private static ReleaseVersion ParseTag(string tag) => tag.StartsWith('v')
        ? ReleaseVersion.Parse(tag[1..]) : throw new InvalidDataException("Release tag is not canonical.");
    private static string Number(long number) => number.ToString(CultureInfo.InvariantCulture);

    private sealed record CachedResponse(string ETag, byte[] Bytes);
    private sealed class MetadataRequestException(ReleaseAvailability status, string message, DateTimeOffset? retryAt) : Exception(message)
    {
        public ReleaseAvailability Status { get; } = status;
        public DateTimeOffset? RetryAt { get; } = retryAt;
    }

    [GeneratedRegex(@"^[a-f0-9]{40}\z", RegexOptions.CultureInvariant, 100)]
    private static partial Regex ShaPattern();
    [GeneratedRegex(@"^sha256:[A-Fa-f0-9]{64}\z", RegexOptions.CultureInvariant, 100)]
    private static partial Regex DigestPattern();
    [GeneratedRegex("<!-- kora-source: (?<source>[a-f0-9]{40}) -->", RegexOptions.CultureInvariant, 100)]
    private static partial Regex SourcePattern();
    [GeneratedRegex(@"^https://github\.com/roryprimrose/Kora/actions/runs/[1-9][0-9]*\z", RegexOptions.CultureInvariant, 100)]
    private static partial Regex WorkflowPattern();
}
