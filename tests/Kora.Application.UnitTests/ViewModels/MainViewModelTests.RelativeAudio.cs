using System.Text.Json;
using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Core.Auditing;
using Kora.Core.Commands;
using Kora.Core.Communication;
using Kora.Core.Configuration;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Voice;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Theory]
    [InlineData("speak louder", 30, 40, false)]
    [InlineData("speak quieter", 30, 20, false)]
    [InlineData("speak faster", 0, 1, true)]
    [InlineData("speak slower", 0, -1, true)]
    public async Task RelativeTypedAndCurrentNameActivatedAliasesShareNativeAuditedPreferences(string alias, int current, int expected, bool rate)
    {
        var fixture = new Fixture(enablePlaybackVolume: true, enableWindowsSpeechRate: true);
        await using var admission = fixture.OutputAdmission;
        await fixture.ViewModel.InitializeAsync();
        if (rate)
        {
            fixture.ViewModel.SelectedWindowsSpeechRate = current;
            await fixture.ViewModel.SaveWindowsSpeechRateCommand.ExecuteAsync();
        }
        else
        {
            fixture.ViewModel.SelectedPlaybackVolume = current;
            await fixture.ViewModel.SavePlaybackVolumeCommand.ExecuteAsync();
        }
        fixture.TextToSpeech.ClearSpokenResponse();
        var microphones = fixture.Voice.StartCalls;
        fixture.Audit.Events.Clear();
        void AssertAdjustment(int previous, int next)
        {
            using var result = JsonDocument.Parse(fixture.ViewModel.ResponseBody);
            var adjustment = result.RootElement.GetProperty("adjustment");
            adjustment.GetProperty(rate ? "previousNativeRate" : "previousPercent").GetInt32().Should().Be(previous);
            adjustment.GetProperty(rate ? "currentNativeRate" : "currentPercent").GetInt32().Should().Be(next);
        }
        await fixture.RunAsync(alias);
        AssertAdjustment(current, expected);
        if (rate) { fixture.RatePreferences.Value.Should().Be(new WindowsSpeechRate(expected)); }
        else { fixture.VolumePreferences.Value.Should().Be(new PlaybackVolume(expected)); }
        fixture.Voice.StartCalls.Should().Be(microphones);
        fixture.Audit.Events.Where(item => item.Category == SecurityAuditCategory.ConfigurationWrite)
            .Select(item => item.Outcome).Should().Equal(SecurityAuditOutcome.Requested, SecurityAuditOutcome.Succeeded);
        await fixture.RunAsync("set assistant.name to Nova");
        fixture.Audit.Events.Clear();
        await fixture.RaiseActivatedTranscriptAsync("Nova, " + alias, 1);
        AssertAdjustment(expected, expected + expected - current);
        fixture.Voice.StartedPhrases.Should().Contain(alias).And.Contain("Nova " + alias).And.NotContain("Kora " + alias);
        if (rate) { fixture.RatePreferences.Value.Should().Be(new WindowsSpeechRate(expected + expected - current)); }
        else { fixture.VolumePreferences.Value.Should().Be(new PlaybackVolume(expected + expected - current)); }
        fixture.Audit.Events.Where(item => item.Category == SecurityAuditCategory.ConfigurationWrite)
            .Should().OnlyContain(item => item.Initiator == SecurityAuditInitiator.VoiceCommand);
        fixture.Reasoner.Requests.Should().BeEmpty();
        fixture.TextToSpeech.SpokenText.Should().BeNull();
        fixture.TextToSpeech.InstallProviderCalls.Should().Be(0);
        fixture.TextToSpeech.RemoveProviderCalls.Should().Be(0);
        fixture.Preferences.SavedVoiceId.Should().BeNull();
        fixture.AudioPreferences.SavedOutputDeviceId.Should().BeNull();
        fixture.ViewModel.Dispose();
    }

    [Theory]
    [InlineData("speak louder", 95, 100, "upper", false)]
    [InlineData("speak quieter", 5, 0, "lower", false)]
    [InlineData("speak faster", 9, 10, "upper", true)]
    [InlineData("speak slower", -9, -10, "lower", true)]
    public async Task RelativeBoundariesReportShortenedStepThenUnchangedWithoutSpuriousSuccess(string alias, int current, int expected, string boundary, bool rate)
    {
        var fixture = new Fixture(enablePlaybackVolume: true, enableWindowsSpeechRate: true);
        await using var admission = fixture.OutputAdmission;
        await fixture.ViewModel.InitializeAsync();
        if (rate) { fixture.RatePreferences.Value = new(current); }
        else { fixture.VolumePreferences.Value = new(current); }
        void AssertExactResult(int previous)
        {
            using var result = JsonDocument.Parse(fixture.ViewModel.ResponseBody);
            var state = result.RootElement.GetProperty("state");
            var scalar = rate ? "value" : "percent";
            state.GetProperty("desired").GetProperty(scalar).GetInt32().Should().Be(expected);
            state.GetProperty("effective").GetProperty(scalar).GetInt32().Should().Be(expected);
            state.GetProperty("source").GetString().Should().Be("saved");
            state.GetProperty("available").GetBoolean().Should().BeTrue();
            var adjustment = result.RootElement.GetProperty("adjustment");
            adjustment.GetProperty(rate ? "previousNativeRate" : "previousPercent").GetInt32().Should().Be(previous);
            adjustment.GetProperty(rate ? "currentNativeRate" : "currentPercent").GetInt32().Should().Be(expected);
        }
        fixture.TextToSpeech.ClearSpokenResponse();
        await fixture.RunAsync(alias);
        fixture.ViewModel.ResponseBody.Should().Contain("\"outcome\":\"saved-" + boundary + "-boundary\"");
        AssertExactResult(current);
        if (rate) { fixture.RatePreferences.Value.Should().Be(new WindowsSpeechRate(expected)); }
        else { fixture.VolumePreferences.Value.Should().Be(new PlaybackVolume(expected)); }
        fixture.Audit.Events.Clear();
        await fixture.RunAsync(alias);
        fixture.ViewModel.ResponseBody.Should().Contain("\"outcome\":\"unchanged-" + boundary + "-boundary\"");
        AssertExactResult(expected);
        fixture.Audit.Events.Should().NotContain(item => item.Category == SecurityAuditCategory.ConfigurationWrite);
        fixture.VolumePreferences.Pending.Should().BeFalse();
        fixture.RatePreferences.Pending.Should().BeFalse();
        fixture.TextToSpeech.SpokenText.Should().BeNull();
    }

    [Theory]
    [InlineData("speak louder", false)]
    [InlineData("speak quieter", false)]
    [InlineData("speak faster", true)]
    [InlineData("speak slower", true)]
    public async Task RelativeControlsRespectProtectedOriginalVoiceAndCannotBeRelabelled(string alias, bool rate)
    {
        var fixture = new Fixture(enablePlaybackVolume: true, enableWindowsSpeechRate: true);
        await using var admission = fixture.OutputAdmission;
        fixture.Voice.Microphones = [new("0", "Headset")];
        fixture.Voice.DefaultMicrophoneId = "0";
        await fixture.ViewModel.InitializeAsync();
        fixture.VolumePreferences.Value = new(30);
        fixture.RatePreferences.Value = new(0);
        await fixture.ViewModel.SetVoiceConsentAsync(true);
        if (!fixture.ViewModel.IsVoiceEnabled) { await fixture.ViewModel.ToggleListeningCommand.ExecuteAsync(); }
        await fixture.ViewModel.SetAllowVoiceActivationDuringCallsAsync(true);
        fixture.CallState.SetState(CallState.Active);
        await fixture.Dispatcher.LastInvocation;
        async Task Execute()
        {
            if (rate) { await fixture.ViewModel.ExecuteWindowsSpeechRateCommandAsync(WindowsSpeechRateCommand.Parse(alias, "Kora")!,
                SecurityAuditInitiator.VoiceCommand, TestContext.Current.CancellationToken); }
            else { await fixture.ViewModel.ExecutePlaybackVolumeCommandAsync(PlaybackVolumeCommand.Parse(alias, "Kora")!,
                SecurityAuditInitiator.VoiceCommand, TestContext.Current.CancellationToken); }
        }
        await Execute();
        fixture.ViewModel.ResponseBody.Should().Contain("\"outcome\":\"denied\"");
        using (var result = JsonDocument.Parse(fixture.ViewModel.ResponseBody))
        {
            var adjustment = result.RootElement.GetProperty("adjustment");
            adjustment.GetProperty(rate ? "previousNativeRate" : "previousPercent").GetInt32().Should().Be(rate ? 0 : 30);
            adjustment.GetProperty(rate ? "currentNativeRate" : "currentPercent").GetInt32().Should().Be(rate ? 0 : 30);
        }
        fixture.CallState.SetState(CallState.Unknown);
        await fixture.Dispatcher.LastInvocation;
        using (HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Application, HostOperation.Request))
        {
            await Execute();
        }
        fixture.VolumePreferences.Value.Should().Be(new PlaybackVolume(30));
        fixture.RatePreferences.Value.Should().Be(new WindowsSpeechRate(0));
        fixture.Audit.Events.Should().NotContain(item => (string.Equals(item.ActionId, "configuration.playback-volume", StringComparison.Ordinal)
            || string.Equals(item.ActionId, "configuration.windows-speech-rate", StringComparison.Ordinal))
            && item.Outcome == SecurityAuditOutcome.Succeeded);
    }

    [Theory]
    [InlineData("question")]
    [InlineData("approval")]
    public async Task RelativeControlsCannotConsumePendingQuestionOrApproval(string pending)
    {
        var fixture = new Fixture(enablePlaybackVolume: true, enableWindowsSpeechRate: true);
        await using var admission = fixture.OutputAdmission;
        fixture.Probe.Status = new("local.inference", "Local", DependencyReadiness.Ready, "ready");
        if (pending is "question") { fixture.Reasoner.Question = new LocalModelQuestion("Choose?", ["Blue", "Green"]); }
        else { fixture.Reasoner.Action = BuiltInAction.LockMachine; }
        await fixture.ViewModel.InitializeAsync();
        await fixture.RunAsync("help me choose");
        await fixture.ViewModel.ActiveReasoningTask!;
        var preview = fixture.ViewModel.ResponseBody;
        fixture.ViewModel.IsResponseInteractionPending.Should().BeTrue();
        await fixture.ViewModel.ExecutePlaybackVolumeCommandAsync(PlaybackVolumeCommand.Parse("speak louder", "Kora")!,
            SecurityAuditInitiator.TypedCommand, TestContext.Current.CancellationToken);
        await fixture.ViewModel.ExecuteWindowsSpeechRateCommandAsync(WindowsSpeechRateCommand.Parse("speak faster", "Kora")!,
            SecurityAuditInitiator.TypedCommand, TestContext.Current.CancellationToken);
        fixture.VolumePreferences.Value.Should().BeNull();
        fixture.RatePreferences.Value.Should().BeNull();
        fixture.ViewModel.ResponseBody.Should().Be(preview);
        fixture.ViewModel.IsResponseInteractionPending.Should().BeTrue();
        fixture.Session.LockCalls.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RelativeCancellationAndCorruptionNeverDefaultOrWrite(bool rate)
    {
        var fixture = new Fixture(enablePlaybackVolume: true, enableWindowsSpeechRate: true);
        await using var admission = fixture.OutputAdmission;
        await fixture.ViewModel.InitializeAsync();
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        if (rate)
        {
            await fixture.ViewModel.ExecuteWindowsSpeechRateCommandAsync(WindowsSpeechRateCommand.Parse("speak faster", "Kora")!,
                SecurityAuditInitiator.TypedCommand, cancelled.Token);
            fixture.RatePreferences.ReadFailure = new InvalidDataException("malformed rate");
            await fixture.RunAsync("speak slower");
            fixture.ViewModel.WindowsSpeechRateStatus.Should().StartWith("Setting unavailable.");
        }
        else
        {
            await fixture.ViewModel.ExecutePlaybackVolumeCommandAsync(PlaybackVolumeCommand.Parse("speak quieter", "Kora")!,
                SecurityAuditInitiator.TypedCommand, cancelled.Token);
            fixture.VolumePreferences.ReadFailure = new InvalidDataException("malformed volume");
            await fixture.RunAsync("speak louder");
            fixture.ViewModel.PlaybackVolumeStatus.Should().StartWith("Setting unavailable.");
        }
        fixture.VolumePreferences.Value.Should().BeNull();
        fixture.RatePreferences.Value.Should().BeNull();
        fixture.Audit.Events.Should().NotContain(item => item.Category == SecurityAuditCategory.ConfigurationWrite);
    }

    [Theory]
    [InlineData("name")]
    [InlineData("generation")]
    public async Task RelativeVolumeRevalidatesCurrentNameAndActivatedGenerationAtAudit(string changed)
    {
        var fixture = new Fixture(enablePlaybackVolume: true);
        await using var admission = fixture.OutputAdmission;
        fixture.Voice.Microphones = [new("0", "Headset")];
        fixture.Voice.DefaultMicrophoneId = "0";
        await fixture.ViewModel.InitializeAsync();
        fixture.VolumePreferences.Value = new(30);
        await fixture.ViewModel.SetVoiceConsentAsync(true);
        if (!fixture.ViewModel.IsVoiceEnabled) { await fixture.ViewModel.ToggleListeningCommand.ExecuteAsync(); }
        fixture.Audit.BeforeWrite = item =>
        {
            if (item.Outcome != SecurityAuditOutcome.Requested
                || !string.Equals(item.ActionId, "configuration.playback-volume", StringComparison.Ordinal)) { return; }
            if (changed is "name") { fixture.ViewModel.SetAssistantNameAsync("Nova").IsCompletedSuccessfully.Should().BeTrue(); }
            else { fixture.Voice.AdvanceGeneration(); }
        };
        await fixture.ViewModel.ExecutePlaybackVolumeCommandAsync(PlaybackVolumeCommand.Parse("speak louder", "Kora")!,
            SecurityAuditInitiator.VoiceCommand, TestContext.Current.CancellationToken);
        fixture.Audit.Events.Should().Contain(item => item.Outcome == SecurityAuditOutcome.Requested
            && string.Equals(item.ActionId, "configuration.playback-volume", StringComparison.Ordinal));
        fixture.VolumePreferences.Value.Should().Be(new PlaybackVolume(30));
    }

    [Theory]
    [InlineData("consent", false)]
    [InlineData("privacy", false)]
    [InlineData("owner", false)]
    [InlineData("locked", false)]
    [InlineData("consent", true)]
    [InlineData("privacy", true)]
    [InlineData("owner", true)]
    [InlineData("locked", true)]
    public async Task RelativeIncreasesCannotEvadeLiveConsentPrivacyOwnershipOrLock(string changed, bool rate)
    {
        var fixture = new Fixture(enablePlaybackVolume: true, enableWindowsSpeechRate: true);
        await using var admission = fixture.OutputAdmission;
        fixture.Voice.Microphones = [new("0", "Headset")];
        fixture.Voice.DefaultMicrophoneId = "0";
        await fixture.ViewModel.InitializeAsync();
        fixture.VolumePreferences.Value = new(0);
        await fixture.ViewModel.SetVoiceConsentAsync(true);
        if (!fixture.ViewModel.IsVoiceEnabled) { await fixture.ViewModel.ToggleListeningCommand.ExecuteAsync(); }
        Task? consentChange = null;
        fixture.Audit.BeforeWrite = item =>
        {
            if (item.Outcome != SecurityAuditOutcome.Requested
                || !string.Equals(item.ActionId, rate ? "configuration.windows-speech-rate" : "configuration.playback-volume", StringComparison.Ordinal)) { return; }
            if (changed is "consent") { consentChange = fixture.ViewModel.SetVoiceConsentAsync(false); }
            if (changed is "privacy") { fixture.PrivacyObservation.Current = fixture.PrivacyObservation.Current with { TopologyRevision = 100 }; }
            if (changed is "owner") { fixture.ViewModel.BindCallOwnershipGate(static () => false); }
            if (changed is "locked") { fixture.Session.IsUnlocked = false; }
        };
        if (rate)
        {
            await fixture.ViewModel.ExecuteWindowsSpeechRateCommandAsync(WindowsSpeechRateCommand.Parse("speak faster", "Kora")!,
                SecurityAuditInitiator.VoiceCommand, TestContext.Current.CancellationToken);
        }
        else
        {
            await fixture.ViewModel.ExecutePlaybackVolumeCommandAsync(PlaybackVolumeCommand.Parse("speak louder", "Kora")!,
                SecurityAuditInitiator.VoiceCommand, TestContext.Current.CancellationToken);
        }
        if (consentChange is not null) { await consentChange; }
        fixture.VolumePreferences.Value.Should().Be(new PlaybackVolume(0));
        fixture.RatePreferences.Value.Should().BeNull();
        fixture.TextToSpeech.SpokenText.Should().BeNull();
    }
}
