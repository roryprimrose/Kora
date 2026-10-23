using System.Text;

using Avalonia.Controls;

using AwesomeAssertions;

using Kora.Core.Presentation;

using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class NativeDetailRendererTests
{
    private static NativeDetailRenderer Renderer => new(NullLogger<NativeDetailRenderer>.Instance);

    [Fact]
    public Task CommonMarkSubsetPreservesNestedTextAndDisplaysInertDestinations() => HeadlessSession.RunAsync(() =>
    {
        const string source = "# Heading\n\nParagraph *one* **two** `three`.\n\n> quote\n>\n> - nested\n\n"
            + "3. first\n4. second\n\n[link](javascript:alert)\n\n<https://example.invalid/resource>\n\n"
            + "```text\ncode\n```\n\n---\n";
        var result = Renderer.Render(source);
        result.IsFallback.Should().BeFalse();
        result.Source.Should().Be(source);
        result.SemanticText.Should().Contain("one two three")
            .And.Contain("> - nested").And.Contain("3. first").And.Contain("4. second")
            .And.Contain("destination: javascript:alert").And.Contain("inert destination");
        result.Status.Should().Contain("inert").And.Contain("plain text");
        var controls = MarkdownDocumentRenderer.CreateControls(result);
        controls.Should().HaveCount(result.Sections.Count);
        controls.Should().OnlyContain(control => control is SelectableTextBlock);
    });

    [Theory]
    [InlineData("<script src=\"https://example.invalid/x.js\">alert(1)</script>", "HTML")]
    [InlineData("a <img src=\"file:///private.png\"> b", "HTML")]
    [InlineData("<iframe src=\"https://example.invalid\"></iframe>", "HTML")]
    [InlineData("![secret](file:///private.png)", "image")]
    [InlineData("![remote](https://example.invalid/image.png)", "image")]
    [InlineData("![data](data:image/png;base64,AA)", "image")]
    [InlineData("```mermaid\ngraph TD; A-->B\n```", "Mermaid")]
    [InlineData("```MeRmAiD title\ngraph TD; A-->B\n```", "Mermaid")]
    [InlineData("| a | b |\n| --- | --- |\n| 1 | 2 |", "extension/table")]
    [InlineData("~~struck~~", "extension/table")]
    [InlineData("- [ ] task", "extension/table")]
    [InlineData("[^footnote]: text", "extension/table")]
    public Task UnsupportedContentHasVisibleLabelAndOneExactBoundedSourceReader(string source, string label) => HeadlessSession.RunAsync(() =>
    {
        var result = Renderer.Render(source);
        result.IsFallback.Should().BeTrue();
        result.Status.Should().Contain(label).And.Contain("fallback");
        result.Source.Should().Be(source);
        result.SemanticText.Should().Be(source);
        var controls = MarkdownDocumentRenderer.CreateControls(result);
        controls.Should().HaveCount(2);
        ((SelectableTextBlock)controls[0]).Text.Should().Be(result.Status);
        ((SelectableTextBlock)controls[1]).Text.Should().Be(source);
    });

    [Theory]
    [InlineData("file:///C:/private.txt")]
    [InlineData("javascript:alert")]
    [InlineData("data:text/html,secret")]
    [InlineData("https://example.invalid/resource")]
    public Task LinkDestinationsAreOnlyLiteralNativeText(string destination) => HeadlessSession.RunAsync(() =>
    {
        var result = Renderer.Render($"[label]({destination})");
        result.IsFallback.Should().BeFalse();
        result.SemanticText.Should().Be($"label (destination: {destination})");
        MarkdownDocumentRenderer.CreateControls(result).Should()
            .OnlyContain(control => control is SelectableTextBlock);
    });

    [Fact]
    public Task ByteAdmissionUsesExactUtf8NotCharacterCount() => HeadlessSession.RunAsync(() =>
    {
        var source = string.Concat(Enumerable.Repeat("😀", NativeDetailProfile.MaximumUtf8Bytes / 4));
        Encoding.UTF8.GetByteCount(source).Should().Be(NativeDetailProfile.MaximumUtf8Bytes);
        var exact = Renderer.Render(source, DetailContentKind.PlainText);
        exact.IsFallback.Should().BeFalse();
        exact.Source.Should().Be(source);
        exact.SemanticText.Should().Be(source);
        var exceeded = Renderer.Render(source + "é");
        exceeded.IsFallback.Should().BeTrue();
        exceeded.Status.Should().Contain("UTF-8").And.Contain("refused").And.Contain("Nothing was truncated");
        // This unadmitted input cannot create an oversized native control.
        exceeded.Source.Should().BeEmpty();
        MarkdownDocumentRenderer.CreateControls(exceeded).Should().HaveCount(2);
    });

    [Fact]
    public void InvalidUnicodeIsRefusedBeforeParse()
    {
        var result = Renderer.Render("\ud800");
        result.IsFallback.Should().BeTrue();
        result.Status.Should().Contain("Unicode").And.Contain("refused");
        result.Source.Should().BeEmpty();
    }

    [Fact]
    public Task PlainTextNeverInvokesMarkdownOrResourceInterpretation() => HeadlessSession.RunAsync(() =>
    {
        const string source = "<script src='https://example.invalid'>secret</script>\n![file](file:///private)";
        var result = Renderer.Render(source, DetailContentKind.PlainText);
        result.IsFallback.Should().BeFalse();
        result.Source.Should().Be(source);
        result.SemanticText.Should().Be(source);
        result.BlockCount.Should().Be(0);
        MarkdownDocumentRenderer.CreateControls(result).Should().ContainSingle();
    });

    [Theory]
    [InlineData(2, false)]
    [InlineData(2, true)]
    [InlineData(-1, false)]
    [InlineData(-1, true)]
    [InlineData(int.MaxValue, false)]
    [InlineData(int.MaxValue, true)]
    public void RendererRejectsUnadmittedContentKindsBeforeParsing(int kind, bool tables)
    {
        const string source = "<h1>Exact source</h1>\r\n\n| Header |\n| --- |\n| value |";
        var renderer = new NativeDetailRenderer(NullLogger<NativeDetailRenderer>.Instance, pipeTablesAvailable: true);
        var result = renderer.Render(source, (DetailContentKind)kind,
            format: tables ? NativeDetailFormat.PipeTablesV2 : NativeDetailFormat.TextV1);
        result.IsFallback.Should().BeTrue();
        result.Status.Should().Be("Unsupported content kind; exact source fallback.");
        result.Source.Should().Be(source);
        result.SemanticText.Should().Be(source);
        result.Sections.Should().BeEmpty();
        result.BlockCount.Should().Be(0);
        result.NodeCount.Should().Be(0);
    }

    [Fact]
    public void SemanticProjectionIsUtf8BoundedAndEmptyRenderingIsNotReportedAsSuccess()
    {
        var unicode = new string('é', (NativeDetailProfile.MaximumUtf8Bytes - 10) / 2);
        var source = $"[label]({unicode})";
        Encoding.UTF8.GetByteCount(source).Should().BeLessThanOrEqualTo(NativeDetailProfile.MaximumUtf8Bytes);
        var result = Renderer.Render(source);
        result.IsFallback.Should().BeTrue();
        result.Status.Should().Contain("Semantic text limit");
        result.Source.Should().Be(source);
        var emptyProjection = Renderer.Render("[definition]: https://example.invalid");
        emptyProjection.IsFallback.Should().BeTrue();
        emptyProjection.Status.Should().Contain("No readable");
        emptyProjection.Source.Should().Be("[definition]: https://example.invalid");
    }

    [Fact]
    public void RepeatedReferenceLinksCannotExpandSmallAdmittedSourceIntoAnUnboundedProjection()
    {
        var source = string.Join(' ', Enumerable.Repeat("[label][ref]", 400))
            + "\n\n[ref]: https://example.invalid/" + new string('a', 128 * 1024);
        Encoding.UTF8.GetByteCount(source).Should().BeLessThan(NativeDetailProfile.MaximumUtf8Bytes);
        var result = Renderer.Render(source);
        result.IsFallback.Should().BeTrue();
        result.Status.Should().Contain("Semantic text limit");
        result.Source.Should().Be(source);
        result.Sections.Should().BeEmpty();
    }

    [Fact]
    public Task DetailStructuredControlsUseNativeHeadingAndCodeStylesWithoutAutomaticCopyControls() => HeadlessSession.RunAsync(() =>
    {
        var result = Renderer.Render("# Heading\n\n```text\ncode\n```");
        var controls = MarkdownDocumentRenderer.CreateControls(result, selectable: false);
        controls.Should().HaveCount(2);
        controls.Should().OnlyContain(control => control.GetType() == typeof(TextBlock));
        ((TextBlock)controls[0]).FontSize.Should().Be(32);
        ((TextBlock)controls[1]).FontSize.Should().Be(13);
        ((TextBlock)controls[1]).Text.Should().Contain("code");
    });

    [Fact]
    public void ExactAndExceededBlockBoundsCountEveryBlockIncludingNestedContainers()
    {
        var exactSource = string.Join("\n\n", Enumerable.Repeat("paragraph", NativeDetailProfile.MaximumBlocks));
        var exact = Renderer.Render(exactSource);
        exact.IsFallback.Should().BeFalse();
        exact.BlockCount.Should().Be(512);
        var overSource = exactSource + "\n\nextra";
        var over = Renderer.Render(overSource);
        over.IsFallback.Should().BeTrue();
        over.Source.Should().Be(overSource);
        over.BlockCount.Should().Be(513);
        var nested = Renderer.Render(string.Join("\n", Enumerable.Repeat("> - item", 260)));
        nested.IsFallback.Should().BeTrue();
        nested.Status.Should().Contain("Structural");
        nested.BlockCount.Should().Be(513);
    }

    [Fact]
    public void IterativeValidationCountsSiblingInlineNodesAndRootContainersAtExactLimit()
    {
        var document = new MarkdownDocument();
        var inline = new ContainerInline();
        var paragraph = new ParagraphBlock { Inline = inline };
        document.Add(paragraph);
        for (var index = 0; index < 4093; index++) { inline.AppendChild(new LiteralInline("x")); }
        var exact = NativeDetailRenderer.Validate(document);
        exact.Error.Should().BeNull();
        exact.Nodes.Should().Be(4096);
        inline.AppendChild(new LiteralInline("x"));
        var over = NativeDetailRenderer.Validate(document);
        over.Error.Should().Contain("4096 nodes");
        over.Nodes.Should().Be(4097);
    }

    [Fact]
    public void IterativeValidationCountsMixedNestedAndSiblingTreesAtDepthBoundary()
    {
        var document = new MarkdownDocument();
        var paragraph = new ParagraphBlock();
        var root = new ContainerInline();
        paragraph.Inline = root;
        document.Add(paragraph);
        var parent = root;
        // Root document depth 0, paragraph 1, inline root 2, nested containers through 31, literal 32.
        for (var depth = 3; depth <= 31; depth++)
        {
            var nested = new EmphasisInline { DelimiterChar = '*', DelimiterCount = 1 };
            parent.AppendChild(new LiteralInline("sibling"));
            parent.AppendChild(nested);
            parent = nested;
        }
        parent.AppendChild(new LiteralInline("last"));
        var exact = NativeDetailRenderer.Validate(document);
        exact.Error.Should().BeNull();
        exact.Depth.Should().Be(32);
        var extra = new EmphasisInline { DelimiterChar = '*', DelimiterCount = 1 };
        extra.AppendChild(new LiteralInline("too deep"));
        parent.AppendChild(extra);
        var over = NativeDetailRenderer.Validate(document);
        over.Error.Should().Contain("depth 32");
        over.Depth.Should().Be(33);
    }

    [Theory]
    [InlineData("> ")]
    [InlineData("[")]
    [InlineData("(")]
    public Task DeepHostileSourceIsRejectedBeforeVisualConstruction(string marker) => HeadlessSession.RunAsync(() =>
    {
        var source = string.Concat(Enumerable.Repeat(marker, 10000)) + "deep";
        var result = Renderer.Render(source);
        result.IsFallback.Should().BeTrue();
        result.Status.Should().Contain("preflight");
        result.BlockCount.Should().Be(0);
        result.NodeCount.Should().Be(0);
        result.Source.Should().Be(source);
        MarkdownDocumentRenderer.CreateControls(result).Should().HaveCount(2);
    });

    [Fact]
    public Task ParsedSiblingInlineNodeBombIsBoundedWithoutDroppingSource() => HeadlessSession.RunAsync(() =>
    {
        var source = string.Concat(Enumerable.Repeat("a *b* ", 1600));
        var result = Renderer.Render(source);
        result.IsFallback.Should().BeTrue();
        result.Status.Should().Contain("4096 nodes");
        result.Source.Should().Be(source);
        MarkdownDocumentRenderer.CreateControls(result).Should().HaveCount(2);
    });

    [Fact]
    public void ParsedNestingIsCheckedAlongWithTheParserNestingCap()
    {
        var source = string.Concat(Enumerable.Repeat("> ", 29)) + "text";
        var exact = Renderer.Render(source);
        exact.IsFallback.Should().BeFalse();
        exact.Depth.Should().Be(32);
        var overSource = "> " + source;
        var over = Renderer.Render(overSource);
        over.IsFallback.Should().BeTrue();
        over.Status.Should().Contain("fallback");
        over.Source.Should().Be(overSource);
    }

    [Fact]
    public Task MissingRendererRetainsTheEntireMaximumByteSourceWithoutTruncation() => HeadlessSession.RunAsync(() =>
    {
        var source = string.Concat(Enumerable.Repeat("😀", NativeDetailProfile.MaximumUtf8Bytes / 4));
        var result = new NativeDetailRenderer(NullLogger<NativeDetailRenderer>.Instance, available: false).Render(source);
        result.IsFallback.Should().BeTrue();
        result.Source.Should().Be(source);
        result.SemanticText.Should().Be(source);
        MarkdownDocumentRenderer.CreateControls(result).Should().HaveCount(2);
    });

    [Fact]
    public void UnknownAstNodeIsNotSilentlyDiscarded()
    {
        var document = new MarkdownDocument { new UnsupportedTestBlock() };
        NativeDetailRenderer.Validate(document).Error.Should().Contain("Unsupported Markdown node");
    }

    [Fact]
    public Task MissingRendererKeepsFullUnicodeSourceAndExplicitFallback() => HeadlessSession.RunAsync(() =>
    {
        var source = "<script>private Unicode 😀 é \r\n</script>";
        var result = new NativeDetailRenderer(NullLogger<NativeDetailRenderer>.Instance, available: false).Render(source);
        result.IsFallback.Should().BeTrue();
        result.Status.Should().Contain("Renderer unavailable");
        result.Source.Should().Be(source);
        result.SemanticText.Should().Be(source);
        ((SelectableTextBlock)MarkdownDocumentRenderer.CreateControls(result)[1]).Text.Should().Be(source);
    });

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void FailureLogsHaveFixedTemplatesAndNoContentOrExceptionPayload(int profile)
    {
        const string secret = "<script>NEVER_LOG_THIS_SECRET</script>";
        var logger = new RecordingLogger();
        var renderer = new NativeDetailRenderer(logger, pipeTablesAvailable: true, checklistsAvailable: true);
        renderer.Render(secret, format: (NativeDetailFormat)profile)
            .IsFallback.Should().BeTrue();
        logger.Messages.Should().ContainSingle();
        logger.Messages[0].Should().Contain("Native detail source fallback:").And.Contain("Structure")
            .And.NotContain(secret).And.NotContain("NEVER_LOG");
        logger.Exceptions.Should().OnlyContain(exception => exception == null);
        logger.Messages[0].Should().Contain(profile switch
        {
            2 => NativeMarkdownChecklistProfile.Name,
            1 => NativeMarkdownTableProfile.Name,
            _ => NativeDetailProfile.Name,
        });
    }

    private sealed class UnsupportedTestBlock() : LeafBlock(null);

    private sealed class RecordingLogger : ILogger<NativeDetailRenderer>
    {
        public List<string> Messages { get; } = [];
        public List<Exception?> Exceptions { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
            Exceptions.Add(exception);
        }
    }
}
