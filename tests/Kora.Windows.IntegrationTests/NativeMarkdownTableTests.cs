using System.Diagnostics;
using System.Text;

using Avalonia.Automation.Peers;
using Avalonia.Controls;

using AwesomeAssertions;

using Kora.Application.Presentation;
using Kora.Controls;
using Kora.Core.Presentation;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class NativeMarkdownTableTests
{
    private const string Source = "| Name | State |\r\n| :--- | ---: |\r\n| **Alpha** | `Ready` |\r\n| Beta | |\r\n";

    private static NativeDocumentResult Render(string source) =>
        new NativeDetailRenderer(NullLogger<NativeDetailRenderer>.Instance, pipeTablesAvailable: true)
            .Render(source, format: NativeDetailFormat.PipeTablesV2);

    [Fact]
    public Task TableRowsExposeCompleteHeaderRelationshipsAsInertAccessibleNativeText() => HeadlessSession.RunAsync(() =>
    {
        var result = Render(Source);
        result.IsFallback.Should().BeFalse();
        result.Source.Should().Be(Source);
        result.Status.Should().Contain(NativeMarkdownTableProfile.Name).And.Contain("not a visual grid");
        result.Sections.Should().HaveCount(3);
        result.SemanticText.Should().Contain("Column 1: Name").And.Contain("Column 2: State")
            .And.Contain("Column 1 (Name): Alpha").And.Contain("Column 2 (State): Ready")
            .And.Contain("Column 2 (State): (empty cell)");
        var controls = MarkdownDocumentRenderer.CreateControls(result, selectable: false);
        controls.Should().OnlyContain(control => control is NamedTextBlock && control.Focusable);
        for (var index = 0; index < controls.Count; index++)
        {
            var peer = ControlAutomationPeer.CreatePeerForElement(controls[index])!;
            peer.GetAutomationControlType().Should().Be(AutomationControlType.Text);
            peer.GetName().Should().Be(result.Sections[index].Text);
            ((TextBlock)controls[index]).ContextMenu.Should().BeNull();
        }
        MarkdownDocumentRenderer.CreateControls(result).Should()
            .OnlyContain(control => control is SelectableTextBlock);
    });

    [Fact]
    public void LegacyProfileAndUnqualifiedDetailRouteDoNotEnableTables()
    {
        var renderer = new NativeDetailRenderer(NullLogger<NativeDetailRenderer>.Instance);
        var legacy = renderer.Render(Source);
        legacy.IsFallback.Should().BeTrue();
        legacy.Status.Should().Contain("Unsupported Markdown extension/table");
        var unavailable = renderer.Render(Source, format: NativeDetailFormat.PipeTablesV2);
        unavailable.IsFallback.Should().BeTrue();
        unavailable.Status.Should().Contain(NativeMarkdownTableProfile.Name).And.Contain("unavailable")
            .And.Contain("qualification").And.Contain("exact source fallback");
        unavailable.Source.Should().Be(Source);
        unavailable.SemanticText.Should().Be(Source);
        renderer.Render("# Existing text", format: NativeDetailFormat.PipeTablesV2)
            .Status.Should().Contain(NativeDetailProfile.Name);
        new NativeDetailRenderer(NullLogger<NativeDetailRenderer>.Instance, pipeTablesAvailable: true)
            .Render(Source).IsFallback.Should().BeTrue();
    }

    [Theory]
    [InlineData("| A | B |\n| --- | --- |\n| one |")]
    [InlineData("| A | B |\n| --- | --- |\n| one | two | lost |")]
    [InlineData("| A | B |\n| --- |\n| one | two |")]
    [InlineData("| A | B |\n| --- | invalid |\n| one | two |")]
    [InlineData("| A | B |\n| --- | --- |\n| one | two |\n\n| C | D |\n| --- | invalid |\n| three | four |")]
    [InlineData("| --- | --- |\n| one | two |")]
    [InlineData("> | A | B |\n> | --- | --- |\n> | one | two |")]
    [InlineData("| A | B |\n| --- | --- |\n| <b>one</b> | two |")]
    [InlineData("| A | B |\n| --- | --- |\n| ![remote](https://example.invalid/a) | two |")]
    [InlineData("| A | B |\n| --- | --- |\n| ~~one~~ | two |")]
    [InlineData("| A | B |\n| --- | --- |\n| one | two |\n\n```mermaid\ngraph TD\n```")]
    public void UnsupportedOrMalformedTablesRetainEntireSourceWithoutPartialRows(string source)
    {
        var result = Render(source);
        result.IsFallback.Should().BeTrue();
        result.Status.Should().Contain("fallback");
        result.Source.Should().Be(source);
        result.SemanticText.Should().Be(source);
        result.Sections.Should().BeEmpty();
    }

    [Fact]
    public void CellTextRetainsEscapesUnicodeAndInertLinkDestinations()
    {
        const string source = "| | Link |\n| --- | --- |\n| a\\|b é 😀 | [label](javascript:alert) |";
        var result = Render(source);
        result.IsFallback.Should().BeFalse();
        result.SemanticText.Should().Contain("Column 1: (empty header)")
            .And.Contain("Column 1 (empty header): a|b é 😀")
            .And.Contain("label (destination: javascript:alert)");
        result.Source.Should().Be(source);
    }

    [Fact]
    public void RowAndColumnBoundsAreInclusiveAndNeverEnlargeTheSharedProfile()
    {
        var rows = Table(1, NativeMarkdownTableProfile.MaximumRowsPerTable);
        Render(rows).IsFallback.Should().BeFalse();
        var overRows = rows + "\n| extra |";
        Render(overRows).IsFallback.Should().BeTrue();
        Render(overRows).Source.Should().Be(overRows);
        var columns = Table(NativeMarkdownTableProfile.MaximumColumns, 2);
        Render(columns).IsFallback.Should().BeFalse();
        var overColumns = Table(NativeMarkdownTableProfile.MaximumColumns + 1, 2);
        Render(overColumns).IsFallback.Should().BeTrue();
        Render(overColumns).Source.Should().Be(overColumns);
    }

    [Fact]
    public void DocumentCellBudgetIncludesAllHeadersAndAllTables()
    {
        var exact = Table(4, 32);
        var result = Render(exact);
        result.IsFallback.Should().BeFalse();
        // Document + table + rows + cells + paragraphs + inline roots + literals.
        result.NodeCount.Should().Be(1 + 1 + 32 + (128 * 4));
        result.BlockCount.Should().Be(1 + 32 + (128 * 2));
        var over = exact + "\n\n" + Table(1, 1);
        var fallback = Render(over);
        fallback.IsFallback.Should().BeTrue();
        fallback.Status.Should().Contain("cell limit");
        fallback.Source.Should().Be(over);
        var multiple = Render(Table(2, 2) + "\n\n" + Table(2, 2));
        multiple.IsFallback.Should().BeFalse();
        multiple.SemanticText.Should().Contain("Table 1").And.Contain("Table 2");
    }

    [Fact]
    public void CellBudgetUsesUtf8BytesAndRejectsExpansionWithoutShortening()
    {
        var value = new string('é', NativeMarkdownTableProfile.MaximumCellUtf8Bytes / 2);
        Encoding.UTF8.GetByteCount(value).Should().Be(NativeMarkdownTableProfile.MaximumCellUtf8Bytes);
        var source = $"| Header |\n| --- |\n| {value} |";
        Render(source).IsFallback.Should().BeFalse();
        var exceeded = $"| Header |\n| --- |\n| {value}é |";
        var result = Render(exceeded);
        result.IsFallback.Should().BeTrue();
        result.Status.Should().Contain("table-cell UTF-8");
        result.Source.Should().Be(exceeded);
        var expansion = string.Join(' ', Enumerable.Repeat("[x][ref]", 10));
        var reference = $"| Header |\n| --- |\n| {expansion} |\n\n[ref]: https://example.invalid/{new string('a', 2000)}";
        Render(reference).IsFallback.Should().BeTrue();
        Render(reference).Source.Should().Be(reference);
    }

    [Fact]
    public void SharedSemanticAndSourceBudgetsRemainFailClosedForTables()
    {
        var header = new string('h', NativeMarkdownTableProfile.MaximumCellUtf8Bytes);
        var source = $"| {header} | {header} | {header} | {header} |\n| --- | --- | --- | --- |\n"
            + string.Join('\n', Enumerable.Repeat("| one | two | three | four |", 31));
        Encoding.UTF8.GetByteCount(source).Should().BeLessThan(NativeDetailProfile.MaximumUtf8Bytes);
        var result = Render(source);
        result.IsFallback.Should().BeTrue();
        result.Status.Should().Contain("Semantic text limit");
        result.Source.Should().Be(source);
        var oversized = Source + new string('x', NativeDetailProfile.MaximumUtf8Bytes);
        var refused = Render(oversized);
        refused.IsFallback.Should().BeTrue();
        refused.Source.Should().BeEmpty();
        refused.Status.Should().Contain("Nothing was truncated");
        Render("\ud800").Source.Should().BeEmpty();
    }

    [Fact]
    public void TableProfileRetainsSharedStructuralLimitsAndParserPreflight()
    {
        var blocks = Source + "\n\n"
            + string.Join("\n\n", Enumerable.Repeat("paragraph", NativeDetailProfile.MaximumBlocks));
        var blockResult = Render(blocks);
        blockResult.IsFallback.Should().BeTrue();
        blockResult.Status.Should().Contain("Structural limit");
        blockResult.Source.Should().Be(blocks);
        var nodes = "| A | B |\n| --- | --- |\n"
            + string.Join('\n', Enumerable.Repeat("| " + string.Concat(Enumerable.Repeat("*one* ", 64)) + " | two |", 31));
        var nodeResult = Render(nodes);
        nodeResult.IsFallback.Should().BeTrue();
        nodeResult.NodeCount.Should().BeGreaterThan(NativeDetailProfile.MaximumNodes);
        nodeResult.Status.Should().Contain("4096 nodes");
        nodeResult.Source.Should().Be(nodes);
        var deep = new string('[', NativeDetailProfile.MaximumNesting + 1) + Source;
        var depthResult = Render(deep);
        depthResult.IsFallback.Should().BeTrue();
        depthResult.Status.Should().Contain("preflight");
        depthResult.NodeCount.Should().Be(0);
    }

    [Fact]
    public void PlainTextAndUnavailableRendererNeverParseTablesEvenWithTheCandidateProfile()
    {
        var enabled = new NativeDetailRenderer(NullLogger<NativeDetailRenderer>.Instance, pipeTablesAvailable: true);
        var plain = enabled.Render(Source, DetailContentKind.PlainText, format: NativeDetailFormat.PipeTablesV2);
        plain.IsFallback.Should().BeFalse();
        plain.SemanticText.Should().Be(Source);
        plain.Status.Should().Contain(NativeDetailProfile.Name).And.NotContain(NativeMarkdownTableProfile.Name);
        var unavailable = new NativeDetailRenderer(NullLogger<NativeDetailRenderer>.Instance,
            available: false, pipeTablesAvailable: true).Render(Source, format: NativeDetailFormat.PipeTablesV2);
        unavailable.IsFallback.Should().BeTrue();
        unavailable.Status.Should().Contain("Renderer unavailable");
        unavailable.Source.Should().Be(Source);
    }

    [Fact]
    public void CandidateProfileRetainsTheExactTypeAllowlistForUnknownDerivedTableNodes()
    {
        var document = new Markdig.Syntax.MarkdownDocument { new UnsupportedTable() };
        NativeDetailRenderer.Validate(document, tables: true).Error.Should().Contain("Unsupported Markdown node");
    }

    [Fact]
    public void RenderActivitiesRetainParentageTruthfulOutcomeAndNoSourceTags()
    {
        var completed = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => string.Equals(source.Name, "Kora.Desktop", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = completed.Add,
        };
        ActivitySource.AddActivityListener(listener);
        using var parent = new Activity("test-parent").Start();
        Render(Source).IsFallback.Should().BeFalse();
        Render("| Secret | B |\n| --- | --- |\n| <b>NEVER_TAG</b> | value |").IsFallback.Should().BeTrue();
        completed.Should().HaveCount(2);
        completed.Should().OnlyContain(activity => activity.ParentSpanId == parent.SpanId
            && activity.OperationName == "presentation.render");
        completed[0].Status.Should().Be(ActivityStatusCode.Ok);
        completed[1].Status.Should().Be(ActivityStatusCode.Error);
        completed.Should().OnlyContain(activity =>
            string.Equals(activity.GetTagItem("kora.presentation.profile") as string,
                NativeMarkdownTableProfile.Name, StringComparison.Ordinal));
        completed.SelectMany(activity => activity.Tags).Should()
            .NotContain(tag => tag.Value != null && tag.Value.Contains("NEVER_TAG", StringComparison.Ordinal));
    }

    [Fact]
    public Task SourceToggleAndClosureReleaseTheCompleteTableProjection() => HeadlessSession.RunAsync(() =>
    {
        var content = new AdmittedDetailContent(new(new(Guid.NewGuid()), 1), DetailContentKind.Markdown,
            DetailContentOrigin.EmbeddedDocument, DetailSensitivity.Public, "Table", "Immutable test", Source);
        var state = new DetailViewerState(content);
        var result = Render(Source);
        state.CompleteRender(state.Generation, result.SemanticText, result.Status, result.IsFallback).Should().BeTrue();
        var generation = state.Generation;
        var window = new DetailWindow(state, result, (_, _, _) =>
            throw new InvalidOperationException("No clipboard authority."));
        window.Show();
        try
        {
            state.ActiveText.Should().Be(result.SemanticText);
            window.FindControl<CheckBox>("SourceToggle")!.IsChecked = true;
            window.FindControl<TextBox>("ContentReader")!.Text.Should().Be(Source);
            window.ClearAndClose();
            window.FindControl<StackPanel>("RenderedDocument")!.Children.Should().BeEmpty();
            window.FindControl<TextBox>("ContentReader")!.Text.Should().BeEmpty();
            state.ActiveText.Should().BeEmpty();
            state.CompleteRender(generation, result.SemanticText, result.Status, false).Should().BeFalse();
        }
        finally { window.ClearAndClose(); }
    });

    private static string Table(int columns, int rows)
    {
        var header = "| " + string.Join(" | ", Enumerable.Repeat("Header", columns)) + " |";
        var separator = "| " + string.Join(" | ", Enumerable.Repeat("---", columns)) + " |";
        var row = "| " + string.Join(" | ", Enumerable.Repeat("value", columns)) + " |";
        return header + "\n" + separator
            + (rows > 1 ? "\n" + string.Join('\n', Enumerable.Repeat(row, rows - 1)) : string.Empty);
    }

    private sealed class UnsupportedTable : Markdig.Extensions.Tables.Table;
}
