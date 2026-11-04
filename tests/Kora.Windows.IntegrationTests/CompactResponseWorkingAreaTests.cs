using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Core.Voice;
using Kora.NativeUxFixture;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class CompactResponseWorkingAreaTests
{
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
                var dismiss = window.GetVisualDescendants().OfType<Button>()
                    .Single(button => string.Equals(button.Content as string, "Dismiss", StringComparison.Ordinal));
                dismiss.BringIntoView();
                window.UpdateLayout();
                AssertInside(window, dismiss);
                dismiss.Focus().Should().BeTrue();
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
            var window = new ResponseWindow(fixture.Main, null, null, () => new(0, 0, 320, 400));
            try
            {
                window.ShowSessions(true, activate: false);
                window.UpdateLayout();
                window.IsExpanded.Should().BeFalse();
                window.MinWidth.Should().Be(320);
                var status = window.FindControl<TextBlock>("CompactLayoutStatus")!;
                status.Text.Should().Contain("Sessions unavailable").And.Contain("820 by 560");
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
    [InlineData(1039, false)]
    [InlineData(1040, true)]
    public async Task SideBySideDetailsKeepsItsAuthoritativeMinimum(int width, bool fits)
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
                if (fits)
                {
                    window.MinWidth.Should().Be(1040);
                    window.Width.Should().Be(1040);
                }
                else { fixture.Main.ResponseBody.Should().Contain("minimum 1040-wide"); }
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
                window.IsExpanded.Should().BeFalse();
                window.Width.Should().Be(320);
                window.Height.Should().Be(400);
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