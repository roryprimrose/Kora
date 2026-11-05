using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using Kora.Core.Dependencies;
using Kora.Core.Maintenance;

namespace Kora.Application.Maintenance;

public sealed class ReleaseInstallerDownloader(HttpClient http, IApplicationDataPaths paths) : IReleaseInstallerDownloader
{
    public static readonly TimeSpan DownloadTimeout = TimeSpan.FromMinutes(15);

    public async Task<IReleaseInstallerDownload> DownloadAsync(ReleaseMetadata release, CancellationToken cancellationToken)
    {
        var installer = InstallerAsset(release);
        using var timeout = new CancellationTokenSource(DownloadTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, cancellationToken);
        var token = linked.Token;
        token.ThrowIfCancellationRequested();
        var root = System.IO.Path.Combine(paths.LocalRoot, "Updates");
        Directory.CreateDirectory(root);
        if (File.GetAttributes(root).HasFlag(FileAttributes.ReparsePoint))
        { throw new InvalidDataException("The update directory must not redirect to another location."); }
        var directory = System.IO.Path.Combine(root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = System.IO.Path.Combine(directory, installer.Name);
        var partial = path + ".partial";
        try
        {
            var uri = new Uri(CanonicalRelease.ApiRoot + "/releases/assets/" + installer.Id.ToString(CultureInfo.InvariantCulture));
            using (var request = GitHubReleaseMetadataClient.CreateRequest(uri, true, null))
            using (var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false))
            {
                if (response.RequestMessage?.RequestUri != uri)
                { throw new InvalidDataException("An implicit installer redirect is not admitted."); }
                if (response.StatusCode is HttpStatusCode.Found or HttpStatusCode.TemporaryRedirect)
                {
                    var location = response.Headers.Location;
                    if (!GitHubReleaseMetadataClient.IsAssetRedirect(location))
                    { throw new InvalidDataException("The installer redirect is not the canonical GitHub asset CDN."); }
                    using var redirectedRequest = GitHubReleaseMetadataClient.CreateRequest(location!, true, null);
                    using var redirected = await http.SendAsync(redirectedRequest, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
                    if (redirected.RequestMessage?.RequestUri != location)
                    { throw new InvalidDataException("An implicit installer CDN redirect is not admitted."); }
                    await SaveAsync(redirected, installer, partial, token).ConfigureAwait(false);
                }
                else { await SaveAsync(response, installer, partial, token).ConfigureAwait(false); }
            }
            token.ThrowIfCancellationRequested();
            File.Move(partial, path);
            var lease = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
            return new Download(path, lease);
        }
        catch
        {
            if (File.Exists(partial)) { File.Delete(partial); }
            if (File.Exists(path)) { File.Delete(path); }
            Directory.Delete(directory);
            throw;
        }
    }

    public static ReleaseAsset InstallerAsset(ReleaseMetadata release)
    {
        if (!release.InstallerUpgradeSupported)
        { throw new InvalidDataException("This release has no supported x64 graceful installer-upgrade protocol. Use explicit external maintenance."); }
        var version = ReleaseVersion.Parse(release.Version.ToString());
        var name = $"Kora-Setup-{version}-win-x64.exe";
        var matches = release.Assets.Where(asset => string.Equals(asset.Name, name, StringComparison.Ordinal)).ToArray();
        if (matches.Length != 1 || matches[0].Id <= 0 || matches[0].Bytes <= 0 || matches[0].Bytes > 2_147_483_648L
            || matches[0].Sha256.Length != 64 || matches[0].Sha256.Any(character => !char.IsAsciiHexDigit(character)))
        { throw new InvalidDataException("The exact release installer identity is missing or malformed."); }
        return matches[0];
    }

    private static async Task SaveAsync(HttpResponseMessage response, ReleaseAsset installer, string path, CancellationToken token)
    {
        if (response.StatusCode != HttpStatusCode.OK)
        { throw new HttpRequestException($"Installer download returned HTTP {(int)response.StatusCode}.", null, response.StatusCode); }
        if (response.Content.Headers.ContentEncoding.Count != 0
            || response.Content.Headers.ContentLength is { } length && length != installer.Bytes)
        { throw new InvalidDataException("Installer size or content encoding differs from its verified identity."); }
        await using var source = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        await using var destination = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, token).ConfigureAwait(false)) != 0)
        {
            total += read;
            if (total > installer.Bytes) { throw new InvalidDataException("Installer body exceeds its declared size."); }
            hash.AppendData(buffer.AsSpan(0, read));
            await destination.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
        }
        if (total != installer.Bytes || !Convert.ToHexString(hash.GetHashAndReset()).Equals(installer.Sha256, StringComparison.OrdinalIgnoreCase))
        { throw new InvalidDataException("Installer final-byte size or SHA-256 differs from the verified release."); }
        await destination.FlushAsync(token).ConfigureAwait(false);
    }

    private sealed class Download(string path, FileStream lease) : IReleaseInstallerDownload
    {
        public string Path => path;
        public ValueTask DisposeAsync() => lease.DisposeAsync();
    }
}
