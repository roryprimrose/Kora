using AwesomeAssertions;
using Kora.Core.Maintenance;

namespace Kora.Core.UnitTests.Maintenance;

public sealed class ReleaseVersionTests
{
    [Theory]
    [InlineData("0.1.0-beta9", "0.1.0-beta10")]
    [InlineData("0.1.0-beta10", "0.1.0")]
    [InlineData("0.1.0", "0.1.1-beta1")]
    [InlineData("0.9.9", "0.10.0-beta1")]
    [InlineData("1.99.99", "2.0.0")]
    public void Uses_numeric_GitVersion_release_ordering(string older, string newer)
    {
        var a = ReleaseVersion.Parse(older);
        var b = ReleaseVersion.Parse(newer);
        a.ToString().Should().Be(older);
        b.ToString().Should().Be(newer);
        (a < b).Should().BeTrue();
        (a <= b).Should().BeTrue();
        (b > a).Should().BeTrue();
        (b >= a).Should().BeTrue();
        (a > b).Should().BeFalse();
        (a >= b).Should().BeFalse();
        (b < a).Should().BeFalse();
        (b <= a).Should().BeFalse();
        a.CompareTo(ReleaseVersion.Parse(older)).Should().Be(0);
        a.CompareTo(null).Should().Be(1);
    }

    [Theory]
    [InlineData("")]
    [InlineData("v0.1.0")]
    [InlineData("01.0.0")]
    [InlineData("0.1.0-beta01")]
    [InlineData("0.1.0-beta.1")]
    [InlineData("0.1.0-rc1")]
    [InlineData("0.1.0+sha")]
    [InlineData("0.1.0\n")]
    [InlineData("2147483648.0.0")]
    [InlineData("0.1.0-beta2147483648")]
    [InlineData("0.1.\u0661")]
    public void Rejects_unknown_or_noncanonical_versions(string text) =>
        ((Action)(() => ReleaseVersion.Parse(text))).Should().Throw<InvalidDataException>();

    [Fact]
    public void Canonical_contract_selects_only_admitted_architecture_and_no_install_authority()
    {
        ((Action)(() => ReleaseVersion.Parse(null!))).Should().Throw<ArgumentNullException>();
        CanonicalRelease.Rid(ReleaseArchitecture.X64).Should().Be("win-x64");
        CanonicalRelease.Rid(ReleaseArchitecture.X86).Should().Be("win-x86");
        ((Action)(() => CanonicalRelease.Rid(ReleaseArchitecture.Unsupported))).Should().Throw<InvalidDataException>();
        var version = ReleaseVersion.Parse("0.1.0-beta0");
        CanonicalRelease.AssetNames(version).Should().HaveCount(9);
        var x86 = new ReleaseMetadata(1, version, new string('a', 40), ReleaseArchitecture.X86, []);
        x86.Page.AbsoluteUri.Should().Be("https://github.com/roryprimrose/Kora/releases/tag/v0.1.0-beta0");
        x86.ArtifactName.Should().Be("Kora-0.1.0-beta0-win-x86.zip");
        x86.ArchitectureDisclosure.Should().Contain("no x86 MSI");
        (x86 with { Architecture = ReleaseArchitecture.X64 }).ArchitectureDisclosure.Should().Contain("outstanding");
        CanonicalRelease.Limits.Should().Contain("not publisher authentication").And.Contain("unknown");
        new ReleaseCheck(ReleaseAvailability.Unknown, "not checked", DateTimeOffset.MinValue).Release.Should().BeNull();
        ReleaseVersion.Parse("0.1.0").CompareTo(ReleaseVersion.Parse("0.1.0")).Should().Be(0);
    }
}
