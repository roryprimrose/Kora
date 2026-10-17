using System.Reflection;

using AwesomeAssertions;

using Kora.Application.ViewModels;
using Kora.Core.Auditing;
using Kora.Core.Commands;
using Kora.Core.Configuration;
using Kora.Core.Voice;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Fact]
    public async Task SupersededResponseCannotBeSpokenAfterItsOutputDiscoveryCompletes()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        fixture.ViewModel.DefaultResponseMode = ResponseOutputMode.VoiceOnly;
        fixture.TextToSpeech.ClearSpokenResponse();
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var token = TestContext.Current.CancellationToken;
        fixture.TextToSpeech.BeforeOutputEnumeration = () =>
        {
            entered.SetResult();
            release.Wait(token);
        };
        var command = fixture.RunAsync("what power action is pending");
        try
        {
            await entered.Task.WaitAsync(token);
            fixture.ViewModel.ReportHostInteractionFailure("A newer response superseded the discovery request.");
            command.IsCompleted.Should().BeFalse();
        }
        finally { release.Set(); }
        await command;
        fixture.ViewModel.ResponseBody.Should().Be("A newer response superseded the discovery request.");
        fixture.TextToSpeech.SpokenText.Should().BeNull();
    }

    [Fact]
    public async Task ForcedDisposalJoinsEveryOwnedWorkerEvenWhenAnEarlierPreferenceWorkerFails()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        using var appearanceRelease = new ManualResetEventSlim();
        using var auditRelease = new ManualResetEventSlim();
        var appearanceEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var auditEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var token = TestContext.Current.CancellationToken;
        fixture.AppearancePreferences.BeforeThemeSave = () =>
        {
            appearanceEntered.SetResult();
            appearanceRelease.Wait(token);
            throw new InvalidOperationException("Fixture appearance worker failure.");
        };
        fixture.ViewModel.ThemeMode = ApplicationThemeMode.Dark;
        await appearanceEntered.Task.WaitAsync(token);
        fixture.Audit.BeforeWrite = audit =>
        {
            if (audit.Category != SecurityAuditCategory.SecurityApproval || audit.Outcome != SecurityAuditOutcome.Requested) { return; }
            auditEntered.SetResult();
            auditRelease.Wait(token);
        };
        var preparation = fixture.ViewModel.PrepareGrantChangeAsync(
            new(GrantChangeOperation.Add, BuiltInAction.LockMachine, ModelApprovalScope.Session));
        await auditEntered.Task.WaitAsync(token);
        var dispose = fixture.ViewModel.DisposeAsync().AsTask();
        appearanceRelease.Set();
        try
        {
            await fixture.ViewModel.AppearanceUpdate;
            dispose.IsCompleted.Should().BeFalse("the outstanding required audit must be joined even after another worker fails");
        }
        finally { auditRelease.Set(); }
        await preparation;
        var closed = () => dispose;
        await closed.Should().ThrowAsync<InvalidOperationException>();
    }

    [Theory]
    [InlineData(false, "hide")]
    [InlineData(false, "lock")]
    [InlineData(true, "hide")]
    [InlineData(true, "lock")]
    [InlineData(true, "call")]
    public async Task PreferenceCompletionNotificationsCannotReviveAnApprovalWhoseAdmissionChanged(bool modelAction, string transition)
    {
        var fixture = new Fixture();
        fixture.Probe.Status = new("local.inference", "Local model",
            Kora.Core.Dependencies.DependencyReadiness.Ready, "Ready");
        fixture.Reasoner.Action = BuiltInAction.LockMachine;
        await fixture.ViewModel.InitializeAsync();
        if (modelAction)
        {
            await fixture.RunAsync("please lock this workstation");
            await fixture.ViewModel.ActiveReasoningTask!;
        }
        else
        {
            await fixture.ViewModel.PrepareGrantChangeAsync(
                new(GrantChangeOperation.Add, BuiltInAction.LockMachine, ModelApprovalScope.Always));
        }
        var changed = false;
        fixture.ViewModel.PropertyChanged += (_, args) =>
        {
            if (changed || !string.Equals(args.PropertyName, nameof(MainViewModel.AlwaysAllowedModelActions), StringComparison.Ordinal)
                || !fixture.Audit.Events.Any(item => item.Category == SecurityAuditCategory.ConfigurationWrite
                    && item.Outcome == SecurityAuditOutcome.Succeeded)) { return; }
            changed = true;
            switch (transition)
            {
                case "hide": fixture.ViewModel.HideApplication(); break;
                case "lock": fixture.Session.IsUnlocked = false; break;
                default: fixture.CallState.SetState(Kora.Core.Communication.CallState.Active); break;
            }
        };
        if (modelAction) { await fixture.ViewModel.ApproveModelActionAlwaysCommand.ExecuteAsync(); }
        else { await fixture.ViewModel.ConfirmGrantChangeAsync(); }
        fixture.ViewModel.AlwaysAllowedModelActions.Should().BeEmpty();
        fixture.Session.LockCalls.Should().Be(0);
    }

    [Fact]
    public async Task LateInternalPresentationAndRejectionContinuationsCannotCreateAuthority()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        var reject = typeof(MainViewModel).GetMethod("RejectPendingModelActionCoreAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
            .CreateDelegate<Func<Task>>(fixture.ViewModel);
        await reject();
        var speak = typeof(MainViewModel).GetMethod("SpeakCurrentResponseAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
            .CreateDelegate<Func<string?, long?, CancellationToken, Task>>(fixture.ViewModel);
        fixture.TextToSpeech.OutputEnumerationException = new IOException("Fixture late output observation failure.");
        await speak(null, null, TestContext.Current.CancellationToken);
        fixture.TextToSpeech.OutputEnumerationException = null;
        fixture.Session.IsUnlocked = false;
        await speak(null, null, TestContext.Current.CancellationToken);
        var propose = typeof(MainViewModel).GetMethod("CreatePowerProposalCoreAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
            .CreateDelegate<Func<BuiltInAction, SecurityAuditInitiator, Task<bool>>>(fixture.ViewModel);
        (await propose(BuiltInAction.ProposeShutdown, SecurityAuditInitiator.TypedCommand)).Should().BeFalse();
        fixture.Session.LockCalls.Should().Be(0);
    }

    [Fact]
    public async Task UnpublishedPowerPreparationsRemainOwnedAcrossSupersessionAndForcedClose()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var token = TestContext.Current.CancellationToken;
        fixture.Audit.BeforeWrite = audit =>
        {
            if (!string.Equals(audit.ActionId, "power.shutdown", StringComparison.Ordinal)
                || audit.Outcome != SecurityAuditOutcome.Requested) { return; }
            entered.SetResult();
            release.Wait(token);
            fixture.ViewModel.Dispose();
        };
        var shutdown = fixture.Catalog.GetCommands("Kora").Single(item => item.Action == BuiltInAction.ProposeShutdown);
        var restart = fixture.Catalog.GetCommands("Kora").Single(item => item.Action == BuiltInAction.ProposeRestart);
        var first = fixture.ViewModel.ExecuteAsync(shutdown, SecurityAuditInitiator.TypedCommand);
        try
        {
            await entered.Task.WaitAsync(token);
            await fixture.ViewModel.ExecuteAsync(restart, SecurityAuditInitiator.TypedCommand);
            fixture.ViewModel.ApprovalPreparation.IsCompleted.Should().BeFalse();
        }
        finally { release.Set(); }
        await first;
        await fixture.ViewModel.ApprovalPreparation;
        fixture.Session.LockCalls.Should().Be(0);
    }

    [Fact]
    public async Task UnexpectedQueuedPreferenceFailureAfterDisposalDoesNotPublishRecovery()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        fixture.AppearancePreferences.BeforePresencePreferenceSave = () =>
        {
            fixture.ViewModel.Dispose();
            throw new NotSupportedException("Fixture disposed boundary.");
        };
        fixture.ViewModel.QueuePresencePosition(new(1, 2));
        await fixture.ViewModel.PreferenceUpdate;
        fixture.AppearancePreferences.SavedPresencePosition.Should().BeNull();
    }

    [Theory]
    [InlineData("local-model")]
    [InlineData("powershell")]
    [InlineData("install-provider")]
    [InlineData("remove-provider")]
    public async Task SetupAndProviderWritesRecheckPrivacyAfterRequestedAudit(string operation)
    {
        var fixture = await Fixture.CreateInitializedAsync();
        fixture.ViewModel.SelectedSpeechProvider = CreateKokoroProvider(operation is "remove-provider");
        fixture.Audit.BeforeWrite = audit =>
        {
            if (audit.Category == SecurityAuditCategory.ResourceWrite && audit.Outcome == SecurityAuditOutcome.Requested)
            { fixture.Session.IsUnlocked = false; }
        };
        switch (operation)
        {
            case "local-model": await fixture.ViewModel.InstallLocalModelAsync(); break;
            case "powershell": await fixture.ViewModel.InstallPowerShellAsync(); break;
            case "install-provider": await fixture.ViewModel.DownloadSpeechProviderCommand.ExecuteAsync(); break;
            default: await fixture.ViewModel.RemoveSpeechProviderCommand.ExecuteAsync(); break;
        }
        fixture.Audit.Events.Should().Contain(item => item.Outcome == SecurityAuditOutcome.Denied);
        fixture.LocalModel.Calls.Should().Be(0);
        fixture.TextToSpeech.InstallProviderCalls.Should().Be(0);
        fixture.TextToSpeech.RemoveProviderCalls.Should().Be(0);
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("owner")]
    [InlineData("call")]
    [InlineData("recovery")]
    [InlineData("input")]
    public async Task ModelApprovalPreparationCannotPublishAfterRequestedAuditAdmissionChanges(string transition)
    {
        var fixture = new Fixture();
        fixture.Probe.Status = new("local.inference", "Local model",
            Kora.Core.Dependencies.DependencyReadiness.Ready, "Ready");
        fixture.Reasoner.Action = BuiltInAction.LockMachine;
        await fixture.ViewModel.InitializeAsync();
        fixture.Audit.BeforeWrite = audit =>
        {
            if (audit.Category != SecurityAuditCategory.SecurityApproval || audit.Outcome != SecurityAuditOutcome.Requested) { return; }
            switch (transition)
            {
                case "cancel":
                    typeof(MainViewModel).GetField("activeReasoningCancellation", BindingFlags.NonPublic | BindingFlags.Instance)!
                        .GetValue(fixture.ViewModel).Should().BeOfType<CancellationTokenSource>().Which.Cancel();
                    break;
                case "owner": fixture.ViewModel.BindCallOwnershipGate(static () => false); break;
                case "call": fixture.CallState.SetState(Kora.Core.Communication.CallState.Active); break;
                case "recovery": fixture.ViewModel.CloseForObservedPrivacyEvent("fixture", false); break;
                default: fixture.ViewModel.HideApplication(); break;
            }
        };
        await fixture.RunAsync("please lock this workstation");
        await fixture.ViewModel.ActiveReasoningTask!;
        await fixture.ViewModel.PrivacyClosureTask;
        fixture.ViewModel.IsModelActionApprovalPending.Should().BeFalse();
        fixture.Session.LockCalls.Should().Be(0);
        fixture.Audit.Events.Should().Contain(item => item.Outcome == SecurityAuditOutcome.Denied);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedAndDisposedNativeAppearanceChangesRemainUnconfirmed(bool failDuringDisposal)
    {
        var fixture = await Fixture.CreateInitializedAsync();
        if (!failDuringDisposal)
        {
            fixture.AppearancePreferences.SaveException = new IOException("Fixture disk failure.");
            fixture.ViewModel.ThemeMode = ApplicationThemeMode.Dark;
            await fixture.ViewModel.AppearanceUpdate;
            fixture.ViewModel.AppearanceSettingStatus.Should().Contain("Fixture disk failure");
            fixture.ViewModel.ThemeMode.Should().Be(ApplicationThemeMode.System);
            return;
        }
        fixture.AppearancePreferences.BeforeThemeSave = fixture.ViewModel.Dispose;
        fixture.AppearancePreferences.SaveException = new InvalidOperationException("Fixture retired write.");
        fixture.ViewModel.ThemeMode = ApplicationThemeMode.Dark;
        await fixture.ViewModel.AppearanceUpdate;
        fixture.ViewModel.ThemeMode = ApplicationThemeMode.Light;
        fixture.AppearancePreferences.SavedMode.Should().BeNull();
    }

    [Fact]
    public async Task AppearanceDisposalClearsQueuedEditsAndPendingTypographyIsExplicit()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var token = TestContext.Current.CancellationToken;
        fixture.AppearancePreferences.BeforeThemeSave = () => { entered.SetResult(); release.Wait(token); };
        fixture.ViewModel.ThemeMode = ApplicationThemeMode.Dark;
        try
        {
            await entered.Task.WaitAsync(token);
            fixture.ViewModel.IsReaderTypographyUpdatePending.Should().BeTrue();
            var open = fixture.ViewModel.EnsureReaderTypographyAvailable;
            open.Should().Throw<InvalidOperationException>().WithMessage("*being saved*");
            fixture.ViewModel.PresenceSizePixels = 500;
            fixture.ViewModel.Dispose();
        }
        finally { release.Set(); }
        await fixture.ViewModel.AppearanceUpdate;
        fixture.AppearancePreferences.SavedPresenceSizePixels.Should().BeNull();
    }

    [Theory]
    [InlineData("disposed")]
    [InlineData("locked")]
    public async Task NativePreferenceSettersRefuseIneligibleHostsWithoutPersisting(string state)
    {
        var fixture = await Fixture.CreateInitializedAsync();
        if (state is "disposed") { fixture.ViewModel.Dispose(); }
        else { fixture.Session.IsUnlocked = false; }
        fixture.ViewModel.LocalModelsEnabled = false;
        await fixture.ViewModel.PreferenceUpdate;
        fixture.ModelExecutionPreferences.Settings.LocalModelsEnabled.Should().BeTrue();
    }

    [Theory]
    [InlineData("disposed")]
    [InlineData("exit")]
    [InlineData("disposed-failure")]
    public async Task NativePreferenceQueueRetiresOnHostClosure(string state)
    {
        var fixture = await Fixture.CreateInitializedAsync();
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var token = TestContext.Current.CancellationToken;
        fixture.AppearancePreferences.BeforePresencePreferenceSave = () => { entered.SetResult(); release.Wait(token); };
        fixture.ViewModel.QueuePresencePosition(new(10, 20));
        Task closing = Task.CompletedTask;
        try
        {
            await entered.Task.WaitAsync(token);
            fixture.ViewModel.QueueResponseWindowPosition(new(30, 40));
            if (state is "exit") { closing = fixture.ViewModel.ExitAsync(); }
            else { fixture.ViewModel.Dispose(); }
            if (state is "disposed-failure") { fixture.AppearancePreferences.SaveException = new IOException("Fixture late failure."); }
        }
        finally { release.Set(); }
        await fixture.ViewModel.PreferenceUpdate;
        await closing;
        fixture.AppearancePreferences.SavedResponseWindowSettings.Should().BeNull();
    }

    [Fact]
    public async Task AnActivePreferenceWriteRefusesAConcurrentExplicitWriteAndFailedModelStateStaysHeld()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var token = TestContext.Current.CancellationToken;
        fixture.AppearancePreferences.BeforePresencePreferenceSave = () => { entered.SetResult(); release.Wait(token); };
        var first = fixture.ViewModel.SetPresencePositionAsync(new(10, 20));
        try
        {
            await entered.Task.WaitAsync(token);
            (await fixture.ViewModel.SetResponseWindowPositionAsync(new(30, 40))).Should().BeFalse();
            fixture.ViewModel.ResponseTitle.Should().Be("Preference change busy.");
        }
        finally { release.Set(); }
        await first;

        fixture.ApprovalPreferences.Save(new(true, [BuiltInAction.LockMachine]));
        fixture.ModelExecutionPreferences.SaveFailure = new IOException("Fixture model write failure.");
        fixture.ViewModel.LocalModelsEnabled = false;
        await fixture.ViewModel.PreferenceUpdate;
        fixture.ViewModel.LocalModelsEnabled = false;
        fixture.ViewModel.RequireAssistantNameForVoiceApproval = false;
        await fixture.ViewModel.PreferenceUpdate;
        var save = typeof(MainViewModel).GetMethod("SaveModelApprovalPreferencesAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
            .CreateDelegate<Func<bool, IEnumerable<BuiltInAction>, SecurityAuditInitiator, Func<bool>?, Task<bool>>>(fixture.ViewModel);
        (await save(true, [], SecurityAuditInitiator.LocalUser, null)).Should().BeFalse();
        fixture.ViewModel.ResponseTitle.Should().Be("Model preferences are unavailable.");
    }

    [Theory]
    [InlineData("disposed")]
    [InlineData("exit")]
    public async Task RefreshCannotPublishAfterOutputEnumerationClosesTheHost(string transition)
    {
        var fixture = await Fixture.CreateInitializedAsync();
        Task closing = Task.CompletedTask;
        fixture.TextToSpeech.BeforeOutputEnumeration = () =>
        {
            if (transition is "disposed") { fixture.ViewModel.Dispose(); }
            else { closing = fixture.ViewModel.ExitAsync(); }
        };
        await fixture.ViewModel.RefreshCommand.ExecuteAsync();
        await closing;
        fixture.ViewModel.IsBusy.Should().BeFalse();
    }

    [Theory]
    [InlineData("disposed")]
    [InlineData("exit")]
    [InlineData("topology")]
    [InlineData("selection")]
    public async Task OutputDiscoveryFencesLateMetadataAndNeverStartsPreview(string transition)
    {
        var fixture = await Fixture.CreateInitializedAsync();
        Task closing = Task.CompletedTask;
        fixture.TextToSpeech.BeforeOutputEnumeration = () =>
        {
            switch (transition)
            {
                case "disposed": fixture.ViewModel.Dispose(); break;
                case "exit": closing = fixture.ViewModel.ExitAsync(); break;
                case "topology":
                    fixture.PrivacyObservation.Current = fixture.PrivacyObservation.Current with { TopologyRevision = 999 };
                    break;
                default:
                    typeof(MainViewModel).GetField("selectedOutputDevice", BindingFlags.Instance | BindingFlags.NonPublic)!
                        .SetValue(fixture.ViewModel, new AudioOutputDevice("changed", "Changed"));
                    break;
            }
        };
        await fixture.ViewModel.PreviewVoiceCommand.ExecuteAsync();
        await closing;
        fixture.TextToSpeech.SpokenText.Should().NotBe("Hello, I'm Kora.");
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("disposed-cancel")]
    [InlineData("superseded-cancel")]
    [InlineData("disposed-failure")]
    [InlineData("superseded-failure")]
    public async Task ConsentPersistenceFailureAndSupersessionCannotReactivateCapture(string transition)
    {
        var fixture = await Fixture.CreateInitializedAsync();
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var token = TestContext.Current.CancellationToken;
        fixture.VoiceConsent.BeforeSave = value =>
        {
            if (!value) { return; }
            entered.SetResult();
            release.Wait(token);
            if (transition.EndsWith("failure", StringComparison.Ordinal)) { throw new IOException("Fixture consent failure."); }
            throw new OperationCanceledException("Fixture retired consent.");
        };
        var grant = fixture.ViewModel.SetVoiceConsentAsync(true);
        Task withdrawal = Task.CompletedTask;
        try
        {
            await entered.Task.WaitAsync(token);
            if (transition.StartsWith("disposed", StringComparison.Ordinal)) { fixture.ViewModel.Dispose(); }
            if (transition.StartsWith("superseded", StringComparison.Ordinal)) { withdrawal = fixture.ViewModel.SetVoiceConsentAsync(false); }
        }
        finally { release.Set(); }
        await Task.WhenAll(grant, withdrawal);
        await fixture.ViewModel.SetVoiceConsentAsync(false);
        fixture.ViewModel.IsVoiceEnabled.Should().BeFalse();
    }
}
