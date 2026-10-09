using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Core;
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
        public Exception? LoadException { get; set; }
        public Exception? SaveException { get; set; }
        public Action? BeforeSave { get; set; }
        public ModelProviderMode? Load() => LoadException is { } exception ? throw exception : Pending ? throw new InvalidDataException() : Mode;
        public ModelProviderMode? ReadBack() => Mode;
        public void BeginWrite() => Pending = true;
        public void Save(ModelProviderMode mode)
        {
            BeforeSave?.Invoke();
            if (SaveException is { } exception) { throw exception; }
            Mode = mode;
        }
        public void ConfirmWrite() => Pending = false;
    }

    [Fact]
    public async Task MissingDisposedInvalidAndCancelledProviderControlsNeverInferOrSave()
    {
        var missing = new Fixture();
        missing.ViewModel.ProviderModeChoices.Should().BeEmpty();
        missing.ViewModel.ProviderModeConfigurationStatus.Should().Contain("unavailable");
        await missing.RunAsync("reset providers.default-mode");
        var f = new Fixture(enableProviderModeConfiguration: true);
        await using var admission = f.OutputAdmission;
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.Invoking(model => model.SaveProviderModeCommand.ExecuteAsync()).Should().ThrowAsync<InvalidOperationException>();
        await f.ViewModel.RefreshProviderModeCommand.ExecuteAsync();
        f.ViewModel.ProviderModeConfigurationStatus.Should().Contain("\"available\":true");
        await f.RunAsync("set providers.default-mode to 1");
        f.ViewModel.ResponseTitle.Should().Be("Clarify the provider setting.");
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        await f.ViewModel.ExecuteProviderModeCommandAsync(new(AppearanceCommandOperation.Reset),
            SecurityAuditInitiator.TypedCommand, cancellationToken: cancelled.Token);
        f.ViewModel.ProviderModeConfigurationStatus.Should().Contain("\"available\":false");
        f.ViewModel.Dispose();
        await f.ViewModel.ResetProviderModeCommand.ExecuteAsync();
        f.ProviderPreferences.Mode.Should().BeNull();
        f.ViewModel.CanChangeProviderMode.Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProviderPreferenceResultsPreserveCompleteSpeakingResponseIncludingWriteFailures(bool fail)
    {
        var f = new Fixture(enableProviderModeConfiguration: true);
        await using var admission = f.OutputAdmission;
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.RefreshProviderModeCommand.ExecuteAsync();
        f.TextToSpeech.SpeakGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var speaking = f.RunAsync("what power action is pending");
        await f.TextToSpeech.SpeakStarted.Task;
        var body = f.ViewModel.ResponseBody;
        if (fail) { f.ProviderPreferences.SaveException = new IOException("owned atomic failure"); }
        f.ViewModel.SelectedProviderModeChoice = f.ViewModel.ProviderModeChoices.Single(choice => choice.Mode == ModelProviderMode.LocalFirst);
        await f.ViewModel.SaveProviderModeCommand.ExecuteAsync();
        f.TextToSpeech.SpeakGate.TrySetResult();
        await speaking;
        f.ViewModel.ResponseBody.Should().Be(body);
        f.ViewModel.IsVisualResponseVisible.Should().BeTrue();
        f.ProviderPreferences.Mode.Should().Be(fail ? null : ModelProviderMode.LocalFirst);
        if (fail) { f.ViewModel.Transcript.Should().Contain("not confirmed"); }
    }

    [Theory]
    [InlineData("receipt")]
    [InlineData("disposed")]
    [InlineData("presentation")]
    [InlineData("no-subscriber")]
    public async Task ProviderPresentationRechecksLateOwnershipAndDisposal(string stage)
    {
        var f = new Fixture(subscribeToWindowActions: stage is not "no-subscriber", enableProviderModeConfiguration: true);
        await using var admission = f.OutputAdmission;
        await f.ViewModel.InitializeAsync();
        f.ViewModel.PropertyChanged += (_, args) =>
        {
            if (string.Equals(args.PropertyName, nameof(f.ViewModel.ProviderModeChoices), StringComparison.Ordinal))
            {
                if (stage is "receipt") { f.Session.IsUnlocked = false; }
                if (stage is "disposed") { f.ViewModel.Dispose(); }
            }
            if (stage is "presentation" && string.Equals(args.PropertyName, nameof(f.ViewModel.ResponseBody), StringComparison.Ordinal))
            { f.Session.IsUnlocked = false; }
        };
        await f.RunAsync("get providers.default-mode");
        f.ProviderPreferences.Mode.Should().BeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProviderSettingsCannotAnswerPendingExactInteractions(bool question)
    {
        var f = new Fixture(enableProviderModeConfiguration: true);
        await using var admission = f.OutputAdmission;
        f.Probe.Status = new("local.inference", "Local model inference (Ollama)", DependencyReadiness.Ready, "ready");
        f.Reasoner.Action = BuiltInAction.LockMachine;
        if (question) { f.Reasoner.Question = new("Exact choice?", ["one", "two"]); f.Reasoner.Action = null; }
        await f.ViewModel.InitializeAsync();
        await f.RunAsync("please do this work");
        await f.ViewModel.ActiveReasoningTask!;
        var preview = f.ViewModel.ResponseBody;
        await f.RunAsync("reset providers.default-mode");
        f.ViewModel.ResponseBody.Should().Be(preview);
        f.ProviderPreferences.Mode.Should().BeNull();
        f.ViewModel.CanChangeProviderMode.Should().BeFalse();
    }

    [Fact]
    public async Task ReentrantProviderCommandCannotAcquireCommittingPresentation()
    {
        var f = new Fixture(enableProviderModeConfiguration: true);
        await using var admission = f.OutputAdmission;
        await f.ViewModel.InitializeAsync();
        Task? reentrant = null;
        var notified = false;
        f.ViewModel.PropertyChanged += (_, args) =>
        {
            if (notified || !string.Equals(args.PropertyName, nameof(f.ViewModel.CanChangeProviderMode), StringComparison.Ordinal)) { return; }
            notified = true;
            reentrant = f.RunAsync("reset providers.default-mode");
        };
        await f.RunAsync("get providers.default-mode");
        await reentrant!;
        f.ProviderPreferences.Mode.Should().BeNull();
    }

    [Fact]
    public async Task OriginalAmbientVoiceIsPreservedAndInvalidReadIsExplicit()
    {
        var f = new Fixture(enableProviderModeConfiguration: true);
        await using var admission = f.OutputAdmission;
        f.Voice.Microphones = [new MicrophoneDevice("voice-fixture", "Synthetic microphone")];
        f.Voice.DefaultMicrophoneId = "voice-fixture";
        await f.ViewModel.InitializeAsync();
        f.CallState.SetState(CallState.Unknown);
        await f.Dispatcher.LastInvocation;
        using (var activity = HostActivity.BeginRoot(Kora.Core.Hosting.HostRequest.Create(Kora.Core.Hosting.RequestOrigin.ActivatedVoice),
            HostActivityLayer.Application, HostOperation.Request))
        {
            await f.ViewModel.ExecuteProviderModeCommandAsync(new(AppearanceCommandOperation.Reset), SecurityAuditInitiator.LocalUser,
                cancellationToken: TestContext.Current.CancellationToken);
        }
        f.ProviderPreferences.Mode.Should().BeNull();
        f.ProviderPreferences.LoadException = new InvalidDataException("corrupt");
        await f.RunAsync("get providers.default-mode");
        f.ViewModel.State.Should().Be(AssistantState.Failure);
    }
}
