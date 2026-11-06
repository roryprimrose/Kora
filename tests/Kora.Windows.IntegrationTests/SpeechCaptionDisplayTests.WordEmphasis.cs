using System.Speech.Synthesis;

using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Application.Voice;
using Kora.Core.Hosting;
using Kora.Core.Voice;
using Kora.Windows.Audio;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SpeechCaptionDisplayTests
{
    private static readonly int[] WordEmphasisReaderScales = [75, 200, 100];

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Native_source_ranges_reach_visible_emphasis_without_stealing_keyboard_pointer_copy_or_reader_focus(bool qualified)
    {
        await HeadlessSession.RunAsync(() =>
        {
            const string text = "Go go \ud83d\ude00 12. Next!";
            var observed = new SpeechCaption();
            var response = Guid.NewGuid();
            var id = observed.Bind(response, HostRequest.Create(RequestOrigin.LocalUi), text, static () => true,
                new(SpeechCaptionPlacement.BottomRight, 30, true));
            var token = new SpeechPlaybackToken(id, 8);
            var prompt = new Prompt(text);
            var native = new WindowsTextToSpeechService.SentenceAlignment(prompt, token, text);
            foreach (var (start, count, milliseconds) in new[] { (0, 2, 100), (3, 2, 200), (6, 2, 300), (9, 2, 400), (13, 4, 1000) })
            {
                native.Observe(prompt, new(token, start, count, TimeSpan.FromMilliseconds(milliseconds)));
            }
            native.Seal(TimeSpan.FromSeconds(2), true, qualified);
            var caption = new Caption();
            var window = new SpeechCaptionWindow(caption);
            using var controller = new SpeechCaptionWindowController(caption, window,
                new Displays(Display(0, 0, 1920, 1080, 1, primary: true)));
            var reader = window.FindControl<TextBox>("CaptionText")!;
            var layer = window.FindControl<SpeechCaptionEmphasisLayer>("CaptionEmphasis")!;
            void At(int milliseconds)
            {
                var position = TimeSpan.FromMilliseconds(milliseconds);
                caption.SpeechCaptionText = observed.Observe(new(true, 0.5, id, 8,
                    Sentence: native.At(position), SentenceAlignmentAvailable: native.Available,
                    Word: native.WordAt(position), WordAlignmentAvailable: native.WordsAvailable), response);
                caption.SpeechCaptionEmphasis = observed.Emphasis;
                controller.Refresh();
                window.UpdateLayout();
            }
            void AssertUnderlines(int start, int count)
            {
                var textPresenter = reader.GetVisualDescendants().OfType<TextPresenter>().Single();
                var origin = textPresenter.TranslatePoint(default, layer)!.Value;
                var expected = qualified
                    ? textPresenter.TextLayout.HitTestTextRange(start, count)
                        .Select(rect => new Rect(rect.X + origin.X, rect.Bottom + origin.Y - 2, rect.Width, 2)).ToArray()
                    : [];
                layer.Underlines().Should().Equal(expected, "the native geometry must belong to this exact engine offset, not the selected or first matching word");
            }

            At(99);
            layer.Underlines().Should().BeEmpty();
            At(100);
            reader.Text.Should().Be(text);
            layer.Underlines().Any().Should().Be(qualified);
            ControlAutomationPeer.CreatePeerForElement(reader)!.GetName().Should().Be("Current spoken utterance");
            var decorationPeer = ControlAutomationPeer.CreatePeerForElement(layer)!;
            decorationPeer.IsControlElement().Should().BeFalse();
            decorationPeer.IsContentElement().Should().BeFalse();
            decorationPeer.GetName().Should().BeEmpty("the range decoration is not another accessible text source");
            layer.Focusable.Should().BeFalse();
            layer.IsHitTestVisible.Should().BeFalse();
            reader.Focus().Should().BeTrue();
            Press(window, Key.Home, PhysicalKey.Home, RawInputModifiers.Control);
            Press(window, Key.Right, PhysicalKey.ArrowRight, RawInputModifiers.Shift);
            Press(window, Key.Right, PhysicalKey.ArrowRight, RawInputModifiers.Shift);
            reader.SelectedText.Should().Be("Go");
            At(200);
            AssertUnderlines(3, 2);
            reader.SelectedText.Should().Be("Go", "an identical later word has a different engine source offset, not a new selection");
            reader.IsFocused.Should().BeTrue();
            caption.SpeechCaptionEmphasis.Should().Be(qualified ? new SpeechCaptionEmphasis(3, 2) : null);
            string? copied = null;
            reader.AddHandler(TextBox.CopyingToClipboardEvent, (_, args) =>
            {
                copied = reader.SelectedText;
                args.Handled = true;
            });
            reader.Copy();
            copied.Should().Be("Go", "copy still uses the user's selection, never the active source range");
            reader.SelectedText.Should().Be("Go");

            foreach (var percent in WordEmphasisReaderScales)
            {
                caption.ReaderTextScalePercent = percent;
                At(300);
                AssertUnderlines(6, 2);
                reader.FontSize.Should().Be(16 * percent / 100.0);
                reader.SelectedText.Should().Be("Go");
                layer.Underlines().Any().Should().Be(qualified);
            }
            var presenter = reader.GetVisualDescendants().OfType<TextPresenter>().Single();
            var first = presenter.TextLayout.HitTestTextPosition(3);
            var end = presenter.TextLayout.HitTestTextPosition(5);
            var startPoint = presenter.TranslatePoint(new Point(first.X + 0.1, first.Center.Y), window)!.Value;
            var endPoint = presenter.TranslatePoint(new Point(end.X + 0.1, end.Center.Y), window)!.Value;
            window.MouseDown(startPoint, MouseButton.Left);
            window.MouseMove(endPoint, RawInputModifiers.LeftMouseButton);
            window.MouseUp(endPoint, MouseButton.Left);
            reader.SelectedText.Should().Be("go");
            At(400);
            AssertUnderlines(9, 2);
            reader.SelectedText.Should().Be("go");
            caption.SpeechCaptionEmphasis.Should().Be(qualified ? new SpeechCaptionEmphasis(9, 2) : null);
            var query = new TextBox { Text = "Independent Find query" };
            var findWindow = new Window { Content = query };
            try
            {
                findWindow.Show();
                query.Focus().Should().BeTrue();
                At(450);
                query.IsFocused.Should().BeTrue();
                reader.SelectedText.Should().Be("go");
                observed.Complete();
                caption.SpeechCaptionText = observed.Observe(SpeechPlaybackFrame.Inactive, response);
                caption.SpeechCaptionEmphasis = observed.Emphasis;
                caption.SpeechCaptionLabel = "PREVIOUS SPEECH";
                controller.Refresh();
                layer.Underlines().Should().BeEmpty();
                reader.Text.Should().Be(text);
                caption.ClosePrivacy();
                reader.Text.Should().BeNull();
                reader.SelectedText.Should().BeEmpty();
                layer.Underlines().Should().BeEmpty();
                At(1000);
                window.IsVisible.Should().BeFalse();
            }
            finally { findWindow.Close(); }
        });
    }

    [Theory]
    [InlineData(-1, 1)]
    [InlineData(0, 0)]
    [InlineData(int.MaxValue, 2)]
    public async Task Invalid_native_projection_fails_explicitly_and_clears_the_surface(int start, int length)
    {
        await HeadlessSession.RunAsync(() =>
        {
            var caption = new Caption { SpeechCaptionEmphasis = new(start, length) };
            var window = new SpeechCaptionWindow(caption);
            using var controller = new SpeechCaptionWindowController(caption, window,
                new Displays(Display(0, 0, 1920, 1080, 1, primary: true)));
            controller.Refresh();
            window.IsVisible.Should().BeFalse();
            caption.Failures.Should().Equal(nameof(InvalidOperationException));
            window.FindControl<TextBox>("CaptionText")!.Text.Should().BeNull();
            window.FindControl<SpeechCaptionEmphasisLayer>("CaptionEmphasis")!.Underlines().Should().BeEmpty();
        });
    }

    private static void Press(Window window, Key key, PhysicalKey physical, RawInputModifiers modifiers)
    {
        window.KeyPress(key, modifiers, physical, null);
        window.KeyRelease(key, modifiers, physical, null);
    }
}
