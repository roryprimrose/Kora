using System.Text;

using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

using AwesomeAssertions;

using Kora.Application.Documentation;
using Kora.Application.Presentation;
using Kora.Core.Hosting;
using Kora.Core.Presentation;
using Kora.Core.Presentation.MarkdownStrikethrough;

using Markdig.Syntax;
using Markdig.Syntax.Inlines;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class MarkdownStrikethroughTests
{
    private static NativeDetailRenderer Renderer(bool qualified = true) =>
        new(NullLogger<NativeDetailRenderer>.Instance, strikethroughAvailable: qualified);

    private static NativeDocumentResult Render(string source) =>
        Renderer().Render(source, format: NativeDetailFormat.MarkdownStrikethroughV4);

    [Theory]
    [InlineData("Before ~~old~~ after", "Before [strikethrough: old] after", "~~old~~", "old")]
    [InlineData("# 😀 ~~e\u0301~~\r\n", "😀 [strikethrough: e\u0301]", "~~e\u0301~~", "e\u0301")]
    [InlineData("> - ~~**old** *word*~~", "> - [strikethrough: old word]", "~~**old** *word*~~", "old word")]
    [InlineData("*~~old~~* **new**", "[strikethrough: old] new", "~~old~~", "old")]
    [InlineData("~~a `literal`~~", "[strikethrough: a literal]", "~~a `literal`~~", "a literal")]
    [InlineData("~~[label](javascript:alert)~~",
        "[strikethrough: label (destination: javascript:alert)]", "~~[label](javascript:alert)~~",
        "label (destination: javascript:alert)")]
    public void ExactSourceRangesAndLabelledProjectionPreserveUnicodeAndOrdinaryEmphasis(
        string source, string semantic, string exactSpan, string displayedSpan)
    {
        var result = Render(source);
        result.IsFallback.Should().BeFalse();
        result.Source.Should().Be(source);
        result.SemanticText.Should().Be(semantic);
        result.Status.Should().Contain(NativeMarkdownStrikethroughProfile.Name).And.Contain("display-only");
        var section = result.Sections.Single();
        var span = section.Strikethrough.Single();
        source[span.SourceStart..span.SourceEnd].Should().Be(exactSpan);
        section.Text[span.TextStart..span.TextEnd].Should().Be(displayedSpan);
    }

    [Theory]
    [InlineData("~one~", "~one~")]
    [InlineData("~~unmatched", "~~unmatched")]
    [InlineData("unmatched~~", "unmatched~~")]
    [InlineData(@"You \~\~see\~\~ this", "You ~~see~~ this")]
    [InlineData("`~~inline~~`", "~~inline~~")]
    [InlineData("```text\n~~fenced~~\n```", "~~fenced~~")]
    [InlineData("~~~text\n~~tilde fenced~~\n~~~", "~~tilde fenced~~")]
    [InlineData("    ~~indented~~", "~~indented~~")]
    public void CodeEscapesAndUnmatchedDelimitersStayLiteral(string source, string semantic)
    {
        var result = Render(source);
        result.IsFallback.Should().BeFalse();
        result.Source.Should().Be(source);
        result.SemanticText.Should().Be(semantic);
        result.Sections.SelectMany(section => section.Strikethrough).Should().BeEmpty();
    }

    [Fact]
    public void NestedAndAdjacentSpansHaveExactSourceAndBoundedSectionCoordinates()
    {
        const string source = "~~outer **bold ~~inner~~** end~~ and ~~next~~";
        var result = Render(source);
        result.IsFallback.Should().BeFalse();
        result.Source.Should().Be(source);
        var section = result.Sections.Single();
        section.Strikethrough.Should().HaveCount(3);
        foreach (var span in section.Strikethrough)
        {
            source[span.SourceStart..span.SourceEnd].Should().StartWith("~~").And.EndWith("~~");
            span.TextStart.Should().BeGreaterThanOrEqualTo(0);
            span.TextEnd.Should().BeLessThanOrEqualTo(section.Text.Length);
        }
        section.Text.Should().Be("[strikethrough: outer bold [strikethrough: inner] end] and [strikethrough: next]");
    }

    [Fact]
    public Task NativeProjectionIsLabelledDecoratedFocusableAndInertWithoutColor() => HeadlessSession.RunAsync(() =>
    {
        var result = Render("Before ~~old~~ after");
        var controls = MarkdownDocumentRenderer.CreateControls(result, selectable: false);
        var control = controls.Should().ContainSingle().Which.Should().BeAssignableTo<TextBlock>().Subject;
        control.Focusable.Should().BeTrue();
        control.ContextMenu.Should().BeNull();
        control.Inlines!.Text.Should().Be(result.SemanticText);
        var runs = control.Inlines.OfType<Run>().ToArray();
        runs.Where(run => run.TextDecorations == TextDecorations.Strikethrough)
            .Should().ContainSingle().Which.Text.Should().Be("old");
        var peer = ControlAutomationPeer.CreatePeerForElement(control)!;
        peer.GetName().Should().Be("Before [strikethrough: old] after");
        AutomationProperties.GetHelpText(control).Should().Contain("not a diff").And.Contain("Exact source");
        peer.GetAutomationControlType().Should().Be(AutomationControlType.Text);
        peer.GetProvider<IInvokeProvider>().Should().BeNull();
        peer.GetProvider<IToggleProvider>().Should().BeNull();
        MarkdownDocumentRenderer.CreateControls(result).Single().Should().BeOfType<SelectableTextBlock>();
        NativeStrikethroughText.Retire(controls);
        control.Inlines.Should().BeEmpty();
        control.Text.Should().BeEmpty();
        peer.GetName().Should().BeNullOrEmpty();
        AutomationProperties.GetHelpText(control).Should().BeEmpty();
    });

    [Fact]
    public void OnlyTheExplicitQualifiedHostProfileCanInterpretStrikethrough()
    {
        const string source = "~~unchanged~~\r\n";
        foreach (var legacy in new[] { NativeDetailFormat.TextV1, NativeDetailFormat.PipeTablesV2,
            NativeDetailFormat.MarkdownChecklistsV3 })
        {
            var result = Renderer().Render(source, format: legacy);
            result.IsFallback.Should().BeTrue();
            result.Source.Should().Be(source);
            result.SemanticText.Should().Be(source);
        }
        var unavailable = Renderer(false).Render(source, format: NativeDetailFormat.MarkdownStrikethroughV4);
        unavailable.IsFallback.Should().BeTrue();
        unavailable.Status.Should().Contain(NativeMarkdownStrikethroughProfile.Name).And.Contain("unavailable");
        unavailable.Source.Should().Be(source);
        unavailable.SemanticText.Should().Be(source);
        unavailable.Sections.Should().BeEmpty();
        Renderer(false).Render("ordinary", format: NativeDetailFormat.MarkdownStrikethroughV4)
            .IsFallback.Should().BeTrue();
        var unknown = Renderer().Render(source, format: (NativeDetailFormat)int.MaxValue);
        unknown.IsFallback.Should().BeTrue();
        unknown.Source.Should().Be(source);
        unknown.SemanticText.Should().Be(source);
        var plain = Renderer().Render(source, DetailContentKind.PlainText,
            format: NativeDetailFormat.MarkdownStrikethroughV4);
        plain.IsFallback.Should().BeFalse();
        plain.SemanticText.Should().Be(source);
        plain.Sections.Single().Strikethrough.Should().BeEmpty();
    }

    [Theory]
    [InlineData("~~old~~ <script>never</script>")]
    [InlineData("~~&amp; &#x1F600;~~")]
    [InlineData("~~old~~ ![remote](https://example.invalid/a.png)")]
    [InlineData("~~old~~ ![local](file:///private)")]
    [InlineData("~~old~~\n\n| Heading |\n| --- |\n| cell |")]
    [InlineData("~~old~~\n\n- [x] task")]
    [InlineData("~~old~~\n\n[^footnote]: text")]
    [InlineData("~~old~~\n\n:::extra")]
    [InlineData("~~old~~\n\n```mermaid\ngraph TD; A-->B\n```")]
    public void UnrelatedExtensionsAndActiveContentNeverPublishPartialSemantics(string source)
    {
        var result = Render(source);
        result.IsFallback.Should().BeTrue();
        result.Source.Should().Be(source);
        result.SemanticText.Should().Be(source);
        result.Sections.Should().BeEmpty();
    }

    [Fact]
    public void SourceAndLabelExpansionRetainTheExactStrictUtf8Ceiling()
    {
        var overhead = NativeMarkdownStrikethroughProfile.StartLabel.Length
            + NativeMarkdownStrikethroughProfile.EndLabel.Length;
        var source = "~~" + new string('a', NativeDetailProfile.MaximumUtf8Bytes - overhead) + "~~";
        var exact = Render(source);
        exact.IsFallback.Should().BeFalse();
        Encoding.UTF8.GetByteCount(exact.SemanticText).Should().Be(NativeDetailProfile.MaximumUtf8Bytes);
        var overSemantic = Render(source.Insert(2, "a"));
        overSemantic.IsFallback.Should().BeTrue();
        overSemantic.Status.Should().Contain("Semantic text limit");
        overSemantic.Source.Should().Be(source.Insert(2, "a"));
        var exactSource = "~~" + new string('a', NativeDetailProfile.MaximumUtf8Bytes - 4) + "~~";
        Encoding.UTF8.GetByteCount(exactSource).Should().Be(NativeDetailProfile.MaximumUtf8Bytes);
        Render(exactSource).Source.Should().Be(exactSource);
        Render(exactSource + "a").Source.Should().BeEmpty();
        Render("\ud800").Status.Should().Contain("Unicode");
    }

    [Fact]
    public void BlocksNodesAndNestingStillFailClosedWithoutPartialProjection()
    {
        foreach (var source in new[]
        {
            string.Join("\n\n", Enumerable.Repeat("~~x~~", NativeDetailProfile.MaximumBlocks + 1)),
            string.Join(' ', Enumerable.Repeat("~~x~~", NativeDetailProfile.MaximumNodes)),
            new string('>', NativeDetailProfile.MaximumNesting + 1) + " ~~x~~",
        })
        {
            var result = Render(source);
            result.IsFallback.Should().BeTrue();
            result.Source.Should().Be(source);
            result.SemanticText.Should().Be(source);
            result.Sections.Should().BeEmpty();
        }
    }

    [Fact]
    public void ExactStructuralCeilingsRemainInclusive()
    {
        var blocks = Render(string.Join("\n\n", Enumerable.Repeat("~~x~~", NativeDetailProfile.MaximumBlocks)));
        blocks.IsFallback.Should().BeFalse();
        blocks.BlockCount.Should().Be(NativeDetailProfile.MaximumBlocks);
        var root = new ContainerInline();
        for (var index = 0; index < NativeDetailProfile.MaximumNodes - 3; index++)
        {
            root.AppendChild(new LiteralInline("x"));
        }
        var document = new MarkdownDocument { new ParagraphBlock { Inline = root } };
        var exact = NativeDetailRenderer.Validate(document, strikethrough: true);
        exact.Error.Should().BeNull();
        exact.Nodes.Should().Be(NativeDetailProfile.MaximumNodes);
        root.AppendChild(new LiteralInline("x"));
        NativeDetailRenderer.Validate(document, strikethrough: true).Error.Should().Contain("4096");
        var nesting = string.Concat(Enumerable.Repeat("> ", 28)) + "~~x~~";
        var nested = Render(nesting);
        nested.IsFallback.Should().BeFalse();
        nested.Depth.Should().Be(NativeDetailProfile.MaximumNesting);
        Render("> " + nesting).IsFallback.Should().BeTrue();
    }

    [Fact]
    public Task CompleteDetailsKeepsExactCopyFindIdentityAndRetiresPrivateInlineState() => HeadlessSession.RunAsync(async () =>
    {
        const string source = "Before ~~old 😀~~ after\r\n";
        var content = Content(source);
        var state = new DetailViewerState(content);
        var generation = state.Generation;
        var result = Render(source);
        state.CompleteRender(generation, result.SemanticText, result.Status, result.IsFallback).Should().BeTrue();
        var writes = 0;
        var view = new DetailContentView(state, result, (_, _, _) => { writes++; return Task.CompletedTask; });
        var stack = view.FindControl<StackPanel>("RenderedDocument")!;
        var text = (TextBlock)stack.Children.Single();
        var peer = ControlAutomationPeer.CreatePeerForElement(text)!;
        state.Search("old 😀").Should().BeTrue();
        state.MatchCount.Should().Be(1);
        state.SetSource(true);
        state.ActiveText.Should().Be(source);
        state.MatchCount.Should().BeNull();
        state.Search("~~").Should().BeTrue();
        state.MatchCount.Should().Be(2);
        state.Content.Should().BeSameAs(content);
        state.Content!.Digest.Should().Be(content.Digest);
        state.TryGetCopySource(true, false, out _).Should().BeFalse("projection cannot bypass the disclosure gate");
        state.TryGetCopySource(true, true, out var original).Should().BeTrue();
        original.Should().Be(source, "the state exposes only unchanged exact source to its existing copy policy");
        view.ClearPresentation();
        view.ClearPresentation();
        state.Content.Should().BeNull();
        state.ActiveText.Should().BeEmpty();
        state.SearchQuery.Should().BeEmpty();
        state.MatchCount.Should().BeNull();
        text.Inlines.Should().BeEmpty();
        peer.GetName().Should().BeNullOrEmpty();
        stack.Children.Should().BeEmpty();
        state.CompleteRender(generation, "late", "late", false).Should().BeFalse();
        writes.Should().Be(0);
        await Task.CompletedTask;
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ControllerUsesHostSelectedProfileAndSourceRevocationCannotCrossSessions(bool qualified)
    {
        var accessible = true;
        var admitted = true;
        var views = new List<TestView>();
        var clipboard = new TestClipboard();
        using var controller = new DetailWindowController(new TestDocumentation(), () => accessible,
            Renderer(qualified), clipboard, (state, _, _) =>
            {
                var view = new TestView(state);
                views.Add(view);
                return view;
            }, NullLogger<DetailWindowController>.Instance,
            finalizedMarkdownFormat: NativeDetailFormat.MarkdownStrikethroughV4);
        var first = Content("~~first~~");
        var second = Content("~~second~~");
        controller.OpenCurrentResponse(first, null, () => admitted).Should().BeTrue();
        controller.OpenCurrentResponse(second, null, () => true).Should().BeTrue();
        views[0].State.Status.Should().Contain(NativeMarkdownStrikethroughProfile.Name);
        views[0].State.RenderState.Should().Be(qualified ? DetailRenderState.Rendered : DetailRenderState.SourceFallback);
        views[0].State.ActiveText.Should().NotContain("second");
        views[1].State.ActiveText.Should().NotContain("first");
        controller.OpenCurrentResponse(first, null, () => admitted).Should().BeTrue();
        views.Should().HaveCount(2);
        views[0].State.Search("first").Should().BeTrue();
        var generation = views[0].State.Generation;
        await controller.CopyAsync(first.Reference, generation, true);
        admitted = false;
        controller.RetireUnavailableCurrentResponses();
        views[0].Cleared.Should().BeTrue();
        views[0].State.SearchQuery.Should().BeEmpty();
        views[0].State.Content.Should().BeNull();
        views[1].Cleared.Should().BeFalse();
        accessible = false;
        controller.ClearForPrivacy();
        views[1].Cleared.Should().BeTrue();
        views[1].State.Content.Should().BeNull();
        views[0].State.CompleteRender(generation, "late", "late", false).Should().BeFalse();
        clipboard.Writes.Should().Be(0);
        controller.OpenCurrentResponse(first, null, () => admitted).Should().BeFalse();
    }

    private static AdmittedDetailContent Content(string source)
    {
        var request = HostRequest.Create(RequestOrigin.LocalUi);
        return new(new(new(Guid.NewGuid()), 1), DetailContentKind.Markdown, DetailContentOrigin.FinalizedResponse,
            DetailSensitivity.DisclosureConfirmationRequired, "Answer", "Immutable host snapshot", source,
            new(request.SessionId, request.RequestId, request.TaskId));
    }

    private sealed class TestDocumentation : IUserDocumentationProvider
    {
        public IReadOnlyList<UserDocumentationPage> GetPages() => [];
        public UserDocumentationPage GetStartPage() => throw new InvalidOperationException("No guide source in this test.");
    }

    private sealed class TestView(DetailViewerState state) : IDetailView
    {
        public DetailViewerState State { get; } = state;
        public bool Cleared { get; private set; }
        public event EventHandler? Closed;
        public void ShowOwned(Window? owner) { }
        public void Activate() { }
        public void ClearAndClose()
        {
            Cleared = true;
            State.Close();
            Closed?.Invoke(this, EventArgs.Empty);
        }
    }

    private sealed class TestClipboard : IDetailClipboard
    {
        public int Writes { get; private set; }
        public Task WritePlainTextAsync(IDetailView view, string source)
        {
            Writes++;
            return Task.CompletedTask;
        }
    }
}
