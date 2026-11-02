using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;

using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Application.Voice;
using Kora.Core.Voice;
using Kora.Core.Communication;
using Kora.Core.Commands;
using Kora.Core.Dependencies;
using Kora.NativeUxFixture;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class ForegroundPttUiContractTests
{
    private const RawInputModifiers Chord = RawInputModifiers.Control | RawInputModifiers.Shift;

    [Theory]
    [InlineData(Key.Space)]
    [InlineData(Key.F9)]
    public async Task ForegroundShortcutUsesOneExistingCaptureAndOnlyItsMatchingRelease(Key key)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var fixture = new NativeUxFixtureSession(Path.GetTempPath(), syntheticVoice: true);
            await using var admission = new AudioControlAdmission(fixture.Interactions, fixture.Interactions, new(fixture.Tasks));
            var configuration = new ForegroundPttConfigurationService(new(fixture), admission, fixture.Audit);
            await fixture.InitializeAsync(null, foregroundPtt: configuration);
            await SaveShortcut(fixture, key == Key.Space ? ForegroundPttShortcut.ControlShiftSpace : ForegroundPttShortcut.ControlShiftF9);
            fixture.Main.BindVoiceOwnershipGate(static () => true);
            await fixture.Main.SetVoiceConsentAsync(true);
            await fixture.Main.ToggleListeningCommand.ExecuteAsync();
            var response = new ResponseWindow(fixture.Main);
            try
            {
                response.Show(); await Activate(response); response.UpdateLayout();
                response.FindControl<Button>("ExpandSessions")!.Focus().Should().BeTrue();
                response.IsActive.Should().BeTrue();
                Press(response, key);
                await fixture.Voice.Started.Task.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
                Press(response, key);
                Release(response, Key.Enter);
                Release(response, Key.LeftCtrl);
                fixture.Voice.StartCalls.Should().Be(1);
                fixture.Voice.EndCalls.Should().Be(0);
                fixture.Main.IsListening.Should().BeTrue();
                Release(response, key);
                fixture.Voice.EndCalls.Should().Be(1);
                var dispatched = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                fixture.Main.PropertyChanged += (_, args) =>
                {
                    if (string.Equals(args.PropertyName, nameof(fixture.Main.ResponseTitle), StringComparison.Ordinal)
                        && string.Equals(fixture.Main.ResponseTitle, "Built-in commands are ready.", StringComparison.Ordinal))
                    { dispatched.TrySetResult(); }
                };
                fixture.Voice.PublishTranscript("Kora, what can you do");
                await dispatched.Task.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
                fixture.Voice.StartCalls.Should().Be(1);
                fixture.Voice.IsCaptureQuiescent.Should().BeTrue();
                fixture.Main.IsListening.Should().BeFalse();
                Release(response, key);
                fixture.Voice.EndCalls.Should().Be(1);
            }
            finally { response.Close(); }
        });
    }

    [Theory]
    [InlineData("unassigned")]
    [InlineData("editor")]
    [InlineData("background")]
    [InlineData("mode")]
    [InlineData("consent")]
    [InlineData("owner")]
    [InlineData("lock")]
    [InlineData("call")]
    public async Task DeniedShortcutDoesNotCaptureOrConsumeOrdinaryInput(string boundary)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var fixture = new NativeUxFixtureSession(Path.GetTempPath(), syntheticVoice: true);
            await using var admission = new AudioControlAdmission(fixture.Interactions, fixture.Interactions, new(fixture.Tasks));
            var configuration = new ForegroundPttConfigurationService(new(fixture), admission, fixture.Audit);
            await fixture.InitializeAsync(null, foregroundPtt: configuration,
                callState: new FixtureBoundaries.FixtureCallState(boundary is "call" ? CallState.Active : CallState.Clear));
            if (boundary is not "unassigned") { await SaveShortcut(fixture, ForegroundPttShortcut.ControlShiftSpace); }
            fixture.Main.BindVoiceOwnershipGate(() => boundary is not "owner");
            if (boundary is not "consent" and not "owner")
            {
                await fixture.Main.SetVoiceConsentAsync(true);
                if (boundary is not "mode" and not "call") { await fixture.Main.ToggleListeningCommand.ExecuteAsync(); }
            }
            if (boundary is "lock") { fixture.Access.SetOpen(false); }
            var response = new ResponseWindow(fixture.Main);
            var other = new Window();
            try
            {
                response.Show(); await Activate(response); response.UpdateLayout();
                if (boundary is "background")
                {
                    Deactivate(response);
                    other.Show(); await Activate(other);
                    response.IsActive.Should().BeFalse();
                }
                if (boundary is "editor")
                {
                    var input = response.FindControl<TextBox>("CommandInput")!;
                    input.Focus().Should().BeTrue();
                    input.SelectAll();
                    response.KeyTextInput("ordinary input");
                    fixture.Main.CommandText.Should().Be("ordinary input");
                }
                var args = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Space,
                    KeyModifiers = KeyModifiers.Control | KeyModifiers.Shift };
                response.RaiseEvent(args);
                args.Handled.Should().BeFalse();
                Release(response, Key.Space);
                fixture.Voice.StartCalls.Should().Be(0);
                fixture.Voice.EndCalls.Should().Be(0);
            }
            finally { other.Close(); response.Close(); }
        });
    }

    [Theory]
    [InlineData(false, Key.Space)]
    [InlineData(false, Key.Enter)]
    [InlineData(true, Key.Space)]
    [InlineData(true, Key.Enter)]
    public async Task PendingInteractionDeniesShortcutButPreservesExplicitMicrophoneButton(bool approval, Key key)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var fixture = new NativeUxFixtureSession(Path.GetTempPath(), syntheticVoice: true);
            await using var admission = new AudioControlAdmission(fixture.Interactions, fixture.Interactions, new(fixture.Tasks));
            var configuration = new ForegroundPttConfigurationService(new(fixture), admission, fixture.Audit);
            var reasoner = new PendingReasoner(approval);
            await fixture.InitializeAsync(null, foregroundPtt: configuration, modelReasoner: reasoner, modelProbe: reasoner);
            await SaveShortcut(fixture, ForegroundPttShortcut.ControlShiftSpace);
            fixture.Main.BindVoiceOwnershipGate(static () => true);
            await fixture.Main.SetVoiceConsentAsync(true);
            await fixture.Main.ToggleListeningCommand.ExecuteAsync();
            fixture.Main.CommandText = "please secure this workstation";
            await fixture.Main.RunTypedCommand.ExecuteAsync();
            await fixture.Main.ActiveReasoningTask!;
            Dispatcher.UIThread.RunJobs();
            fixture.Main.IsResponseInteractionPending.Should().BeTrue();
            var response = new ResponseWindow(fixture.Main);
            try
            {
                response.Show(); await Activate(response); response.UpdateLayout();
                var button = response.FindControl<Button>("PushToTalkButton")!;
                button.Focus().Should().BeTrue();
                Press(response, Key.Space); Release(response, Key.Space);
                fixture.Voice.StartCalls.Should().Be(0);
                response.KeyPress(key, RawInputModifiers.None,
                    key == Key.Space ? PhysicalKey.Space : PhysicalKey.Enter, null);
                await fixture.Voice.Started.Task.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
                fixture.Main.IsListening.Should().BeTrue("the button retains its original question/approval command route");
                fixture.Main.IsResponseInteractionPending.Should().BeTrue();
                Release(response, key);
                fixture.Voice.EndCalls.Should().Be(1);
                fixture.Voice.IsCaptureQuiescent.Should().BeTrue();
                fixture.Main.IsModelActionApprovalPending.Should().Be(approval);
                fixture.Main.IsModelQuestionPending.Should().Be(!approval);
            }
            finally { response.Close(); }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationAndDisposalKeepTheHoldSlotUntilActualRelease(bool opening)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var fixture = new NativeUxFixtureSession(Path.GetTempPath(), syntheticVoice: true);
            await fixture.InitializeAsync();
            fixture.Main.BindVoiceOwnershipGate(static () => true);
            await fixture.Main.SetVoiceConsentAsync(true);
            await fixture.Main.ToggleListeningCommand.ExecuteAsync();
            var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            if (opening) { fixture.Voice.PendingLiteralOpen = pending; }
            else { fixture.Voice.PendingCommandEnd = pending; }
            var button = new Button();
            var window = new Window { Content = button };
            using var binding = new PushToTalkButtonBinding(window, button, fixture.Main);
            try
            {
                window.Show(); await Activate(window); button.Focus().Should().BeTrue();
                window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
                await fixture.Voice.Started.Task.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
                Release(window, Key.Space);
                if (!opening)
                { await fixture.Voice.CommandEndStarted.Task.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken); }
                binding.Quiescence.IsCompleted.Should().BeFalse("physical capture has not joined");
                window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                fixture.Voice.StartCalls.Should().Be(1);
                binding.Dispose();
                binding.Quiescence.IsCompleted.Should().BeFalse();
                pending.TrySetResult();
                await binding.Quiescence.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
                fixture.Voice.IsCaptureQuiescent.Should().BeTrue();
                fixture.Voice.StartCalls.Should().Be(1);
            }
            finally { pending.TrySetResult(); window.Close(); }
        });
    }

    [Theory]
    [InlineData("deactivate")]
    [InlineData("hide")]
    [InlineData("close")]
    [InlineData("focus")]
    [InlineData("replacement")]
    [InlineData("withdraw")]
    public async Task OwnedHoldReleasesOnLifecycleLossWithoutRepeatReopening(string boundary)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var fixture = new NativeUxFixtureSession(Path.GetTempPath(), syntheticVoice: true);
            await using var admission = new AudioControlAdmission(fixture.Interactions, fixture.Interactions, new(fixture.Tasks));
            var configuration = new ForegroundPttConfigurationService(new(fixture), admission, fixture.Audit);
            await fixture.InitializeAsync(null, foregroundPtt: configuration);
            await SaveShortcut(fixture, ForegroundPttShortcut.ControlShiftSpace);
            fixture.Main.BindVoiceOwnershipGate(static () => true);
            await fixture.Main.SetVoiceConsentAsync(true);
            await fixture.Main.ToggleListeningCommand.ExecuteAsync();
            var response = new ResponseWindow(fixture.Main);
            var other = new Window();
            try
            {
                response.Show(); await Activate(response); response.UpdateLayout();
                Press(response, Key.Space);
                await fixture.Voice.Started.Task.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
                switch (boundary)
                {
                    case "deactivate":
                        Deactivate(response);
                        other.Show(); await Activate(other);
                        response.IsActive.Should().BeFalse();
                        break;
                    case "hide": response.Hide(); break;
                    case "close": response.Close(); break;
                    case "focus": response.FindControl<Button>("ExpandSessions")!.Focus(); break;
                    case "replacement": await SaveShortcut(fixture, ForegroundPttShortcut.ControlShiftF9); break;
                    case "withdraw": await fixture.Main.SetVoiceConsentAsync(false); break;
                }
                Dispatcher.UIThread.RunJobs();
                fixture.Voice.IsCaptureQuiescent.Should().BeTrue();
                if (boundary is not "close") { Press(response, Key.Space); }
                fixture.Voice.StartCalls.Should().Be(1);
                if (boundary is not "close") { Release(response, Key.Space); }
            }
            finally { other.Close(); response.Close(); }
        });
    }

    [Fact]
    public async Task NativeShortcutSelectionIsAccessibleAndNeverOpensCapture()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var fixture = new NativeUxFixtureSession(Path.GetTempPath(), syntheticVoice: true);
            await using var admission = new AudioControlAdmission(fixture.Interactions, fixture.Interactions, new(fixture.Tasks));
            var configuration = new ForegroundPttConfigurationService(new(fixture), admission, fixture.Audit);
            await fixture.InitializeAsync(null, foregroundPtt: configuration);
            fixture.Main.BindForegroundPttNativeLifetime(static () => true);
            var settings = new SettingsWindow(fixture.Main);
            try
            {
                settings.Show(); settings.ShowVoiceRecovery(); settings.UpdateLayout();
                var choice = settings.FindControl<ComboBox>("ForegroundPttShortcutChoice")!;
                ControlAutomationPeer.CreatePeerForElement(choice)!.GetName().Should().Be("Foreground push-to-talk shortcut");
                choice.SelectedItem = ForegroundPttShortcut.ControlShiftSpace;
                await fixture.Main.SaveForegroundPttCommand.ExecuteAsync();
                fixture.Main.EffectiveForegroundPttShortcut.Should().Be(ForegroundPttShortcut.ControlShiftSpace);
                choice.SelectedItem = ForegroundPttShortcut.ControlShiftF9;
                await fixture.Main.CancelForegroundPttCommand.ExecuteAsync();
                fixture.Main.SelectedForegroundPttShortcut.Should().Be(ForegroundPttShortcut.ControlShiftSpace);
                fixture.Voice.StartCalls.Should().Be(0);
                fixture.Main.HasVoiceConsent.Should().BeFalse();
            }
            finally { settings.Close(); }
        });
    }

    private static void Press(Window window, Key key) => window.KeyPress(key, Chord,
        key == Key.Space ? PhysicalKey.Space : PhysicalKey.F9, null);
    private sealed class PendingReasoner(bool approval) : ILocalModelReasoner, ISetupDependencyProbe
    {
        public string TaskId => "local.inference";
        public string TaskName => "Synthetic pending interaction";
        public ValueTask<DependencyStatus> ProbeAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(new DependencyStatus(TaskId, TaskName, DependencyReadiness.Ready, "Synthetic implementation seam only."));
        public Task<LocalModelResponse> ReasonAsync(string request, LocalModelContext context, LocalModelArtifact? artifact,
            CancellationToken cancellationToken) => Task.FromResult(approval
                ? new LocalModelResponse(null, BuiltInAction.EnableHostedModels)
                : new LocalModelResponse(null, null, Question: new("Which target?", ["One", "Two"])));
    }

    private static void Release(Window window, Key key) => window.KeyRelease(key, RawInputModifiers.None,
        key == Key.Space ? PhysicalKey.Space : key == Key.F9 ? PhysicalKey.F9 : PhysicalKey.None, null);
    private static void Deactivate(Window window)
    {
        // The native callback is an internal Avalonia interface member; invoke that seam, not window or authority fields.
        var callback = typeof(Avalonia.Platform.IWindowBaseImpl).GetProperty("Deactivated",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
        callback.Should().NotBeNull();
        callback!.GetValue(window.PlatformImpl).Should().BeOfType<Action>().Which.Invoke();
    }

    private static async Task Activate(Window window)
    {
        var active = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Changed(object? sender, AvaloniaPropertyChangedEventArgs args)
        {
            if (args.Property == Window.IsActiveProperty && window.IsActive) { active.TrySetResult(); }
        }
        window.PropertyChanged += Changed;
        try
        {
            window.Activate();
            if (!window.IsActive) { await active.Task.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken); }
        }
        finally { window.PropertyChanged -= Changed; }
    }
    private static async Task SaveShortcut(NativeUxFixtureSession fixture, ForegroundPttShortcut shortcut)
    {
        fixture.Main.BindForegroundPttNativeLifetime(static () => true);
        fixture.Main.SelectedForegroundPttShortcut = shortcut;
        await fixture.Main.SaveForegroundPttCommand.ExecuteAsync();
        fixture.Main.EffectiveForegroundPttShortcut.Should().Be(shortcut, fixture.Main.Transcript);
    }
}
