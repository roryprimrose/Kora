using System.Text;

using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;

using AwesomeAssertions;

using Kora.Controls;
using Kora.Core.Presentation;

using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class NativeMarkdownChecklistTests
{
    private const string Source = "- [ ] Deploy **Alpha**\r\n- [x] Verify `Beta`\r\n- [X] Finish Gamma\r\n";

    private static NativeDocumentResult Render(string source) =>
        new NativeDetailRenderer(NullLogger<NativeDetailRenderer>.Instance, checklistsAvailable: true)
            .Render(source, format: NativeDetailFormat.MarkdownChecklistsV3);

    [Fact]
    public Task ChecklistStatesExposeInertAccessibleTextRatherThanCheckboxes() => HeadlessSession.RunAsync(() =>
    {
        var result = Render(Source);
        result.IsFallback.Should().BeFalse();
        result.Source.Should().Be(Source);
        result.Status.Should().Contain(NativeMarkdownChecklistProfile.Name).And.Contain("display-only");
        result.Sections.Should().HaveCount(3).And.OnlyContain(section => section.Style == NativeTextStyle.Checklist);
        result.SemanticText.Should().Contain("- Unchecked: Deploy Alpha")
            .And.Contain("- Checked: Verify Beta").And.Contain("- Checked: Finish Gamma");
        var controls = MarkdownDocumentRenderer.CreateControls(result, selectable: false);
        controls.Should().OnlyContain(control => control is NamedTextBlock && control.Focusable);
        for (var index = 0; index < controls.Count; index++)
        {
            var peer = ControlAutomationPeer.CreatePeerForElement(controls[index])!;
            peer.GetAutomationControlType().Should().Be(AutomationControlType.Text);
            peer.GetName().Should().Be(result.Sections[index].Text);
            peer.GetProvider<IToggleProvider>().Should().BeNull();
            peer.GetProvider<IInvokeProvider>().Should().BeNull();
            ((TextBlock)controls[index]).ContextMenu.Should().BeNull();
        }
        MarkdownDocumentRenderer.CreateControls(result).Should()
            .OnlyContain(control => control is SelectableTextBlock);
    });

    [Fact]
    public void ExplicitHostProfileAndIndependentQualificationAreRequired()
    {
        var renderer = new NativeDetailRenderer(NullLogger<NativeDetailRenderer>.Instance, pipeTablesAvailable: true);
        foreach (var format in new[] { NativeDetailFormat.TextV1, NativeDetailFormat.PipeTablesV2 })
        {
            var legacy = renderer.Render(Source, format: format);
            legacy.IsFallback.Should().BeTrue();
            legacy.Source.Should().Be(Source);
        }
        var unavailable = renderer.Render(Source, format: NativeDetailFormat.MarkdownChecklistsV3);
        unavailable.IsFallback.Should().BeTrue();
        unavailable.Status.Should().Contain(NativeMarkdownChecklistProfile.Name).And.Contain("unavailable")
            .And.Contain("qualification");
        unavailable.Source.Should().Be(Source);
        unavailable.SemanticText.Should().Be(Source);
        unavailable.Sections.Should().BeEmpty();
        renderer.Render("Ordinary text", format: NativeDetailFormat.MarkdownChecklistsV3)
            .IsFallback.Should().BeTrue("an unqualified requested profile cannot masquerade as native-text-v1");
        new NativeDetailRenderer(NullLogger<NativeDetailRenderer>.Instance, checklistsAvailable: true)
            .Render(Source).IsFallback.Should().BeTrue("availability does not let markup select a profile");
        var unknown = renderer.Render(Source, format: (NativeDetailFormat)int.MaxValue);
        unknown.IsFallback.Should().BeTrue();
        unknown.Source.Should().Be(Source);
        new NativeDetailRenderer(NullLogger<NativeDetailRenderer>.Instance, available: false, checklistsAvailable: true)
            .Render(Source, format: NativeDetailFormat.MarkdownChecklistsV3).IsFallback.Should().BeTrue();
    }

    [Fact]
    public void OrdinaryListsEscapesAndCodeKeepLiteralMeaning()
    {
        const string source = "3. [ ] pending\n4. ordinary\n\n> - [x] quoted\n\n- [ ] parent\n  - [X] nested\n\n"
            + @"- \[x] escaped" + "\n\n`- [ ] inline`\n\n```text\n- [x] fenced\n```\n\n    - [ ] indented";
        var result = Render(source);
        result.IsFallback.Should().BeFalse();
        result.SemanticText.Should().Contain("3. Unchecked: pending").And.Contain("4. ordinary")
            .And.Contain("> - Checked: quoted").And.Contain("- Unchecked: parent")
            .And.Contain("- - Checked: nested").And.Contain("- [x] escaped")
            .And.Contain("- [ ] inline").And.Contain("- [x] fenced").And.Contain("- [ ] indented");
        result.Source.Should().Be(source);
        var literal = Render("Paragraph [x] is not a task.\n\n- [z] ordinary\n\n- `[ ]` code label");
        literal.IsFallback.Should().BeFalse();
        literal.SemanticText.Should().Contain("Paragraph [x] is not a task.").And.Contain("- [z] ordinary")
            .And.Contain("- [ ] code label");
        literal.Sections.Should().NotContain(section => section.Style == NativeTextStyle.Checklist);
    }

    [Theory]
    [InlineData("<script src='https://example.invalid/a'>never</script>")]
    [InlineData("- [ ] a <img src='file:///private'>")]
    [InlineData("- [x] ![remote](https://example.invalid/a.png)")]
    [InlineData("- [ ] ![data](data:image/png;base64,AA)")]
    [InlineData("- [ ] ~~struck~~")]
    [InlineData(":::unsupported")]
    [InlineData("[^footnote]: text")]
    [InlineData("| Header |\n| --- |\n| value |")]
    [InlineData("```mermaid\ngraph TD; A-->B\n```")]
    public void UnsafeOrUnsupportedContentRetainsWholeSourceInsteadOfPartialChecklist(string suffix)
    {
        var source = Source + "\n" + suffix;
        var result = Render(source);
        result.IsFallback.Should().BeTrue();
        result.Source.Should().Be(source);
        result.SemanticText.Should().Be(source);
        result.Sections.Should().BeEmpty();
    }

    [Fact]
    public void LinkDestinationsAreInertAndPlainTextNeverInterpretsChecklistMarkup()
    {
        var result = Render("- [x] [label](javascript:alert)");
        result.IsFallback.Should().BeFalse();
        result.SemanticText.Should().Contain("Checked: label (destination: javascript:alert)");
        var literal = new NativeDetailRenderer(NullLogger<NativeDetailRenderer>.Instance, checklistsAvailable: true)
            .Render(Source, DetailContentKind.PlainText, format: NativeDetailFormat.MarkdownChecklistsV3);
        literal.IsFallback.Should().BeFalse();
        literal.SemanticText.Should().Be(Source);
        literal.BlockCount.Should().Be(0);
    }

    [Fact]
    public void ExactSourceAndExpandedSemanticBytesUseUnchangedStrictUtf8Budget()
    {
        const string prefix = "- [ ] ";
        var padding = new string('é', (NativeDetailProfile.MaximumUtf8Bytes - prefix.Length - 2) / 2);
        var exact = prefix + padding + "aa";
        Encoding.UTF8.GetByteCount(exact).Should().Be(NativeDetailProfile.MaximumUtf8Bytes);
        var expansion = Render(exact);
        expansion.IsFallback.Should().BeTrue("display-state labels must also fit the shared semantic budget");
        expansion.Status.Should().Contain("Semantic text limit");
        expansion.Source.Should().Be(exact);
        var semanticExact = prefix + padding[..^3] + "a";
        var rendered = Render(semanticExact);
        rendered.IsFallback.Should().BeFalse();
        Encoding.UTF8.GetByteCount(rendered.SemanticText).Should().Be(NativeDetailProfile.MaximumUtf8Bytes);
        var exceeded = Render(exact + "é");
        exceeded.IsFallback.Should().BeTrue();
        exceeded.Source.Should().BeEmpty("unadmitted oversized source cannot construct a fallback control");
        exceeded.Status.Should().Contain("Nothing was truncated");
        Render("\ud800").Status.Should().Contain("Unicode");
    }

    [Fact]
    public void ExactAndExceededBlockAndDepthBoundsIncludeChecklistContainers()
    {
        var blocks = "- [ ] item\n\n" + string.Join("\n\n",
            Enumerable.Repeat("paragraph", NativeDetailProfile.MaximumBlocks - 3));
        var exact = Render(blocks);
        exact.IsFallback.Should().BeFalse();
        exact.BlockCount.Should().Be(NativeDetailProfile.MaximumBlocks);
        var over = Render(blocks + "\n\nextra");
        over.IsFallback.Should().BeTrue();
        over.BlockCount.Should().Be(NativeDetailProfile.MaximumBlocks + 1);
        over.Source.Should().Be(blocks + "\n\nextra");
        var depth = string.Concat(Enumerable.Repeat("> ", 27)) + "- [ ] item";
        var deepest = Render(depth);
        deepest.IsFallback.Should().BeFalse();
        deepest.Depth.Should().Be(NativeDetailProfile.MaximumNesting);
        var exceeded = Render("> " + depth);
        exceeded.IsFallback.Should().BeTrue();
        exceeded.Source.Should().Be("> " + depth);
        Render(new string(' ', 66) + "- [ ] item").Status.Should().Contain("preflight");
        Render(new string('[', 33) + "x").Status.Should().Contain("preflight");
    }

    [Fact]
    public void ExactNodeBudgetCountsTaskStateInlineAndRejectsOneMoreNode()
    {
        var pipeline = new MarkdownPipelineBuilder().UseTaskLists().Build();
        var document = Markdown.Parse("- [ ] item", pipeline);
        var inline = document.Descendants<ParagraphBlock>().Single().Inline!;
        var initial = NativeDetailRenderer.Validate(document, checklists: true);
        initial.Error.Should().BeNull();
        for (var index = initial.Nodes; index < NativeDetailProfile.MaximumNodes; index++)
        { inline.AppendChild(new LiteralInline("x")); }
        var exact = NativeDetailRenderer.Validate(document, checklists: true);
        exact.Error.Should().BeNull();
        exact.Nodes.Should().Be(NativeDetailProfile.MaximumNodes);
        NativeDetailRenderer.Validate(document).Error.Should().Contain("Unsupported Markdown node");
        inline.AppendChild(new LiteralInline("x"));
        var over = NativeDetailRenderer.Validate(document, checklists: true);
        over.Error.Should().Contain("4096 nodes");
        over.Nodes.Should().Be(NativeDetailProfile.MaximumNodes + 1);
        var bomb = Source + "\n" + string.Concat(Enumerable.Repeat("a *b* ", 1600));
        var fallback = Render(bomb);
        fallback.IsFallback.Should().BeTrue();
        fallback.Source.Should().Be(bomb);
        fallback.Sections.Should().BeEmpty();
    }
}
