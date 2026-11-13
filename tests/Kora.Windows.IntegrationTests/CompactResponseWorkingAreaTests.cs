using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Application.ViewModels;
using Kora.Core.Voice;
using Kora.NativeUxFixture;

using Microsoft.Extensions.Logging.Abstractions;

using SkiaSharp;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class CompactResponseWorkingAreaTests
{
    private static readonly string[] SubmittedActivityControls = ["RefreshSubmittedActivity", "StopSubmittedActivity"];

    [Theory]
    [InlineData(320, 400, 1)]
    [InlineData(640, 800, 2)]
    [InlineData(480, 570, 1)]
    [InlineData(1200, 900, 1)]
    public async Task CompactContentActionsAndInputFillAnUnframedViewport(int width, int height, double scale)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var fixture = new NativeUxFixtureSession(ScratchParent(), silentCaption: true);
            await fixture.InitializeAsync();
            var window = new ResponseWindow(fixture.Main, null, null, () => new(0, 0, width, height));
            try
            {
                window.SetRenderScaling(scale);
                window.ShowResponse();
                window.UpdateLayout();
                AssertCompactFrameContainsControls(window);
                window.ShowSessions(true, activate: false);
                window.UpdateLayout();
                window.FindControl<Border>("CurrentInteractionContent")!.CornerRadius.Should().Be(new CornerRadius(16));
                window.ShowSessions(false, activate: false);
                window.UpdateLayout();
                AssertCompactFrameContainsControls(window);
                fixture.Voice.StartCalls.Should().Be(0);
                fixture.SilentSpeech!.AdmittedRequests.Should().Be(0);
            }
            finally { window.Close(); }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CompactBackgroundCoversEveryViewportCorner(bool dark)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var fixture = new NativeUxFixtureSession(ScratchParent(), silentCaption: true);
            await fixture.InitializeAsync();
            var window = new ResponseWindow(fixture.Main);
            var panelColor = dark ? Colors.DarkSlateGray : Colors.WhiteSmoke;
            window.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
            window.Resources["KoraWindowBackgroundBrush"] = Brushes.Black;
            window.Resources["KoraPresencePanelBrush"] = new SolidColorBrush(panelColor);
            try
            {
                window.ShowResponse();
                window.UpdateLayout();
                using (window.CaptureRenderedFrame()) { }
                using var frame = window.CaptureRenderedFrame();
                frame.Should().NotBeNull();
                using var stream = new MemoryStream();
                frame!.Save(stream, PngBitmapEncoderOptions.Default);
                stream.Position = 0;
                using var pixels = SKBitmap.Decode(stream);
                var viewport = window.FindControl<ScrollViewer>("ResponseViewport")!;
                var bounds = new Rect(viewport.TranslatePoint(default, window)!.Value, viewport.Bounds.Size);
                var expected = new SKColor(panelColor.R, panelColor.G, panelColor.B, panelColor.A);
                Point[] corners =
                [
                    bounds.TopLeft + new Vector(1, 1),
                    bounds.TopRight + new Vector(-2, 1),
                    bounds.BottomLeft + new Vector(1, -2),
                    bounds.BottomRight + new Vector(-2, -2),
                ];
                foreach (var corner in corners)
                {
                    pixels.GetPixel((int)(corner.X * window.RenderScaling), (int)(corner.Y * window.RenderScaling))
                        .Should().Be(expected, "the chat background must reach the client edges without an inset frame");
                }
                fixture.Voice.StartCalls.Should().Be(0);
                fixture.SilentSpeech!.AdmittedRequests.Should().Be(0);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public async Task InteractiveFixtureWorkbenchExpandsAndCollapsesTheSameResponseWithoutSendingInput()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var fixture = new NativeUxFixtureSession(ScratchParent(), silentCaption: true);
            await fixture.InitializeAsync();
            using var sessions = new SessionsWindowController(fixture.Main, fixture.Sessions, fixture.Evidence,
                fixture.Access, NullLogger<SessionsViewModel>.Instance);
            sessions.Bind();
            using var responses = NativeUxFixtureWindow.CreateResponseWorkbench(fixture.Main, sessions, null);
            var window = responses.PrepareWorkbenchOwner();
            window.ShowResponse();
            fixture.Main.CommandText = "Unsent fixture input";

            window.FindControl<Button>("ExpandSessions")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await sessions.InteractionOperation;
            responses.PrepareWorkbenchOwner().Should().BeSameAs(window);
            window.IsExpanded.Should().BeTrue();
            window.IsSessionListPresented.Should().BeTrue();
            window.FindControl<WrapPanel>("ExpandedControls")!.IsEffectivelyVisible.Should().BeTrue();

            window.FindControl<Button>("CollapseWorkbench")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window.IsExpanded.Should().BeFalse();
            window.FindControl<WrapPanel>("ExpandedControls")!.IsEffectivelyVisible.Should().BeFalse();
            fixture.Main.CommandText.Should().Be("Unsent fixture input");
            fixture.Voice.StartCalls.Should().Be(0);
            fixture.SilentSpeech!.AdmittedRequests.Should().Be(0);
        });
    }

    [Fact]
    public async Task IdleCompactResponseHasOnlyCoreGlyphActionsAndExplicitOverflow()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var fixture = new NativeUxFixtureSession(ScratchParent(), silentCaption: true);
            await fixture.InitializeAsync();
            var window = new ResponseWindow(fixture.Main);
            try
            {
                window.ShowResponse();
                window.UpdateLayout();
                ((ISolidColorBrush)window.Background!).Color.A.Should().Be(255);
                var buttons = window.GetVisualDescendants().OfType<Button>()
                    .Where(button => button.IsEffectivelyVisible
                        && !button.GetVisualAncestors().OfType<Avalonia.Controls.Primitives.ScrollBar>().Any()).ToArray();
                buttons.Select(button => button.Name).Should().BeEquivalentTo(
                    ["ExpandSessions", "MinimizeWorkbench", "MaximizeWorkbench", "CloseWorkbench",
                        "ResponseMoreActions", "PushToTalkButton", "SubmitResponseCommand"]);
                foreach (var button in buttons)
                {
                    if (button.Classes.Contains("caption")) { continue; }
                    if (!string.Equals(button.Name, "PushToTalkButton", StringComparison.Ordinal))
                    { button.Content.Should().BeOfType<PathIcon>(); }
                    ToolTip.GetTip(button).Should().NotBeNull();
                    AutomationProperties.GetName(button).Should().NotBeNullOrWhiteSpace();
                    button.Width.Should().Be(28);
                    button.Height.Should().Be(28);
                }
                window.FindControl<TextBlock>("SubmittedActivityStatus")!.IsEffectivelyVisible.Should().BeFalse();
                window.FindControl<Button>("CancelResponseTask")!.IsEffectivelyVisible.Should().BeFalse();
                window.FindControl<Button>("StopResponseSpeech")!.IsEffectivelyVisible.Should().BeFalse();
                var more = window.FindControl<Button>("ResponseMoreActions")!;
                var morePosition = more.TranslatePoint(default, window)!.Value;
                morePosition.X.Should().BeGreaterThan(window.ClientSize.Width / 2);
                more.BringIntoView();
                window.UpdateLayout();
                more.Focus().Should().BeTrue();
                window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
                window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
                Dispatcher.UIThread.RunJobs();
                var menu = (MenuFlyout)more.Flyout!;
                menu.IsOpen.Should().BeTrue();
                menu.Items.OfType<MenuItem>().Select(item => item.Header).Should().Contain("Current response details");
                menu.Items.OfType<MenuItem>().Select(item => item.Header).Should().Contain("Dismiss response");
                menu.Hide();
                fixture.Voice.StartCalls.Should().Be(0);
                fixture.SilentSpeech!.AdmittedRequests.Should().Be(0);
                window.ShowSessions(true, activate: false);
                window.FindControl<TextBlock>("SubmittedActivityStatus")!.IsEffectivelyVisible.Should().BeTrue();
            }
            finally { window.Close(); }
        });
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, false)]
    public async Task ContentPointerPressesDoNotStartWindowMove(bool expanded, bool text, bool rightButton)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var fixture = new NativeUxFixtureSession(ScratchParent(), silentCaption: true);
            await fixture.InitializeAsync();
            fixture.Main.ReportHostInteractionFailure("Readable response text");
            var window = new ResponseWindow(fixture.Main);
            try
            {
                window.ShowResponse();
                if (expanded) { window.ShowSessions(true, activate: false); }
                window.UpdateLayout();
                bool? handled = null;
                object? source = null;
                window.AddHandler(InputElement.PointerPressedEvent, (_, args) =>
                {
                    handled = args.Handled;
                    source = args.Source;
                },
                    RoutingStrategies.Bubble, handledEventsToo: true);
                var point = new Point(2, window.FindControl<Border>("WorkbenchCaption")!.Bounds.Height + 2);
                if (text)
                {
                    var title = window.FindControl<TextBlock>("ResponseBodyText")!;
                    title.BringIntoView();
                    window.UpdateLayout();
                    point = title.TranslatePoint(new Point(2, 2), window)!.Value;
                    title.Cursor!.ToString().Should().Be("Arrow");
                }
                var button = rightButton ? MouseButton.Right : MouseButton.Left;
                window.MouseDown(point, button);
                window.MouseUp(point, button);
                handled.Should().BeFalse($"pointer source was {source?.GetType().Name}");
                fixture.Voice.StartCalls.Should().Be(0);
                fixture.SilentSpeech!.AdmittedRequests.Should().Be(0);
            }
            finally { window.Close(); }
        });
    }

    [Theory]
    [InlineData(320, 400, 1)]
    [InlineData(640, 800, 2)]
    [InlineData(479, 569, 1)]
    [InlineData(480, 400, 1)]
    [InlineData(320, 690, 1)]
    [InlineData(480, 570, 1)]
    [InlineData(960, 1140, 2)]
    [InlineData(1200, 900, 1)]
    public async Task CompactResponseFitsAndOriginalControlsRemainReachable(int width, int height, double scale)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var fixture = new NativeUxFixtureSession(ScratchParent(), silentCaption: true);
            await fixture.InitializeAsync();
            fixture.Main.DefaultResponseMode = ResponseOutputMode.VisualOnly;
            fixture.Main.CommandText = "what power action is pending";
            await fixture.Main.RunTypedCommand.ExecuteAsync();
            fixture.Main.CommandText = "Original unsent command";
            var body = fixture.Main.ResponseBody;
            var actions = fixture.Main.ResponseActions.ToArray();
            var capture = fixture.Voice.StartCalls;
            var speech = fixture.SilentSpeech!.AdmittedRequests;
            var position = fixture.Main.ResponseWindowPosition;
            var area = new PixelRect(-800, -500, width, height);
            var window = new ResponseWindow(fixture.Main, null, null, () => area);
            try
            {
                window.SetRenderScaling(scale);
                window.ShowResponse();
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();

                window.Width.Should().BeLessThanOrEqualTo(width / scale);
                window.Height.Should().BeLessThanOrEqualTo(height / scale);
                window.MinWidth.Should().Be(Math.Min(480, width / scale));
                window.MinHeight.Should().Be(Math.Min(570, height / scale));
                if (width / scale < 560 || height / scale < 690)
                {
                    area.Contains(new PixelRect(window.Position,
                        new PixelSize((int)Math.Ceiling(window.Width * scale),
                            (int)Math.Ceiling(window.Height * scale)))).Should().BeTrue();
                }
                var input = window.FindControl<TextBox>("CommandInput")!;
                input.Text.Should().Be("Original unsent command");
                AutomationProperties.GetName(input).Should().Be("Typed command or artifact slash input");
                var inputRow = (Grid)input.Parent!;
                var microphone = window.FindControl<Button>("PushToTalkButton")!;
                var submit = window.FindControl<Button>("SubmitResponseCommand")!;
                var reservedWidth = submit.Bounds.Width + (microphone.IsVisible ? microphone.Bounds.Width : 0)
                    + (inputRow.ColumnSpacing * 2);
                var viewport = window.FindControl<ScrollViewer>("ResponseViewport")!;
                var availableWidth = Math.Min(inputRow.Bounds.Width - reservedWidth, viewport.Viewport.Width);
                input.Bounds.Width.Should().BeApproximately(availableWidth, 0.01,
                    "the input fills available row space without exceeding the visible narrow-screen viewport");
                var reader = window.GetVisualDescendants().OfType<TextBlock>()
                    .Single(text => string.Equals(text.Text, body, StringComparison.Ordinal));
                reader.Text.Should().Be(body);
                reader.BringIntoView();
                window.UpdateLayout();
                AssertVerticallyInside(window, reader);
                input.BringIntoView();
                window.UpdateLayout();
                AssertInside(window, input);
                input.Focus().Should().BeTrue();
                input.Text = "Edited without submission";
                fixture.Main.CommandText.Should().Be("Edited without submission");
                var dismiss = window.FindControl<Button>("CloseWorkbench")!;
                dismiss.BringIntoView();
                window.UpdateLayout();
                AssertInside(window, dismiss);
                dismiss.Focus().Should().BeTrue();
                foreach (var name in SubmittedActivityControls)
                {
                    var control = window.FindControl<Button>(name)!;
                    control.IsEffectivelyVisible.Should().BeFalse("work management belongs to the expanded workbench");
                }
                fixture.Main.ResponseBody.Should().Be(body);
                fixture.Main.ResponseActions.Should().Equal(actions);
                fixture.Main.ResponseWindowPosition.Should().Be(position);
                fixture.Voice.StartCalls.Should().Be(capture);
                fixture.SilentSpeech.AdmittedRequests.Should().Be(speech);
                window.ShowActivated.Should().BeFalse();
                window.IsExpanded.Should().BeFalse();
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public async Task EntireLongResponseAndControlsCanBeReadWithKeyboardWithoutChangingSourceOrDurableState()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var fixture = new NativeUxFixtureSession(ScratchParent(), silentCaption: true);
            await fixture.InitializeAsync();
            var original = string.Join("\n", Enumerable.Range(1, 80).Select(index => $"Exact native response line {index}"));
            fixture.Main.ReportHostInteractionFailure(original);
            fixture.Main.CommandText = "Unsent input";
            var window = new ResponseWindow(fixture.Main, null, null, () => new(-600, 20, 320, 400));
            try
            {
                var storeBytes = await ReadStoreBytes(fixture);
                var capture = fixture.Voice.StartCalls;
                var speech = fixture.SilentSpeech!.AdmittedRequests;
                window.ShowResponse();
                window.UpdateLayout();
                window.IsActive.Should().BeFalse("fitting and passive reading must not activate");
                var reader = window.FindControl<TextBlock>("ResponseBodyText")!;
                reader.Text.Should().Be(original);
                var body = window.FindControl<ScrollViewer>("ResponseBodyViewport")!;
                body.BringIntoView();
                window.UpdateLayout();
                body.Focus().Should().BeTrue();
                var pages = (int)Math.Ceiling(body.Extent.Height / body.Viewport.Height);
                for (var index = 0; index < pages; index++)
                {
                    window.KeyPress(Key.PageDown, RawInputModifiers.None, PhysicalKey.PageDown, "PageDown");
                }
                window.UpdateLayout();
                body.Offset.Y.Should().Be(body.Extent.Height - body.Viewport.Height);
                var end = reader.TranslatePoint(new Point(0, reader.Bounds.Height), body)!.Value;
                end.Y.Should().BeLessThanOrEqualTo(body.Bounds.Height);
                body.Offset.Y.Should().BeGreaterThan(0, "the complete original, including its last line, must be scrollable");

                var viewport = window.FindControl<ScrollViewer>("ResponseViewport")!;
                var scroll = ControlAutomationPeer.CreatePeerForElement(viewport)!.GetProvider<IScrollProvider>()!;
                scroll.HorizontallyScrollable.Should().BeTrue();
                scroll.SetScrollPercent(100, 0);
                window.UpdateLayout();
                viewport.Offset.X.Should().Be(viewport.Extent.Width - viewport.Viewport.Width);
                viewport.Focus().Should().BeTrue();
                window.KeyPress(Key.PageDown, RawInputModifiers.None, PhysicalKey.PageDown, "PageDown");
                window.UpdateLayout();
                viewport.Offset.Y.Should().BeGreaterThan(0);
                var input = window.FindControl<TextBox>("CommandInput")!;
                input.BringIntoView();
                window.UpdateLayout();
                input.Focus().Should().BeTrue();
                window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, "Tab");
                window.FocusManager!.GetFocusedElement().Should().BeOfType<Button>();
                fixture.Main.CommandText.Should().Be("Unsent input");
                fixture.Main.ResponseBody.Should().Be(original);
                fixture.Voice.StartCalls.Should().Be(capture);
                fixture.SilentSpeech.AdmittedRequests.Should().Be(speech);
                (await ReadStoreBytes(fixture)).Should().Equal(storeBytes,
                    "reading, scrolling and traversing controls must not change task/grant/history/retention stores");
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public async Task UndersizedExpandedLayoutReportsReachableFallbackWithoutReplacingCurrentResponse()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var fixture = new NativeUxFixtureSession(ScratchParent(), silentCaption: true);
            await fixture.InitializeAsync();
            fixture.Main.ReportHostInteractionFailure("Original admitted response");
            var window = new ResponseWindow(fixture.Main, null, null, () => new(0, 0, 300, 280));
            try
            {
                window.ShowSessions(true, activate: false);
                window.UpdateLayout();
                window.IsExpanded.Should().BeFalse();
                window.MinWidth.Should().Be(300);
                var status = window.FindControl<TextBlock>("CompactLayoutStatus")!;
                status.Text.Should().Contain("Workbench unavailable").And.Contain("320 by 300");
                status.BringIntoView();
                window.UpdateLayout();
                AssertVerticallyInside(window, status);
                fixture.Main.ResponseBody.Should().Be("Original admitted response");
                window.FindControl<TextBox>("CommandInput")!.IsEffectivelyVisible.Should().BeTrue();
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public async Task PrivateRetirementClearsScrolledResponseAndCannotBeRevealedByOldViewportControls()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var fixture = new NativeUxFixtureSession(ScratchParent(), silentCaption: true);
            await fixture.InitializeAsync();
            fixture.Main.DefaultResponseMode = ResponseOutputMode.VisualOnly;
            fixture.Main.CommandText = "what power action is pending";
            await fixture.Main.RunTypedCommand.ExecuteAsync();
            fixture.Main.ReportHostInteractionFailure("Private original response");
            var window = new ResponseWindow(fixture.Main, null, null, () => new(0, 0, 320, 400));
            // The full App hides its desktop windows on this event; this fixture has no desktop lifetime.
            void HideForPrivacy(object? sender, EventArgs args) => window.Hide();
            fixture.Main.PrivacyClosureRequested += HideForPrivacy;
            try
            {
                window.ShowResponse();
                window.UpdateLayout();
                var reader = window.FindControl<TextBlock>("ResponseBodyText")!;
                var input = window.FindControl<TextBox>("CommandInput")!;
                input.BringIntoView();
                window.UpdateLayout();
                fixture.Main.CloseForObservedPrivacyEvent("Synthetic compact privacy retirement", hidePresentation: true);
                await fixture.Main.PrivacyClosureTask;
                Dispatcher.UIThread.RunJobs();
                window.IsVisible.Should().BeFalse();
                reader.Text.Should().NotContain("Private original response");
                reader.BringIntoView();
                input.BringIntoView();
                window.ShowSessions(false, activate: false);
                window.IsVisible.Should().BeFalse();
                fixture.Main.CanRevealPrivatePresentation.Should().BeFalse();
            }
            finally
            {
                fixture.Main.PrivacyClosureRequested -= HideForPrivacy;
                window.Close();
            }
        });
    }

    [Theory]
    [InlineData(320, true)]
    [InlineData(640, true)]
    [InlineData(1039, true)]
    [InlineData(1040, true)]
    public async Task DetailsAllowsResponsiveWorkbenchWidthsWithoutChangingSource(int width, bool fits)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var fixture = new NativeUxFixtureSession(ScratchParent(), silentCaption: true);
            await fixture.InitializeAsync();
            var window = new ResponseWindow(fixture.Main, null, null, () => new(0, 0, width, 900));
            try
            {
                window.ShowSessions(true, activate: false);
                window.PrepareDetailsLayout().Should().Be(fits);
                window.MinWidth.Should().Be(320);
                window.Width.Should().BeLessThanOrEqualTo(width);
                window.IsExpanded.Should().BeTrue();
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public async Task ExplicitCollapseRestoresCompactScrollingAfterExpandedLayout()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var fixture = new NativeUxFixtureSession(ScratchParent(), silentCaption: true);
            await fixture.InitializeAsync();
            var area = new PixelRect(0, 0, 1200, 900);
            var window = new ResponseWindow(fixture.Main, null, null, () => area);
            try
            {
                window.ShowSessions(true, activate: false);
                window.UpdateLayout();
                window.IsExpanded.Should().BeTrue();
                var viewport = window.FindControl<ScrollViewer>("ResponseViewport")!;
                viewport.VerticalScrollBarVisibility.Should().Be(Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled);
                area = new(0, 0, 320, 400);
                window.ShowSessions(false, activate: false);
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                window.IsExpanded.Should().BeFalse();
                window.Width.Should().Be(320);
                window.Height.Should().Be(400);
                window.ClientSize.Should().Be(new Size(320, 400));
                window.Bounds.Size.Should().Be(new Size(320, 400));
                viewport.VerticalScrollBarVisibility.Should().Be(Avalonia.Controls.Primitives.ScrollBarVisibility.Auto);
                var input = window.FindControl<TextBox>("CommandInput")!;
                input.BringIntoView();
                window.UpdateLayout();
                AssertInside(window, input);
            }
            finally { window.Close(); }
        });
    }

    private static Task<byte[]> ReadStoreBytes(NativeUxFixtureSession fixture) =>
        File.ReadAllBytesAsync(Path.Combine(fixture.LocalRoot, "InteractionStorageV1", "interaction.db"),
            TestContext.Current.CancellationToken);

    internal static void AssertCompactFrameFillsViewport(ResponseWindow window)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        window.ClientSize.Should().Be(new Size(window.Width, window.Height));
        var viewport = window.FindControl<ScrollViewer>("ResponseViewport")!;
        var layout = window.FindControl<Grid>("ResponseLayout")!;
        layout.Bounds.Width.Should().BeApproximately(Math.Max(480, viewport.Viewport.Width), 0.01);
        var frame = window.FindControl<Border>("CompactFrame")!;
        frame.Bounds.Size.Should().Be(layout.Bounds.Size);
        AssertCompactFrameContainsControls(window);
    }

    internal static void AssertCompactFrameContainsControls(ResponseWindow window)
    {
        var frame = window.FindControl<Border>("CompactFrame")!;
        frame.IsEffectivelyVisible.Should().BeTrue();
        frame.Margin.Should().Be(default(Thickness));
        frame.BorderThickness.Should().Be(default(Thickness));
        frame.CornerRadius.Should().Be(default(CornerRadius));
        var interior = new Rect(frame.Bounds.Size);
        var controls = window.GetVisualDescendants().OfType<Control>()
            .Where(control => control.IsEffectivelyVisible && control is Button or TextBox
                && !control.GetVisualAncestors().Contains(window.FindControl<Border>("WorkbenchCaption"))
                && !control.GetVisualAncestors().OfType<Avalonia.Controls.Primitives.ScrollBar>().Any()).ToArray();
        controls.Should().NotBeEmpty();
        foreach (var control in controls)
        {
            control.GetVisualAncestors().Should().Contain(frame, $"{control.Name} belongs inside the chat content");
            var origin = control.TranslatePoint(default, frame)!.Value;
            var visible = new Rect(origin, control.Bounds.Size);
            foreach (var clip in control.GetVisualAncestors().TakeWhile(ancestor => ancestor != frame)
                .Where(ancestor => ancestor.ClipToBounds))
            {
                var clipOrigin = clip.TranslatePoint(default, frame)!.Value;
                visible = visible.Intersect(new Rect(clipOrigin, clip.Bounds.Size));
            }
            (visible.Width <= 0 || visible.Height <= 0 || interior.Contains(visible)).Should().BeTrue(
                $"{control.Name} has visible bounds {visible} inside the frame interior {interior}");
        }
    }

    private static void AssertInside(Window window, Control control)
    {
        AssertVerticallyInside(window, control);
        var origin = control.TranslatePoint(default, window)!.Value;
        origin.X.Should().BeGreaterThanOrEqualTo(0);
        (origin.X + control.Bounds.Width).Should().BeLessThanOrEqualTo(window.Bounds.Width);
    }

    private static void AssertVerticallyInside(Window window, Control control)
    {
        var origin = control.TranslatePoint(default, window)!.Value;
        origin.Y.Should().BeGreaterThanOrEqualTo(0);
        (origin.Y + control.Bounds.Height).Should().BeLessThanOrEqualTo(window.Bounds.Height);
        control.Bounds.Width.Should().BeGreaterThan(0);
        control.Bounds.Height.Should().BeGreaterThan(0);
    }

    private static string ScratchParent()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Kora.slnx"))) { root = root.Parent; }
        return Directory.CreateDirectory(Path.Combine(
            root?.FullName ?? throw new InvalidOperationException("Repository root unavailable."),
            ".scratch", "compact-response-tests")).FullName;
    }
}