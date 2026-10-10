using AwesomeAssertions;
using Kora.Core.Authorization;
using Kora.Core.Hosting;
using Kora.Core.Network;

namespace Kora.Core.UnitTests.Network;

public sealed class PreapprovedUriPatternTests
{
    [Theory]
    [InlineData("https://example.com", "https://example.com/", true)]
    [InlineData("https://*.example.com/*", "https://api.example.com/data", true)]
    [InlineData("https://*.example.com/*", "https://deep.api.example.com/data", false)]
    [InlineData("https://api.*.example.com/*", "https://api.eu.example.com/data", true)]
    [InlineData("https://*.localhost/*", "https://api.localhost/data", true)]
    [InlineData("https://example.com/api/*?format=json", "https://example.com/api/items?format=json", true)]
    [InlineData("https://example.com/api/*?format=json", "https://example.com/api/items?format=xml", false)]
    [InlineData("https://example.com/*", "http://example.com/data", false)]
    [InlineData("https://example.com/*", "https://example.com.evil.test/data", false)]
    [InlineData("https://example.com:8443/*", "https://example.com:8443/data", true)]
    [InlineData("https://example.com:8443/*", "https://example.com/data", false)]
    [InlineData("http://127.0.0.1/*", "http://127.0.0.1/data", true)]
    public void MatchesCanonicalHttpAddressesOnly(string pattern, string address, bool expected) =>
        PreapprovedUriPattern.Parse(pattern).Matches(new Uri(address)).Should().Be(expected);

    [Theory]
    [InlineData("")]
    [InlineData(" https://example.com/")]
    [InlineData("https://example.com/ ")]
    [InlineData("example.com/")]
    [InlineData("https:example.com/")]
    [InlineData("ftp://example.com/")]
    [InlineData("https://user@example.com/")]
    [InlineData("https://example.com/#fragment")]
    [InlineData("https://foo*.example.com/")]
    [InlineData("https://example.com:0/")]
    [InlineData("https://example.com:65536/")]
    [InlineData("https://[::1]/")]
    [InlineData("https://[::1]:8443/")]
    [InlineData("https://[not-an-ip-address]/")]
    [InlineData("https://[::1/")]
    [InlineData("https://example.com./")]
    [InlineData("https://.example.com/")]
    [InlineData("https://example.com:/")]
    [InlineData("https://example.com:abc/")]
    [InlineData("https://example.com:-1/")]
    [InlineData("https://example.com:1.5/")]
    [InlineData("https:///path")]
    [InlineData("https://*/")]
    [InlineData("http://*")]
    [InlineData("https://*:443/")]
    [InlineData("https://*:8443/*")]
    [InlineData("https://*?format=*")]
    [InlineData("https://*.*/*")]
    [InlineData("https://*.*.*:8443/*")]
    [InlineData("https://**.example.com/")]
    [InlineData("https://example.com/\n")]
    [InlineData("https://example.com/a\tb")]
    [InlineData("https://example.com/a\0b")]
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

    [Fact]
    public void WebPageBindingAcceptsCanonicalHttpButDoesNotTreatHttpsAsTheSameDestination()
    {
        var address = new Uri("HTTP://EXAMPLE.COM:80/page?q=1");
        var canonical = new Uri("http://example.com/page?q=1");
        var encrypted = new Uri("https://example.com/page?q=1");

        WebPageAccessBinding.DestinationDigest(address)
            .Should().Be(WebPageAccessBinding.DestinationDigest(canonical))
            .And.NotBe(WebPageAccessBinding.DestinationDigest(encrypted));
    }

    [Theory]
    [InlineData("/relative", UriKind.Relative)]
    [InlineData("file:///C:/secret.txt", UriKind.Absolute)]
    [InlineData("ftp://example.com/page", UriKind.Absolute)]
    public void WebPageBindingRejectsRelativeAndUnsupportedAddresses(string address, UriKind kind) =>
        FluentActions.Invoking(() => WebPageAccessBinding.DestinationDigest(new Uri(address, kind)))
            .Should().Throw<ArgumentException>();

    [Fact]
    public void WebPageBindingRejectsNullAddressesAndProposals()
    {
        FluentActions.Invoking(() => WebPageAccessBinding.DestinationDigest(null!))
            .Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => WebPageAccessBinding.Matches(null!, new Uri("https://example.com/page")))
            .Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => PreapprovedUriPattern.Parse("https://example.com/*").Matches(null!))
            .Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData(HostOperationEffect.BoundedRead, WebPageAccessBinding.ActionId, "https://example.com/page?q=1", true)]
    [InlineData(HostOperationEffect.BoundedRead, WebPageAccessBinding.ActionId, "https://example.com/page?q=2", false)]
    [InlineData(HostOperationEffect.Unknown, WebPageAccessBinding.ActionId, "https://example.com/page?q=1", false)]
    [InlineData(HostOperationEffect.BoundedRead, "network.other-action", "https://example.com/page?q=1", false)]
    public void WebPageBindingRequiresABoundedReadOfTheExactActionAndDestination(
        HostOperationEffect effect,
        string actionId,
        string address,
        bool expected)
    {
        var destination = new Uri("HTTPS://EXAMPLE.COM:443/page?q=1");
        var proposal = Proposal(effect, actionId, destination);

        WebPageAccessBinding.Matches(proposal, new Uri(address)).Should().Be(expected);
    }

    [Theory]
    [InlineData(HostOperationEffect.Unknown, WebPageAccessBinding.ActionId)]
    [InlineData(HostOperationEffect.BoundedRead, "network.other-action")]
    public void UnrelatedProposalsDoNotEvaluateTheDestination(HostOperationEffect effect, string actionId)
    {
        var proposal = Proposal(effect, actionId, new Uri("https://example.com/page"));

        WebPageAccessBinding.Matches(proposal, null!).Should().BeFalse();
    }

    private static HostOperationProposal Proposal(HostOperationEffect effect, string actionId, Uri destination)
    {
        var request = new HostRequest(
            new(Guid.Parse("11111111-1111-1111-1111-111111111111")),
            new(Guid.Parse("22222222-2222-2222-2222-222222222222")),
            new(Guid.Parse("33333333-3333-3333-3333-333333333333")),
            RequestOrigin.LocalUi,
            new(Guid.Parse("44444444-4444-4444-4444-444444444444")));
        var digest = new string('a', 64);
        var binding = new ExactOperationBinding(
            actionId, "builtin", "fixture", digest, digest, digest, digest, digest, digest, digest,
            WebPageAccessBinding.DestinationDigest(destination), digest, new(1));
        return new(
            request,
            new(Guid.Parse("55555555-5555-5555-5555-555555555555")),
            new(1),
            binding,
            effect,
            new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero));
    }
}
