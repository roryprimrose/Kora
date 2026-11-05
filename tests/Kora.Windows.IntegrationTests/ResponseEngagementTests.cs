using System.Reflection;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;

using AwesomeAssertions;

using Kora.Application.Visuals;
using Kora.NativeUxFixture;
using Kora.Windows.IntegrationTests.Storage;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class ResponseEngagementTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ActiveKeyboardReadingPausesAutoHideAndLeavingStartsAFullPeriod(bool deactivate)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var time = new InteractionStorageFixture.Clock { UseFrozenTimers = true };
            using var fixture = new NativeUxFixtureSession(Path.GetFullPath("."), syntheticVoice: true,
                timeProvider: time);
            await fixture.InitializeAsync();
            fixture.Main.CommandText = "what power action is pending";
            await fixture.Main.RunTypedCommand.ExecuteAsync();
            var window = new ResponseWindow(fixture.Main, timeProvider: time);
            try
            {
                window.ShowResponse();
                window.UpdateLayout();
                window.MouseMove(new Point(-10, -10));
                HeadlessSession.SetActivation(window, true);
                var input = window.FindControl<TextBox>("CommandInput")!;
                input.Focus().Should().BeTrue();
                input.Text = "unsent keyboard input";
                Dispatcher.UIThread.RunJobs();
                var body = fixture.Main.ResponseBody;
                var title = fixture.Main.ResponseTitle;
                Timeout(window).IsScheduled.Should().BeFalse("active keyboard reading is not inactivity");

                Advance(time, fixture.Main.ResponseTimeoutSeconds * 3);
                Expire(window);
                window.IsVisible.Should().BeTrue();
                input.IsFocused.Should().BeTrue();
                window.FindControl<Button>("ExpandSessions")!.Focus().Should().BeTrue();
                Timeout(window).IsScheduled.Should().BeFalse("moving focus inside the same response is still engagement");

                if (deactivate) { HeadlessSession.SetActivation(window, false); }
                else { window.FocusManager!.Focus(null); }
                Timeout(window).IsScheduled.Should().BeTrue();
                Advance(time, fixture.Main.ResponseTimeoutSeconds - 0.1);
                Expire(window);
                window.IsVisible.Should().BeTrue("the old expired deadline must not be reused");
                Advance(time, 0.1);
                Expire(window);
                window.IsVisible.Should().BeFalse();
                fixture.Main.CommandText.Should().Be("unsent keyboard input");
                fixture.Main.ResponseBody.Should().Be(body);
                fixture.Main.ResponseTitle.Should().Be(title);
                fixture.Voice.StartCalls.Should().Be(0);
                fixture.Voice.EndCalls.Should().Be(0);
                fixture.Main.IsSpeaking.Should().BeFalse();
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public async Task StationaryPointerPausesAutoHideWithoutActivatingAndLeavingStartsAFullPeriod()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var time = new InteractionStorageFixture.Clock { UseFrozenTimers = true };
            using var fixture = new NativeUxFixtureSession(Path.GetFullPath("."), syntheticVoice: true,
                timeProvider: time);
            await fixture.InitializeAsync();
            var window = new ResponseWindow(fixture.Main, timeProvider: time);
            try
            {
                window.ShowResponse();
                window.UpdateLayout();
                HeadlessSession.SetActivation(window, false);
                window.MouseMove(new Point(40, 40));
                window.IsPointerOver.Should().BeTrue();
                window.IsActive.Should().BeFalse();
                Timeout(window).IsScheduled.Should().BeFalse();
                Advance(time, fixture.Main.ResponseTimeoutSeconds * 3);
                Expire(window);
                window.IsVisible.Should().BeTrue("a stationary pointer must not need repeated movement to keep reading");
                window.IsActive.Should().BeFalse("pointer reading must not activate passive status");

                window.MouseMove(new Point(-10, -10));
                window.IsPointerOver.Should().BeFalse();
                Timeout(window).IsScheduled.Should().BeTrue();
                Advance(time, fixture.Main.ResponseTimeoutSeconds - 0.1);
                Expire(window);
                window.IsVisible.Should().BeTrue();
                Advance(time, 0.1);
                Expire(window);
                window.IsVisible.Should().BeFalse();
                fixture.Voice.StartCalls.Should().Be(0);
                fixture.Voice.EndCalls.Should().Be(0);
            }
            finally { window.Close(); }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EngagementNeverPreventsExplicitHideOrPrivacyRetirement(bool privacy)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var time = new InteractionStorageFixture.Clock { UseFrozenTimers = true };
            using var fixture = new NativeUxFixtureSession(Path.GetFullPath("."), syntheticVoice: true,
                timeProvider: time);
            await fixture.InitializeAsync();
            fixture.Main.CommandText = "what power action is pending";
            await fixture.Main.RunTypedCommand.ExecuteAsync();
            var window = new ResponseWindow(fixture.Main, timeProvider: time);
            // Match App's private-window retirement without a real desktop application lifetime.
            void PrivacyClosed(object? sender, EventArgs args) => window.Hide();
            fixture.Main.PrivacyClosureRequested += PrivacyClosed;
            try
            {
                window.ShowResponse();
                window.UpdateLayout();
                HeadlessSession.SetActivation(window, true);
                window.FindControl<TextBox>("CommandInput")!.Focus().Should().BeTrue();
                window.MouseMove(new Point(40, 40));
                if (privacy)
                {
                    fixture.Access.SetOpen(false);
                    await fixture.Main.PrivacyClosureTask;
                    fixture.Main.CanRevealPrivatePresentation.Should().BeFalse();
                }
                else { window.HideResponse(); }
                Dispatcher.UIThread.RunJobs();
                window.IsVisible.Should().BeFalse();
                Timeout(window).IsScheduled.Should().BeFalse();
                Advance(time, fixture.Main.ResponseTimeoutSeconds * 3);
                Expire(window);
                window.IsVisible.Should().BeFalse("late timeout callbacks cannot re-open retired presentation");
                fixture.Voice.StartCalls.Should().Be(0);
            }
            finally
            {
                fixture.Main.PrivacyClosureRequested -= PrivacyClosed;
                window.Close();
            }
        });
    }

    [Fact]
    public async Task LeavingEngagementDoesNotStartATimeoutForUnacknowledgedFailure()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var time = new InteractionStorageFixture.Clock { UseFrozenTimers = true };
            using var fixture = new NativeUxFixtureSession(Path.GetFullPath("."), timeProvider: time);
            await fixture.InitializeAsync();
            var window = new ResponseWindow(fixture.Main, timeProvider: time);
            try
            {
                window.ShowResponse();
                window.UpdateLayout();
                HeadlessSession.SetActivation(window, true);
                window.FindControl<TextBox>("CommandInput")!.Focus().Should().BeTrue();
                fixture.Main.ReportHostInteractionFailure("Synthetic readable failure.");
                HeadlessSession.SetActivation(window, false);
                window.FocusManager!.Focus(null);
                window.MouseMove(new Point(-10, -10));
                Timeout(window).IsScheduled.Should().BeFalse();
                Advance(time, fixture.Main.ResponseTimeoutSeconds * 3);
                Expire(window);
                window.IsVisible.Should().BeTrue();
                fixture.Main.ResponseBody.Should().Be("Synthetic readable failure.");
            }
            finally { window.Close(); }
        });
    }

    private static void Advance(InteractionStorageFixture.Clock time, double seconds) =>
        time.MonotonicTicks += TimeSpan.FromSeconds(seconds).Ticks;

    // Drive the legacy dispatcher timer with its injected clock instead of waiting for wall time.
    private static void Expire(ResponseWindow window) =>
        typeof(ResponseWindow).GetMethod("OnResponseTimeout", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(window, [null, EventArgs.Empty]);

    private static PresentationInactivityTimeout Timeout(ResponseWindow window) =>
        (PresentationInactivityTimeout)typeof(ResponseWindow).GetField("responseInactivity",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
}
