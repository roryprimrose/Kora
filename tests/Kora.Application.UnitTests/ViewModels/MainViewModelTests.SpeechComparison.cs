using AwesomeAssertions;

using Kora.Application.Presentation;
using Kora.Application.Configuration;
using Kora.Application.UnitTests.Voice;
using Kora.Application.Voice;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Platform;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Fact]
    public async Task SpeechComparisonMissingRuntimeIsExplicitAndPresentationPropertiesRemainUnavailable()
    {
        var f = new Fixture();
        f.ViewModel.SpeechComparisonPhrase.Should().BeEmpty();
        f.ViewModel.SpeechComparisonPhrase = "ignored";
        f.ViewModel.SpeechComparisonStatus.Should().Contain("unavailable");
        f.ViewModel.SpeechComparisonProfiles.Should().BeEmpty();
        f.ViewModel.SpeechComparisonResults.Should().BeEmpty();
        f.ViewModel.CanTestSpeechComparison.Should().BeFalse();
        f.ViewModel.IsSpeechComparisonRunning.Should().BeFalse();
        await FluentActions.Awaiting(() => f.ViewModel.ReviewSpeechComparisonCommand.ExecuteAsync()).Should().ThrowAsync<InvalidOperationException>();
        await f.ViewModel.DisposeAsync();
    }

    [Theory]
    [InlineData("origin")]
    [InlineData("name")]
    [InlineData("presenter")]
    [InlineData("microphone")]
    public async Task SpeechComparisonRejectsUnavailableOriginalAdmissionBeforeCapture(string reason)
    {
        var runtime = new SpeechSetupComparisonTests.Runtime();
        await using var comparison = new SpeechSetupComparison(runtime, runtime);
        var f = new Fixture(speechSetupComparison: comparison);
        f.VoiceConsent.Consent = true;
        if (!string.Equals(reason, "microphone", StringComparison.Ordinal))
        {
            f.Voice.Microphones = [new("mic", "Headset")];
            f.Voice.DefaultMicrophoneId = "mic";
        }
        if (string.Equals(reason, "name", StringComparison.Ordinal)) { f.NamePreferences.Name = "Supported Commands"; }
        await f.ViewModel.InitializeAsync();
        f.ViewModel.BindSpeechInputNativeLifetime(static () => true);
        f.ViewModel.SpeechComparisonPhrase.Should().Contain("quiet garden");
        if (string.Equals(reason, "presenter", StringComparison.Ordinal))
        {
            await FluentActions.Awaiting(() => f.RunAsync("compare speech input")).Should().ThrowAsync<InvalidOperationException>();
        }
        else
        {
            using var root = HostActivity.BeginRoot(
                HostRequest.Create(string.Equals(reason, "origin", StringComparison.Ordinal) ? RequestOrigin.HostSystem : RequestOrigin.LocalUi),
                HostActivityLayer.Application, HostOperation.Request);
            await FluentActions.Awaiting(() => f.ViewModel.ReviewSpeechComparisonCommand.ExecuteAsync()).Should().ThrowAsync<InvalidOperationException>();
        }
        runtime.Captures.Should().Be(0);
        await f.ViewModel.DisposeAsync();
    }

    [Theory]
    [InlineData("native")]
    [InlineData("typed")]
    [InlineData("activated")]
    public async Task SpeechComparisonNativeAndTypedRoutesExecuteSameInertOperationWithoutPreferenceOrRoutingEffects(string route)
    {
        var runtime = new SpeechSetupComparisonTests.Runtime();
        await using var comparison = new SpeechSetupComparison(runtime, runtime);
        var f = new Fixture(speechSetupComparison: comparison);
        f.VoiceConsent.Consent = true;
        f.Voice.Microphones = [new("mic", "Headset")];
        f.Voice.DefaultMicrophoneId = "mic";
        await f.ViewModel.InitializeAsync();
        f.ViewModel.BindSpeechInputNativeLifetime(static () => true);
        var navigation = new SettingsNavigationFake();
        f.ViewModel.BindSettingsSectionNavigation(navigation);
        var listening = f.ViewModel.IsVoiceEnabled;
        var preference = f.AudioPreferences.MicrophoneId;
        var audits = f.Audit.Events.Count;
        f.ViewModel.SpeechComparisonPhrase = "Kora confirm delete";
        runtime.Capture = (_, _, _) => Task.FromResult(new Kora.Core.Voice.SpeechComparisonObservation(
            "Kora confirm delete", TimeSpan.FromMilliseconds(10)));
        if (string.Equals(route, "typed", StringComparison.Ordinal)) { await f.RunAsync("compare speech input"); }
        else if (string.Equals(route, "activated", StringComparison.Ordinal))
        { await f.RaiseActivatedTranscriptAsync("Kora compare speech input", 1); }
        else { await f.ViewModel.ReviewSpeechComparisonCommand.ExecuteAsync(); }
        var title = f.ViewModel.ResponseTitle;
        var body = f.ViewModel.ResponseBody;
        f.ViewModel.SpeechComparisonProfiles.Should().Contain("Installed test profile");
        f.ViewModel.SpeechComparisonDisclosure.Should().Contain("Nothing is executed");
        f.ViewModel.CanTestSpeechComparison.Should().BeTrue(f.ViewModel.SpeechComparisonStatus);
        f.ViewModel.IsSpeechComparisonRunning.Should().BeFalse();
        if (string.Equals(route, "typed", StringComparison.Ordinal)) { await f.RunAsync("test speech comparison"); }
        else if (string.Equals(route, "activated", StringComparison.Ordinal))
        {
            await f.ViewModel.BeginPushToTalkAsync();
            await f.ViewModel.EndPushToTalkAsync();
            await f.Voice.RaiseTranscriptAsync("Kora test speech comparison", 1);
        }
        else { await f.ViewModel.TestSpeechComparisonCommand.ExecuteAsync(); }
        f.ViewModel.SpeechComparisonResults.Should().Contain("correct").And.Contain("10.0 ms");
        f.ViewModel.SpeechComparisonStatus.Should().Contain("complete");
        f.ViewModel.ResponseTitle.Should().Be(title);
        f.ViewModel.ResponseBody.Should().Be(body);
        f.Voice.StartCalls.Should().Be(string.Equals(route, "activated", StringComparison.Ordinal) ? 2 : 0);
        f.Reasoner.Requests.Should().BeEmpty();
        f.AudioPreferences.MicrophoneId.Should().Be(preference);
        f.ViewModel.IsVoiceEnabled.Should().Be(listening);
        f.VoiceConsent.Consent.Should().BeTrue();
        f.Audit.Events.Count.Should().Be(audits);
        if (!string.Equals(route, "native", StringComparison.Ordinal))
        { navigation.Sections.Should().Equal(SettingsSection.SpeechAudio); await f.RunAsync("cancel speech comparison"); }
        else { await f.ViewModel.CancelSpeechComparisonCommand.ExecuteAsync(); }
        f.ViewModel.SpeechComparisonResults.Should().BeEmpty();
        await f.ViewModel.DisposeAsync();
    }

    [Theory]
    [InlineData("phrase")]
    [InlineData("profile")]
    [InlineData("privacy")]
    [InlineData("close")]
    [InlineData("owner")]
    [InlineData("source")]
    public async Task SpeechComparisonCurrentReviewIsInvalidatedByRelevantInputChanges(string change)
    {
        var runtime = new SpeechSetupComparisonTests.Runtime();
        await using var comparison = new SpeechSetupComparison(runtime, runtime);
        var f = new Fixture(speechSetupComparison: comparison);
        f.VoiceConsent.Consent = true;
        f.Voice.Microphones = [new("mic", "Headset")];
        f.Voice.DefaultMicrophoneId = "mic";
        await f.ViewModel.InitializeAsync();
        f.ViewModel.BindSpeechInputNativeLifetime(static () => true);
        await f.ViewModel.ReviewSpeechComparisonCommand.ExecuteAsync();
        f.ViewModel.CanTestSpeechComparison.Should().BeTrue(f.ViewModel.SpeechComparisonStatus);
        switch (change)
        {
            case "phrase": f.ViewModel.SpeechComparisonPhrase = "Different"; break;
            case "profile": f.ViewModel.SelectedSpeechInputResource = Kora.Core.Voice.SpeechInputResource.Gpu; break;
            case "privacy":
                var previous = f.PrivacyObservation.Current;
                f.PrivacyObservation.Current = f.PrivacyObservation.Current with { SessionState = WindowsSessionState.Locked };
                f.PrivacyObservation.Publish(new(previous, f.PrivacyObservation.Current)); break;
            case "close": f.ViewModel.BindSpeechInputNativeLifetime(static () => false); break;
            case "owner": f.ViewModel.BindVoiceOwnershipGate(static () => false); break;
            default: await f.RunAsync("help"); break;
        }
        f.ViewModel.CanTestSpeechComparison.Should().BeFalse();
        f.ViewModel.SpeechComparisonResults.Should().BeEmpty();
        runtime.Acquisitions.Should().Be(0);
        await f.ViewModel.DisposeAsync();
    }
}
