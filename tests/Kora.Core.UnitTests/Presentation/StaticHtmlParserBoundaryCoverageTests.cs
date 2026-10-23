using System.Text;

using AwesomeAssertions;

using Kora.Core.Presentation;

namespace Kora.Core.UnitTests.Presentation;

public sealed class StaticHtmlParserBoundaryCoverageTests
{
    [Theory]
    [InlineData("h1")]
    [InlineData("h2")]
    [InlineData("h3")]
    [InlineData("h4")]
    [InlineData("h5")]
    [InlineData("h6")]
    [InlineData("p")]
    [InlineData("pre")]
    [InlineData("span")]
    [InlineData("strong")]
    [InlineData("em")]
    [InlineData("b")]
    [InlineData("i")]
    [InlineData("code")]
    public void EveryPhrasingContainerRejectsNestedBlockWithoutPublishingEarlierText(string container)
    {
        var source = $"<p>Earlier valid text</p><{container}>Before<div>Invalid block</div>After</{container}>";
        AssertFallback(source, StaticHtmlFailure.UnsupportedMarkup);
    }

    [Theory]
    [InlineData("<div><li>Orphan</li></div>")]
    [InlineData("<ul><li>Valid</li><span>Not an item</span></ul>")]
    [InlineData("<ol><li>Valid</li><span>Not an item</span></ol>")]
    public void InvalidListParentsNeverAcquireListItemSemantics(string source) =>
        AssertFallback(source, StaticHtmlFailure.UnsupportedMarkup);

    [Theory]
    [InlineData("ul", "- First\n\nContinuation\n\n- Second")]
    [InlineData("ol", "1. First\n\nContinuation\n\n2. Second")]
    public void ListWhitespaceAndMultipleParagraphsPreserveOneItemLabel(string list, string expected)
    {
        var source = $"<{list}> \r\n\t<li><p>First</p><p>Continuation</p></li> \r\n\t<li>Second</li> \r\n\t</{list}>";
        var result = StaticHtmlParser.Parse(source, TestContext.Current.CancellationToken);
        result.IsFallback.Should().BeFalse();
        result.Source.Should().Be(source);
        result.SemanticText.Should().Be(expected);
        result.Sections.Select(section => section.Kind).Should().Equal(
            StaticHtmlSectionKind.ListItem, StaticHtmlSectionKind.ListItem, StaticHtmlSectionKind.ListItem);
    }

    [Fact]
    public void XmlWhitespaceCollapseRetainsBreaksAndPreformattedWhitespaceWithoutChangingSource()
    {
        const string source = " \r\n\t<p>a \t &#13; <br/> \t b</p><pre> &#13;\t\ncode </pre>";
        var result = StaticHtmlParser.Parse(source, TestContext.Current.CancellationToken);
        result.IsFallback.Should().BeFalse();
        result.Source.Should().Be(source);
        result.SemanticText.Should().Be("a \nb\n\n \r\t\ncode ");
        result.Sections[1].Kind.Should().Be(StaticHtmlSectionKind.Code);
        result.EntityReferenceCount.Should().Be(2);
    }

    [Theory]
    [InlineData("end-element")]
    [InlineData("next-block")]
    [InlineData("end-fragment")]
    public void SemanticOverflowAtEveryFlushBoundaryFallsBackToTheWholeAdmittedFragment(string boundary)
    {
        const int dividers = 255;
        var prefix = string.Concat(Enumerable.Repeat("<hr/>", dividers));
        var text = new string('x', StaticHtmlProfile.MaximumSemanticUtf8Bytes - (dividers * 18) + 1);
        var source = boundary switch
        {
            "end-element" => prefix + "<p>" + text + "</p>",
            "next-block" => prefix + text + "<p>Never published</p>",
            _ => prefix + text,
        };
        Encoding.UTF8.GetByteCount(source).Should().BeLessThan(StaticHtmlProfile.MaximumUtf8Bytes,
            "the semantic budget, not source admission, must reject the complete projection");
        AssertFallback(source, StaticHtmlFailure.SemanticLimit);
    }

    private static void AssertFallback(string source, StaticHtmlFailure failure)
    {
        var result = StaticHtmlParser.Parse(source, TestContext.Current.CancellationToken);
        result.Failure.Should().Be(failure);
        result.IsFallback.Should().BeTrue();
        result.Source.Should().Be(source);
        result.SemanticText.Should().Be(source);
        result.Sections.Should().BeEmpty();
    }
}
