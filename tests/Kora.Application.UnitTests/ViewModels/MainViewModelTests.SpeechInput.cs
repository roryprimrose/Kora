using AwesomeAssertions;

using Kora.Application.Configuration;
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
        fixture.Reasoner.Action = BuiltInAction.LockMachine;
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
        fixture.ViewModel.SpeechInputResources.Should().Contain(SpeechInputResource.Gpu);
        fixture.ViewModel.SpeechInputResidencies.Should().Contain(SpeechInputResidency.KeepLoaded);
        if (kind is "write") { fixture.InputPreferences.WriteFailure = new IOException("preference inaccessible"); }
        if (kind is "cancel") { fixture.InputPreferences.WriteFailure = new OperationCanceledException(); }
        var command = kind is "clarify" ? new SpeechInputCommand(AppearanceCommandOperation.Clarify, Error: SpeechInputCommand.Syntax)
            : new SpeechInputCommand(AppearanceCommandOperation.Set, kind is "missing" ? null
                : kind is "invalid" ? SpeechInputSelection.Default with { ProfileId = "unknown" } : SpeechInputSelection.Default);
        await fixture.ViewModel.ExecuteSpeechInputCommandAsync(command, SecurityAuditInitiator.TypedCommand);
        fixture.ViewModel.ResponseTitle.Should().Contain(kind is "clarify" ? "Clarify" : "not confirmed");
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
        var save = fixture.ViewModel.SaveSpeechInputCommand.ExecuteAsync();
        await fixture.InputRuntime.RetirementStarted.Task;
        try
        {
            fixture.ViewModel.CanChangeSpeechInputNative.Should().BeFalse();
            await fixture.ViewModel.ExecuteSpeechInputCommandAsync(new(AppearanceCommandOperation.Get), SecurityAuditInitiator.TypedCommand);
            fixture.ViewModel.BindSpeechInputNativeLifetime(static () => false);
        }
        finally { fixture.InputRuntime.RetirementGate.SetResult(); }
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
        var previous = fixture.ViewModel.ResponseBody;
        fixture.InputRuntime.AfterPublish = () => fixture.ViewModel.BindSpeechInputNativeLifetime(static () => false);
        await fixture.ViewModel.SaveSpeechInputCommand.ExecuteAsync();
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