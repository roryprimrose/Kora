using System.Collections.Concurrent;
using System.ComponentModel;

using Avalonia.Controls;
using Avalonia.Threading;

using AwesomeAssertions;

using Kora.Application.Configuration;
using Kora.Application.Voice;
using Kora.Core.Voice;
using Kora.NativeUxFixture;
using Kora.Windows.Input;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class GlobalPushToTalkHotkeyTests
{
    [Fact]
    public async Task UnassignedRegistersNothingAndConflictsHaveNoFallback()
    {
        using var native = new Native();
        await using var hotkey = new GlobalPushToTalkHotkey(native);
        await hotkey.ConfigureAsync(GlobalPttShortcut.Unassigned);
        native.Registrations.Should().Be(0);
        native.KeyChecks.Should().Be(0);
        native.Conflict = true;
        await FluentActions.Awaiting(() => hotkey.ConfigureAsync(GlobalPttShortcut.ControlShiftSpace))
            .Should().ThrowAsync<Win32Exception>();
        native.Registrations.Should().Be(1);
        native.Registered.Should().BeFalse();
        hotkey.IsHeld(0).Should().BeFalse();
    }

    [Fact]
    public async Task RepeatsDoNotReopenAndReleaseChecksOnlyRunDuringAHold()
    {
        using var native = new Native();
        await using var hotkey = new GlobalPushToTalkHotkey(native);
        var presses = 0;
        var released = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pressed = new TaskCompletionSource<long>(TaskCreationOptions.RunContinuationsAsynchronously);
        hotkey.HoldChanged += (generation, down) =>
        {
            if (down) { Interlocked.Increment(ref presses); pressed.TrySetResult(generation); }
            else { released.TrySetResult(); }
        };
        await hotkey.ConfigureAsync(GlobalPttShortcut.ControlShiftF9);
        native.Press();
        var generation = await pressed.Task.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
        hotkey.IsHeld(generation).Should().BeTrue();
        native.Press();
        native.Release();
        await released.Task.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
        presses.Should().Be(1);
        hotkey.IsHeld(generation).Should().BeFalse();
        native.HeldWaits.Should().BeGreaterThan(0);
        await hotkey.ConfigureAsync(GlobalPttShortcut.Unassigned);
        native.Registered.Should().BeFalse();
    }

    [Theory]
    [InlineData("release")]
    [InlineData("lock")]
    [InlineData("privacy")]
    [InlineData("dispose")]
    public async Task AnotherForegroundApplicationCanHoldCaptureAndRevocationJoinsIt(string boundary)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            await using var fixture = new NativeUxFixtureSession(Environment.CurrentDirectory, syntheticVoice: true);
            await fixture.InitializeAsync();
            fixture.Main.BindVoiceOwnershipGate(static () => true);
            await fixture.Main.SetVoiceConsentAsync(true);
            var preferences = new LocalGlobalPttPreferences(fixture);
            preferences.Save(GlobalPttShortcut.ControlShiftF9);
            await using var admission = new AudioControlAdmission(fixture.Interactions, fixture.Interactions, new(fixture.Tasks));
            var configuration = new GlobalPttConfigurationService(preferences, admission, fixture.Audit);
            using var native = new Native();
            var hotkey = new GlobalPushToTalkHotkey(native);
            using var binding = new GlobalPushToTalkBinding(fixture.Main, configuration, hotkey);
            await binding.InitializeAsync();
            var otherApplication = new Window();
            otherApplication.Show();
            HeadlessSession.SetActivation(otherApplication, true);
            var pressed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            hotkey.HoldChanged += (_, down) => { if (down) { pressed.TrySetResult(); } };
            try
            {
                fixture.Main.HasDetailsQuestion.Should().BeFalse();
                native.Press();
                await pressed.Task.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
                await fixture.Voice.Started.Task.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
                fixture.Voice.StartCalls.Should().Be(1);
                fixture.Main.IsListening.Should().BeTrue();
                if (boundary is "release") { native.Release(); }
                if (boundary is "lock") { fixture.Access.SetLocked(); }
                if (boundary is "privacy") { fixture.Access.SetOpen(false); }
                if (boundary is "dispose") { binding.Dispose(); }
                await fixture.Voice.CommandEndStarted.Task.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
                await Dispatcher.UIThread.InvokeAsync(() => { });
                binding.Dispose();
                await binding.Quiescence;
                fixture.Voice.IsCaptureQuiescent.Should().BeTrue();
                fixture.Main.IsListening.Should().BeFalse();
                native.Registered.Should().BeFalse();
            }
            finally { otherApplication.Close(); }
        });
    }

    [Fact]
    public async Task NativeSettingsAndCommandConflictRemainExplicitWithoutEnablingCapture()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            await using var fixture = new NativeUxFixtureSession(Environment.CurrentDirectory, syntheticVoice: true);
            await fixture.InitializeAsync();
            await using var admission = new AudioControlAdmission(fixture.Interactions, fixture.Interactions, new(fixture.Tasks));
            var configuration = new GlobalPttConfigurationService(new(fixture), admission, fixture.Audit);
            using var native = new Native { Conflict = true };
            var hotkey = new GlobalPushToTalkHotkey(native);
            using var binding = new GlobalPushToTalkBinding(fixture.Main, configuration, hotkey);
            await binding.InitializeAsync();
            fixture.Main.BindGlobalPttNativeLifetime(static () => true);
            fixture.Main.SelectedGlobalPttShortcut = GlobalPttShortcut.ControlShiftSpace;
            await fixture.Main.SaveGlobalPttCommand.ExecuteAsync();
            fixture.Main.Transcript.Should().Contain("registration unavailable");
            fixture.Main.GlobalPttStatus.Should().Contain("No fallback");
            fixture.Main.EffectiveGlobalPttShortcut.Should().Be(GlobalPttShortcut.ControlShiftSpace);
            native.Registered.Should().BeFalse();
            fixture.Voice.StartCalls.Should().Be(0);
            fixture.Main.HasVoiceConsent.Should().BeFalse();
            binding.Dispose();
            await binding.Quiescence;
        });
    }

    [Fact]
    public async Task ReleaseDeliversTheSameActivatedTranscriptCommandWithoutChangingConsent()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            await using var fixture = new NativeUxFixtureSession(Environment.CurrentDirectory, syntheticVoice: true);
            await fixture.InitializeAsync();
            fixture.Main.BindVoiceOwnershipGate(static () => true);
            await fixture.Main.SetVoiceConsentAsync(true);
            var preferences = new LocalGlobalPttPreferences(fixture);
            preferences.Save(GlobalPttShortcut.ControlShiftF9);
            await using var admission = new AudioControlAdmission(fixture.Interactions, fixture.Interactions, new(fixture.Tasks));
            using var native = new Native();
            var hotkey = new GlobalPushToTalkHotkey(native);
            using var binding = new GlobalPushToTalkBinding(fixture.Main,
                new(preferences, admission, fixture.Audit), hotkey);
            await binding.InitializeAsync();
            native.Press();
            await fixture.Voice.Started.Task.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
            native.Release();
            await fixture.Voice.CommandEndStarted.Task.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
            await Dispatcher.UIThread.InvokeAsync(() => { });
            var dispatched = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void OnTranscript(object? sender, PropertyChangedEventArgs args)
            {
                if (string.Equals(args.PropertyName, nameof(fixture.Main.Transcript), StringComparison.Ordinal)
                    && fixture.Main.Transcript.StartsWith("Global hold-to-talk shortcut:", StringComparison.Ordinal))
                { dispatched.TrySetResult(); }
            }
            fixture.Main.PropertyChanged += OnTranscript;
            try
            {
                fixture.Voice.PublishTranscript("Kora, get global push-to-talk shortcut");
                await dispatched.Task.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
                fixture.Main.Transcript.Should().Contain("Control Shift F9");
                fixture.Main.HasVoiceConsent.Should().BeTrue();
                fixture.Voice.StartCalls.Should().Be(1);
                fixture.Voice.IsCaptureQuiescent.Should().BeTrue();
            }
            finally
            {
                fixture.Main.PropertyChanged -= OnTranscript;
                binding.Dispose();
                await binding.Quiescence;
            }
        });
    }

    [Fact]
    public async Task DisposalWaitsForCaptureReleaseRatherThanOnlyCancellingIt()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            await using var fixture = new NativeUxFixtureSession(Environment.CurrentDirectory, syntheticVoice: true);
            await fixture.InitializeAsync();
            fixture.Main.BindVoiceOwnershipGate(static () => true);
            await fixture.Main.SetVoiceConsentAsync(true);
            var preferences = new LocalGlobalPttPreferences(fixture);
            preferences.Save(GlobalPttShortcut.ControlShiftSpace);
            await using var admission = new AudioControlAdmission(fixture.Interactions, fixture.Interactions, new(fixture.Tasks));
            using var native = new Native();
            var hotkey = new GlobalPushToTalkHotkey(native);
            using var binding = new GlobalPushToTalkBinding(fixture.Main,
                new(preferences, admission, fixture.Audit), hotkey);
            await binding.InitializeAsync();
            native.Press();
            await fixture.Voice.Started.Task.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
            var end = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            fixture.Voice.PendingCommandEnd = end;
            binding.Dispose();
            await fixture.Voice.CommandEndStarted.Task.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
            binding.Quiescence.IsCompleted.Should().BeFalse();
            end.SetResult();
            await binding.Quiescence;
            fixture.Voice.IsCaptureQuiescent.Should().BeTrue();
            native.Registered.Should().BeFalse();
        });
    }

    private sealed class Native : IGlobalPttNative, IDisposable
    {
        private readonly BlockingCollection<GlobalPttNativeMessage> messages = new();
        private int down;
        private int registered;
        private int registrations;
        private int keyChecks;
        private int heldWaits;
        internal bool Conflict { get; set; }
        internal bool Registered => Volatile.Read(ref registered) != 0;
        internal int Registrations => Volatile.Read(ref registrations);
        internal int KeyChecks => Volatile.Read(ref keyChecks);
        internal int HeldWaits => Volatile.Read(ref heldWaits);
        public uint Initialize() => 1;
        public void Wake(uint threadId) => messages.Add(GlobalPttNativeMessage.Wake);
        public void Register(GlobalPttShortcut shortcut)
        {
            Interlocked.Increment(ref registrations);
            if (Conflict) { throw new Win32Exception(1409, "Chord already registered."); }
            Volatile.Write(ref registered, 1);
        }
        public void Unregister() => Volatile.Write(ref registered, 0);
        public GlobalPttNativeMessage Wait(bool held)
        {
            if (held) { Interlocked.Increment(ref heldWaits); }
            return messages.Take();
        }
        public bool IsHeld(GlobalPttShortcut shortcut)
        {
            Interlocked.Increment(ref keyChecks);
            return Volatile.Read(ref down) != 0;
        }
        internal void Press()
        {
            Volatile.Write(ref down, 1);
            messages.Add(GlobalPttNativeMessage.Press);
        }
        internal void Release()
        {
            Volatile.Write(ref down, 0);
            messages.Add(GlobalPttNativeMessage.CheckRelease);
        }
        public void Dispose() => messages.Dispose();
    }
}
