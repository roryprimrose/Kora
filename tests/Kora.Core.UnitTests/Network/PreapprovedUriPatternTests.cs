using AwesomeAssertions;
using Kora.Core.Network;

namespace Kora.Core.UnitTests.Network;

public sealed class PreapprovedUriPatternTests
{
    [Theory]
    [InlineData("https://example.com", "https://example.com/", true)]
    [InlineData("https://*.example.com/*", "https://api.example.com/data", true)]
    [InlineData("https://*.example.com/*", "https://deep.api.example.com/data", false)]
    [InlineData("https://example.com/api/*?format=json", "https://example.com/api/items?format=json", true)]
    [InlineData("https://example.com/api/*?format=json", "https://example.com/api/items?format=xml", false)]
    [InlineData("https://example.com/*", "http://example.com/data", false)]
    [InlineData("https://example.com/*", "https://example.com.evil.test/data", false)]
    [InlineData("https://example.com:8443/*", "https://example.com:8443/data", true)]
    [InlineData("https://example.com:8443/*", "https://example.com/data", false)]
    public void MatchesCanonicalHttpAddressesOnly(string pattern, string address, bool expected) =>
        PreapprovedUriPattern.Parse(pattern).Matches(new Uri(address)).Should().Be(expected);

    [Theory]
    [InlineData("")]
    [InlineData(" https://example.com/")]
    [InlineData("ftp://example.com/")]
    [InlineData("https://user@example.com/")]
    [InlineData("https://example.com/#fragment")]
    [InlineData("https://foo*.example.com/")]
    [InlineData("https://example.com:0/")]
    [InlineData("https://example.com:65536/")]
    [InlineData("https://[::1]/")]
    public void RejectsAmbiguousOrUnsupportedPatterns(string pattern) =>
        FluentActions.Invoking(() => PreapprovedUriPattern.Parse(pattern))
            .Should().Throw<ArgumentException>();

    [Fact]
    public void CanonicalizesCaseInternationalHostsDefaultPortsAndRootPaths()
    {
        var pattern = PreapprovedUriPattern.Parse("HTTPS://münich.example:443");

        pattern.Value.Should().Be("https://xn--mnich-kva.example/");
        pattern.Matches(new Uri("https://xn--mnich-kva.example/")).Should().BeTrue();
    }

    [Fact]
    public void SettingsRejectDuplicatesAndStayFailClosedForUnsupportedCandidates()
    {
        FluentActions.Invoking(() => PreapprovedUriSettings.Create(
            ["https://example.com", "https://example.com/"]))
            .Should().Throw<ArgumentException>();

        var settings = PreapprovedUriSettings.Create(["https://example.com/*"]);
        settings.IsPreapproved(new Uri("file:///C:/secret.txt")).Should().BeFalse();
        settings.IsPreapproved(new Uri("https://user@example.com/data")).Should().BeFalse();
        settings.IsPreapproved(new Uri("https://example.com/data#section")).Should().BeFalse();
    }
}
