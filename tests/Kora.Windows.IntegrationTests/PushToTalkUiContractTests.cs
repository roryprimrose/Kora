using System.ComponentModel;
using System.Reflection;
using System.Xml.Linq;

using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Controls;
using Kora.Application;
using Kora.NativeUxFixture;
using Kora.Windows.IntegrationTests.Storage;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class PushToTalkUiContractTests
{
    [Fact]
    public void Settings_has_no_push_to_talk_control_or_command_capture_handlers()
    {
        var root = SourceRoot();
        var settings = XDocument.Load(Path.Combine(root, "src", "Kora", "SettingsWindow.axaml"));
        settings.Descendants().Attributes().Where(attribute =>
            string.Equals(attribute.Value, "PushToTalkButton", StringComparison.Ordinal)
            || attribute.Value.Contains("BeginPushToTalk", StringComparison.Ordinal)
            || attribute.Value.Contains("EndPushToTalk", StringComparison.Ordinal)
            || attribute.Value.Contains("OnPushToTalk", StringComparison.Ordinal))
            .Should().BeEmpty();
        var codeBehind = File.ReadAllText(Path.Combine(root, "src", "Kora", "SettingsWindow.axaml.cs"));
        codeBehind.Should().NotContain("pushToTalkButton")
            .And.NotContain("OnPushToTalk")
            .And.NotContain("BeginPushToTalk")
            .And.NotContain("EndPushToTalk")
            .And.NotContain("OnCaptureSurfaceUnavailable");
    }

    [Fact]
    public async Task Settings_retains_voice_configuration_and_recovery_without_a_capture_entrypoint()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var fixture = new NativeUxFixtureSession(Path.GetTempPath());
            await fixture.InitializeAsync();
            var recoveryRequests = 0;
            var window = new SettingsWindow(fixture.Main, () => recoveryRequests++);
            try
            {
                window.Show();
                window.ShowVoiceRecovery();
                window.UpdateLayout();
                window.FindControl<Button>("PushToTalkButton").Should().BeNull();
                var buttons = window.GetLogicalDescendants().OfType<Button>().ToArray();
                buttons.Should().NotContain(button =>
                    ReferenceEquals(button.Command, fixture.Main.BeginPushToTalkCommand)
                    || ReferenceEquals(button.Command, fixture.Main.EndPushToTalkCommand));
                foreach (var command in new[]
                {
                    fixture.Main.EnableVoiceConsentCommand,
                    fixture.Main.WithdrawVoiceConsentCommand,
                    fixture.Main.ToggleListeningCommand,
                    fixture.Main.OpenMicrophonePrivacySettingsCommand,
                })
                {
                    buttons.Should().Contain(button => ReferenceEquals(button.Command, command));
                }
                buttons.Should().NotContain(button => ReferenceEquals(button.Command, fixture.Main.RefreshMicrophonesCommand));
                var microphoneActions = window.GetVisualDescendants().OfType<SettingActionsButton>().Single(button =>
                    string.Equals(button.SettingName, "Microphone", StringComparison.Ordinal));
                var microphoneMenu = (MenuFlyout)microphoneActions.Flyout!;
                microphoneMenu.ShowAt(microphoneActions);
                window.UpdateLayout();
                var refresh = microphoneMenu.Items.OfType<MenuItem>().Single();
                refresh.Command.Should().BeSameAs(fixture.Main.RefreshMicrophonesCommand);
                ControlAutomationPeer.CreatePeerForElement(refresh)!.GetName().Should().Be("Refresh microphones");
                microphoneMenu.Hide();
                var recovery = window.FindControl<Button>("ChooseMicrophone")!;
                recovery.IsEnabled.Should().BeTrue();
                recovery.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                recoveryRequests.Should().Be(1);
                fixture.Main.VoiceConsentDescription.Should().Contain("Settings only manages listening permission and readiness");
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public async Task Response_window_exposes_hold_to_talk_only_for_disabled_listening_mode()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var fixture = new NativeUxFixtureSession(Path.GetTempPath(), syntheticVoice: true);
            await fixture.InitializeAsync();
            fixture.Main.BindVoiceOwnershipGate(static () => true);
            await fixture.Main.SetVoiceConsentAsync(true);
            fixture.Main.IsVoiceEnabled.Should().BeTrue();
            var response = new ResponseWindow(fixture.Main);
            try
            {
                response.Show();
                response.Activate();
                response.UpdateLayout();
                var button = response.FindControl<Button>("PushToTalkButton")!;
                button.IsVisible.Should().BeFalse();
                fixture.Voice.StartCalls.Should().Be(0);
                await fixture.Main.ToggleListeningCommand.ExecuteAsync();
                response.UpdateLayout();
                fixture.Main.IsVoiceEnabled.Should().BeFalse();
                button.IsVisible.Should().BeTrue();
                button.IsEnabled.Should().BeTrue(fixture.Main.ListeningStatus);
                ControlAutomationPeer.CreatePeerForElement(button)!.GetName().Should().Be("Push to talk; hold while speaking");
                button.Focus().Should().BeTrue();
                response.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
                await fixture.Voice.Started.Task.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
                fixture.Voice.StartCalls.Should().Be(1);
                fixture.Main.IsListening.Should().BeTrue();
                fixture.Main.CanPushToTalk.Should().BeFalse();
                button.IsEnabled.Should().BeTrue("the held control must still receive release");
                response.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
                fixture.Voice.EndCalls.Should().Be(1);
                fixture.Voice.IsCaptureQuiescent.Should().BeTrue();
                fixture.Main.IsListening.Should().BeFalse();
            }
            finally { response.Close(); }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReviewedCommandNativeControlsCorrectBeforeNormalDispatchOrClearOnHide(bool cancel)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var time = new InteractionStorageFixture.Clock { UseFrozenTimers = true };
            var dispatcher = new DeferredCommandDispatcher();
            using var fixture = new NativeUxFixtureSession(Path.GetTempPath(), syntheticVoice: true, timeProvider: time);
            await fixture.InitializeAsync(null, dispatcher: dispatcher);
            fixture.Main.BindVoiceOwnershipGate(static () => true);
            fixture.Main.BindReviewedVoiceCommand(
                Microsoft.Extensions.Logging.Abstractions.NullLogger<Kora.Application.Voice.ReviewedVoiceCommandCapture>.Instance,
                time);
            await fixture.Main.SetVoiceConsentAsync(true);
            var response = new ResponseWindow(fixture.Main, timeProvider: time);
            try
            {
                response.Show();
                response.Activate();
                response.UpdateLayout();
                response.FindControl<Button>("BeginReviewedVoiceCommand")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await fixture.Voice.Started.Task.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
                fixture.Main.IsReviewedVoiceCommandActive.Should().BeTrue(fixture.Main.ReviewedVoiceCommandStatus);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                ExpireResponseTimeout(response, time, fixture.Main.ResponseTimeoutSeconds);
                response.IsVisible.Should().BeTrue("ordinary auto-hide must not close an owned command capture");
                fixture.Voice.IsCaptureQuiescent.Should().BeFalse();
                var previous = fixture.Main.ResponseTitle;
                fixture.Voice.PublishTranscript("Kora, stop listening");
                fixture.Main.ResponseTitle.Should().Be(previous);
                response.FindControl<Button>("FinishReviewedVoiceCommand")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { });
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                response.UpdateLayout();
                fixture.Main.HasReviewedVoiceCommand.Should().BeTrue(fixture.Main.ReviewedVoiceCommandStatus);
                var input = response.FindControl<TextBox>("ReviewedVoiceCommandInput")!;
                ControlAutomationPeer.CreatePeerForElement(input)!.GetName()
                    .Should().Be("Exact command transcript; edit without executing");
                input.Text.Should().Be("Kora, stop listening");
                input.Text = "Kora, what version are you running";
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                fixture.Main.ReviewedVoiceCommandText.Should().Be(input.Text);
                fixture.Main.ResponseTitle.Should().Be(previous);
                fixture.Main.IsVoiceEnabled.Should().BeTrue();
                ExpireResponseTimeout(response, time, fixture.Main.ResponseTimeoutSeconds);
                response.IsVisible.Should().BeTrue("ordinary auto-hide must not retire an editable reviewed command");
                fixture.Main.HasReviewedVoiceCommand.Should().BeTrue(fixture.Main.ReviewedVoiceCommandStatus);
                if (cancel)
                {
                    response.Hide();
                    await fixture.Main.CancelReviewedVoiceCommandAsync();
                    fixture.Main.ResponseTitle.Should().Be(previous);
                }
                else
                {
                    var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    void OnCompleted(object? sender, PropertyChangedEventArgs args)
                    {
                        if (!fixture.Main.IsReviewedVoiceCommandActive
                            && string.Equals(fixture.Main.ResponseTitle, "Kora version", StringComparison.Ordinal))
                        { completed.TrySetResult(); }
                    }
                    fixture.Main.PropertyChanged += OnCompleted;
                    dispatcher.PauseNext = true;
                    try
                    {
                        response.FindControl<Button>("ConfirmReviewedVoiceCommand")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        await dispatcher.Started.Task.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
                        fixture.Main.IsReviewedVoiceCommandActive.Should().BeTrue();
                        fixture.Main.HasReviewedVoiceCommand.Should().BeFalse("confirmation consumes the candidate before dispatch");
                        fixture.Main.ResponseTitle.Should().Be(previous, "a held dispatch is not a completed version query");
                        ExpireResponseTimeout(response, time, fixture.Main.ResponseTimeoutSeconds);
                        response.IsVisible.Should().BeTrue("auto-hide must not revoke the original held voice dispatch");
                        completed.Task.IsCompleted.Should().BeFalse();
                        dispatcher.Release.TrySetResult();
                        await completed.Task.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
                    }
                    finally
                    {
                        dispatcher.Release.TrySetResult();
                        if (dispatcher.Started.Task.IsCompleted)
                        { await dispatcher.Finished.Task.WaitAsync(TestContext.Current.CancellationToken); }
                        fixture.Main.PropertyChanged -= OnCompleted;
                    }
                    fixture.Main.ResponseTitle.Should().Be("Kora version");
                    fixture.Main.Transcript.Should().Be($"\u201cKora, what version are you running\u201d \u00b7 {1f:P0} confidence");
                    fixture.Main.IsReviewedVoiceCommandActive.Should().BeFalse();
                    ExpireResponseTimeout(response, time, fixture.Main.ResponseTimeoutSeconds);
                    response.IsVisible.Should().BeFalse("completed ordinary feedback resumes the configured auto-hide policy");
                }
                await fixture.Main.CancelReviewedVoiceCommandAsync();
                await fixture.Main.ResponseOutputUpdate;
                await fixture.Main.PreferenceUpdate;
                fixture.Main.ReviewedVoiceCommandText.Should().BeEmpty();
                fixture.Voice.IsCaptureQuiescent.Should().BeTrue();
                fixture.Voice.StartCalls.Should().Be(1);
                fixture.Voice.EndCalls.Should().Be(1);
                fixture.Main.IsVoiceEnabled.Should().BeTrue("review and dispatch do not change listening preferences");
            }
            finally { response.Close(); }
        });
    }

    private static void ExpireResponseTimeout(ResponseWindow response, InteractionStorageFixture.Clock time, int seconds)
    {
        time.MonotonicTicks += TimeSpan.FromSeconds(seconds + 1).Ticks;
        // Drive the legacy native timer callback with an injected clock, without waiting for dispatcher wall time.
        typeof(ResponseWindow).GetMethod("OnResponseTimeout", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(response, [null, EventArgs.Empty]);
    }

    private sealed class DeferredCommandDispatcher : IUiDispatcher
    {
        private readonly AvaloniaUiDispatcher inner = new();
        internal bool PauseNext { get; set; }
        internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Finished { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task InvokeAsync(Func<Task> action)
        {
            var held = PauseNext;
            if (held)
            {
                PauseNext = false;
                Started.TrySetResult();
                await Release.Task;
            }
            try { await inner.InvokeAsync(action); }
            finally { if (held) { Finished.TrySetResult(); } }
        }

        public void Post(Action action) => inner.Post(action);
    }

    private static string SourceRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Kora.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Source root unavailable.");
    }
}
