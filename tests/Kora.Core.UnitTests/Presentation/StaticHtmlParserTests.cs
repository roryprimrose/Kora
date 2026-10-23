using System.Diagnostics;
using System.Text;

using AwesomeAssertions;

using Kora.Core.Presentation;

namespace Kora.Core.UnitTests.Presentation;

[Collection("Host tracing")]
public sealed class StaticHtmlParserTests
{
    [Fact]
    public void ParsePreservesExactSourceAndCompleteInertSemanticStructure()
    {
        const string source = "<div><h1>Summary &amp; result</h1>\r\n<p>Hello <strong>bold</strong> <em>text</em><br/>next.</p>"
            + "<ol><li>First</li><li><p>Second</p><ul><li>Nested</li></ul></li></ol>"
            + "<blockquote><p>Quoted</p></blockquote><pre><code>a &lt; b\n\tline</code></pre><hr/></div>";
        var result = StaticHtmlParser.Parse(source, TestContext.Current.CancellationToken);
        result.IsFallback.Should().BeFalse();
        result.Failure.Should().Be(StaticHtmlFailure.None);
        result.Source.Should().Be(source);
        result.SemanticText.Should().Be("Summary & result\n\nHello bold text\nnext.\n\n1. First\n\n2. Second\n\n- Nested\n\n> Quoted\n\na < b\n\tline\n\nHorizontal rule.");
        result.Sections.Select(section => section.Kind).Should().Equal(
            StaticHtmlSectionKind.Heading1, StaticHtmlSectionKind.Paragraph, StaticHtmlSectionKind.ListItem,
            StaticHtmlSectionKind.ListItem, StaticHtmlSectionKind.ListItem, StaticHtmlSectionKind.Quote,
            StaticHtmlSectionKind.Code, StaticHtmlSectionKind.Divider);
        result.Sections.IsDefault.Should().BeFalse();
    }

    [Theory]
    [InlineData("h1", StaticHtmlSectionKind.Heading1)]
    [InlineData("h2", StaticHtmlSectionKind.Heading2)]
    [InlineData("h3", StaticHtmlSectionKind.Heading3)]
    [InlineData("h4", StaticHtmlSectionKind.Heading4)]
    [InlineData("h5", StaticHtmlSectionKind.Heading5)]
    [InlineData("h6", StaticHtmlSectionKind.Heading6)]
    public void ParsePreservesHeadingLevel(string tag, StaticHtmlSectionKind kind)
    {
        var result = StaticHtmlParser.Parse($"<{tag}>Heading</{tag}>", TestContext.Current.CancellationToken);
        result.IsFallback.Should().BeFalse();
        result.Sections.Should().ContainSingle().Which.Should().Be(new StaticHtmlSection("Heading", kind));
    }

    [Theory]
    [InlineData("span")]
    [InlineData("strong")]
    [InlineData("em")]
    [InlineData("b")]
    [InlineData("i")]
    [InlineData("code")]
    public void ParseFlattensOnlyAdmittedInlinePresentationWithoutLosingText(string tag)
    {
        var result = StaticHtmlParser.Parse($"<p>one <{tag}>two</{tag}> three</p>", TestContext.Current.CancellationToken);
        result.SemanticText.Should().Be("one two three");
        result.Sections.Should().ContainSingle().Which.Kind.Should().Be(StaticHtmlSectionKind.Paragraph);
    }

