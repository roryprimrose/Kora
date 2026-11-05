using AwesomeAssertions;
using Kora.Application.ViewModels;
using Kora.Core.Platform;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Theory]
    [InlineData("show")]
    [InlineData("settings")]
    [InlineData("sessions")]
    public async Task PrivatePresentationRecoveryRequiresANewGestureAfterActualJoinedRetirement(string route)
    {
        var fixture = CreateVoicePrivacyFixture();
        await fixture.ViewModel.InitializeAsync();
        fixture.TextToSpeech.StopGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var opened = 0;
        fixture.ViewModel.WindowActionRequested += (_, action) => { if (action == WindowAction.Show) { opened++; } };
        fixture.ViewModel.SettingsRequested += (_, _) => opened++;
        fixture.ViewModel.SessionsRequested += (_, _) => opened++;
        void Recover()
        {
            if (route is "show") { fixture.ViewModel.ShowApplication(); }
            else if (route is "settings") { fixture.ViewModel.ShowSettings(); }
            else { fixture.ViewModel.ShowSessions(); }
        }

        fixture.ViewModel.CloseForObservedPrivacyEvent("Synthetic private retirement", true);
        Recover();
        fixture.ViewModel.IsPrivacyPresentationHeld.Should().BeTrue();
        opened.Should().Be(0);
        fixture.TextToSpeech.StopGate.SetResult();
        await fixture.ViewModel.PrivacyClosureTask;
        fixture.ViewModel.IsPrivacyPresentationHeld.Should().BeTrue();
        opened.Should().Be(0, "completion must not replay the denied earlier gesture");
        Recover();
        fixture.ViewModel.IsPrivacyPresentationHeld.Should().BeFalse();
        opened.Should().Be(1);
        fixture.Voice.IsListening.Should().BeFalse();
        fixture.Session.LockCalls.Should().Be(0);
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("unknown")]
    [InlineData("failed-retirement")]
    [InlineData("capture-held")]
    [InlineData("output-held")]
    public async Task PrivatePresentationRecoveryCannotReleaseAnUnavailableBoundary(string boundary)
    {
        var fixture = CreateVoicePrivacyFixture();
        await fixture.ViewModel.InitializeAsync();
        if (boundary is "failed-retirement") { fixture.TextToSpeech.StopException = new IOException("Synthetic closure failure"); }
        fixture.ViewModel.CloseForObservedPrivacyEvent("Synthetic owner loss", true);
        await fixture.ViewModel.PrivacyClosureTask;
        if (boundary is "owner") { fixture.ViewModel.BindCallOwnershipGate(static () => false); }
        if (boundary is "unknown")
        { fixture.PrivacyObservation.Current = fixture.PrivacyObservation.Current with { SessionState = WindowsSessionState.Unknown }; }
        if (boundary is "capture-held") { fixture.Voice.PreventQuiescence = true; }
        if (boundary is "output-held")
        {
            fixture.TextToSpeech.RemainSpeakingOnStop = true;
            await fixture.TextToSpeech.StopAsync(TestContext.Current.CancellationToken);
        }
        var opened = 0;
        fixture.ViewModel.WindowActionRequested += (_, action) => { if (action == WindowAction.Show) { opened++; } };
        fixture.ViewModel.SettingsRequested += (_, _) => opened++;
        fixture.ViewModel.SessionsRequested += (_, _) => opened++;
        fixture.ViewModel.ShowApplication();
        fixture.ViewModel.ShowSettings();
        fixture.ViewModel.ShowSessions();
        fixture.ViewModel.IsPrivacyPresentationHeld.Should().BeTrue();
        opened.Should().Be(0);
        fixture.Voice.IsListening.Should().BeFalse();
        fixture.Session.LockCalls.Should().Be(0);
    }

    [Fact]
    public async Task PrivatePresentationCannotRecoverFromAnOlderConcurrentRetirement()
    {
        var fixture = CreateVoicePrivacyFixture();
        await fixture.ViewModel.InitializeAsync();
        var first = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var second = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stops = 0;
        fixture.TextToSpeech.BeforeStop = () => fixture.TextToSpeech.StopGate = ++stops == 1 ? first : second;
        fixture.ViewModel.CloseForObservedPrivacyEvent("First synthetic retirement", true);
        var original = fixture.ViewModel.PrivacyClosureTask;
        fixture.ViewModel.CloseForObservedPrivacyEvent("Second synthetic retirement", true);
        first.SetResult();
        await original;
        fixture.ViewModel.ShowApplication();
        fixture.ViewModel.IsPrivacyPresentationHeld.Should().BeTrue();
        second.SetResult();
        await fixture.ViewModel.PrivacyClosureTask;
        fixture.ViewModel.IsPrivacyPresentationHeld.Should().BeTrue();
        fixture.ViewModel.ShowApplication();
        fixture.ViewModel.IsPrivacyPresentationHeld.Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PrivatePresentationRecoveryCannotRevealAfterReentrantAdmissionLoss(bool closeAgain)
    {
        var fixture = CreateVoicePrivacyFixture();
        await fixture.ViewModel.InitializeAsync();
        fixture.ViewModel.CloseForObservedPrivacyEvent("Synthetic retirement", true);
        await fixture.ViewModel.PrivacyClosureTask;
        var opened = 0;
        fixture.ViewModel.SettingsRequested += (_, _) => opened++;
        fixture.ViewModel.PropertyChanged += (_, args) =>
        {
            if (!string.Equals(args.PropertyName, nameof(MainViewModel.IsPrivacyPresentationHeld), StringComparison.Ordinal)
                || fixture.ViewModel.IsPrivacyPresentationHeld) { return; }
            if (closeAgain) { fixture.ViewModel.CloseForObservedPrivacyEvent("Reentrant retirement", true); }
            else { fixture.ViewModel.BindCallOwnershipGate(static () => false); }
        };
        fixture.ViewModel.ShowSettings();
        await fixture.ViewModel.PrivacyClosureTask;
        fixture.ViewModel.IsPrivacyPresentationHeld.Should().BeTrue();
        fixture.ViewModel.CanRevealPrivatePresentation.Should().BeFalse();
        opened.Should().Be(0);
    }

    [Fact]
    public async Task PrivatePresentationRecoveryCannotBeRequestedByBackgroundHostWork()
    {
        var fixture = CreateVoicePrivacyFixture();
        await fixture.ViewModel.InitializeAsync();
        fixture.ViewModel.CloseForObservedPrivacyEvent("Synthetic retirement", true);
        await fixture.ViewModel.PrivacyClosureTask;
        var request = new HostRequest(new(Guid.NewGuid()), new(Guid.NewGuid()), new(Guid.NewGuid()), RequestOrigin.HostSystem);
        using (var activity = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request))
        { fixture.ViewModel.ShowApplication(); }
        fixture.ViewModel.IsPrivacyPresentationHeld.Should().BeTrue();
        fixture.ViewModel.ShowApplication();
        fixture.ViewModel.IsPrivacyPresentationHeld.Should().BeFalse();
    }

    [Fact]
    public async Task PrivatePresentationRemainsRetiredAfterNormalUnlockRestoresOnlyVoiceReadiness()
    {
        var fixture = CreateVoicePrivacyFixture();
        await fixture.ViewModel.InitializeAsync();
        PublishVoiceSession(fixture, WindowsSessionState.Locked);
        await fixture.ViewModel.PrivacyClosureTask;
        PublishVoiceSession(fixture, WindowsSessionState.Unlocked);
        await fixture.ViewModel.VoiceUnlockRecoveryTask;
        fixture.ViewModel.IsVoiceEnabled.Should().BeTrue();
        fixture.ViewModel.IsListening.Should().BeFalse();
        fixture.ViewModel.IsPrivacyPresentationHeld.Should().BeTrue();
        fixture.ViewModel.ShowSessions();
        fixture.ViewModel.IsPrivacyPresentationHeld.Should().BeFalse();
        fixture.Voice.IsListening.Should().BeFalse();
    }
}
