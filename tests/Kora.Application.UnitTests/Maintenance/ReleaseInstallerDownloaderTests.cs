using System.Net;
using System.Security.Cryptography;
using AwesomeAssertions;
using Kora.Application.Maintenance;
using Kora.Core.Dependencies;
using Kora.Core.Maintenance;

namespace Kora.Application.UnitTests.Maintenance;

public sealed class ReleaseInstallerDownloaderTests : IDisposable
{
    private readonly string root = Path.Combine(Environment.CurrentDirectory, ".net-test-artifacts", "update-download-" + Guid.NewGuid().ToString("N"));
    private static readonly byte[] Bytes = "exact installer bytes, never executed by tests"u8.ToArray();
    private static ReleaseAsset Asset => new(42, "Kora-Setup-2.0.0-win-x64.exe", Bytes.Length, Convert.ToHexString(SHA256.HashData(Bytes)));
    private static ReleaseMetadata Release => new(1, ReleaseVersion.Parse("2.0.0"), new string('a', 40), ReleaseArchitecture.X64, [Asset])
    { InstallerProtocol = CanonicalRelease.InstallerProtocol };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Obtains_only_the_exact_asset_and_leases_verified_bytes(bool redirect)
    {
        var requests = new List<Uri>();
        using var http = new HttpClient(new Handler(request =>
        {
            requests.Add(request.RequestUri!);
            if (redirect && requests.Count == 1)
            {
                var response = Response(request, HttpStatusCode.Found);
                response.Headers.Location = new("https://release-assets.githubusercontent.com/github-production-release-asset/42?token=ephemeral");
                return response;
            }
            return Response(request, HttpStatusCode.OK, Bytes);
        }));
        var downloader = new ReleaseInstallerDownloader(http, new Paths(root));
        string path;
        await using (var result = await downloader.DownloadAsync(Release, TestContext.Current.CancellationToken))
        {
            path = result.Path;
            Path.GetFileName(path).Should().Be(Asset.Name);
            (await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken)).Should().Equal(Bytes);
            requests[0].AbsoluteUri.Should().Be(CanonicalRelease.ApiRoot + "/releases/assets/42");
            requests.Should().HaveCount(redirect ? 2 : 1);
        }
        File.Exists(path).Should().BeTrue();
        Directory.GetFiles(root, "*.partial", SearchOption.AllDirectories).Should().BeEmpty();
    }

    [Theory]
    [InlineData("digest")]
    [InlineData("short")]
    [InlineData("long")]
    [InlineData("length")]
    [InlineData("encoded")]
    [InlineData("http")]
    [InlineData("partial")]
    [InlineData("implicit")]
    [InlineData("request-missing")]
    [InlineData("null-request")]
    [InlineData("redirect")]
    [InlineData("relative")]
    [InlineData("missing")]
    [InlineData("redirect-chain")]
    [InlineData("cdn-implicit")]
    [InlineData("cdn-request-missing")]
    [InlineData("cdn-null-request")]
    public async Task Rejects_changed_truncated_oversized_or_redirected_bytes_and_removes_partial_output(string failure)
    {
        var calls = 0;
        using var http = new HttpClient(new Handler(request =>
        {
            calls++;
            var content = failure switch
            {
                "digest" => new byte[Bytes.Length],
                "short" => Bytes[..^1],
                "long" => Bytes.Concat(new byte[] { 1 }).ToArray(),
                _ => Bytes,
            };
            var status = failure switch
            {
                "http" => HttpStatusCode.ServiceUnavailable,
                "partial" => HttpStatusCode.PartialContent,
                "redirect" or "relative" or "missing" or "redirect-chain" or "cdn-implicit" or "cdn-request-missing" or "cdn-null-request" => HttpStatusCode.Found,
                _ => HttpStatusCode.OK,
            };
            var response = Response(request, status, content);
            if (failure is "encoded") { response.Content.Headers.ContentEncoding.Add("gzip"); }
            if (failure is "length") { response.Content.Headers.ContentLength = 1; }
            if (failure is "short" or "long") { response.Content.Headers.ContentLength = null; }
            if (failure is "implicit" || failure is "cdn-implicit" && calls == 2)
            { response.RequestMessage = new(HttpMethod.Get, "https://example.com/"); }
            if (failure is "request-missing" || failure is "cdn-request-missing" && calls == 2)
            { response.RequestMessage = new HttpRequestMessage(); }
            if (failure is "null-request" || failure is "cdn-null-request" && calls == 2)
            { response.RequestMessage = null; }
            if (failure is "redirect") { response.Headers.Location = new("https://example.com/installer"); }
            if (failure is "relative") { response.Headers.Location = new("/installer", UriKind.Relative); }
            if (failure is "redirect-chain" or "cdn-implicit" or "cdn-request-missing" or "cdn-null-request")
            { response.Headers.Location = new("https://release-assets.githubusercontent.com/github-production-release-asset/42"); }
            return response;
        }));
        var downloader = new ReleaseInstallerDownloader(http, new Paths(root));
        var action = () => downloader.DownloadAsync(Release, TestContext.Current.CancellationToken);
        await action.Should().ThrowAsync<Exception>();
        Directory.GetFiles(root, "*", SearchOption.AllDirectories).Should().BeEmpty();
        Directory.GetDirectories(Path.Combine(root, "Updates")).Should().BeEmpty();
    }

    [Fact]
    public async Task Cancellation_before_download_performs_no_io()
    {
        using var http = new HttpClient(new Handler(_ => throw new InvalidOperationException("No network expected.")));
        var downloader = new ReleaseInstallerDownloader(http, new Paths(root));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var action = () => downloader.DownloadAsync(Release, cancellation.Token);
        await action.Should().ThrowAsync<OperationCanceledException>();
        Directory.Exists(root).Should().BeFalse();
    }

    [Fact]
    public async Task A_failed_transport_leaves_no_partial_file()
    {
        using var http = new HttpClient(new Handler(_ => throw new HttpRequestException("fixture")));
        var downloader = new ReleaseInstallerDownloader(http, new Paths(root));
        var action = () => downloader.DownloadAsync(Release, TestContext.Current.CancellationToken);
        await action.Should().ThrowAsync<HttpRequestException>();
        Directory.GetDirectories(Path.Combine(root, "Updates")).Should().BeEmpty();
    }

    [Fact]
    public async Task A_colliding_final_file_is_not_overwritten_or_executed()
    {
        using var http = new HttpClient(new Handler(request =>
        {
            var directory = Directory.GetDirectories(Path.Combine(root, "Updates")).Single();
            File.WriteAllBytes(Path.Combine(directory, Asset.Name), [1, 2, 3]);
            return Response(request, HttpStatusCode.OK, Bytes);
        }));
        var downloader = new ReleaseInstallerDownloader(http, new Paths(root));
        var action = () => downloader.DownloadAsync(Release, TestContext.Current.CancellationToken);
        await action.Should().ThrowAsync<IOException>();
        Directory.GetFiles(root, "*", SearchOption.AllDirectories).Should().BeEmpty();
    }

    [Fact]
    public async Task A_redirected_update_directory_is_rejected_before_transfer()
    {
        var target = Path.Combine(root, "target");
        var redirected = Path.Combine(root, "Updates");
        Directory.CreateDirectory(target);
        if (OperatingSystem.IsWindows())
        {
            using var process = new System.Diagnostics.Process
            {
                StartInfo = new("pwsh")
                {
                    UseShellExecute = false,
                    ArgumentList =
                    {
                        "-NoProfile", "-NonInteractive", "-Command",
                        "New-Item -ItemType Junction -Path '" + redirected.Replace("'", "''", StringComparison.Ordinal)
                            + "' -Target '" + target.Replace("'", "''", StringComparison.Ordinal) + "' | Out-Null",
                    },
                },
            };
            process.Start().Should().BeTrue();
            await process.WaitForExitAsync(TestContext.Current.CancellationToken);
            process.ExitCode.Should().Be(0);
        }
        else { Directory.CreateSymbolicLink(redirected, target); }
        using var http = new HttpClient(new Handler(_ => throw new InvalidOperationException("No transfer expected.")));
        var action = () => new ReleaseInstallerDownloader(http, new Paths(root))
            .DownloadAsync(Release, TestContext.Current.CancellationToken);
        await action.Should().ThrowAsync<InvalidDataException>();
        Directory.GetFiles(target).Should().BeEmpty();
        Directory.Delete(redirected);
    }

    [Theory]
    [InlineData("architecture")]
    [InlineData("legacy")]
    [InlineData("missing")]
    [InlineData("duplicate")]
    [InlineData("id")]
    [InlineData("size")]
    [InlineData("large")]
    [InlineData("hash")]
    [InlineData("hex")]
    [InlineData("foreign-name")]
    public void Rejects_unadmitted_installer_identity_before_network_or_file_access(string failure)
    {
        var release = failure switch
        {
            "architecture" => Release with { Architecture = ReleaseArchitecture.X86 },
            "legacy" => Release with { InstallerProtocol = 0 },
            "missing" => Release with { Assets = [] },
            "duplicate" => Release with { Assets = [Asset, Asset] },
            "id" => Release with { Assets = [Asset with { Id = 0 }] },
            "size" => Release with { Assets = [Asset with { Bytes = 0 }] },
            "large" => Release with { Assets = [Asset with { Bytes = 2_147_483_649 }] },
            "hash" => Release with { Assets = [Asset with { Sha256 = "bad" }] },
            "foreign-name" => Release with { Assets = [Asset with { Name = "foreign.exe" }] },
            _ => Release with { Assets = [Asset with { Sha256 = new string('z', 64) }] },
        };
        var action = () => ReleaseInstallerDownloader.InstallerAsset(release);
        action.Should().Throw<InvalidDataException>();
        Directory.Exists(root).Should().BeFalse();
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) { Directory.Delete(root, recursive: true); }
    }

    private sealed class Paths(string root) : IApplicationDataPaths
    {
        public string LocalRoot => root;
        public string RoamingRoot => root;
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }

    private static HttpResponseMessage Response(HttpRequestMessage request, HttpStatusCode status, byte[]? content = null) =>
        new(status) { RequestMessage = request, Content = new ByteArrayContent(content ?? []) };
}
