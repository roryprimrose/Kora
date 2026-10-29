using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Application.Presentation;
using Kora.Controls;
using Kora.Core.Context;
using Kora.Core.Presentation;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class NativeTableAccessibilityTests
{
    private const string Source = "| Name | State |\r\n| --- | --- |\r\n| Alpha | Ready |\r\n| Beta | |\r\n";

    [Fact]
    public Task AdmittedTableIsDiscoverableAndReadsExactHeaderAssociatedCellsWithoutLiveAnnouncements() =>
        HeadlessSession.RunAsync(() =>
        {
            var (window, state, table) = Open(() => true);
            try
            {
                var peer = ControlAutomationPeer.CreatePeerForElement(table)!;
                peer.GetAutomationControlType().Should().Be(AutomationControlType.Table);
                peer.GetName().Should().Be("Read-only table 1, 2 columns, 2 data rows");
                var grid = table.GetVisualDescendants().OfType<Grid>().Single();
                var headers = grid.Children.OfType<Button>().ToArray();
                var cells = grid.Children.OfType<NamedTextBlock>().ToArray();
                ControlAutomationPeer.CreatePeerForElement(headers[0])!.GetChildren().Should().BeEmpty();
                headers[0].Focus().Should().BeTrue();
                Press(window, Key.Down, PhysicalKey.ArrowDown);
                cells[0].IsFocused.Should().BeTrue();
                var cellPeer = ControlAutomationPeer.CreatePeerForElement(cells[0])!;
                cellPeer.GetName().Should().Be("Table 1, row 1, original row 1, column 1 (Name): Alpha");
                cellPeer.GetLabeledBy().Should().BeSameAs(ControlAutomationPeer.CreatePeerForElement(headers[0]));
                Press(window, Key.End, PhysicalKey.End, RawInputModifiers.Control);
                cells[^1].IsFocused.Should().BeTrue();
                ControlAutomationPeer.CreatePeerForElement(cells[^1])!.GetName().Should()
                    .Be("Table 1, row 2, original row 2, column 2 (State): (empty cell)");
                table.Readers.Should().OnlyContain(reader => AutomationProperties.GetLiveSetting(reader) == AutomationLiveSetting.Off);
                cells.Should().OnlyContain(cell => cell.ContextMenu == null);
                table.GetVisualDescendants().Should().NotContain(control => control is TextBox || control is SelectableTextBlock);
                var name = cellPeer.GetName();
                foreach (var reader in table.Readers) { NativeReaderTypography.Capture(reader)(200); }
                cellPeer.GetName().Should().Be(name);
                state.Content!.Source.Should().Be(Source);
                state.Find("Ready", SnapshotFindDirection.First).Should().BeTrue();
                state.MatchCount.Should().Be(1);
                window.FindControl<CheckBox>("SourceToggle")!.IsChecked = true;
                window.FindControl<TextBox>("ContentReader")!.Text.Should().Be(Source);
                cellPeer.GetName().Should().BeNullOrEmpty();
                peer.GetChildren().Should().BeEmpty();
                window.FindControl<CheckBox>("SourceToggle")!.IsChecked = false;
                cellPeer.SetFocus();
                cells[0].IsFocused.Should().BeTrue();
                state.Content.Source.Should().Be(Source);
            }
            finally { window.ClearAndClose(); }
        });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task RetainedAutomationPeersCannotDiscloseOrFocusAfterReadAdmissionLoss(bool header) =>
        HeadlessSession.RunAsync(() =>
        {
            var admitted = true;
            var (window, state, table) = Open(() => admitted);
            try
            {
                var grid = table.GetVisualDescendants().OfType<Grid>().Single();
                Control target = header ? grid.Children.OfType<Button>().First()
                    : grid.Children.OfType<NamedTextBlock>().First();
                var peer = ControlAutomationPeer.CreatePeerForElement(target)!;
                peer.GetName().Should().Contain(header ? "Name" : "Alpha");
                var nameEvents = new List<AutomationPropertyChangedEventArgs>();
                peer.PropertyChanged += (_, args) =>
                {
                    if (args.Property == AutomationElementIdentifiers.NameProperty) { nameEvents.Add(args); }
                };
                window.FindControl<TextBox>("SearchInput")!.Focus().Should().BeTrue();
                admitted = false;
                peer.GetName().Should().BeNullOrEmpty();
                state.Content.Should().BeNull();
                table.Readers.Should().OnlyContain(reader => string.IsNullOrEmpty(reader.Text));
                peer.SetFocus();
                target.IsFocused.Should().BeFalse();
                peer.GetName().Should().BeNullOrEmpty();
                ControlAutomationPeer.CreatePeerForElement(table)!.GetChildren().Should().BeEmpty();
                nameEvents.Should().NotContain(change => (change.NewValue as string ?? string.Empty)
                    .Contains(header ? "Name" : "Alpha", StringComparison.Ordinal));
            }
            finally { window.ClearAndClose(); }
        });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task AutomationFocusRevalidatesAdmissionAcrossBringIntoViewAndFocus(bool duringFocus) =>
        HeadlessSession.RunAsync(() =>
        {
            var admitted = true;
            var (window, state, table) = Open(() => admitted);
            try
            {
                var cell = table.GetVisualDescendants().OfType<Grid>().Single()
                    .Children.OfType<NamedTextBlock>().First();
                var peer = ControlAutomationPeer.CreatePeerForElement(cell)!;
                if (duringFocus) { cell.GotFocus += (_, _) => admitted = false; }
                else { cell.AddHandler(Control.RequestBringIntoViewEvent, (_, _) => admitted = false); }
                peer.SetFocus();
                window.FindControl<StackPanel>("RenderedDocument")!.Children.Should().BeEmpty();
                state.Content.Should().BeNull();
                cell.Text.Should().BeEmpty();
                peer.GetName().Should().BeNullOrEmpty();
                window.FindControl<StackPanel>("RenderedDocument")!.Children.Should().BeEmpty();
            }
            finally { window.ClearAndClose(); }
        });

    [Fact]
    public Task ReplacedTableAutomationCannotReadOrFocusTheOtherExactResult() =>
        HeadlessSession.RunAsync(() =>
        {
            var (first, firstState, oldTable) = Open(() => true);
            var oldCell = oldTable.GetVisualDescendants().OfType<Grid>().Single()
                .Children.OfType<NamedTextBlock>().First();
            var oldPeer = ControlAutomationPeer.CreatePeerForElement(oldCell)!;
            first.ClearAndClose();
            var (second, secondState, table) = Open(() => true);
            try
            {
                var currentCell = table.GetVisualDescendants().OfType<Grid>().Single()
                    .Children.OfType<NamedTextBlock>().Last();
                currentCell.Focus().Should().BeTrue();
                oldPeer.SetFocus();
                oldPeer.GetName().Should().BeNullOrEmpty();
                currentCell.IsFocused.Should().BeTrue();
                firstState.Content.Should().BeNull();
                secondState.Content!.Source.Should().Be(Source);
                secondState.Reference.Should().NotBe(firstState.Reference);
            }
            finally { second.ClearAndClose(); }
        });

    private static (DetailWindow Window, DetailViewerState State, NativeTableControl Table) Open(Func<bool> admission)
    {
        var content = new AdmittedDetailContent(new(new(Guid.NewGuid()), 1), DetailContentKind.Markdown,
            DetailContentOrigin.EmbeddedDocument, DetailSensitivity.Public, "Table", "Exact immutable test", Source);
        var state = new DetailViewerState(content, admission);
        var result = new NativeDetailRenderer(NullLogger<NativeDetailRenderer>.Instance, pipeTablesAvailable: true)
            .Render(Source, format: NativeDetailFormat.PipeTablesV2);
        result.IsFallback.Should().BeFalse();
        state.CompleteRender(state.Generation, result.SemanticText, result.Status, false).Should().BeTrue();
        var window = new DetailWindow(state, result, (_, _, _) => throw new InvalidOperationException("No clipboard authority."))
        { Width = 900, Height = 1100 };
        window.Show();
        window.UpdateLayout();
        return (window, state, window.FindControl<StackPanel>("RenderedDocument")!.Children.OfType<NativeTableControl>().Single());
    }

    private static void Press(Window window, Key key, PhysicalKey physical, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        window.KeyPress(key, modifiers, physical, key.ToString());
        window.KeyRelease(key, modifiers, physical, key.ToString());
    }
}
