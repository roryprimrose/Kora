using AwesomeAssertions;
using Kora.Core.Network;

namespace Kora.Core.UnitTests.Network;

public sealed class WebPageAddressSyntaxTests
{
    [Theory]
    [InlineData("https://EXAMPLE.com/private?secret=token#hidden", "https", "example.com", 443, true, true)]
    [InlineData("http://example.com:80", "http", "example.com", 80, false, false)]
    [InlineData("https://example.com:443/", "https", "example.com", 443, false, false)]
    [InlineData("http://example.com:65535/%2f%20?%23", "http", "example.com", 65535, true, false)]
    [InlineData("https://bücher.example/原文", "https", "xn--bcher-kva.example", 443, false, false)]
    [InlineData("http://127.0.0.1:8080/path", "http", "127.0.0.1", 8080, false, false)]
    [InlineData("https://[::1]/#?secret", "https", "::1", 443, false, true)]
    [InlineData("https://[2001:db8::1]:8443/?#", "https", "2001:db8::1", 8443, true, true)]
    [InlineData("HTTPS://example.com/../unchanged?x#y", "https", "example.com", 443, true, true)]
    [InlineData("https://example.com?", "https", "example.com", 443, true, false)]
    [InlineData("https://example.com#", "https", "example.com", 443, false, true)]
    [InlineData("https://example.com:0/", "https", "example.com", 0, false, false)]
    public void Exact_syntax_returns_only_parser_facts_without_mutating_source(
        string text, string scheme, string host, int port, bool query, bool fragment)
    {
        var original = text;
        var result = WebPageAddressSyntax.InspectExact(text);
        result.Outcome.Should().Be(WebUrlSyntaxOutcome.Accepted);
        result.Facts.Should().Be(new WebUrlSyntaxFacts(scheme, host, port, query, fragment));
        text.Should().BeSameAs(original);
        result.ToString().Should().NotContain("private").And.NotContain("token").And.NotContain("hidden");
    }

    [Theory]
    [InlineData(" https://example.com")]
    [InlineData("https://example.com ")]
    [InlineData("https://example.com/a b")]
    [InlineData("https://example.com\n")]
    [InlineData("https://example.com/\t")]
    [InlineData("https://example.com/\u0000")]
    [InlineData("https://example.com/\u200b")]
    [InlineData("https://example.com/\u202e")]
    [InlineData("https://example.com/\u00a0")]
    [InlineData("https:\\\\example.com")]
    [InlineData("https://example.com/a\\b")]
    [InlineData("/relative")]
    [InlineData("//example.com")]
    [InlineData("http:example.com")]
    [InlineData("file:///etc/private")]
    [InlineData("data:text/plain,secret")]
    [InlineData("javascript:alert(1)")]
    [InlineData("urn:private:secret")]
    [InlineData("ftp://example.com")]
    [InlineData("https://user:secret@example.com")]
    [InlineData("https://@example.com")]
    [InlineData("https://user@example.com")]
    [InlineData("https://example.com https://other.example")]
    [InlineData("https://example.com\nhttps://other.example")]
    [InlineData("https://")]
    [InlineData("https://example.com:65536")]
    [InlineData("https://example.com:-1")]
    [InlineData("https://example.com:")]
    [InlineData("https://-a.example/")]
    [InlineData("https://example.com/|")]
    [InlineData("https://example.com/%")]
    [InlineData("https://example.com/%0")]
    [InlineData("https://example.com/%xz")]
    [InlineData("https://example.com/%0z")]
    [InlineData("https://[fe80::1%25secret]/")]
    [InlineData("https://exa%6dple.com/")]
    public void Refusal_has_only_generic_typed_reason(string text)
    {
        var result = WebPageAddressSyntax.InspectExact(text);
        result.Outcome.Should().Be(WebUrlSyntaxOutcome.InvalidSyntax);
        result.Facts.Should().BeNull();
        result.ToString().Should().NotContain(text);
    }

    [Fact]
    public void Whole_text_bounds_are_utf8_and_never_truncate()
    {
        const string prefix = "https://example.com/";
        var exact = prefix + new string('x', WebPageCapability.MaximumInputUtf8Bytes - prefix.Length);
        WebPageAddressSyntax.InspectExact(exact).Outcome.Should().Be(WebUrlSyntaxOutcome.Accepted);
        foreach (var text in new[] { "", exact + "x", prefix + new string('é', 1024) })
        {
            WebPageAddressSyntax.InspectExact(text).Should().Be(new WebUrlSyntaxResult(WebUrlSyntaxOutcome.OutsideBounds));
        }
        var inspectNull = () => WebPageAddressSyntax.InspectExact(null!);
        inspectNull.Should().Throw<ArgumentNullException>();
        WebPageAddressSyntax.InspectExact(prefix + "\ud800").Outcome.Should().Be(WebUrlSyntaxOutcome.InvalidSyntax);
    }

    [Fact]
    public void Runtime_binding_still_forbids_fragments_and_credentials()
    {
        foreach (var text in new[] { "https://example.com/#fragment", "https://user:password@example.com", "file:///x", "/relative" })
        {
            var bind = () => WebPageAccessBinding.DestinationDigest(new Uri(text, UriKind.RelativeOrAbsolute));
            bind.Should().Throw<ArgumentException>();
        }
        WebPageAccessBinding.DestinationDigest(new Uri("https://example.com/?secret")).Should().HaveLength(64);
    }
}
