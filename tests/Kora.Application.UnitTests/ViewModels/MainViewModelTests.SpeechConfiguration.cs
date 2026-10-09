using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Core;
using Kora.Core.Auditing;
using Kora.Core.Communication;
using Kora.Core.Configuration;
using Kora.Core.Hosting;
using Kora.Core.Voice;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Fact]
    public async Task Installed_provider_voice_discovery_exact_commands_and_UI_share_saved_effective_state()
    {
        var fixture = new Fixture();
        fixture.TextToSpeech.Providers = [CreateWindowsProvider(), CreateKokoroProvider(true)];
        fixture.TextToSpeech.Voices = [new("female", "Windows", "en-US", SpeechVoiceGender.Female), CreateKokoroVoice("af_heart", "Heart")];
        await fixture.ViewModel.InitializeAsync();
        var notified = new HashSet<string?>(StringComparer.Ordinal);
        fixture.ViewModel.PropertyChanged += (_, args) =>
        {
            notified.Add(args.PropertyName);
            if (string.Equals(args.PropertyName, nameof(fixture.ViewModel.SelectedInstalledSpeechProvider), StringComparison.Ordinal))
            {
                fixture.ViewModel.SelectedInstalledSpeechProvider = fixture.ViewModel.SelectedInstalledSpeechProvider;
            }
        };
        await fixture.RunAsync("Kora, list speech settings");
        fixture.ViewModel.ResponseBody.Should().Contain("speech.provider").And.Contain("speech.voice")
            .And.Contain("voice-output").And.Contain("atomic-save").And.Contain("af_heart");
        fixture.ViewModel.SpeechOptions.Should().BeSameAs(SpeechOptionRegistry.Options);
        await fixture.RunAsync("set speech provider to kokoro");
        fixture.ViewModel.SelectedInstalledSpeechProvider!.Id.Should().Be(SpeechProviderIds.Kokoro);
        fixture.ViewModel.SelectedVoice!.Id.Should().Be("af_heart");
        fixture.Preferences.SavedProviderId.Should().Be(SpeechProviderIds.Kokoro);
        await fixture.RunAsync("set speech.voice to af_heart");
        fixture.Preferences.SavedVoiceId.Should().Be("af_heart");
        await fixture.RunAsync("get speech.voice");
        fixture.ViewModel.ResponseBody.Should().Contain("af_heart").And.Contain("saved").And.Contain("revision");
        await fixture.ViewModel.ResetSpeechVoiceCommand.ExecuteAsync();
        fixture.Preferences.VoiceId.Should().BeNull();
        await fixture.ViewModel.ResetSpeechProviderCommand.ExecuteAsync();
        fixture.ViewModel.SelectedVoice!.Id.Should().Be("female");
        fixture.ViewModel.SelectedInstalledSpeechProvider = fixture.ViewModel.InstalledSpeechProviders.Single(provider =>
            string.Equals(provider.Id, SpeechProviderIds.Kokoro, StringComparison.Ordinal));
        await fixture.RunAsync("reset speech.provider");
        fixture.ViewModel.SelectedVoice!.Id.Should().Be("female");
        fixture.ViewModel.SelectedInstalledSpeechProvider = null;
        notified.Should().Contain(nameof(fixture.ViewModel.SelectedInstalledSpeechProvider))
            .And.Contain(nameof(fixture.ViewModel.SpeechSettingStatus)).And.Contain(nameof(fixture.ViewModel.SelectedVoice));
        fixture.Reasoner.Requests.Should().BeEmpty();
        fixture.TextToSpeech.InstallProviderCalls.Should().Be(0);
        fixture.TextToSpeech.RemoveProviderCalls.Should().Be(0);
        fixture.ViewModel.SelectedOutputDevice!.IsSystemDefault.Should().BeTrue();
    }

    [Fact]
    public async Task Activated_voice_uses_original_channel_and_protected_calls_reject_both_set_and_reset()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        await fixture.RaiseActivatedTranscriptAsync("Kora, set speech.voice to female", 1);
        fixture.Preferences.SavedVoiceId.Should().Be("female");
        fixture.CallState.SetState(CallState.Active);
        await fixture.Dispatcher.LastInvocation;
        await fixture.RaiseActivatedTranscriptAsync("Kora, reset speech.voice", 1);
        fixture.Preferences.VoiceId.Should().Be("female");
        fixture.Audit.Events.Should().Contain(item => item.ActionId == "configuration.voice-selection"
            && item.Initiator == SecurityAuditInitiator.VoiceCommand && item.Outcome == SecurityAuditOutcome.Denied);
        await fixture.RunAsync("reset speech.voice");
        fixture.Preferences.VoiceId.Should().BeNull();
        fixture.ViewModel.IsProtectedCall.Should().BeTrue();
        fixture.ViewModel.IsVisualResponseVisible.Should().BeTrue();
        fixture.TextToSpeech.SpokenText.Should().BeNull();
    }

    [Fact]
    public async Task Uninstalled_review_is_not_selection_and_invalid_exact_choices_never_reach_reasoning_or_provisioning()
    {
        var fixture = new Fixture();
        fixture.TextToSpeech.Providers = [CreateWindowsProvider(), CreateKokoroProvider(false)];
        await fixture.ViewModel.InitializeAsync();
        fixture.ViewModel.SelectedSpeechProvider = fixture.ViewModel.SpeechProviders.Single(provider =>
            string.Equals(provider.Id, SpeechProviderIds.Kokoro, StringComparison.Ordinal));
        fixture.ViewModel.SelectedVoice!.Id.Should().Be("female");
        fixture.ViewModel.InstalledSpeechProviders.Should().ContainSingle();
        await fixture.RunAsync("set speech.provider to kokoro");
        fixture.ViewModel.State.Should().Be(AssistantState.Failure);
        fixture.ViewModel.SelectedVoice!.Id.Should().Be("female");
        await fixture.RunAsync("set speech.provider to unknown");
        fixture.ViewModel.ResponseTitle.Should().Contain("Choose");
        await fixture.RunAsync("set speech.voice to removed");
        fixture.ViewModel.ResponseTitle.Should().Contain("Choose");
        await fixture.RunAsync("set speech.voice");
        fixture.ViewModel.ResponseTitle.Should().Contain("Clarify");
        fixture.TextToSpeech.InstallProviderCalls.Should().Be(0);
        fixture.Reasoner.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Dispatch_rechecks_host_call_revision_and_disposal_and_surfaces_failed_persistence()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        fixture.Dispatcher.BeforeInvoke = () => fixture.CallState.SetState(CallState.Active);
        await fixture.RunAsync("set speech.voice to female");
        fixture.Preferences.SavedVoiceId.Should().BeNull();
        fixture.ViewModel.SpeechSettingStatus.Should().Contain("policy changed");
        fixture.Dispatcher.BeforeInvoke = null;
        fixture.Preferences.SaveException = new IOException("fixture atomic failure");
        await fixture.RunAsync("reset speech.voice");
        fixture.ViewModel.ResponseTitle.Should().Be("The speech setting was not changed.");
        fixture.ViewModel.SpeechSettingStatus.Should().Contain("fixture atomic failure");
        fixture.Preferences.SaveException = null;
        fixture.Dispatcher.BeforeInvoke = fixture.ViewModel.Dispose;
        await fixture.RunAsync("reset speech.provider");
        fixture.Preferences.SavedProviderId.Should().BeNull();
    }

    [Fact]
    public async Task Invalid_saved_state_recovery_invalid_UI_and_absent_commands_are_explicit()
    {
        var fixture = new Fixture();
        fixture.Preferences.ProviderId = "unknown";
        await fixture.ViewModel.InitializeAsync();
        await fixture.RunAsync("get speech.provider");
        fixture.ViewModel.ResponseBody.Should().Contain("invalid");
        fixture.ViewModel.SelectedVoice = new("unknown", "Unknown", "en-US", SpeechVoiceGender.Female);
        fixture.ViewModel.ResponseTitle.Should().Contain("Choose");
        await fixture.RunAsync("reset speech.voice");
        fixture.ViewModel.ResponseTitle.Should().Contain("Choose");
        await fixture.RunAsync("reset speech.provider");
        fixture.ViewModel.SelectedVoice!.Id.Should().Be("female");
        var missing = () => fixture.ViewModel.ExecuteSpeechCommandAsync(
            new(AppearanceCommandOperation.Set), SecurityAuditInitiator.TypedCommand);
        await missing.Should().ThrowAsync<InvalidOperationException>();
        await fixture.ViewModel.ExecuteSpeechCommandAsync(new(AppearanceCommandOperation.Set,
            SpeechOptionRegistry.Get(SpeechOption.Voice)), SecurityAuditInitiator.TypedCommand);
        fixture.ViewModel.ResponseTitle.Should().Contain("Choose");
        fixture.Session.IsUnlocked = false;
        await fixture.ViewModel.ExecuteSpeechCommandAsync(new(AppearanceCommandOperation.List), SecurityAuditInitiator.TypedCommand);
        fixture.ViewModel.SelectedInstalledSpeechProvider = new("unknown", "Unknown", "Unknown", true, false, null, "female");
        fixture.ViewModel.ResponseTitle.Should().Contain("Choose");
    }

    [Fact]
    public async Task UI_and_qualified_commands_can_choose_other_installed_provider_without_a_default()
    {
        var fixture = new Fixture();
        fixture.TextToSpeech.Providers = [CreateWindowsProvider(), CreateKokoroProvider(true) with { DefaultVoiceId = null }];
        fixture.TextToSpeech.Voices = [new("female", "Windows", "en-US", SpeechVoiceGender.Female), CreateKokoroVoice("foreign", "Foreign")];
        await fixture.ViewModel.InitializeAsync();
        await fixture.RunAsync("set speech.voice to kokoro / foreign");
        fixture.ViewModel.SelectedInstalledSpeechProvider!.Id.Should().Be(SpeechProviderIds.Kokoro);
        fixture.ViewModel.SelectedVoice!.Id.Should().Be("foreign");
        fixture.ViewModel.SelectedVoice = fixture.ViewModel.InstalledSpeechVoices.Single(voice =>
            string.Equals(voice.Id, "female", StringComparison.Ordinal));
        fixture.Preferences.ProviderId.Should().Be(SpeechProviderIds.Windows);
        fixture.Preferences.VoiceId.Should().Be("female");
        fixture.TextToSpeech.InstallProviderCalls.Should().Be(0);
    }
}
