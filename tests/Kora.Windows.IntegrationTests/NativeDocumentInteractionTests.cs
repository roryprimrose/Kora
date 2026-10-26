using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Application.Presentation;
using Kora.Controls;
using Kora.Core.Presentation;
using Kora.Core.Context;

using Markdig;
using Markdig.Extensions.AutoIdentifiers;
using Markdig.Renderers.Html;
using Markdig.Syntax;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class NativeDocumentInteractionTests
{
    private const string Table = "| Name | Identity |\n| --- | --- |\n| b | first-b |\n| a | first-a |\n| a | second-a |\n| | empty |\n| c | last-c |";

    [Theory]
    [InlineData((int)NativeDetailFormat.TextV1)]
    [InlineData((int)NativeDetailFormat.PipeTablesV2)]
    [InlineData((int)NativeDetailFormat.MarkdownChecklistsV3)]
    [InlineData((int)NativeDetailFormat.MarkdownStrikethroughV4)]
    public void HeadingProjectionUsesActualParserIdsAndKeepsSemanticText(int value)
    {
        const string source = "# Repeat\n\n# Repeat\n\n## Caf\u00e9 \u6771\u4eac\n\n[go](#repeat-1) [remote](https://example.invalid) `#repeat`";
        var parsed = Markdown.Parse(source, new MarkdownPipelineBuilder()
            .UseAutoIdentifiers(AutoIdentifierOptions.None).Build());
        var result = Render(source, (NativeDetailFormat)value);
        result.IsFallback.Should().BeFalse();
        result.Sections.Where(section => section.HeadingId is not null).Select(section => section.HeadingId)
            .Should().Equal(parsed.Descendants<HeadingBlock>().Select(heading => heading.GetAttributes().Id));
        result.Sections.SelectMany(section => section.Links).Should()
            .ContainSingle().Which.Should().Be(new NativeDocumentLink("go", "#repeat-1"));
        result.SemanticText.Should().Contain("go (destination: #repeat-1)")
            .And.Contain("remote (destination: https://example.invalid)").And.Contain("#repeat");
        result.Source.Should().Be(source);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task PointerAndActualKeyboardReachExactDuplicateHeadingInTheOpenedDocument(bool useMouse) =>
        HeadlessSession.RunAsync(() =>
        {
            const string source = "[second](#repeat-1)\n\n# Repeat\n\nFirst body\n\n# Repeat\n\nSecond body";
            var (window, state) = Open(source);
            try
            {
                var panel = window.FindControl<StackPanel>("RenderedDocument")!;
                var targets = panel.Children.OfType<TextBlock>().Where(text => string.Equals(text.Text, "Repeat", StringComparison.Ordinal)).ToArray();
                var link = panel.Children.OfType<Button>().Single();
                if (useMouse) { PointerClick(window, link); }
                else { link.Focus().Should().BeTrue(); Press(window, Key.Enter, PhysicalKey.Enter); }
                targets[1].IsFocused.Should().BeTrue();
                targets[0].IsFocused.Should().BeFalse();
                state.Status.Should().Be("Focused the exact document heading.");
                state.Content!.Source.Should().Be(source);
                state.ActiveText.Should().Contain("Second body");
                ControlAutomationPeer.CreatePeerForElement(link)!.GetAutomationControlType().Should().Be(AutomationControlType.Button);
                ControlAutomationPeer.CreatePeerForElement(targets[1])!.GetName().Should().Contain("Repeat");
            }
            finally { window.ClearAndClose(); }
        });

    [Fact]
    public Task EncodedUnicodeFragmentUsesExactParserIdentityWithoutNormalizationOrTextReconstruction() =>
        HeadlessSession.RunAsync(() =>
        {
            const string headingSource = "# Caf\u00e9 \u6771\u4eac \U0001F600\n";
            var heading = Markdown.Parse(headingSource, new MarkdownPipelineBuilder()
                .UseAutoIdentifiers(AutoIdentifierOptions.None).Build()).Descendants<HeadingBlock>().Single();
            var source = $"[unicode](#{Uri.EscapeDataString(heading.GetAttributes().Id!)})\n\n{headingSource}";
            var (window, state) = Open(source);
            try
            {
                var panel = window.FindControl<StackPanel>("RenderedDocument")!;
                var link = panel.Children.OfType<Button>().Single();
                link.Focus();
                Press(window, Key.Space, PhysicalKey.Space);
                panel.Children.OfType<TextBlock>().Single(text => text.Focusable).IsFocused.Should().BeTrue();
                state.Status.Should().Be("Focused the exact document heading.");
            }
            finally { window.ClearAndClose(); }
        });

    [Theory]
    [InlineData("#missing", "not found")]
    [InlineData("#", "malformed")]
    [InlineData("#bad%ZZ", "malformed")]
    [InlineData("#bad%FF", "malformed")]
    [InlineData("#bad%00", "malformed")]
    [InlineData("#bad% A", "malformed")]
    [InlineData("#Heading", "not found")]
    [InlineData("https://example.invalid/#heading", "non-local")]
    [InlineData("javascript:alert", "non-local")]
    [InlineData("file:///C:/private", "non-local")]
    public Task InvalidOrMissingFragmentsAreExplicitAndNeverRetargetFocus(string destination, string status) =>
        HeadlessSession.RunAsync(() =>
        {
            var messages = new List<string>();
            var interaction = new NativeDocumentInteraction(() => true, messages.Add);
            var result = Render("# Heading");
            var controls = MarkdownDocumentRenderer.CreateControls(result, selectable: false, interaction);
            var panel = new StackPanel();
            foreach (var control in controls) { panel.Children.Add(control); }
            var window = new Window { Content = panel };
            window.Show();
            try
            {
                interaction.Activate(destination);
                messages.Should().ContainSingle().Which.Should().Contain(status);
                controls.Should().OnlyContain(control => !control.IsFocused);
                result.Source.Should().Be("# Heading");
                panel.GetVisualDescendants().Should().NotContain(control => control is Button);
            }
            finally { interaction.Retire(); window.Close(); }
        });

    [Fact]
    public Task CompleteDocumentSupportsAnchorThenKeyboardHeaderCellSortingResetAndUnchangedFindSelection() =>
        HeadlessSession.RunAsync(() =>
        {
            var source = "[table](#comparison)\n\n# Comparison\n\n" + Table;
            var copies = 0;
            var (window, state) = Open(source, NativeDetailFormat.PipeTablesV2, copied: () => copies++);
            try
            {
                var panel = window.FindControl<StackPanel>("RenderedDocument")!;
                var link = panel.Children.OfType<Button>().Single();
                link.Focus();
                Press(window, Key.Enter, PhysicalKey.Enter);
                panel.Children.OfType<TextBlock>().Single(text => string.Equals(text.Text, "Comparison", StringComparison.Ordinal)).IsFocused.Should().BeTrue();
                var table = panel.Children.OfType<NativeTableControl>().Single();
                var grid = table.GetVisualDescendants().OfType<Grid>().Single();
                var headers = grid.Children.OfType<Button>().ToArray();
                var original = grid.Children.OfType<NamedTextBlock>().Where(cell => Grid.GetColumn(cell) == 1).ToArray();
                var beta = grid.Children.OfType<NamedTextBlock>().Single(cell => string.Equals(cell.Text, "b", StringComparison.Ordinal));
                beta.Focus().Should().BeTrue();
                Press(window, Key.Right, PhysicalKey.ArrowRight);
                original[0].IsFocused.Should().BeTrue();
                Press(window, Key.Up, PhysicalKey.ArrowUp);
                headers[1].IsFocused.Should().BeTrue();
                Press(window, Key.Home, PhysicalKey.Home);
                headers[0].IsFocused.Should().BeTrue();
                Press(window, Key.Down, PhysicalKey.ArrowDown);
                beta.IsFocused.Should().BeTrue();
                var cellPeer = ControlAutomationPeer.CreatePeerForElement(beta)!;
                cellPeer.GetName().Should().Contain("column 1 (Name)").And.Contain("b");
                AutomationProperties.GetLabeledBy(beta).Should().BeSameAs(headers[0]);
                window.FindControl<TextBox>("ContentReader")!.SelectionStart = 2;
                window.FindControl<TextBox>("ContentReader")!.SelectionEnd = 5;
                headers[0].Focus();
                Press(window, Key.Enter, PhysicalKey.Enter);
                RowIdentities(grid).Should().Equal("empty", "first-a", "second-a", "first-b", "last-c");
                Press(window, Key.Enter, PhysicalKey.Enter);
                RowIdentities(grid).Should().Equal("last-c", "first-b", "first-a", "second-a", "empty");
                var reset = table.Children.OfType<Button>().Single();
                reset.Focus();
                Press(window, Key.Enter, PhysicalKey.Enter);
                RowIdentities(grid).Should().Equal("first-b", "first-a", "second-a", "empty", "last-c");
                beta.IsFocused.Should().BeTrue();
                beta.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.C, KeyModifiers = KeyModifiers.Control });
                copies.Should().Be(0);
                window.FindControl<TextBox>("ContentReader")!.SelectionStart.Should().Be(2);
                window.FindControl<TextBox>("ContentReader")!.SelectionEnd.Should().Be(5);
                state.Content!.Source.Should().Be(source);
                state.Find("first-a", SnapshotFindDirection.First).Should().BeTrue();
                state.MatchCount.Should().Be(1);
                window.FindControl<CheckBox>("SourceToggle")!.IsChecked = true;
                state.ActiveText.Should().Be(source);
                state.Content.Source.Should().Be(source);
            }
            finally { window.ClearAndClose(); }
        });

    [Fact]
    public Task EmptyTableDuplicateHeadersAndBoundaryKeysRemainReadableWithoutEditing() =>
        HeadlessSession.RunAsync(() =>
        {
            var (window, state) = Open("| | |\n| --- | --- |", NativeDetailFormat.PipeTablesV2);
            try
            {
                var table = window.FindControl<StackPanel>("RenderedDocument")!.Children.OfType<NativeTableControl>().Single();
                var grid = table.GetVisualDescendants().OfType<Grid>().Single();
                var headers = grid.Children.OfType<Button>().ToArray();
                headers.Should().HaveCount(2);
                headers[0].Focus();
                Press(window, Key.Down, PhysicalKey.ArrowDown);
                headers[0].IsFocused.Should().BeTrue();
                Press(window, Key.End, PhysicalKey.End, RawInputModifiers.Control);
                headers[1].IsFocused.Should().BeTrue();
                Press(window, Key.Enter, PhysicalKey.Enter);
                grid.Children.OfType<NamedTextBlock>().Should().BeEmpty();
                state.Content!.Source.Should().Be("| | |\n| --- | --- |");
            }
            finally { window.ClearAndClose(); }
        });

    [Fact]
    public Task TwoDocumentsDoNotShareHeadingTargetsAndOldActionsCannotSurviveAdmissionRevocation() =>
        HeadlessSession.RunAsync(() =>
        {
            var admitted = true;
            var source = "[go](#comparison)\n\n# Comparison\n\n" + Table;
            var (first, firstState) = Open(source, NativeDetailFormat.PipeTablesV2, () => admitted);
            var (second, secondState) = Open(source, NativeDetailFormat.PipeTablesV2);
            try
            {
                var firstPanel = first.FindControl<StackPanel>("RenderedDocument")!;
                var firstLink = firstPanel.Children.OfType<Button>().Single();
                var oldTable = firstPanel.Children.OfType<NativeTableControl>().Single();
                var oldHeader = oldTable.GetVisualDescendants().OfType<Grid>().Single().Children.OfType<Button>().First();
                var oldCells = oldTable.GetVisualDescendants().OfType<NamedTextBlock>().ToArray();
                var secondPanel = second.FindControl<StackPanel>("RenderedDocument")!;
                var secondLink = secondPanel.Children.OfType<Button>().Single();
                secondLink.Focus();
                Press(second, Key.Enter, PhysicalKey.Enter);
                secondPanel.Children.OfType<TextBlock>().Single(text => string.Equals(text.Text, "Comparison", StringComparison.Ordinal)).IsFocused.Should().BeTrue();
                admitted = false;
                firstLink.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                firstState.Content.Should().BeNull();
                firstPanel.Children.Should().BeEmpty();
                oldHeader.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                oldCells.Should().OnlyContain(cell => string.IsNullOrEmpty(cell.Text));
                oldHeader.Content.Should().BeNull();
                secondState.Content!.Source.Should().Be(source);
                secondPanel.Children.OfType<NativeTableControl>().Should().ContainSingle();
                firstState.ActiveText.Should().BeEmpty();
            }
            finally { first.ClearAndClose(); second.ClearAndClose(); }
        });

    [Fact]
    public Task HiddenSourceProjectionAndRetiredControlsCannotNavigateOrSortAndReopenStartsInOriginalOrder() =>
        HeadlessSession.RunAsync(() =>
        {
            var source = "[go](#comparison)\n\n# Comparison\n\n" + Table;
            var (window, state) = Open(source, NativeDetailFormat.PipeTablesV2);
            try
            {
                var panel = window.FindControl<StackPanel>("RenderedDocument")!;
                var link = panel.Children.OfType<Button>().Single();
                var table = panel.Children.OfType<NativeTableControl>().Single();
                var grid = table.GetVisualDescendants().OfType<Grid>().Single();
                var header = grid.Children.OfType<Button>().First();
                window.FindControl<CheckBox>("SourceToggle")!.IsChecked = true;
                header.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                state.Status.Should().Contain("unavailable");
                RowIdentities(grid).Should().Equal("first-b", "first-a", "second-a", "empty", "last-c");
                link.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                state.Status.Should().Contain("unavailable");
                window.FindControl<CheckBox>("SourceToggle")!.IsChecked = false;
                window.FindControl<CheckBox>("ReadingTextToggle")!.IsChecked = true;
                header.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                state.Status.Should().Contain("unavailable");
                RowIdentities(grid).Should().Equal("first-b", "first-a", "second-a", "empty", "last-c");
                var generation = state.Generation;
                window.ClearAndClose();
                link.Content.Should().BeNull();
                header.Content.Should().BeNull();
                link.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                header.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                state.CompleteRender(generation, source, "Late replacement", false).Should().BeFalse();
                state.Content.Should().BeNull();
                var (reopened, reopenedState) = Open(source, NativeDetailFormat.PipeTablesV2);
                try
                {
                    var reopenedGrid = reopened.FindControl<StackPanel>("RenderedDocument")!
                        .Children.OfType<NativeTableControl>().Single().GetVisualDescendants().OfType<Grid>().Single();
                    RowIdentities(reopenedGrid).Should().Equal("first-b", "first-a", "second-a", "empty", "last-c");
                    reopenedState.Content!.Source.Should().Be(source);
                }
                finally { reopened.ClearAndClose(); }
            }
            finally { window.ClearAndClose(); }
        });

    [Fact]
    public Task TablePayloadIsImmutableParserDataAndDefaultFacadeStillUsesOriginalInertText() => HeadlessSession.RunAsync(() =>
    {
        var result = Render("| Same | Same |\n| --- | --- |\n| \u00e9 | [go](#target) |\n\n# Target", NativeDetailFormat.PipeTablesV2);
        var header = result.Sections.Single(section => section.Table is { IsHeader: true });
        header.Table!.Data.Headers.Should().Equal("Same", "Same");
        header.Table.Data.Rows.Single().Should().Equal("\u00e9", "go (destination: #target)");
        header.Links.Should().ContainSingle().Which.Should().Be(new NativeDocumentLink("go", "#target"));
        MarkdownDocumentRenderer.CreateControls(result).Should().OnlyContain(control => control is SelectableTextBlock);
        result.Sections.Single(section => section.Table is { IsHeader: false }).Table!.Data
            .Should().BeSameAs(header.Table.Data);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task RevocationDuringBringIntoViewOrFocusCannotClaimNavigationSuccess(bool duringFocus) =>
        HeadlessSession.RunAsync(() =>
        {
            var admitted = true;
            var (window, state) = Open("[go](#target)\n\n# Target", admission: () => admitted);
            try
            {
                var panel = window.FindControl<StackPanel>("RenderedDocument")!;
                var target = panel.Children.OfType<TextBlock>().Single(text => text.Focusable);
                if (duringFocus) { target.GotFocus += (_, _) => admitted = false; }
                else { target.AddHandler(Control.RequestBringIntoViewEvent, (_, _) => admitted = false); }
                panel.Children.OfType<Button>().Single().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                state.Content.Should().BeNull();
                state.Status.Should().NotBe("Focused the exact document heading.");
                panel.Children.Should().BeEmpty();
                target.Text.Should().BeEmpty();
            }
            finally { window.ClearAndClose(); }
        });

    [Fact]
    public Task InclusiveMaximumTableSupportsLastCellAndFirstHeaderKeyboardNavigationWithinControlBudget() =>
        HeadlessSession.RunAsync(() =>
        {
            const string header = "| H | H | H | H |\n| --- | --- | --- | --- |\n";
            var source = header + string.Join('\n', Enumerable.Range(1, NativeMarkdownTableProfile.MaximumRowsPerTable - 1)
                .Select(row => $"| {row} | {row} | {row} | {row} |"));
            var (window, state) = Open(source, NativeDetailFormat.PipeTablesV2);
            try
            {
                var table = window.FindControl<StackPanel>("RenderedDocument")!.Children.OfType<NativeTableControl>().Single();
                var grid = table.GetVisualDescendants().OfType<Grid>().Single();
                grid.Children.OfType<NamedTextBlock>().Should().HaveCount(124);
                var firstHeader = grid.Children.OfType<Button>().First();
                firstHeader.Focus();
                Press(window, Key.End, PhysicalKey.End, RawInputModifiers.Control);
                var last = grid.Children.OfType<NamedTextBlock>().Single(cell => Grid.GetRow(cell) == 31 && Grid.GetColumn(cell) == 3);
                last.IsFocused.Should().BeTrue();
                ControlAutomationPeer.CreatePeerForElement(last)!.GetName().Should()
                    .Contain("row 31").And.Contain("column 4 (H)").And.Contain("31");
                Press(window, Key.Home, PhysicalKey.Home, RawInputModifiers.Control);
                firstHeader.IsFocused.Should().BeTrue();
                table.GetVisualDescendants().OfType<Control>().Count().Should().BeLessThan(NativeDetailProfile.MaximumNodes);
                state.Content!.Source.Should().Be(source);
            }
            finally { window.ClearAndClose(); }
        });

    [Fact]
    public Task TextSortingDoesNotInferNumbersAndKeepsStableTiesWhenReversed() =>
        HeadlessSession.RunAsync(() =>
        {
            const string source = "| Value | Identity |\n| --- | --- |\n| 2 | two |\n| 10 | first-ten |\n| 10 | second-ten |";
            var (window, state) = Open(source, NativeDetailFormat.PipeTablesV2);
            try
            {
                var table = window.FindControl<StackPanel>("RenderedDocument")!.Children.OfType<NativeTableControl>().Single();
                var grid = table.GetVisualDescendants().OfType<Grid>().Single();
                var header = grid.Children.OfType<Button>().First();
                var readers = table.Readers.ToArray();
                readers.Should().OnlyContain(reader => reader.FontSize == 14);
                foreach (var reader in readers) { NativeReaderTypography.Capture(reader)(200); }
                header.Focus();
                Press(window, Key.Enter, PhysicalKey.Enter);
                RowIdentities(grid).Should().Equal("first-ten", "second-ten", "two");
                Press(window, Key.Enter, PhysicalKey.Enter);
                RowIdentities(grid).Should().Equal("two", "first-ten", "second-ten");
                table.Readers.Should().Equal(readers);
                table.Readers.Should().OnlyContain(reader => reader.FontSize == 28);
                state.Content!.Source.Should().Be(source);
            }
            finally { window.ClearAndClose(); }
        });

    [Fact]
    public Task DetachmentRetiresDocumentControlsWithoutChangingTheImmutableSourceOrTargetingAnotherView() =>
        HeadlessSession.RunAsync(() =>
        {
            var source = "[go](#target)\n\n# Target\n\n" + Table;
            var (window, state) = Open(source, NativeDetailFormat.PipeTablesV2);
            try
            {
                var panel = window.FindControl<StackPanel>("RenderedDocument")!;
                var link = panel.Children.OfType<Button>().Single();
                var table = panel.Children.OfType<NativeTableControl>().Single();
                var header = table.GetVisualDescendants().OfType<Grid>().Single().Children.OfType<Button>().First();
                window.Content = null;
                link.Content.Should().BeNull();
                header.Content.Should().BeNull();
                table.Readers.Should().OnlyContain(reader => string.IsNullOrEmpty(reader.Text));
                link.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                header.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                state.Content!.Source.Should().Be(source);
                state.Status.Should().NotContain("Focused the exact");
            }
            finally { window.ClearAndClose(); }
        });

    private static string?[] RowIdentities(Grid grid) => grid.Children.OfType<NamedTextBlock>()
        .Where(cell => Grid.GetColumn(cell) == 1).OrderBy(Grid.GetRow).Select(cell => cell.Text).ToArray();

    private static NativeDocumentResult Render(string source, NativeDetailFormat format = NativeDetailFormat.TextV1) =>
        new NativeDetailRenderer(NullLogger<NativeDetailRenderer>.Instance, pipeTablesAvailable: true,
            checklistsAvailable: true, strikethroughAvailable: true).Render(source, format: format);

    private static (DetailWindow Window, DetailViewerState State) Open(string source,
        NativeDetailFormat format = NativeDetailFormat.TextV1, Func<bool>? admission = null, Action? copied = null)
    {
        var content = new AdmittedDetailContent(new(new(Guid.NewGuid()), 1), DetailContentKind.Markdown,
            DetailContentOrigin.EmbeddedDocument, DetailSensitivity.Public, "Report", "Immutable test", source);
        var state = new DetailViewerState(content, admission);
        var result = Render(source, format);
        result.IsFallback.Should().BeFalse();
        state.CompleteRender(state.Generation, result.SemanticText, result.Status, false).Should().BeTrue();
        var window = new DetailWindow(state, result, (_, _, _) =>
        {
            if (copied is null) { throw new InvalidOperationException("No clipboard authority."); }
            copied();
            return Task.CompletedTask;
        })
        { Width = 900, Height = 1100 };
        window.Show();
        window.UpdateLayout();
        return (window, state);
    }

    private static void Press(Window window, Key key, PhysicalKey physical, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        window.KeyPress(key, modifiers, physical, key.ToString());
        window.KeyRelease(key, modifiers, physical, key.ToString());
    }

    private static void PointerClick(Window window, Control control)
    {
        control.BringIntoView();
        window.UpdateLayout();
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
    }
}
