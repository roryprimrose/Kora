using AwesomeAssertions;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Maintenance;
using System.Text.Json.Nodes;

namespace Kora.Application.UnitTests.Maintenance;

[Collection("Host tracing")]
public sealed class ReleaseVersionSelectionTests
{
    [Theory]
    [InlineData("1.2.3", ReleaseChannel.Production)]
    [InlineData("1.2.3", ReleaseChannel.Preview)]
    [InlineData("1.2.3-beta12", ReleaseChannel.Preview)]
    public async Task Exact_version_uses_only_the_canonical_tag_and_reverifies_assets(string version, ReleaseChannel channel)
    {
        using var fixture = new ReleaseFixture(version);
        using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Application, HostOperation.Request);
        var result = await fixture.Client.CheckVersionAsync(channel, "1.0.0", ReleaseArchitecture.X64,
            ReleaseVersion.Parse(version), TestContext.Current.CancellationToken);
        result.Status.Should().Be(ReleaseAvailability.Available);
        result.Release!.Version.ToString().Should().Be(version);
        fixture.Requests[0].AbsoluteUri.Should().Be(CanonicalRelease.ApiRoot + "/releases/tags/v" + version);
        fixture.Requests.Should().HaveCount(5);
        root.Complete(HostOperationOutcome.Completed);
    }

    [Fact]
    public async Task Including_betas_still_selects_a_newer_stable_release()
    {
        using var fixture = new ReleaseFixture();
        var beta = fixture.Release.DeepClone();
        beta["tag_name"] = "v1.2.3-beta999";
        beta["prerelease"] = true;
        fixture.Pages = _ => new JsonArray(beta, fixture.Release.DeepClone());
        var result = await fixture.CheckAsync(ReleaseChannel.Preview, "1.2.3-beta999");
        result.Status.Should().Be(ReleaseAvailability.Available);
        result.Release!.Version.ToString().Should().Be("1.2.3");
    }

    [Fact]
    public async Task Beta_discovery_verifies_the_complete_current_sized_release_catalog_not_just_the_first_hundred_records()
    {
        using var fixture = new ReleaseFixture("1.2.3-beta662");
        const int records = 529;
        fixture.Pages = page =>
        {
            var start = (page - 1) * Kora.Application.Maintenance.GitHubReleaseMetadataClient.PageSize;
            var count = Math.Min(Kora.Application.Maintenance.GitHubReleaseMetadataClient.PageSize, records - start);
            return new JsonArray(Enumerable.Range(start, count).Select(index => index == 0
                ? fixture.Release.DeepClone()
                : new JsonObject
                {
                    ["draft"] = false, ["prerelease"] = true,
                    ["tag_name"] = "v0.0.0-beta" + index.ToString(System.Globalization.CultureInfo.InvariantCulture),
                }).ToArray());
        };
        var result = await fixture.CheckAsync(ReleaseChannel.Preview, "1.2.3-beta661");
        result.Status.Should().Be(ReleaseAvailability.Available);
        result.Release!.Version.ToString().Should().Be("1.2.3-beta662");
        fixture.Requests.Count(uri => uri.Query.Length != 0).Should().Be(27);
        fixture.Requests.Should().HaveCount(31);
    }

    [Fact]
    public async Task Stable_only_policy_rejects_an_explicit_beta_without_network_access()
    {
        using var fixture = new ReleaseFixture("1.2.3-beta12");
        using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Application, HostOperation.Request);
        var result = await fixture.Client.CheckVersionAsync(ReleaseChannel.Production, "1.0.0", ReleaseArchitecture.X64,
            ReleaseVersion.Parse("1.2.3-beta12"), TestContext.Current.CancellationToken);
        result.Status.Should().Be(ReleaseAvailability.Unknown);
        result.Reason.Should().Contain("selected release policy");
        fixture.Requests.Should().BeEmpty();
        root.Complete(HostOperationOutcome.Completed);
    }

    [Fact]
    public async Task An_exact_tag_cannot_return_a_different_version()
    {
        using var fixture = new ReleaseFixture();
        using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Application, HostOperation.Request);
        var result = await fixture.Client.CheckVersionAsync(ReleaseChannel.Production, "1.0.0", ReleaseArchitecture.X64,
            ReleaseVersion.Parse("1.2.4"), TestContext.Current.CancellationToken);
        result.Status.Should().Be(ReleaseAvailability.Unknown);
        result.Reason.Should().Contain("requested exact version");
        fixture.Requests.Should().ContainSingle();
        root.Complete(HostOperationOutcome.Completed);
    }

    [Fact]
    public async Task Missing_exact_version_is_not_reinterpreted_as_latest()
    {
        using var fixture = new ReleaseFixture();
        var action = () => fixture.Client.CheckVersionAsync(ReleaseChannel.Production, "1.0.0",
            ReleaseArchitecture.X64, null!, CancellationToken.None);
        await action.Should().ThrowAsync<ArgumentNullException>();
        fixture.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Missing_exact_release_remains_unavailable_without_an_artifact_request()
    {
        using var fixture = new ReleaseFixture();
        fixture.Override = request => fixture.Respond(request, System.Net.HttpStatusCode.NotFound);
        using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Application, HostOperation.Request);
        var result = await fixture.Client.CheckVersionAsync(ReleaseChannel.Production, "1.0.0",
            ReleaseArchitecture.X64, ReleaseVersion.Parse("1.2.3"), TestContext.Current.CancellationToken);
        result.Status.Should().Be(ReleaseAvailability.Unavailable);
        fixture.Requests.Should().ContainSingle();
        root.Complete(HostOperationOutcome.Completed);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task Legacy_and_future_installer_contracts_allow_checks_but_only_current_protocol_allows_upgrade(int protocol)
    {
        using var fixture = new ReleaseFixture();
        if (protocol == 0) { fixture.Manifest.Remove("installerProtocol"); }
        else { fixture.Manifest["installerProtocol"] = protocol; }
        fixture.BindManifest();
        var result = await fixture.CheckAsync();
        result.Status.Should().Be(ReleaseAvailability.Available);
        result.Release!.InstallerProtocol.Should().Be(protocol);
        result.Release.InstallerUpgradeSupported.Should().Be(protocol == CanonicalRelease.InstallerProtocol);
    }

    [Theory]
    [InlineData("text")]
    [InlineData("fraction")]
    [InlineData("negative")]
    [InlineData("overflow")]
    public async Task Malformed_installer_protocol_is_never_an_authorized_default(string malformed)
    {
        using var fixture = new ReleaseFixture();
        fixture.Manifest["installerProtocol"] = malformed switch
        {
            "text" => JsonValue.Create("2"),
            "fraction" => JsonValue.Create(2.5),
            "negative" => JsonValue.Create(-1),
            _ => JsonValue.Create(long.MaxValue),
        };
        fixture.BindManifest();
        var result = await fixture.CheckAsync();
        result.Status.Should().Be(ReleaseAvailability.Unknown);
        result.Reason.Should().Contain("protocol declaration is malformed");
    }
}
