using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Application.Presentation;
using Kora.Core.Hosting;
using Kora.Core.Presentation;
using Kora.NativeUxFixture;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class DetailReaderLayoutTests
{
    [Theory]
    [InlineData(640, 500, 75, false)]
    [InlineData(640, 500, 100, false)]
    [InlineData(640, 500, 200, false)]
    [InlineData(960, 760, 200, false)]
    [InlineData(640, 500, 75, true)]
    [InlineData(640, 500, 100, true)]
    [InlineData(640, 500, 200, true)]
    [InlineData(960, 760, 200, true)]
    public Task LongChromeAndFindKeepTheExactResultReadableAndAllActionsReachable(int width, int height, int percent, bool finalized) =>
        HeadlessSession.RunAsync(async () =>
    {
        using var fixture = new NativeUxFixtureSession(Path.GetFullPath("."));
        await fixture.InitializeAsync();
        var literal = new string('a', 256);
        var source = "# Original result\n\n" + literal + "\n\n"
            + string.Join("\n\n", Enumerable.Range(1, 80).Select(index => $"Unchanged result paragraph {index}."));
        var request = HostRequest.Create(RequestOrigin.ActivatedVoice);
        var content = new AdmittedDetailContent(new(new(Guid.NewGuid()), 1), DetailContentKind.Markdown,
            finalized ? DetailContentOrigin.FinalizedResponse : DetailContentOrigin.EmbeddedDocument,
            finalized ? DetailSensitivity.DisclosureConfirmationRequired : DetailSensitivity.Public,
            string.Join(" ", Enumerable.Repeat("Long result title", 14)),
            string.Join(" ", Enumerable.Repeat("Exact immutable source", 11)), source,
            finalized ? new(request.SessionId, request.RequestId, request.TaskId) : null);
        var state = new DetailViewerState(content, finalized ? () => true : null);
        var result = new NativeDetailRenderer(NullLogger<NativeDetailRenderer>.Instance).Render(source, content.Kind);
        state.CompleteRender(state.Generation, result.SemanticText, result.Status, result.IsFallback);
        var writes = 0;
        var window = new DetailWindow(state, result, (_, _, _) => { writes++; return Task.CompletedTask; }, fixture.Main)
        { Width = width, Height = height };
        window.Show();
        try
        {
            fixture.Main.ReaderTextScalePercent = percent;
            await fixture.Main.AppearanceUpdate;
            var search = Required<TextBox>(window, "SearchInput");
            search.Text = literal;
            Required<Button>(window, "FirstMatch").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window.UpdateLayout();
            state.MatchCount.Should().Be(1);
            Required<ScrollViewer>(window, "RenderedViewport").Bounds.Height.Should().BeGreaterThanOrEqualTo(160,
                "supported Details dimensions must retain a useful body viewport, not merely a nonzero strip");

            if (finalized)
            {
                var index = window.GetVisualDescendants().OfType<Expander>().Single();
                index.IsExpanded.Should().BeFalse();
                search.Focus().Should().BeTrue();
                window.KeyPress(Key.H, RawInputModifiers.Alt, PhysicalKey.H, null);
                window.KeyRelease(Key.H, RawInputModifiers.Alt, PhysicalKey.H, null);
                window.UpdateLayout();
                index.IsExpanded.Should().BeTrue();
                var entries = (StackPanel)((ScrollViewer)index.Content!).Content!;
                entries.Children.OfType<Button>().Single().IsFocused.Should().BeTrue();
                window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                Required<StackPanel>(window, "RenderedDocument").Children.OfType<TextBlock>()
                    .Single(block => block.Focusable).IsFocused.Should().BeTrue();
                state.Status.Should().Be("Focused the exact document heading.");
                Required<ScrollViewer>(window, "RenderedViewport").Bounds.Height.Should().BeGreaterThanOrEqualTo(160);
            }
            else { window.GetVisualDescendants().OfType<Expander>().Should().BeEmpty(); }
            search.Text.Should().Be(literal);

            var metadata = Required<TextBlock>(window, "DigestLabel");
            var chrome = metadata.GetVisualAncestors().OfType<ScrollViewer>().First();
            RevealWithKeyboard(window, metadata, chrome);
            AssertWithin(metadata, chrome);

            var close = Required<Button>(window, "CloseDetail");
            var actions = close.GetVisualAncestors().OfType<ScrollViewer>().First();
            RevealWithKeyboard(window, close, actions);
            AssertWithin(close, actions);
            actions.Offset.Y.Should().BeGreaterThan(0, "bounded chrome scrolls instead of consuming the reading area");

            var reader = Required<TextBox>(window, "ContentReader");
            Required<CheckBox>(window, "ReadingTextToggle").IsChecked = true;
            window.UpdateLayout();
            reader.Bounds.Height.Should().BeGreaterThanOrEqualTo(160);
            reader.Text.Should().Be(result.SemanticText);
            Required<CheckBox>(window, "SourceToggle").IsChecked = true;
            window.UpdateLayout();
            reader.Bounds.Height.Should().BeGreaterThanOrEqualTo(160);
            reader.Text.Should().Be(source);
            reader.Focus().Should().BeTrue();
            reader.CaretIndex = 0;
            window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
            window.KeyRelease(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
            reader.CaretIndex.Should().BeGreaterThan(0, "child reader arrows must retain native caret navigation");
            window.KeyPress(Key.Up, RawInputModifiers.None, PhysicalKey.ArrowUp, null);
            window.KeyRelease(Key.Up, RawInputModifiers.None, PhysicalKey.ArrowUp, null);
            reader.CaretIndex.Should().Be(0);
            reader.Text.Should().Be(source);
            search.Text.Should().BeEmpty();
            state.Content.Should().BeSameAs(content);
            state.Content.Digest.Should().Be(content.Digest);
            writes.Should().Be(0);

            close.Focus().Should().BeTrue();
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            window.IsVisible.Should().BeFalse();
            state.Content.Should().BeNull();
            reader.Text.Should().BeEmpty();
            search.Text.Should().BeEmpty();
            writes.Should().Be(0);
        }
        finally { window.ClearAndClose(); }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task ChromeLineNavigationCannotKeepPrivateOrClosedContentAlive(bool alreadyClosed) => HeadlessSession.RunAsync(() =>
    {
        var current = true;
        var content = new AdmittedDetailContent(new(new(Guid.NewGuid()), 1), DetailContentKind.PlainText,
            DetailContentOrigin.EmbeddedDocument, DetailSensitivity.DisclosureConfirmationRequired,
            "Private result", "Exact source", "Private immutable result body");
        var state = new DetailViewerState(content, () => current);
        var result = new NativeDetailRenderer(NullLogger<NativeDetailRenderer>.Instance).Render(content.Source, content.Kind);
        state.CompleteRender(state.Generation, result.SemanticText, result.Status, result.IsFallback);
        var writes = 0;
        var window = new DetailWindow(state, result, (_, _, _) => { writes++; return Task.CompletedTask; });
        window.Show();
        try
        {
            var viewport = Required<ScrollViewer>(window, "ProvenanceViewport");
            viewport.Focus().Should().BeTrue();
            current = false;
            if (alreadyClosed) { window.ClearAndClose(); }
            var key = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Down };
            viewport.RaiseEvent(key);
            key.Handled.Should().BeTrue();
            window.IsVisible.Should().BeFalse();
            state.Content.Should().BeNull();
            Required<TextBox>(window, "ContentReader").Text.Should().BeEmpty();
            Required<TextBlock>(window, "ProvenanceLabel").Text.Should().BeEmpty();
            writes.Should().Be(0);
        }
        finally { window.ClearAndClose(); }
    });

    private static void RevealWithKeyboard(Window window, Control target, ScrollViewer viewport)
    {
        viewport.Focus().Should().BeTrue();
        for (var step = 0; step < 128; step++)
        {
            var position = target.TranslatePoint(default, viewport) ?? throw new InvalidOperationException("Detached native control.");
            if (position.Y >= -1 && position.Y + target.Bounds.Height <= viewport.Bounds.Height + 1) { return; }
            var up = position.Y < 0;
            var key = up ? Key.Up : Key.Down;
            var physical = up ? PhysicalKey.ArrowUp : PhysicalKey.ArrowDown;
            window.KeyPress(key, RawInputModifiers.None, physical, null);
            window.KeyRelease(key, RawInputModifiers.None, physical, null);
            window.UpdateLayout();
        }
        AssertWithin(target, viewport);
    }

    private static void AssertWithin(Control control, ScrollViewer viewport)
    {
        var position = control.TranslatePoint(default, viewport) ?? throw new InvalidOperationException("Detached native control.");
        position.Y.Should().BeGreaterThanOrEqualTo(-1);
        (position.Y + control.Bounds.Height).Should().BeLessThanOrEqualTo(viewport.Bounds.Height + 1);
        viewport.Bounds.Height.Should().BeGreaterThan(0);
    }

    private static T Required<T>(DetailWindow window, string name) where T : Control =>
        window.FindControl<T>(name) ?? throw new InvalidOperationException("Missing native detail control.");
}
