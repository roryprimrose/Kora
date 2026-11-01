using AwesomeAssertions;

using Kora.Application.Configuration;
using Kora.Application.ViewModels;
using Kora.Core.Auditing;
using Kora.Core.Commands;
using Kora.Core.Configuration;
using Kora.Core.Communication;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Voice;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Fact]
    public async Task MissingOrUnreadableNameAuthorityDoesNotOfferAnotherWakeProfile()
    {
        var unbound = new Fixture();
        unbound.ViewModel.SpeechInputProfiles.Should().Equal(SpeechInputProfileCatalogue.Profiles);
        unbound.ViewModel.SpeechInputResources.Should().Equal(SpeechInputResource.WindowsManaged);
        unbound.ViewModel.Dispose();
        var fixture = new Fixture(enableSpeechInput: true);
        await using var configuration = fixture.InputConfiguration!;
        await using var admission = fixture.OutputAdmission;
        await fixture.ViewModel.InitializeAsync();
        var names = (AssistantNameConfigurationService)typeof(MainViewModel)
            .GetField("assistantNameConfiguration", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(fixture.ViewModel)!;
        FluentActions.Invoking(() => configuration.BindAssistantName(names)).Should().Throw<InvalidOperationException>();
        FluentActions.Invoking(() => configuration.BindAssistantName(null!)).Should().Throw<ArgumentNullException>();
        fixture.NamePreferences.Name = "Supported Commands";
        names.Reload();
        fixture.ViewModel.SpeechInputProfiles.Should().Equal(SpeechInputProfileCatalogue.Profiles);
        fixture.ViewModel.SpeechInputResources.Should().Equal(SpeechInputResource.WindowsManaged);
        fixture.ViewModel.Dispose();
    }

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
        fixture.InputPreferences.Value.Should().BeNull();
        fixture.ViewModel.ResponseTitle.Should().Contain("not confirmed");
        fixture.ViewModel.SpeechInputStatus.Should().Contain("Saved:").And.Contain("Desired:").And.Contain("Additional exact local");
        fixture.ViewModel.SelectedSpeechInputResource = SpeechInputResource.WindowsManaged;
        fixture.ViewModel.SelectedSpeechInputResidency = SpeechInputResidency.CaptureLifetime;
        var native = await BeginSpeechInputReview(fixture, fixture.ViewModel.SaveSpeechInputCommand.ExecuteAsync);
        fixture.InputPreferences.Value.Should().BeNull();
        fixture.ViewModel.SpeechInputReviewStatus.Should().Contain("Required:").And.Contain("Proposed:").And.Contain("Desired:");
        await fixture.ViewModel.ConfirmSpeechInputCommand.ExecuteAsync();
        await native;
        fixture.InputPreferences.Value.Should().Be(SpeechInputSelection.Default);
        fixture.InputRuntime.ProfileGeneration.Number.Should().Be(1);
        await fixture.ViewModel.InspectSpeechInputCommand.ExecuteAsync();
        fixture.ViewModel.ResponseBody.Should().Contain("Windows activated adapter selected");
        fixture.Voice.StartCalls.Should().Be(starts);
        var activated = await BeginSpeechInputReview(fixture, () =>
            fixture.RaiseActivatedTranscriptAsync("Kora, set speech.input to windows-activated-v1 WindowsManaged CaptureLifetime", 1));
        using (var confirmation = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.ActivatedVoice), HostActivityLayer.Application, HostOperation.Request))
        {
            await fixture.ViewModel.ExecuteSpeechInputCommandAsync(new(AppearanceCommandOperation.Set, ReviewDecision: true), SecurityAuditInitiator.VoiceCommand);
            confirmation.Complete(HostOperationOutcome.Completed);
        }
        await activated;
        fixture.InputRuntime.ProfileGeneration.Number.Should().Be(2);
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

    [Theory]
    [InlineData("unbound")]
    [InlineData("owner")]
    [InlineData("pending")]
    [InlineData("disposed")]
    public async Task SpeechInputUnavailableHostsNeverOverwritePendingInteraction(string kind)
    {
        var fixture = new Fixture(enableSpeechInput: kind is not "unbound");
        await using var configuration = fixture.InputConfiguration;
        await using var admission = fixture.OutputAdmission;
        fixture.Probe.Status = new("local.inference", "Local model inference (Ollama)", Kora.Core.Dependencies.DependencyReadiness.Ready, "ready");
        fixture.Reasoner.Action = BuiltInAction.EnableHostedModels;
        await fixture.ViewModel.InitializeAsync();
        if (kind is "owner") { fixture.ViewModel.BindCallOwnershipGate(static () => false); }
        if (kind is "pending")
        {
            await fixture.RunAsync("lock this workstation please");
            await fixture.ViewModel.ActiveReasoningTask!;
            fixture.ViewModel.IsModelActionApprovalPending.Should().BeTrue();
        }
        if (kind is "disposed") { fixture.ViewModel.Dispose(); }
        if (kind is "unbound") { fixture.ViewModel.SpeechInputStatus.Should().Contain("profile selection is unavailable"); }
        var previous = fixture.ViewModel.ResponseBody;
        fixture.ViewModel.CanChangeSpeechInputNative.Should().BeFalse();
        await fixture.ViewModel.ExecuteSpeechInputCommandAsync(new(AppearanceCommandOperation.Get), SecurityAuditInitiator.TypedCommand);
        fixture.ViewModel.ResponseBody.Should().Be(previous);
        fixture.InputPreferences.Value.Should().BeNull();
        fixture.ViewModel.Dispose();
    }

    [Theory]
    [InlineData("clarify")]
    [InlineData("missing")]
    [InlineData("invalid")]
    [InlineData("write")]
    [InlineData("cancel")]
    public async Task SpeechInputInvalidOrFailedSelectionIsExplicitAndNeverStartsCapture(string kind)
    {
        var fixture = new Fixture(enableSpeechInput: true);
        await using var configuration = fixture.InputConfiguration!;
        await using var admission = fixture.OutputAdmission;
        await fixture.ViewModel.InitializeAsync();
        fixture.ViewModel.SelectedSpeechInputProfile = null;
        fixture.ViewModel.SelectedSpeechInputProfile.Should().BeNull();
        fixture.ViewModel.SelectedSpeechInputProfile = fixture.ViewModel.SpeechInputProfiles.Single();
        fixture.ViewModel.SpeechInputResources.Should().Equal(SpeechInputResource.WindowsManaged);
        fixture.ViewModel.SpeechInputResidencies.Should().Equal(SpeechInputResidency.CaptureLifetime);
        if (kind is "write") { fixture.InputPreferences.WriteFailure = new IOException("preference inaccessible"); }
        if (kind is "cancel") { fixture.InputPreferences.WriteFailure = new OperationCanceledException(); }
        var command = kind is "clarify" ? new SpeechInputCommand(AppearanceCommandOperation.Clarify, Error: SpeechInputCommand.Syntax)
            : new SpeechInputCommand(AppearanceCommandOperation.Set, kind is "missing" ? null
                : kind is "invalid" ? SpeechInputSelection.Default with { ProfileId = "unknown" } : SpeechInputSelection.Default);
        if (kind is "write" or "cancel")
        {
            var change = await BeginSpeechInputReview(fixture, () => fixture.ViewModel.ExecuteSpeechInputCommandAsync(command, SecurityAuditInitiator.TypedCommand));
            await fixture.ViewModel.ConfirmSpeechInputCommand.ExecuteAsync();
            await change;
        }
        else { await fixture.ViewModel.ExecuteSpeechInputCommandAsync(command, SecurityAuditInitiator.TypedCommand); }
        if (kind is "cancel") { fixture.ViewModel.Transcript.Should().Contain("cancelled"); }
        else { fixture.ViewModel.ResponseTitle.Should().Contain(kind is "clarify" ? "Clarify" : "not confirmed"); }
        fixture.InputPreferences.Value.Should().BeNull();
        fixture.Voice.StartCalls.Should().Be(0);
        fixture.ViewModel.Dispose();
    }

    [Fact]
    public async Task SpeechInputConcurrentOrRevokedNativeSaveCannotAcquireAnotherAdmission()
    {
        var fixture = new Fixture(enableSpeechInput: true);
        await using var configuration = fixture.InputConfiguration!;
        await using var admission = fixture.OutputAdmission;
        await fixture.ViewModel.InitializeAsync();
        fixture.InputRuntime.RetirementGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var save = await BeginSpeechInputReview(fixture, fixture.ViewModel.SaveSpeechInputCommand.ExecuteAsync);
        var confirmation = fixture.ViewModel.ConfirmSpeechInputCommand.ExecuteAsync();
        await fixture.InputRuntime.RetirementStarted.Task;
        try
        {
            fixture.ViewModel.CanChangeSpeechInputNative.Should().BeFalse();
            await fixture.ViewModel.ExecuteSpeechInputCommandAsync(new(AppearanceCommandOperation.Get), SecurityAuditInitiator.TypedCommand);
            fixture.ViewModel.BindSpeechInputNativeLifetime(static () => false);
        }
        finally { fixture.InputRuntime.RetirementGate.SetResult(); }
        await confirmation;
        await save;
        fixture.InputPreferences.Value.Should().BeNull();
        fixture.InputRuntime.ProfileGeneration.Number.Should().Be(0);
        fixture.ViewModel.Dispose();
    }

    [Fact]
    public async Task SpeechInputCompletedSaveDoesNotRevealResultsAfterNativeLifetimeRevocation()
    {
        var fixture = new Fixture(enableSpeechInput: true);
        await using var configuration = fixture.InputConfiguration!;
        await using var admission = fixture.OutputAdmission;
        await fixture.ViewModel.InitializeAsync();
        fixture.InputRuntime.AfterPublish = () => fixture.ViewModel.BindSpeechInputNativeLifetime(static () => false);
        var save = await BeginSpeechInputReview(fixture, fixture.ViewModel.SaveSpeechInputCommand.ExecuteAsync);
        var previous = fixture.ViewModel.ResponseBody;
        await fixture.ViewModel.ConfirmSpeechInputCommand.ExecuteAsync();
        await save;
        fixture.InputPreferences.Value.Should().Be(SpeechInputSelection.Default);
        fixture.ViewModel.ResponseBody.Should().Be(previous);
        fixture.ViewModel.Dispose();
    }

    [Fact]
    public async Task SpeechInputProtectedCallDeniesOriginalVoiceEvenWhenActivationRemainsAllowed()
    {
        var fixture = new Fixture(enableSpeechInput: true);
        await using var configuration = fixture.InputConfiguration!;
        await using var admission = fixture.OutputAdmission;
        fixture.VoiceConsent.Consent = true;
        fixture.Voice.Microphones = [new("mic", "Headset")];
        fixture.Voice.DefaultMicrophoneId = "mic";
        await fixture.ViewModel.InitializeAsync();
        fixture.CallState.SetState(CallState.Active);
        fixture.ViewModel.IsVoiceEnabled.Should().BeTrue();
        using (var activity = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.ActivatedVoice),
            HostActivityLayer.Application, HostOperation.Request))
        {
            await fixture.ViewModel.ExecuteSpeechInputCommandAsync(new(AppearanceCommandOperation.Set, SpeechInputSelection.Default),
                SecurityAuditInitiator.VoiceCommand);
            fixture.ViewModel.Transcript.Should().Contain("denied");
            activity.Complete(HostOperationOutcome.Completed);
        }
        fixture.InputPreferences.Value.Should().BeNull();
        fixture.InputRuntime.ProfileGeneration.Number.Should().Be(0);
        fixture.ViewModel.Dispose();
    }

    [Fact]
    public async Task SpeechInputInvalidSavedNameCannotUsePrefixButExactUnprefixedRecoveryStillWorks()
    {
        var fixture = new Fixture(enableSpeechInput: true);
        await using var configuration = fixture.InputConfiguration!;
        await using var admission = fixture.OutputAdmission;
        fixture.NamePreferences.Name = "Supported Commands";
        await fixture.ViewModel.InitializeAsync();
        await fixture.RunAsync("Kora, get speech.input");
        fixture.ViewModel.Transcript.Should().Contain("prefix routing is unavailable");
        fixture.InputPreferences.Value.Should().BeNull();
        await fixture.RunAsync("get speech.input");
        fixture.ViewModel.ResponseBody.Should().Contain("Windows activated adapter selected");
        fixture.ViewModel.Dispose();
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("draft")]
    [InlineData("profile")]
    [InlineData("residency")]
    [InlineData("native-close")]
    [InlineData("call")]
    [InlineData("owner")]
    [InlineData("consent")]
    public async Task SpeechInputReviewCancellationOrDriftNeverRetiresOrReplacesIntent(string change)
    {
        var fixture = new Fixture(enableSpeechInput: true);
        await using var configuration = fixture.InputConfiguration!;
        await using var admission = fixture.OutputAdmission;
        if (change is "consent") { fixture.VoiceConsent.Consent = true; }
        await fixture.ViewModel.InitializeAsync();
        fixture.ViewModel.SelectedSpeechInputProfile = fixture.ViewModel.SelectedSpeechInputProfile;
        fixture.ViewModel.SelectedSpeechInputResource = fixture.ViewModel.SelectedSpeechInputResource;
        fixture.ViewModel.SelectedSpeechInputResidency = fixture.ViewModel.SelectedSpeechInputResidency;
        var save = await BeginSpeechInputReview(fixture, fixture.ViewModel.SaveSpeechInputCommand.ExecuteAsync);
        fixture.InputRuntime.RetirementStarted.Task.IsCompleted.Should().BeFalse();
        switch (change)
        {
            case "cancel": await fixture.ViewModel.CancelSpeechInputCommand.ExecuteAsync(); break;
            case "draft": fixture.ViewModel.SelectedSpeechInputResource = SpeechInputResource.Cpu; break;
            case "profile": fixture.ViewModel.SelectedSpeechInputProfile = null; break;
            case "residency": fixture.ViewModel.SelectedSpeechInputResidency = SpeechInputResidency.KeepLoaded; break;
            case "native-close": fixture.ViewModel.BindSpeechInputNativeLifetime(static () => false); break;
            case "call": fixture.CallState.SetState(CallState.Active); break;
            case "owner": fixture.ViewModel.BindCallOwnershipGate(static () => false); break;
            case "consent": await fixture.ViewModel.WithdrawVoiceConsentCommand.ExecuteAsync(); break;
        }
        await fixture.ViewModel.ConfirmSpeechInputCommand.ExecuteAsync();
        if (fixture.ViewModel.HasSpeechInputReview) { await fixture.ViewModel.CancelSpeechInputCommand.ExecuteAsync(); }
        await save;
        fixture.InputPreferences.Value.Should().BeNull();
        fixture.InputRuntime.ProfileGeneration.Number.Should().Be(0);
        fixture.InputRuntime.RetirementStarted.Task.IsCompleted.Should().BeFalse();
        fixture.ViewModel.HasSpeechInputReview.Should().BeFalse();
        fixture.ViewModel.Dispose();
    }

    [Theory]
    [InlineData("lifetime")]
    [InlineData("model")]
    [InlineData("system")]
    public async Task SpeechInputConfirmationRequiresLiveOriginalNativeAdmissionAndNeverModelAuthority(string change)
    {
        var fixture = new Fixture(enableSpeechInput: true);
        await using var configuration = fixture.InputConfiguration!;
        await using var admission = fixture.OutputAdmission;
        await fixture.ViewModel.InitializeAsync();
        var nativeCurrent = true;
        fixture.ViewModel.BindSpeechInputNativeLifetime(() => nativeCurrent);
        var save = await BeginSpeechInputReview(fixture, fixture.ViewModel.SaveSpeechInputCommand.ExecuteAsync);
        if (change is "lifetime") { nativeCurrent = false; }
        fixture.ViewModel.CanConfirmSpeechInputReview.Should().Be(change is not "lifetime");
        using (var incoming = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Application, HostOperation.Request))
        {
            await fixture.ViewModel.ExecuteSpeechInputCommandAsync(new(AppearanceCommandOperation.Set, ReviewDecision: true),
                change is "model" ? SecurityAuditInitiator.ModelSuggestion
                    : change is "system" ? SecurityAuditInitiator.System : SecurityAuditInitiator.LocalUser);
            incoming.Complete(HostOperationOutcome.Completed);
        }
        fixture.InputPreferences.Value.Should().BeNull();
        fixture.InputRuntime.RetirementStarted.Task.IsCompleted.Should().BeFalse();
        await fixture.ViewModel.CancelSpeechInputCommand.ExecuteAsync();
        await save;
        fixture.ViewModel.SpeechInputReviewStatus.Should().Contain("No speech-input switch");
        fixture.ViewModel.CanConfirmSpeechInputReview.Should().BeFalse();
        fixture.ViewModel.Dispose();
    }

    [Fact]
    public async Task SpeechInputDraftChangesWithoutAReviewNeverAlterTheSelectedRuntimeOrPreference()
    {
        var fixture = new Fixture(enableSpeechInput: true);
        await using var configuration = fixture.InputConfiguration!;
        await using var admission = fixture.OutputAdmission;
        await fixture.ViewModel.InitializeAsync();
        var original = fixture.ViewModel.SpeechInputStatus;
        fixture.ViewModel.SelectedSpeechInputResource = SpeechInputResource.Cpu;
        fixture.ViewModel.SelectedSpeechInputResource = SpeechInputResource.WindowsManaged;
        fixture.ViewModel.SelectedSpeechInputResidency = SpeechInputResidency.KeepLoaded;
        fixture.ViewModel.SelectedSpeechInputResidency = SpeechInputResidency.CaptureLifetime;
        fixture.ViewModel.SpeechInputStatus.Should().Be(original);
        fixture.ViewModel.HasSpeechInputReview.Should().BeFalse();
        fixture.InputPreferences.Value.Should().BeNull();
        fixture.InputRuntime.RetirementStarted.Task.IsCompleted.Should().BeFalse();
        fixture.ViewModel.Dispose();
    }

    [Fact]
    public async Task SpeechInputTypedSetReviewsBeforeExactConfirmationAndCannotBeConfirmedByVoice()
    {
        var fixture = new Fixture(enableSpeechInput: true);
        await using var configuration = fixture.InputConfiguration!;
        await using var admission = fixture.OutputAdmission;
        await fixture.ViewModel.InitializeAsync();
        var save = await BeginSpeechInputReview(fixture, () =>
            fixture.RunAsync("set speech.input to windows-activated-v1 WindowsManaged CaptureLifetime"));
        using (var voice = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.ActivatedVoice), HostActivityLayer.Application, HostOperation.Request))
        {
            await fixture.ViewModel.ExecuteSpeechInputCommandAsync(new(AppearanceCommandOperation.Set, ReviewDecision: true), SecurityAuditInitiator.VoiceCommand);
            voice.Complete(HostOperationOutcome.Completed);
        }
        fixture.ViewModel.HasSpeechInputReview.Should().BeTrue();
        fixture.InputPreferences.Value.Should().BeNull();
        await fixture.RunAsync("confirm speech.input");
        await save;
        fixture.InputPreferences.Value.Should().Be(SpeechInputSelection.Default);
        await fixture.RunAsync("confirm speech.input");
        fixture.ViewModel.Transcript.Should().Contain("No current speech-input review");
        fixture.InputRuntime.ProfileGeneration.Number.Should().Be(1);
        fixture.ViewModel.Dispose();
    }

    private static async Task<Task> BeginSpeechInputReview(Fixture fixture, Func<Task> begin)
    {
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
        {
            if (fixture.ViewModel.HasSpeechInputReview) { ready.TrySetResult(); }
        }
        fixture.ViewModel.PropertyChanged += Changed;
        try
        {
            var operation = begin();
            await Task.WhenAny(ready.Task, operation).WaitAsync(TestContext.Current.CancellationToken);
            await operation;
            fixture.ViewModel.HasSpeechInputReview.Should().BeTrue();
            return operation;
        }
        finally { fixture.ViewModel.PropertyChanged -= Changed; }
    }

    private sealed class FakeSpeechInputPreferences : ISpeechInputPreferences
    {
        internal SpeechInputSelection? Value { get; private set; }
        internal Exception? WriteFailure { get; set; }
        private bool pending;
        public SpeechInputSelection? Load() => pending ? throw new InvalidDataException("unconfirmed") : Value;
        public SpeechInputSelection? ReadBack() => Value;
        public void BeginWrite() => pending = true;
        public void Save(SpeechInputSelection selection)
        {
            if (WriteFailure is { } failure) { throw failure; }
            Value = selection;
        }
        public void ConfirmWrite() => pending = false;
    }

    private sealed class SpeechInputTestRuntime(FakeVoiceRecognitionService voice) : ISpeechInputProfileRuntime
    {
        public SpeechInputGeneration ProfileGeneration { get; private set; } = new(0, SpeechInputSelection.Default);
        public bool IsProfileAvailable { get; private set; } = true;
        internal TaskCompletionSource? RetirementGate { get; set; }
        internal TaskCompletionSource RetirementStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Action? AfterPublish { get; set; }
        public void InitializeProfile(SpeechInputSelection? selection)
        {
            IsProfileAvailable = selection is not null && SpeechInputProfileCatalogue.UnavailableReason(selection) is null;
            if (selection is not null) { ProfileGeneration = new(0, selection); }
        }
        public async Task<ISpeechInputProfileSwitch> RetireProfileAsync(CancellationToken cancellationToken)
        {
            await voice.StopAsync(cancellationToken);
            RetirementStarted.TrySetResult();
            if (RetirementGate is { } pending) { await pending.Task.WaitAsync(cancellationToken); }
            return new Lease(this);
        }
        private sealed class Lease(SpeechInputTestRuntime owner) : ISpeechInputProfileSwitch
        {
            public void Publish(SpeechInputGeneration generation)
            {
                owner.ProfileGeneration = generation;
                owner.IsProfileAvailable = SpeechInputProfileCatalogue.UnavailableReason(generation.Selection) is null;
                owner.AfterPublish?.Invoke();
            }
            public void HoldUnavailable() => owner.IsProfileAvailable = false;
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}