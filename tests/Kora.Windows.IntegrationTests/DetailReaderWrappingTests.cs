using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Application.Presentation;
using Kora.Core.Presentation;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class DetailReaderWrappingTests
{
    private static readonly double[] Widths = [640, 960];
    private static readonly double[] FontSizes = [12, 24, 36];

    [Theory]
    [InlineData("keyboard")]
    [InlineData("pointer")]
    [InlineData("wheel")]
    public Task NewReaderInputSupersedesDeferredReadingPosition(string input) =>
        HeadlessSession.RunAsync(() =>
        {
            var state = State(Content("plain"));
            var window = Window(state, (_, _, _) => throw new InvalidOperationException("No clipboard effect."));
            window.Show();
            try
            {
                Required<CheckBox>(window, "ReadingTextToggle").IsChecked = true;
                Required<CheckBox>(window, "WrapTextToggle").IsChecked = false;
                var reader = Required<TextBox>(window, "ContentReader");
                reader.Focus().Should().BeTrue();
                reader.CaretIndex = 100;
                window.UpdateLayout();
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                var viewport = reader.GetVisualDescendants().OfType<ScrollViewer>().Single();
                viewport.Offset = new(450, 0);
                var position = window.Reader.CaptureReadingPosition();
                position.TextOffset.X.Should().Be(450);
                window.Reader.RestoreReadingPosition(position);
                var point = reader.TranslatePoint(new(20, 20), window)!.Value;
                if (string.Equals(input, "keyboard", StringComparison.Ordinal))
                {
                    window.KeyPress(Key.F12, RawInputModifiers.None, PhysicalKey.F12, null);
                    window.KeyRelease(Key.F12, RawInputModifiers.None, PhysicalKey.F12, null);
                }
                else if (string.Equals(input, "pointer", StringComparison.Ordinal))
                {
                    window.MouseDown(point, MouseButton.Left);
                    window.MouseUp(point, MouseButton.Left);
                }
                else { window.MouseWheel(point, new(1, 0), RawInputModifiers.None); }
                viewport.Offset = new(100, 0);
                window.Reader.CaptureReadingPosition().TextOffset.X.Should().Be(100,
                    "new input retires the pending snapshot before another Hide can capture it");
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                viewport.Offset.X.Should().NotBe(450, "the old callback cannot overwrite a later reading action");
                reader.Text.Should().Be(state.Content!.Source);
            }
            finally { window.ClearAndClose(); }
        });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task DeferredPositionRestoreCannotOutliveSourceAdmissionOrClosure(bool close) =>
        HeadlessSession.RunAsync(() =>
        {
            var content = Content("plain");
            var allowed = true;
            var state = State(content, () => allowed);
            var writes = 0;
            var window = Window(state, (_, _, _) => { writes++; return Task.CompletedTask; });
            window.Show();
            try
            {
                var reader = Required<TextBox>(window, "ContentReader");
                Required<CheckBox>(window, "ReadingTextToggle").IsChecked = true;
                Required<CheckBox>(window, "WrapTextToggle").IsChecked = false;
                reader.CaretIndex = 5;
                window.UpdateLayout();
                var position = window.Reader.CaptureReadingPosition();
                Required<CheckBox>(window, "WrapTextToggle").IsChecked = true;
                window.Reader.RestoreReadingPosition(position);
                window.Reader.CaptureReadingPosition().Should().Be(position);
                if (close) { window.ClearAndClose(); }
                else { allowed = false; }
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();

                window.IsVisible.Should().BeFalse();
                state.Content.Should().BeNull();
                reader.Text.Should().BeEmpty();
                reader.SelectedText.Should().BeEmpty();
                Required<CheckBox>(window, "WrapTextToggle").IsEnabled.Should().BeFalse();
                writes.Should().Be(0);
            }
            finally { window.ClearAndClose(); }
        });

    [Theory]
    [InlineData("plain")]
    [InlineData("markdown")]
    [InlineData("fallback")]
    public Task LocalWrappingPreservesExactTextSelectionAndFindWithAccessibleRecovery(string profile) =>
        HeadlessSession.RunAsync(() =>
        {
            var content = Content(profile);
            var state = State(content);
            var writes = 0;
            var window = Window(state, (_, _, _) => { writes++; return Task.CompletedTask; });
            window.Show();
            try
            {
                var toggle = Required<CheckBox>(window, "WrapTextToggle");
                var reader = Required<TextBox>(window, "ContentReader");
                toggle.IsChecked.Should().BeTrue();
                toggle.IsVisible.Should().Be(string.Equals(profile, "fallback", StringComparison.Ordinal));
                if (!string.Equals(profile, "fallback", StringComparison.Ordinal))
                {
                    Required<CheckBox>(window, "ReadingTextToggle").IsChecked = true;
                }
                toggle.IsVisible.Should().BeTrue();
                Required<TextBox>(window, "SearchInput").Text = "alpha";
                Required<Button>(window, "FirstMatch").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var text = reader.Text;
                reader.SelectionStart = 0;
                reader.SelectionEnd = 5;
                var selected = reader.SelectedText;
                var caret = reader.CaretIndex;
                var before = (state.Generation, state.MatchStart, state.MatchCount, state.MatchOrdinal, state.Status);
                var peer = ControlAutomationPeer.CreatePeerForElement(toggle)!;
                peer.GetAutomationControlType().Should().Be(AutomationControlType.CheckBox);
                peer.GetName().Should().Be("Wrap reader text");
                peer.GetHelpText().Should().Contain("Not a saved preference");
                var provider = peer.GetProvider<IToggleProvider>()!;
                provider.Should().NotBeNull();
                provider.Toggle();

                foreach (var width in Widths)
                {
                    window.Width = width;
                    foreach (var fontSize in FontSizes)
                    {
                        reader.FontSize = fontSize;
                        window.UpdateLayout();
                        var viewport = reader.GetVisualDescendants().OfType<ScrollViewer>().Single();
                        reader.TextWrapping.Should().Be(TextWrapping.NoWrap);
                        viewport.HorizontalScrollBarVisibility.Should().Be(ScrollBarVisibility.Auto);
                        viewport.Extent.Width.Should().BeGreaterThan(viewport.Viewport.Width);
                        viewport.Offset = new(100, 0);
                        window.UpdateLayout();
                        viewport.Offset.X.Should().BeGreaterThan(0);

                        provider.Toggle();
                        window.UpdateLayout();
                        reader.TextWrapping.Should().Be(TextWrapping.Wrap);
                        viewport.HorizontalScrollBarVisibility.Should().Be(ScrollBarVisibility.Disabled);
                        viewport.Extent.Width.Should().BeLessThanOrEqualTo(viewport.Viewport.Width + 1);
                        viewport.Offset.X.Should().Be(0);
                        reader.Bounds.Height.Should().BeGreaterThan(0);

                        toggle.Focus().Should().BeTrue();
                        window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
                        window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
                        toggle.IsFocused.Should().BeTrue();
                        toggle.IsChecked.Should().BeFalse();
                    }
                }

                reader.Text.Should().Be(text);
                reader.SelectedText.Should().Be(selected);
                reader.CaretIndex.Should().Be(caret);
                state.Content.Should().BeSameAs(content);
                (state.Generation, state.MatchStart, state.MatchCount, state.MatchOrdinal, state.Status).Should().Be(before);
                Required<TextBox>(window, "SearchInput").Text.Should().Be("alpha");
                writes.Should().Be(0);
                Required<Button>(window, "NextMatch").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                state.MatchOrdinal.Should().Be(2);

                Required<CheckBox>(window, "SourceToggle").IsChecked = true;
                reader.Text.Should().Be(content.Source);
                reader.TextWrapping.Should().Be(TextWrapping.NoWrap, "projection changes retain only this view's layout choice");
                if (string.Equals(profile, "markdown", StringComparison.Ordinal))
                {
                    Required<CheckBox>(window, "ReadingTextToggle").IsChecked = false;
                    Required<CheckBox>(window, "SourceToggle").IsChecked = false;
                    toggle.IsVisible.Should().BeFalse();
                    Required<CheckBox>(window, "SourceToggle").IsChecked = true;
                    toggle.IsVisible.Should().BeTrue();
                    reader.TextWrapping.Should().Be(TextWrapping.NoWrap);
                }
                content.Source.Should().Contain("\t").And.Contain("🙂");
                writes.Should().Be(0);
            }
            finally { window.ClearAndClose(); }
        });

    [Theory]
    [InlineData("privacy")]
    [InlineData("replacement")]
    [InlineData("close")]
    [InlineData("during-layout")]
    public Task RetirementClearsAndDisablesOldControlsAndFreshAdmissionStartsWrapped(string reason) =>
        HeadlessSession.RunAsync(() =>
        {
            var content = Content("plain");
            var current = content;
            var allowed = true;
            var state = State(content, () => allowed && ReferenceEquals(content, current));
            var writes = 0;
            var window = Window(state, (_, _, _) => { writes++; return Task.CompletedTask; });
            window.Show();
            var toggle = Required<CheckBox>(window, "WrapTextToggle");
            var reader = Required<TextBox>(window, "ContentReader");
            if (string.Equals(reason, "during-layout", StringComparison.Ordinal))
            {
                reader.PropertyChanged += (_, args) =>
                {
                    if (args.Property == TextBox.TextWrappingProperty) { state.Close(); }
                };
            }
            else if (string.Equals(reason, "close", StringComparison.Ordinal)) { window.ClearAndClose(); }
            else if (string.Equals(reason, "replacement", StringComparison.Ordinal)) { current = Content("plain"); }
            else { allowed = false; }

            toggle.IsChecked = false;
            window.IsVisible.Should().BeFalse();
            state.Content.Should().BeNull();
            reader.Text.Should().BeEmpty();
            reader.SelectedText.Should().BeEmpty();
            toggle.IsEnabled.Should().BeFalse();
            toggle.IsChecked = true;
            toggle.IsChecked = false;
            reader.Text.Should().BeEmpty();
            writes.Should().Be(0);
            window.ClearAndClose();

            var freshState = State(content);
            var fresh = Window(freshState, (_, _, _) => throw new InvalidOperationException("No clipboard effect."));
            fresh.Show();
            try
            {
                Required<CheckBox>(fresh, "WrapTextToggle").IsChecked.Should().BeTrue();
                Required<TextBox>(fresh, "ContentReader").TextWrapping.Should().Be(TextWrapping.Wrap);
                Required<TextBox>(fresh, "ContentReader").Text.Should().Be(content.Source);
            }
            finally { fresh.ClearAndClose(); }
        });

    private static AdmittedDetailContent Content(string profile)
    {
        var source = "alpha\t🙂  alpha\r\n" + new string('M', 300) + "\r\nend";
        if (string.Equals(profile, "markdown", StringComparison.Ordinal)) { source = "# Heading\n\n" + source; }
        if (string.Equals(profile, "fallback", StringComparison.Ordinal)) { source = "<script>inert</script>\n" + source; }
        return new(new(new(Guid.NewGuid()), 1),
            string.Equals(profile, "plain", StringComparison.Ordinal) ? DetailContentKind.PlainText : DetailContentKind.Markdown,
            DetailContentOrigin.EmbeddedDocument, DetailSensitivity.Public, "Reader", "Embedded exact document", source);
    }

    private static DetailViewerState State(AdmittedDetailContent content, Func<bool>? admission = null)
    {
        var state = new DetailViewerState(content, admission);
        var result = new NativeDetailRenderer(NullLogger<NativeDetailRenderer>.Instance).Render(content.Source, content.Kind);
        state.CompleteRender(state.Generation, result.SemanticText, result.Status, result.IsFallback);
        return state;
    }

    private static DetailWindow Window(DetailViewerState state, Func<bool, int?, int, Task> copy) =>
        new(state, new NativeDetailRenderer(NullLogger<NativeDetailRenderer>.Instance)
            .Render(state.Content!.Source, state.Content.Kind), copy) { Width = 640, Height = 760 };

    private static T Required<T>(DetailWindow window, string name) where T : Control =>
        window.FindControl<T>(name) ?? throw new InvalidOperationException("Missing native control.");
}