    [Theory]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("<style>p{display:none}</style>")]
    [InlineData("<p style='color:red'>x</p>")]
    [InlineData("<p onclick='run()'>x</p>")]
    [InlineData("<p title='hidden'>x</p>")]
    [InlineData("<p xmlns='urn:host'>x</p>")]
    [InlineData("<p xmlns:host='urn:host'>x</p>")]
    [InlineData("<p xml:space='preserve'>x</p>")]
    [InlineData("<img src='file:///private'/>")]
    [InlineData("<iframe src='https://example.invalid'/>")]
    [InlineData("<a href='javascript:run()'>click</a>")]
    [InlineData("<a>label</a>")]
    [InlineData("<form><input/></form>")]
    [InlineData("<svg><foreignObject/></svg>")]
    [InlineData("<link rel='stylesheet' href='https://example.invalid'/>")]
    [InlineData("<meta http-equiv='refresh' content='0'/>")]
    [InlineData("<object data='file:///private'/>")]
    [InlineData("<table><tr><td>x</td></tr></table>")]
    [InlineData("<P>uppercase</P>")]
    [InlineData("<p><div>invalid nesting</div></p>")]
    [InlineData("<p>one<p>two")]
    [InlineData("<span><p>block</p></span>")]
    [InlineData("<ul><p>not an item</p></ul>")]
    [InlineData("<ol>not an item</ol>")]
    [InlineData("<li>orphan</li>")]
    [InlineData("<p/>")]
    [InlineData("<br></br>")]
    [InlineData("<!-- hidden -->text")]
    [InlineData("<![CDATA[not silently unwrapped]]>")]
    [InlineData("<?processing instruction?>text")]
    public void ParseRejectsUnsupportedMarkupAsWholeSource(string source) =>
        AssertFallback(StaticHtmlParser.Parse(source, TestContext.Current.CancellationToken), source, StaticHtmlFailure.UnsupportedMarkup);

    [Theory]
    [InlineData("<p>unclosed")]
    [InlineData("<p>x</div>")]
    [InlineData("<p>raw & content</p>")]
    [InlineData("<p>&nbsp;</p>")]
    [InlineData("<p>&#0;</p>")]
    [InlineData("<p>&#xD800;</p>")]
    [InlineData("<p>&#x110000;</p>")]
    [InlineData("<p>&unknown;</p>")]
    [InlineData("<!DOCTYPE p SYSTEM 'https://example.invalid/a'><p>x</p>")]
    [InlineData("<!DOCTYPE p [<!ENTITY e SYSTEM 'file:///private'>]><p>&e;</p>")]
    [InlineData("<!DOCTYPE p [<!ENTITY e 'expanded'>]><p>&e;</p>")]
    [InlineData("<p>\0</p>")]
    public void ParseRejectsMalformedAndExternalEntitiesWithoutPartialSuccess(string source) =>
        AssertFallback(StaticHtmlParser.Parse(source, TestContext.Current.CancellationToken), source, StaticHtmlFailure.MalformedSource);

    [Fact]
    public void ParseDecodesOnlyStrictPredefinedAndNumericEntitiesOnce()
    {
        const string source = "<p>&amp; &lt; &gt; &apos; &quot; &#65; &#x1F600; &amp;lt; &#160;</p>";
        StaticHtmlParser.Parse(source, TestContext.Current.CancellationToken).SemanticText.Should().Be("& < > ' \" A 😀 &lt; \u00a0");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" \r\n\t")]
    [InlineData("<div><p> </p></div>")]
    public void ParseRejectsEmptyProjectionsWithoutClaimingSuccess(string source) =>
        AssertFallback(StaticHtmlParser.Parse(source, TestContext.Current.CancellationToken), source, StaticHtmlFailure.EmptyProjection);

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    public void ParseEnforcesExactUtf8ByteBoundaryWithoutTruncation(int adjustment)
    {
        var source = "<p>" + new string('é', (StaticHtmlProfile.MaximumUtf8Bytes - 8) / 2)
            + new string('x', 1 + adjustment) + "</p>";
        Encoding.UTF8.GetByteCount(source).Should().Be(StaticHtmlProfile.MaximumUtf8Bytes + adjustment);
        var result = StaticHtmlParser.Parse(source, TestContext.Current.CancellationToken);
        result.Source.Should().Be(source);
        if (adjustment > 0) { AssertFallback(result, source, StaticHtmlFailure.ByteLimit); }
        else { result.IsFallback.Should().BeFalse(); }
    }

    [Theory]
    [InlineData(15)]
    [InlineData(16)]
    [InlineData(17)]
    public void ParseEnforcesExactElementDepthBoundary(int depth)
    {
        var source = string.Concat(Enumerable.Repeat("<div>", depth)) + "text"
            + string.Concat(Enumerable.Repeat("</div>", depth));
        var result = StaticHtmlParser.Parse(source, TestContext.Current.CancellationToken);
        result.Depth.Should().Be(depth);
        if (depth > StaticHtmlProfile.MaximumDepth) { AssertFallback(result, source, StaticHtmlFailure.DepthLimit); }
        else { result.IsFallback.Should().BeFalse(); }
    }

    [Theory]
    [InlineData(255)]
    [InlineData(256)]
    [InlineData(257)]
    public void ParseEnforcesExactBlockBoundary(int blocks)
    {
        var source = string.Concat(Enumerable.Repeat("<p>x</p>", blocks));
        var result = StaticHtmlParser.Parse(source, TestContext.Current.CancellationToken);
        result.BlockCount.Should().Be(blocks);
        if (blocks > StaticHtmlProfile.MaximumBlocks) { AssertFallback(result, source, StaticHtmlFailure.BlockLimit); }
        else { result.Sections.Should().HaveCount(blocks); }
    }

    [Theory]
    [InlineData(2047)]
    [InlineData(2048)]
    [InlineData(2049)]
    public void ParseEnforcesExactElementAndTextNodeBoundary(int nodes)
    {
        var source = string.Concat(Enumerable.Repeat("<span>x</span>", 1023)) + "t"
            + string.Concat(Enumerable.Repeat("<br/>", nodes - 2047));
        var result = StaticHtmlParser.Parse(source, TestContext.Current.CancellationToken);
        result.NodeCount.Should().Be(nodes);
        if (nodes > StaticHtmlProfile.MaximumNodes) { AssertFallback(result, source, StaticHtmlFailure.NodeLimit); }
        else { result.IsFallback.Should().BeFalse(); }
    }

    [Theory]
    [InlineData(2047, "&amp;", "&")]
    [InlineData(2048, "&amp;", "&")]
    [InlineData(2049, "&amp;", "&")]
    [InlineData(2047, "&#65;", "A")]
    [InlineData(2048, "&#65;", "A")]
    [InlineData(2049, "&#65;", "A")]
    [InlineData(2047, "&#x1F600;", "😀")]
    [InlineData(2048, "&#x1F600;", "😀")]
    [InlineData(2049, "&#x1F600;", "😀")]
    public void ParseEnforcesExactEntityReferenceBoundaryIndependentlyOfCoalescedNodes(int entities, string reference, string decoded)
    {
        var source = "<p>" + string.Concat(Enumerable.Repeat(reference, entities)) + "</p>";
        var result = StaticHtmlParser.Parse(source, TestContext.Current.CancellationToken);
        result.EntityReferenceCount.Should().Be(entities);
        if (entities > StaticHtmlProfile.MaximumEntityReferences) { AssertFallback(result, source, StaticHtmlFailure.EntityLimit); }
        else
        {
            result.NodeCount.Should().Be(2, "all references decode into one text node inside one element; the entity cap is not a reader-event proxy");
            result.SemanticText.Should().Be(string.Concat(Enumerable.Repeat(decoded, entities)));
        }
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    public void ParseEnforcesExactCompleteSemanticByteBoundary(int adjustment)
    {
        var source = new string('x', StaticHtmlProfile.MaximumSemanticUtf8Bytes - (256 * 18) + adjustment)
            + string.Concat(Enumerable.Repeat("<hr/>", 256));
        Encoding.UTF8.GetByteCount(source).Should().BeLessThan(StaticHtmlProfile.MaximumUtf8Bytes,
            "the decoded semantic limit must be reached independently of the source-byte limit");
        var result = StaticHtmlParser.Parse(source, TestContext.Current.CancellationToken);
        if (adjustment > 0) { AssertFallback(result, source, StaticHtmlFailure.SemanticLimit); }
        else
        {
            result.IsFallback.Should().BeFalse();
            Encoding.UTF8.GetByteCount(result.SemanticText).Should().Be(StaticHtmlProfile.MaximumSemanticUtf8Bytes + adjustment);
        }
    }

    [Fact]
    public void ParseRejectsInvalidUnicodeAndHonorsCancellationAndNullGuards()
    {
        const string source = "<p>\ud800</p>";
        AssertFallback(StaticHtmlParser.Parse(source, TestContext.Current.CancellationToken), source, StaticHtmlFailure.InvalidUnicode);
        var cancel = () => StaticHtmlParser.Parse("<p>x</p>", new CancellationToken(true));
        cancel.Should().Throw<OperationCanceledException>();
        var missing = () => StaticHtmlParser.Parse(null!, TestContext.Current.CancellationToken);
        missing.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ParseUsesCausalActivityAndNeverTagsSourceOrEntityDestinations()
    {
        var stopped = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => string.Equals(source.Name, "Kora.Core", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = stopped.Add,
        };
        ActivitySource.AddActivityListener(listener);
        using var parent = new Activity("test-request").Start();
        StaticHtmlParser.Parse("<p>private source</p>", TestContext.Current.CancellationToken);
        StaticHtmlParser.Parse("<script>private source</script>", TestContext.Current.CancellationToken);
        var activities = stopped.Where(activity => string.Equals(activity.OperationName, "presentation.static-html.parse", StringComparison.Ordinal)).ToArray();
        activities.Should().HaveCount(2);
        activities.Should().OnlyContain(activity => string.Equals(activity.ParentId, parent.Id, StringComparison.Ordinal));
        activities[0].Status.Should().Be(ActivityStatusCode.Ok);
        activities[1].Status.Should().Be(ActivityStatusCode.Error);
        activities.SelectMany(activity => activity.TagObjects).Should().NotContain(pair =>
            pair.Value != null && pair.Value.ToString()!.Contains("private source", StringComparison.Ordinal));
        activities.Should().OnlyContain(activity => !activity.Baggage.Any());
    }

    private static void AssertFallback(StaticHtmlDocument result, string source, StaticHtmlFailure failure)
    {
        result.Failure.Should().Be(failure);
        result.IsFallback.Should().BeTrue();
        result.Source.Should().Be(source);
        result.SemanticText.Should().Be(source);
        result.Sections.Should().BeEmpty();
    }
}
