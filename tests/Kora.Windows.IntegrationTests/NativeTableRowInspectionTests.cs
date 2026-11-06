using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Application.Presentation;
using Kora.Controls;
using Kora.Core.Context;
using Kora.Core.Presentation;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class NativeTableRowInspectionTests
{
    private const string Source = "| Name | State | State |\n| --- | --- | --- |\n| Beta | Ready | |\n| Alpha | Other | Last |";

    [Theory]
    [InlineData(Key.Enter, PhysicalKey.Enter)]
    [InlineData(Key.Space, PhysicalKey.Space)]
    public Task InspectingSortedRowReadsEveryExactCellAndReturnsToOriginalCell(Key key, PhysicalKey physical) =>
        HeadlessSession.RunAsync(() =>
        {
            var (window, state, table) = Open();
            try
            {
                var grid = table.GetVisualDescendants().OfType<Grid>().Single();
                var header = grid.Children.OfType<Button>().First();
                header.Focus();
                Press(window, Key.Enter, PhysicalKey.Enter);
                Press(window, Key.Down, PhysicalKey.ArrowDown);
                Press(window, Key.Down, PhysicalKey.ArrowDown);
                Press(window, Key.Right, PhysicalKey.ArrowRight);
                var selected = grid.Children.OfType<NamedTextBlock>().Single(cell => Equals(cell.Text, "Ready"));
                selected.IsFocused.Should().BeTrue();
                Press(window, key, physical);
                var reader = Inspection(table);
                reader.IsFocused.Should().BeTrue();
                reader.Text.Should().Be("Table 1, row 2, original row 1." + Environment.NewLine
                    + "Column 1 (Name): Beta" + Environment.NewLine
                    + "Column 2 (State): Ready" + Environment.NewLine
                    + "Column 3 (State): (empty cell)");
                var peer = ControlAutomationPeer.CreatePeerForElement(reader)!;
                peer.GetName().Should().Be("Inspected table row: " + reader.Text);
                peer.GetAutomationControlType().Should().Be(AutomationControlType.Text);
                AutomationProperties.GetLiveSetting(reader).Should().Be(AutomationLiveSetting.Off);
                var readback = reader.Text;
                reader.RaiseEvent(new KeyEventArgs
                {
                    RoutedEvent = InputElement.KeyDownEvent,
                    Key = Key.C,
                    KeyModifiers = KeyModifiers.Control,
                });
                state.Status.Should().Contain("Focus the continuous content reader");
                reader.Text.Should().Be(readback);
                FindButton(table, "Return to inspected cell").Focus();
                Press(window, Key.Enter, PhysicalKey.Enter);
                selected.IsFocused.Should().BeTrue();
                reader.Text.Should().BeEmpty();
                reader.IsVisible.Should().BeFalse();
                state.Content!.Source.Should().Be(Source);
                state.ActiveText.Should().Contain("Beta").And.Contain("Alpha");
                state.Find("Ready", SnapshotFindDirection.First).Should().BeTrue();
                state.MatchCount.Should().Be(1);
                table.GetVisualDescendants().Should().NotContain(control => control is TextBox || control is SelectableTextBlock);
                window.OwnedWindows.Should().BeEmpty();
            }
            finally { window.ClearAndClose(); }
        });

    [Fact]
    public Task PointerSelectionAndNativeInspectButtonKeepOriginalRowAcrossSortAndReset() =>
        HeadlessSession.RunAsync(() =>
        {
            var (window, state, table) = Open();
            try
            {
                FindButton(table, "Inspect focused row").IsEnabled.Should().BeFalse();
                var grid = table.GetVisualDescendants().OfType<Grid>().Single();
                var selected = grid.Children.OfType<NamedTextBlock>().Single(cell => Equals(cell.Text, "Last"));
                PointerClick(window, selected);
                selected.IsFocused.Should().BeTrue();
                PointerClick(window, FindButton(table, "Inspect focused row"));
                var reader = Inspection(table);
                reader.IsFocused.Should().BeTrue();
                reader.Text.Should().StartWith("Table 1, row 2, original row 2.");
                var header = grid.Children.OfType<Button>().First();
                PointerClick(window, header);
                state.Status.Should().Contain("sorted by column 1, ascending");
                reader.Text.Should().StartWith("Table 1, row 1, original row 2.").And.Contain("Column 3 (State): Last");
                PointerClick(window, header);
                reader.Text.Should().StartWith("Table 1, row 2, original row 2.");
                PointerClick(window, FindButton(table, "Reset table order"));
                reader.Text.Should().StartWith("Table 1, row 2, original row 2.");
                PointerClick(window, FindButton(table, "Return to inspected cell"));
                selected.IsFocused.Should().BeTrue();
                state.Content!.Source.Should().Be(Source);
            }
            finally { window.ClearAndClose(); }
        });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task HiddenOrRetiredInspectionCannotDiscloseOrRefocusAnotherProjection(bool retire) =>
        HeadlessSession.RunAsync(() =>
        {
            var admitted = true;
            var (window, state, table) = Open(admission: () => admitted);
            try
            {
                var cell = table.GetVisualDescendants().OfType<Grid>().Single().Children.OfType<NamedTextBlock>().First();
                cell.Focus();
                Press(window, Key.Enter, PhysicalKey.Enter);
                var reader = Inspection(table);
                var peer = ControlAutomationPeer.CreatePeerForElement(reader)!;
                var inspect = FindButton(table, "Inspect focused row");
                var back = FindButton(table, "Return to inspected cell");
                window.FindControl<TextBox>("SearchInput")!.Focus().Should().BeTrue();
                if (retire) { admitted = false; }
                else { window.FindControl<CheckBox>("SourceToggle")!.IsChecked = true; }
                peer.GetName().Should().BeNullOrEmpty();
                peer.SetFocus();
                inspect.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                back.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                reader.IsFocused.Should().BeFalse();
                if (retire)
                {
                    reader.Text.Should().BeEmpty();
                    state.Content.Should().BeNull();
                    table.Readers.Should().OnlyContain(value => string.IsNullOrEmpty(value.Text));
                }
                else
                {
                    state.Status.Should().Contain("unavailable");
                    state.ActiveText.Should().Be(Source);
                    state.Content!.Source.Should().Be(Source);
                }
            }
            finally { window.ClearAndClose(); }
        });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task InspectionRevalidatesAcrossBringIntoViewAndFocusWithoutRestoringPrivateText(bool duringFocus) =>
        HeadlessSession.RunAsync(() =>
        {
            var admitted = true;
            var (window, state, table) = Open(admission: () => admitted);
            try
            {
                var reader = Inspection(table);
                var peer = ControlAutomationPeer.CreatePeerForElement(reader)!;
                if (duringFocus) { reader.GotFocus += (_, _) => admitted = false; }
                else { reader.AddHandler(Control.RequestBringIntoViewEvent, (_, _) => admitted = false); }
                table.GetVisualDescendants().OfType<Grid>().Single().Children.OfType<NamedTextBlock>().First().Focus();
                FindButton(table, "Inspect focused row").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                state.Content.Should().BeNull();
                reader.Text.Should().BeEmpty();
                peer.GetName().Should().BeNullOrEmpty();
                reader.IsVisible.Should().BeFalse();
                table.IsEnabled.Should().BeFalse();
            }
            finally { window.ClearAndClose(); }
        });

    [Fact]
    public Task ReopeningHasNoInspectionAndOldAutomationCannotReachTheNewTable() =>
        HeadlessSession.RunAsync(() =>
        {
            var (first, _, oldTable) = Open();
            var oldCell = oldTable.GetVisualDescendants().OfType<Grid>().Single().Children.OfType<NamedTextBlock>().First();
            oldCell.Focus();
            Press(first, Key.Enter, PhysicalKey.Enter);
            var oldReader = Inspection(oldTable);
            var oldPeer = ControlAutomationPeer.CreatePeerForElement(oldReader)!;
            first.ClearAndClose();
            var (second, state, table) = Open();
            try
            {
                var current = table.GetVisualDescendants().OfType<Grid>().Single().Children.OfType<NamedTextBlock>().Last();
                current.Focus();
                oldPeer.SetFocus();
                oldPeer.GetName().Should().BeNullOrEmpty();
                oldReader.Text.Should().BeEmpty();
                current.IsFocused.Should().BeTrue();
                Inspection(table).Text.Should().BeNullOrEmpty();
                Inspection(table).IsVisible.Should().BeFalse();
                state.Content!.Source.Should().Be(Source);
            }
            finally { second.ClearAndClose(); }
        });

    [Fact]
    public Task MaximumWidthAndCellBytesRemainCompleteWithEmptyHeaders() =>
        HeadlessSession.RunAsync(() =>
        {
            var value = new string('\u00e9', NativeMarkdownTableProfile.MaximumCellUtf8Bytes / 2);
            var cells = Enumerable.Repeat(value, NativeMarkdownTableProfile.MaximumColumns).ToArray();
            var source = "| " + string.Join(" | ", Enumerable.Repeat(string.Empty, cells.Length)) + " |\n| "
                + string.Join(" | ", Enumerable.Repeat("---", cells.Length)) + " |\n| " + string.Join(" | ", cells) + " |";
            var (window, state, table) = Open(source);
            try
            {
                table.GetVisualDescendants().OfType<Grid>().Single().Children.OfType<NamedTextBlock>().First().Focus();
                Press(window, Key.Enter, PhysicalKey.Enter);
                var reader = Inspection(table);
                foreach (var column in Enumerable.Range(1, cells.Length))
                {
                    reader.Text.Should().Contain($"Column {column} (empty header): {value}");
                }
                state.Content!.Source.Should().Be(source);
            }
            finally { window.ClearAndClose(); }
        });

    [Theory]
    [InlineData(4, false)]
    [InlineData(5, true)]
    public void InspectorControlsParticipateInTheExactInclusiveDocumentControlBudget(int paragraphs, bool fallback)
    {
        var source = string.Concat(Enumerable.Repeat("```\nliteral\n```\n\n", 314))
            + "| Header |\n| --- |\n| Cell |\n\n"
            + string.Join("\n\n", Enumerable.Repeat("paragraph", paragraphs));
        var result = new NativeDetailRenderer(NullLogger<NativeDetailRenderer>.Instance, pipeTablesAvailable: true)
            .Render(source, format: NativeDetailFormat.PipeTablesV2);
        result.IsFallback.Should().BeFalse();
        (314 * 13 + 7 + 2 + 1 + paragraphs).Should().Be(fallback ? NativeDetailProfile.MaximumNodes + 1
            : NativeDetailProfile.MaximumNodes);
        MarkdownDocumentRenderer.RequiresFinalSourceFallback(result).Should().Be(fallback);
        result.Source.Should().Be(source);
    }

    [Fact]
    public Task ReentrantReadbackWithdrawalClearsTextAndSuppressesLateInspectionPublication() =>
        HeadlessSession.RunAsync(() =>
        {
            var admitted = true;
            var (window, state, table) = Open(admission: () => admitted);
            try
            {
                var reader = Inspection(table);
                var peer = ControlAutomationPeer.CreatePeerForElement(reader)!;
                peer.PropertyChanged += (_, args) =>
                {
                    if (args.Property == AutomationElementIdentifiers.NameProperty
                        && args.NewValue is string name && name.Contains("Beta", StringComparison.Ordinal))
                    { admitted = false; }
                };
                table.GetVisualDescendants().OfType<Grid>().Single().Children.OfType<NamedTextBlock>().First().Focus();
                FindButton(table, "Inspect focused row").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                state.Content.Should().BeNull();
                reader.Text.Should().BeEmpty();
                reader.IsVisible.Should().BeFalse();
                FindButton(table, "Return to inspected cell").IsVisible.Should().BeFalse();
                peer.GetName().Should().BeNullOrEmpty();
            }
            finally { window.ClearAndClose(); }
        });

    private static NamedTextBlock Inspection(NativeTableControl table) =>
        table.Children.OfType<NamedTextBlock>().Last();

    private static Button FindButton(NativeTableControl table, string content) =>
        table.Children.OfType<Button>().Single(button => Equals(button.Content, content));

    private static (DetailWindow Window, DetailViewerState State, NativeTableControl Table) Open(
        string source = Source, Func<bool>? admission = null)
    {
        var content = new AdmittedDetailContent(new(new(Guid.NewGuid()), 1), DetailContentKind.Markdown,
            DetailContentOrigin.EmbeddedDocument, DetailSensitivity.Public, "Table", "Exact immutable test", source);
        var state = new DetailViewerState(content, admission);
        var result = new NativeDetailRenderer(NullLogger<NativeDetailRenderer>.Instance, pipeTablesAvailable: true)
            .Render(source, format: NativeDetailFormat.PipeTablesV2);
        result.IsFallback.Should().BeFalse();
        state.CompleteRender(state.Generation, result.SemanticText, result.Status, false).Should().BeTrue();
        var window = new DetailWindow(state, result, (_, _, _) => throw new InvalidOperationException("No clipboard authority."))
        { Width = 900, Height = 1100 };
        window.Show();
        window.UpdateLayout();
        return (window, state, window.FindControl<StackPanel>("RenderedDocument")!.Children.OfType<NativeTableControl>().Single());
    }

    private static void Press(Window window, Key key, PhysicalKey physical)
    {
        window.KeyPress(key, RawInputModifiers.None, physical, key.ToString());
        window.KeyRelease(key, RawInputModifiers.None, physical, key.ToString());
    }

    private static void PointerClick(Window window, Control control)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        control.BringIntoView();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        using var frame = window.CaptureRenderedFrame();
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        var hit = window.InputHitTest(point);
        var viewport = control.GetVisualAncestors().OfType<ScrollViewer>().First();
        (ReferenceEquals(hit, control) || hit is Visual visual && visual.GetVisualAncestors().Contains(control))
            .Should().BeTrue($"the pointer must hit the requested {control.GetType().Name}, not {hit?.GetType().Name}; "
                + $"point {point}, target bounds {control.Bounds}, viewport {viewport.Bounds}, offset {viewport.Offset}");
        window.MouseMove(point);
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
    }
}
