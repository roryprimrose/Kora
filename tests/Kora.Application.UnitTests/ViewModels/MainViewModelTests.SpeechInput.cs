using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Core.Auditing;
using Kora.Core.Voice;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Fact]
    public async Task SpeechInputNativeTypedAndActivatedRoutesShareSelectionWithoutHiddenEffects()
    {
        var fixture = new Fixture(enableSpeechInput: true);
        await using var configuration = fixture.InputConfiguration!;
        await using var admission = fixture.OutputAdmission;
        await fixture.ViewModel.InitializeAsync();
        fixture.TextToSpeech.ClearSpokenResponse();
        var starts = fixture.Voice.StartCalls;
        await fixture.RunAsync("set speech.input to windows-activated-v1 Gpu KeepLoaded");
        fixture.InputPreferences.Value!.Resource.Should().Be(SpeechInputResource.Gpu);
        fixture.ViewModel.SpeechInputStatus.Should().Contain("Saved:").And.Contain("Desired:").And.Contain("Effective: unavailable");
        fixture.ViewModel.SelectedSpeechInputResource = SpeechInputResource.WindowsManaged;
        fixture.ViewModel.SelectedSpeechInputResidency = SpeechInputResidency.CaptureLifetime;
        fixture.InputPreferences.Value.Resource.Should().Be(SpeechInputResource.Gpu);
        await fixture.ViewModel.SaveSpeechInputCommand.ExecuteAsync();
        fixture.InputPreferences.Value.Should().Be(SpeechInputSelection.Default);
        fixture.InputRuntime.ProfileGeneration.Number.Should().Be(2);
        await fixture.ViewModel.InspectSpeechInputCommand.ExecuteAsync();
        fixture.ViewModel.ResponseBody.Should().Contain("Windows activated adapter selected");
        fixture.Voice.StartCalls.Should().Be(starts);
        await fixture.RaiseActivatedTranscriptAsync("Kora, set speech.input to windows-activated-v1 WindowsManaged CaptureLifetime", 1);
        fixture.InputRuntime.ProfileGeneration.Number.Should().Be(3);
        fixture.Audit.Events.Where(item => string.Equals(item.ActionId, "configuration.speech-input-profile", StringComparison.Ordinal))
            .Should().Contain(item => item.Initiator == SecurityAuditInitiator.VoiceCommand);
        fixture.Voice.StartCalls.Should().Be(starts + 1);
        fixture.TextToSpeech.SpokenText.Should().BeNull();
        fixture.TextToSpeech.InstallProviderCalls.Should().Be(0);
        fixture.TextToSpeech.RemoveProviderCalls.Should().Be(0);
        fixture.Reasoner.Requests.Should().BeEmpty();
        fixture.ViewModel.Dispose();
    }

    [Fact]
    public async Task SpeechInputNativeLifetimeCannotBeRevivedByAnOldSettingsBinding()
    {
        var fixture = new Fixture(enableSpeechInput: true);
        await using var configuration = fixture.InputConfiguration!;
        await using var admission = fixture.OutputAdmission;
        await fixture.ViewModel.InitializeAsync();
        fixture.ViewModel.BindSpeechInputNativeLifetime(static () => false);
        fixture.ViewModel.CanChangeSpeechInputNative.Should().BeFalse();
        await fixture.ViewModel.SaveSpeechInputCommand.ExecuteAsync();
        fixture.InputPreferences.Value.Should().BeNull();
        fixture.InputRuntime.ProfileGeneration.Number.Should().Be(0);
        fixture.ViewModel.Dispose();
    }

    private sealed class FakeSpeechInputPreferences : ISpeechInputPreferences
    {
        internal SpeechInputSelection? Value { get; private set; }
        private bool pending;
        public SpeechInputSelection? Load() => pending ? throw new InvalidDataException("unconfirmed") : Value;
        public SpeechInputSelection? ReadBack() => Value;
        public void BeginWrite() => pending = true;
        public void Save(SpeechInputSelection selection) => Value = selection;
        public void ConfirmWrite() => pending = false;
    }

    private sealed class SpeechInputTestRuntime(FakeVoiceRecognitionService voice) : ISpeechInputProfileRuntime
    {
        public SpeechInputGeneration ProfileGeneration { get; private set; } = new(0, SpeechInputSelection.Default);
        public bool IsProfileAvailable { get; private set; } = true;
        public void InitializeProfile(SpeechInputSelection? selection)
        {
            IsProfileAvailable = selection is not null && SpeechInputProfileCatalogue.UnavailableReason(selection) is null;
            if (selection is not null) { ProfileGeneration = new(0, selection); }
        }
        public async Task<ISpeechInputProfileSwitch> RetireProfileAsync(CancellationToken cancellationToken)
        {
            await voice.StopAsync(cancellationToken);
            return new Lease(this);
        }
        private sealed class Lease(SpeechInputTestRuntime owner) : ISpeechInputProfileSwitch
        {
            public void Publish(SpeechInputGeneration generation)
            {
                owner.ProfileGeneration = generation;
                owner.IsProfileAvailable = SpeechInputProfileCatalogue.UnavailableReason(generation.Selection) is null;
            }
            public void HoldUnavailable() => owner.IsProfileAvailable = false;
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
