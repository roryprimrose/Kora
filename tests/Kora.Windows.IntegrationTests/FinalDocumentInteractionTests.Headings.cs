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
using Kora.Core.Presentation;

namespace Kora.Windows.IntegrationTests;

public sealed partial class FinalDocumentInteractionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task HeadingIndexInputSupersedesRestoredPositionWithoutRetainingWithdrawnContent(bool retained) =>
        HeadlessSession.RunAsync(() =>
        {
            var admitted = true;
            var text = "# First\n\n" + string.Concat(Enumerable.Repeat("Long document paragraph.\n\n", 40))
                + "## Last\n\nFinal paragraph";
            var state = State(text, () => admitted, retained);
            var view = new DetailContentView(state, Render(text),
                (_, _, _, _) => throw new InvalidOperationException("No clipboard authority."));
            var window = new Window { Content = view, Width = 900, Height = 600 };
            window.Show();
            try
            {
                view.FocusReader();
                window.UpdateLayout();
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                var viewport = view.FindControl<ScrollViewer>("RenderedViewport")!;
                viewport.Offset = new(0, 180);
                var position = view.CaptureReadingPosition();
                position.RenderedOffset.Y.Should().Be(180);
                view.RestoreReadingPosition(position);

                window.KeyPress(Key.H, RawInputModifiers.Alt, PhysicalKey.H, null);
                window.KeyRelease(Key.H, RawInputModifiers.Alt, PhysicalKey.H, null);
                var index = view.GetVisualDescendants().OfType<Expander>().Single();
                var entries = HeadingEntries(index);
                index.IsExpanded.Should().BeTrue();
                entries[0].IsFocused.Should().BeTrue();
                var navigatedOffset = viewport.Offset;
                navigatedOffset.Y.Should().BeLessThan(position.RenderedOffset.Y);
                view.CaptureReadingPosition().RenderedOffset.Should().Be(navigatedOffset,
                    "heading navigation supersedes the pending restored position before another Hide");
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                viewport.Offset.Y.Should().BeApproximately(navigatedOffset.Y, 1,
                    "deferred restoration cannot undo explicit heading navigation");
                entries[0].IsFocused.Should().BeTrue();
                Click(entries[^1]);
                view.FindControl<StackPanel>("RenderedDocument")!.Children.OfType<TextBlock>()
                    .Single(block => Equals(block.Text, "Last")).IsFocused.Should().BeTrue();
                state.Content!.Source.Should().Be(text);
                window.OwnedWindows.Should().BeEmpty();

                var peer = ControlAutomationPeer.CreatePeerForElement(entries[0])!;
                admitted = false;
                peer.GetName().Should().BeNullOrEmpty();
                peer.IsEnabled().Should().BeFalse();
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                state.Content.Should().BeNull();
                view.FindControl<TextBox>("ContentReader")!.Text.Should().BeEmpty();
                view.FindControl<StackPanel>("RenderedDocument")!.Children.Should().BeEmpty();
                index.Content.Should().BeNull();
            }
            finally { view.ClearPresentation(); window.Close(); }
        });

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public Task HeadingIndexCompletesExactDuplicateAndUnicodeNavigationWithoutAuthoredLinks(bool retained, bool keyboard) =>
        HeadlessSession.RunAsync(() =>
        {
            const string text = "# Repeat\n\nFirst\n\n### Repeat\n\nSecond\n\n###### Caf\u00e9 \u6771\u4eac\n\nLast";
            var result = Render(text);
            var state = State(text, () => true, retained);
            var view = new DetailContentView(state, result, (_, _, _, _) => throw new InvalidOperationException("No clipboard authority."));
            var window = new Window { Content = view, Width = 900, Height = 1000 };
            window.Show();
            try
            {
                var index = view.GetVisualDescendants().OfType<Expander>().Single();
                index.IsExpanded.Should().BeFalse();
                view.FocusReader();
                Press(window, Key.H, PhysicalKey.H, RawInputModifiers.Alt);
                index.IsExpanded.Should().BeTrue();
                window.UpdateLayout();
                var entries = HeadingEntries(index);
                entries.Select(button => ControlAutomationPeer.CreatePeerForElement(button)!.GetName()).Should().Equal(
                    "Heading 1 of 3, level 1: Repeat", "Heading 2 of 3, level 3: Repeat",
                    "Heading 3 of 3, level 6: Caf\u00e9 \u6771\u4eac");
                var targets = view.FindControl<StackPanel>("RenderedDocument")!.Children.OfType<TextBlock>()
                    .Where(block => block.Focusable).ToArray();
                result.Sections.SelectMany(section => section.Links).Should().BeEmpty();
                for (var ordinal = 0; ordinal < entries.Length; ordinal++)
                {
                    if (keyboard)
                    {
                        entries[ordinal].Focus().Should().BeTrue();
                        Press(window, ordinal == 1 ? Key.Space : Key.Enter, ordinal == 1 ? PhysicalKey.Space : PhysicalKey.Enter);
                    }
                    else
                    {
                        entries[ordinal].BringIntoView();
                        window.UpdateLayout();
                        using var frame = window.CaptureRenderedFrame();
                        var point = entries[ordinal].TranslatePoint(
                            new Point(entries[ordinal].Bounds.Width / 2, entries[ordinal].Bounds.Height / 2), window)!.Value;
                        var hit = window.InputHitTest(point) as Visual;
                        (ReferenceEquals(hit, entries[ordinal]) || hit?.GetVisualAncestors().Contains(entries[ordinal]) == true)
                            .Should().BeTrue($"entry {ordinal} at {point} should be pointer-reachable, not {hit?.GetType().Name}");
                        window.MouseMove(point);
                        window.MouseDown(point, MouseButton.Left);
                        window.MouseUp(point, MouseButton.Left);
                    }
                    targets[ordinal].IsFocused.Should().BeTrue();
                    state.Status.Should().Be("Focused the exact document heading.");
                    ControlAutomationPeer.CreatePeerForElement(entries[ordinal])!.GetName()
                        .Should().Be(AutomationProperties.GetName(entries[ordinal]));
                }
                Press(window, Key.H, PhysicalKey.H, RawInputModifiers.Alt);
                entries[0].IsFocused.Should().BeTrue();
                state.Content!.Source.Should().Be(text);
                state.ActiveText.Should().Be(result.SemanticText);
                window.OwnedWindows.Should().BeEmpty();
            }
            finally { view.ClearPresentation(); window.Close(); }
        });

    [Theory]
    [InlineData("source")]
    [InlineData("continuous")]
    [InlineData("privacy")]
    [InlineData("close")]
    [InlineData("detach")]
    public Task HeadingIndexRetainsNoNavigationOrAccessibleReadbackAfterViewWithdrawal(string boundary) =>
        HeadlessSession.RunAsync(() =>
        {
            var admitted = true;
            const string text = "# Private heading\n\nExact body";
            var state = State(text, () => admitted);
            var view = new DetailContentView(state, Render(text), (_, _, _, _) => Task.CompletedTask);
            var window = new Window { Content = view };
            window.Show();
            var index = view.GetVisualDescendants().OfType<Expander>().Single();
            index.IsExpanded = true;
            var entry = HeadingEntries(index).Single();
            var peer = ControlAutomationPeer.CreatePeerForElement(entry)!;
            var target = view.FindControl<StackPanel>("RenderedDocument")!.Children.OfType<TextBlock>().Single(block => block.Focusable);
            try
            {
                if (boundary is "source") { view.FindControl<CheckBox>("SourceToggle")!.IsChecked = true; }
                else if (boundary is "continuous") { view.FindControl<CheckBox>("ReadingTextToggle")!.IsChecked = true; }
                else if (boundary is "privacy") { admitted = false; }
                else if (boundary is "close") { view.ClearPresentation(); }
                else { window.Content = null; }
                peer.GetName().Should().BeNullOrEmpty();
                peer.IsEnabled().Should().BeFalse();
                peer.IsKeyboardFocusable().Should().BeFalse();
                peer.SetFocus();
                Click(entry);
                target.IsFocused.Should().BeFalse();
                entry.IsFocused.Should().BeFalse();
                if (boundary is "source" or "continuous")
                {
                    state.Content!.Source.Should().Be(text);
                    state.Status.Should().Contain("unavailable");
                    view.FindControl<CheckBox>(boundary is "source" ? "SourceToggle" : "ReadingTextToggle")!.IsChecked = false;
                    Click(entry);
                    target.IsFocused.Should().BeTrue();
                }
                else
                {
                    entry.Content.Should().BeNull();
                    index.Content.Should().BeNull();
                    target.Text.Should().BeEmpty();
                }
            }
            finally { view.ClearPresentation(); window.Close(); }
        });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task HeadingIndexCannotClaimCompletionWhenAdmissionChangesDuringNavigation(bool duringFocus) =>
        HeadlessSession.RunAsync(() =>
        {
            var admitted = true;
            const string text = "# Target";
            var state = State(text, () => admitted);
            var view = new DetailContentView(state, Render(text), (_, _, _, _) => Task.CompletedTask);
            var window = new Window { Content = view };
            window.Show();
            try
            {
                var index = view.GetVisualDescendants().OfType<Expander>().Single();
                index.IsExpanded = true;
                var entry = HeadingEntries(index).Single();
                var target = view.FindControl<StackPanel>("RenderedDocument")!.Children.OfType<TextBlock>().Single();
                if (duringFocus) { target.GotFocus += (_, _) => admitted = false; }
                else { target.AddHandler(Control.RequestBringIntoViewEvent, (_, _) => admitted = false); }
                Click(entry);
                state.Content.Should().BeNull();
                state.Status.Should().NotBe("Focused the exact document heading.");
                entry.Content.Should().BeNull();
                target.Text.Should().BeEmpty();
            }
            finally { view.ClearPresentation(); window.Close(); }
        });

    [Theory]
    [InlineData(314, 7, false)]
    [InlineData(314, 8, true)]
    [InlineData(315, 0, true)]
    public Task HeadingIndexIsIncludedInTheExactFinalControlBudget(int codeBlocks, int paragraphs, bool fallback) =>
        HeadlessSession.RunAsync(() =>
        {
            var text = "# Heading\n\n" + string.Concat(Enumerable.Repeat("```\ncode\n```\n\n", codeBlocks))
                + string.Concat(Enumerable.Repeat("Extra paragraph\n\n", paragraphs));
            var result = Render(text);
            result.IsFallback.Should().BeFalse();
            (codeBlocks * 13 + 1 + 6 + paragraphs > NativeDetailProfile.MaximumNodes).Should().Be(fallback);
            var state = State(text, () => true);
            var view = new DetailContentView(state, result, (_, _, _, _) => Task.CompletedTask);
            var window = new Window { Content = view };
            window.Show();
            try
            {
                MarkdownDocumentRenderer.RequiresFinalSourceFallback(result).Should().Be(fallback);
                view.GetVisualDescendants().OfType<Expander>().Count().Should().Be(fallback ? 0 : 1);
                state.RenderState.Should().Be(fallback ? DetailRenderState.SourceFallback : DetailRenderState.Rendered);
                state.Content!.Source.Should().Be(text);
                if (fallback) { state.ActiveText.Should().Be(text); }
            }
            finally { view.ClearPresentation(); window.Close(); }
        });

    [Theory]
    [InlineData("No headings")]
    [InlineData("```\n# Literal code, not a heading\n```")]
    [InlineData("# Heading\n\n<img src='https://example.invalid'>")]
    public Task NonHeadingOrFallbackFinalDocumentsNeverInventHeadingNavigation(string text) =>
        HeadlessSession.RunAsync(() =>
        {
            var result = Render(text);
            var state = State(text, () => true);
            var view = new DetailContentView(state, result, (_, _, _, _) => Task.CompletedTask);
            var window = new Window { Content = view };
            window.Show();
            try { view.GetVisualDescendants().OfType<Expander>().Should().BeEmpty(); }
            finally { view.ClearPresentation(); window.Close(); }
        });

    [Fact]
    public Task HeadingIndexNeverRetargetsAnotherViewAfterItsOwnRenderTokenChanges() =>
        HeadlessSession.RunAsync(() =>
        {
            const string text = "# Same heading";
            var state = State(text, () => true);
            var otherState = State(text, () => true, retained: true);
            var view = new DetailContentView(state, Render(text), (_, _, _, _) => Task.CompletedTask);
            var other = new DetailContentView(otherState, Render(text), (_, _, _, _) => Task.CompletedTask);
            var window = new Window { Content = view };
            var otherWindow = new Window { Content = other };
            window.Show();
            otherWindow.Show();
            try
            {
                var index = view.GetVisualDescendants().OfType<Expander>().Single();
                var entry = HeadingEntries(index).Single();
                state.CompleteRender(state.Generation, "New render", "New render", fallback: false).Should().BeTrue();
                entry.Content.Should().BeNull();
                Click(entry);
                otherState.Content!.Source.Should().Be(text);
                var otherIndex = other.GetVisualDescendants().OfType<Expander>().Single();
                otherIndex.IsExpanded = true;
                Click(HeadingEntries(otherIndex).Single());
                other.FindControl<StackPanel>("RenderedDocument")!.Children.OfType<TextBlock>().Single()
                    .IsFocused.Should().BeTrue();
            }
            finally { view.ClearPresentation(); other.ClearPresentation(); window.Close(); otherWindow.Close(); }
        });

    private static Button[] HeadingEntries(Expander index) =>
        ((StackPanel)((ScrollViewer)index.Content!).Content!).Children.OfType<Button>().ToArray();
}
