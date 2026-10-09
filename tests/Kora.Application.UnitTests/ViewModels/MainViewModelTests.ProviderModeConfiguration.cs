using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Core.Auditing;
using Kora.Core.Commands;
using Kora.Core.Communication;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Voice;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Fact]
    public async Task NativeTypedAndActivatedProviderSettingsSharePersistenceWithoutDispatch()
    {
        var f = new Fixture(enableProviderModeConfiguration: true);
        await using var admission = f.OutputAdmission;
        f.Voice.Microphones = [new MicrophoneDevice("voice-fixture", "Synthetic microphone")];
        f.Voice.DefaultMicrophoneId = "voice-fixture";
        await f.ViewModel.InitializeAsync();
        f.TextToSpeech.ClearSpokenResponse();
        await f.RunAsync("Kora, list provider settings");
        f.ViewModel.ResponseBody.Should().Contain("\"source\":\"default\"").And.Contain("\"desired\":\"LocalOnly\"");
        await f.RunAsync("set providers.default-mode to HostedPreferred");
        f.ProviderPreferences.Mode.Should().Be(ModelProviderMode.HostedPreferred);
        await f.RunAsync("get providers.default-mode");
        f.ViewModel.ResponseBody.Should().Contain("\"saved\":\"HostedPreferred\"");
        await f.ViewModel.RefreshProviderModeCommand.ExecuteAsync();
        f.ViewModel.SelectedProviderModeChoice = f.ViewModel.ProviderModeChoices.Single(choice => choice.Mode == ModelProviderMode.LocalFirst);
        await f.ViewModel.SaveProviderModeCommand.ExecuteAsync();
        f.ProviderPreferences.Mode.Should().Be(ModelProviderMode.LocalFirst);
        await f.ViewModel.ResetProviderModeCommand.ExecuteAsync();
        f.ProviderPreferences.Mode.Should().Be(ModelProviderMode.LocalOnly);
        await f.RaiseActivatedTranscriptAsync("Kora, set providers.default-mode to LocalFirst", 1);
        f.ProviderPreferences.Mode.Should().Be(ModelProviderMode.LocalFirst);
        f.Voice.StartedPhrases.Should().Contain("set providers.default-mode to HostedPreferred");
        f.TextToSpeech.SpokenText.Should().BeNull();
        f.Reasoner.Requests.Should().BeEmpty();
        f.ProviderPreferences.Pending.Should().BeFalse();
        f.OutputPreferences.SavedMode.Should().BeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DirectVoiceSettingsPreserveOriginalChannelAndProtectedCallRefusal(bool protectedCall)
    {
        var f = new Fixture(enableProviderModeConfiguration: true);
        await using var admission = f.OutputAdmission;
        f.Voice.Microphones = [new MicrophoneDevice("voice-fixture", "Synthetic microphone")];
        f.Voice.DefaultMicrophoneId = "voice-fixture";
        await f.ViewModel.InitializeAsync();
        if (protectedCall) { f.CallState.SetState(CallState.Unknown); await f.Dispatcher.LastInvocation; }
        HostActivity.Current.Should().BeNull();
        await f.ViewModel.ExecuteProviderModeCommandAsync(new(AppearanceCommandOperation.Reset), SecurityAuditInitiator.VoiceCommand,
            cancellationToken: TestContext.Current.CancellationToken);
        f.ProviderPreferences.Mode.Should().Be(protectedCall ? null : ModelProviderMode.LocalOnly);
        f.ViewModel.ResponseBody.Should().Contain(protectedCall ? "\"outcome\":\"denied\"" : "\"outcome\":\"saved\"");
        f.Reasoner.Requests.Should().BeEmpty();
    }

    public sealed class FakeProviderPreferences : IModelProviderModePreferences
    {
        public ModelProviderMode? Mode { get; private set; }
        public bool Pending { get; private set; }
        public ModelProviderMode? Load() => Pending ? throw new InvalidDataException() : Mode;
        public ModelProviderMode? ReadBack() => Mode;
        public void BeginWrite() => Pending = true;
        public void Save(ModelProviderMode mode) => Mode = mode;
        public void ConfirmWrite() => Pending = false;
    }
}
