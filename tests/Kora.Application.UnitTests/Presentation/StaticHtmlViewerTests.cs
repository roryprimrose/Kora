using System.Text;

using AwesomeAssertions;

using Kora.Application.Presentation;
using Kora.Core.Presentation;

namespace Kora.Application.UnitTests.Presentation;

public sealed class StaticHtmlViewerTests
{
    [Fact]
    public void AdmittedProjectionUsesExactViewerSourceAndDisclosurePolicyWithoutRetargeting()
    {
        const string source = "<h2>Private &amp; exact</h2>\r\n<p>Body</p>";
        var content = Content(source);
        var registry = new DetailViewerRegistry();
        var live = true;
        var viewer = registry.Open(content, true, () => live);
        viewer.Status.Should().Contain(StaticHtmlProfile.Name);
        var document = StaticHtmlParser.Parse(content.Source, TestContext.Current.CancellationToken);
        var generation = viewer.Generation;
        viewer.CompleteRender(generation, document.SemanticText, "Rendered strict inert fragment.", false).Should().BeTrue();
        viewer.ActiveText.Should().Be("Private & exact\n\nBody");
        viewer.Search("exact").Should().BeTrue();
        viewer.TryGetCopySource(true, false, out _).Should().BeFalse();
        viewer.TryGetCopySource(true, true, out var copied).Should().BeTrue();
        copied.Should().Be(source);
        viewer.SetSource(true);
        viewer.ActiveText.Should().Be(source);
        viewer.SearchQuery.Should().BeEmpty();
        registry.Open(content, true).Should().BeSameAs(viewer);
        var foreign = new AdmittedDetailContent(content.Reference, content.Kind, content.Origin, content.Sensitivity,
            content.Title, content.Provenance, "<p>foreign</p>");
        var replace = () => registry.Open(foreign, true);
        replace.Should().Throw<InvalidDataException>();
        live = false;
        viewer.ActiveText.Should().BeEmpty();
        viewer.Content.Should().BeNull();
        viewer.SearchQuery.Should().BeEmpty();
        viewer.CompleteRender(generation, document.SemanticText, "Late render", false).Should().BeFalse();
        viewer.TryGetCopySource(true, true, out _).Should().BeFalse();
        content.Source.Should().Be(source);
    }

    [Fact]
    public void HtmlSemanticLimitIsSeparateFromWholeSourceFallbackAdmissionLimit()
    {
        var source = "<p>" + new string('x', StaticHtmlProfile.MaximumUtf8Bytes) + "</p>";
        var content = Content(source);
        content.Utf8Bytes.Should().BeLessThan(NativeDetailProfile.MaximumUtf8Bytes);
        content.Profile.Should().Be(StaticHtmlProfile.Name);
        var viewer = new DetailViewerState(content);
        var tooLarge = () => viewer.CompleteRender(viewer.Generation,
            new string('x', StaticHtmlProfile.MaximumSemanticUtf8Bytes + 1), "Wrong success", false);
        tooLarge.Should().Throw<InvalidDataException>();
        viewer.CompleteRender(viewer.Generation, source, "Oversize static fragment; exact source fallback.", true).Should().BeTrue();
        viewer.RenderState.Should().Be(DetailRenderState.SourceFallback);
        viewer.ActiveText.Should().Be(source);
        viewer.Close();
        viewer.ActiveText.Should().BeEmpty();
    }

    [Fact]
    public void InvalidUnicodeAndOversizeAdmissionCannotCreateAnHtmlViewerOrChangeOtherProfiles()
    {
        var invalid = () => Content("<p>\ud800</p>");
        invalid.Should().Throw<EncoderFallbackException>();
        var oversized = () => Content(new string('x', NativeDetailProfile.MaximumUtf8Bytes + 1));
        oversized.Should().Throw<InvalidDataException>();
        var plain = new AdmittedDetailContent(new(new(Guid.NewGuid()), 1), DetailContentKind.PlainText,
            DetailContentOrigin.EmbeddedDocument, DetailSensitivity.Public, "Text", "Host snapshot", "<p>Not HTML</p>");
        plain.Profile.Should().Be(NativeDetailProfile.Name);
        new DetailViewerState(plain).ActiveText.Should().Be(plain.Source);
    }

    private static AdmittedDetailContent Content(string source) => new(new(new(Guid.NewGuid()), 1),
        DetailContentKind.StaticHtml, DetailContentOrigin.EmbeddedDocument, DetailSensitivity.DisclosureConfirmationRequired,
        "Static result", "Host-admitted immutable test result", source);
}
