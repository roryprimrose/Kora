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
    [InlineData("https://example.com./")]
    [InlineData("https://.example.com/")]
    [InlineData("https://example.com:/")]
    [InlineData("https://example.com:abc/")]
    [InlineData("https://example.com:-1/")]
    [InlineData("https://example.com:1.5/")]
    [InlineData("https:///path")]
    [InlineData("https://*/")]
    [InlineData("https://**.example.com/")]
    [InlineData("https://example.com/\n")]
    public void RejectsAmbiguousOrUnsupportedPatterns(string pattern) =>
        FluentActions.Invoking(() => PreapprovedUriPattern.Parse(pattern))
            .Should().Throw<ArgumentException>();

    [Fact]
    public void RejectsNullAndOversizedPatterns()
    {
        FluentActions.Invoking(() => PreapprovedUriPattern.Parse(null!))
            .Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => PreapprovedUriPattern.Parse(
                $"https://example.com/{new string('a', PreapprovedUriPattern.MaximumPatternUtf8Bytes)}"))
            .Should().Throw<ArgumentException>();
    }

    [Fact]
    public void CanonicalizesCaseInternationalHostsDefaultPortsAndRootPaths()
    {
        var pattern = PreapprovedUriPattern.Parse("HTTPS://münich.example:443");

        pattern.Value.Should().Be("https://xn--mnich-kva.example/");
        pattern.Matches(new Uri("https://xn--mnich-kva.example/")).Should().BeTrue();
        pattern.ToString().Should().Be(pattern.Value);
        PreapprovedUriPattern.Parse("http://example.com:80?format=*").Value
            .Should().Be("http://example.com/?format=*");
        PreapprovedUriPattern.Parse("https://example.com/kora-uri-wildcard-0/*").Value
            .Should().Be("https://example.com/kora-uri-wildcard-0/*");
    }

    [Fact]
    public void SettingsRejectDuplicatesAndStayFailClosedForUnsupportedCandidates()
    {
        FluentActions.Invoking(() => PreapprovedUriSettings.Create(null!))
            .Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => PreapprovedUriSettings.Create(
            ["https://example.com", "https://example.com/"]))
            .Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => PreapprovedUriSettings.Create(
                Enumerable.Range(0, PreapprovedUriPattern.MaximumPatternCount + 1)
                    .Select(index => $"https://host-{index}.example.com/")))
            .Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => PreapprovedUriSettings.Create(
                Enumerable.Range(0, PreapprovedUriPattern.MaximumPatternCount)
                    .Select(index => $"https://host-{index}.example.com/{new string('a', 250)}")))
            .Should().Throw<ArgumentException>();

        var settings = PreapprovedUriSettings.Create(["https://example.com/*"]);
        settings.IsPreapproved(new Uri("https://example.com/data")).Should().BeTrue();
        settings.IsPreapproved(new Uri("file:///C:/secret.txt")).Should().BeFalse();
        settings.IsPreapproved(new Uri("https://user@example.com/data")).Should().BeFalse();
        settings.IsPreapproved(new Uri("https://example.com/data#section")).Should().BeFalse();
        settings.IsPreapproved(new Uri("/relative", UriKind.Relative)).Should().BeFalse();
        FluentActions.Invoking(() => settings.IsPreapproved(null!))
            .Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void WebPageBindingUsesTheCanonicalExactDestination()
    {
        var canonical = new Uri("HTTPS://EXAMPLE.COM:443/page?q=1");
        var same = new Uri("https://example.com/page?q=1");
        var changed = new Uri("https://example.com/page?q=2");

        WebPageAccessBinding.DestinationDigest(canonical)
            .Should().Be(WebPageAccessBinding.DestinationDigest(same))
            .And.NotBe(WebPageAccessBinding.DestinationDigest(changed));
        FluentActions.Invoking(() =>
                WebPageAccessBinding.DestinationDigest(new Uri("https://user@example.com/page")))
            .Should().Throw<ArgumentException>();
        FluentActions.Invoking(() =>
                WebPageAccessBinding.DestinationDigest(new Uri("https://example.com/page#fragment")))
            .Should().Throw<ArgumentException>();
    }
}
