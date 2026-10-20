using AwesomeAssertions;

using Kora.Core.Auditing;
using Kora.Core.Commands;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Fact]
    public async Task SupersededPreparationStillHoldsHandoffUntilItsOwnedAuditFinishes()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var token = TestContext.Current.CancellationToken;
        var requested = 0;
        fixture.Audit.BeforeWrite = audit =>
        {
            if (audit.Outcome != SecurityAuditOutcome.Requested
                || Interlocked.Increment(ref requested) != 1) { return; }
            entered.TrySetResult();
            release.Wait(token);
        };
        var first = fixture.ViewModel.PrepareGrantChangeAsync(
            new(GrantChangeOperation.Add, BuiltInAction.LockMachine, ModelApprovalScope.Session));
        try
        {
            await entered.Task.WaitAsync(token);
            await fixture.ViewModel.PrepareGrantChangeAsync(
                new(GrantChangeOperation.Add, BuiltInAction.ProposeRestart, ModelApprovalScope.Session));
            first.IsCompleted.Should().BeFalse();
            (await fixture.ViewModel.TryPrepareHandoffAsync()).Should().BeFalse();
        }
        finally { release.Set(); }
        await first;
        fixture.ViewModel.IsGrantChangePending.Should().BeTrue();
        fixture.ViewModel.ResponseBody.Should().Contain(nameof(BuiltInAction.ProposeRestart));
        fixture.ViewModel.SessionAllowedModelActions.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TerminalReceiptCannotRestoreAConcurrentRevocationOrDispatchItsRetiredContinuation(bool modelAction)
    {
        var fixture = new Fixture();
        fixture.Probe.Status = new("local.inference", "Local model",
            Kora.Core.Dependencies.DependencyReadiness.Ready, "Ready");
        fixture.ApprovalPreferences.Save(new(true, [BuiltInAction.ProposeRestart]));
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
                new(GrantChangeOperation.Add, BuiltInAction.LockMachine, ModelApprovalScope.Session));
        }
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var token = TestContext.Current.CancellationToken;
        fixture.Audit.BeforeWrite = audit =>
        {
            if (audit.Category != SecurityAuditCategory.SecurityApproval
                || audit.Outcome != SecurityAuditOutcome.Succeeded) { return; }
            entered.TrySetResult();
            release.Wait(token);
        };
        var confirmation = modelAction ? fixture.ViewModel.ApproveModelActionForSessionCommand.ExecuteAsync()
            : fixture.ViewModel.ConfirmGrantChangeAsync();
        try
        {
            await entered.Task.WaitAsync(token);
            await fixture.ViewModel.RevokeModelActionApprovalAsync(BuiltInAction.ProposeRestart, ModelApprovalScope.Always);
        }
        finally { release.Set(); }
        await confirmation;
        fixture.ApprovalPreferences.Preferences.AlwaysAllowedActions.Should().BeEmpty();
        fixture.ViewModel.AlwaysAllowedModelActions.Should().BeEmpty();
        fixture.ViewModel.SessionAllowedModelActions.Should().BeEmpty();
        fixture.Session.LockCalls.Should().Be(0);
    }

    [Fact]
    public async Task ForcedDisposalJoinsAnUnpublishedRequestedAuditWithoutPublishingLateApproval()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var token = TestContext.Current.CancellationToken;
        fixture.Audit.BeforeWrite = audit =>
        {
            if (audit.Outcome != SecurityAuditOutcome.Requested) { return; }
            entered.TrySetResult();
            release.Wait(token);
        };
        var preparation = fixture.ViewModel.PrepareGrantChangeAsync(
            new(GrantChangeOperation.Add, BuiltInAction.LockMachine, ModelApprovalScope.Session));
        await entered.Task.WaitAsync(token);
        var dispose = fixture.ViewModel.DisposeAsync().AsTask();
        try { dispose.IsCompleted.Should().BeFalse(); }
        finally { release.Set(); }
        await Task.WhenAll(dispose, preparation);
        fixture.ViewModel.IsGrantChangePending.Should().BeFalse();
        fixture.ViewModel.SessionAllowedModelActions.Should().BeEmpty();
        fixture.Audit.Events.Should().NotContain(item => item.Outcome == SecurityAuditOutcome.Succeeded);
    }

    [Fact]
    public async Task SingleUseActionWaitsForItsApprovalAuditAndHideRetiresDispatchWhileItWaits()
    {
        var fixture = new Fixture();
        fixture.Probe.Status = new("local.inference", "Local model",
            Kora.Core.Dependencies.DependencyReadiness.Ready, "Ready");
        fixture.Reasoner.Action = BuiltInAction.LockMachine;
        await fixture.ViewModel.InitializeAsync();
        await fixture.RunAsync("please lock this workstation");
        await fixture.ViewModel.ActiveReasoningTask!;
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var token = TestContext.Current.CancellationToken;
        fixture.Audit.BeforeWrite = audit =>
        {
            if (audit.Category != SecurityAuditCategory.SecurityApproval
                || audit.Outcome != SecurityAuditOutcome.Succeeded) { return; }
            entered.TrySetResult();
            release.Wait(token);
        };
        var approval = fixture.ViewModel.ApproveModelActionCommand.ExecuteAsync();
        try
        {
            await entered.Task.WaitAsync(token);
            approval.IsCompleted.Should().BeFalse();
            fixture.Session.LockCalls.Should().Be(0);
            await fixture.ViewModel.HideApplicationAsync();
        }
        finally { release.Set(); }
        await approval;
        fixture.Session.LockCalls.Should().Be(0);
        fixture.ViewModel.IsModelActionApprovalPending.Should().BeFalse();
    }

    [Fact]
    public async Task RequestedGrantAuditYieldsAndNewInputRetiresTheUnpublishedPreparation()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var token = TestContext.Current.CancellationToken;
        fixture.Audit.BeforeWrite = audit =>
        {
            if (audit.Category != SecurityAuditCategory.SecurityApproval
                || audit.Outcome != SecurityAuditOutcome.Requested) { return; }
            entered.TrySetResult();
            release.Wait(token);
        };
        var preparation = fixture.ViewModel.PrepareGrantChangeAsync(
            new(GrantChangeOperation.Add, BuiltInAction.LockMachine, ModelApprovalScope.Session));
        try
        {
            await entered.Task.WaitAsync(token);
            preparation.IsCompleted.Should().BeFalse();
            fixture.ViewModel.IsGrantChangePending.Should().BeFalse();
            fixture.ViewModel.ConfirmGrantChangeCommand.CanExecute(null).Should().BeFalse();
            (await fixture.ViewModel.TryPrepareHandoffAsync()).Should().BeFalse();
            fixture.ViewModel.ShowSettings();
            await fixture.RunAsync("what power action is pending");
        }
        finally { release.Set(); }
        await preparation;
        fixture.ViewModel.IsGrantChangePending.Should().BeFalse();
        fixture.ViewModel.SessionAllowedModelActions.Should().BeEmpty();
        fixture.Audit.Events.Should().Contain(item => item.Outcome == SecurityAuditOutcome.Cancelled
            && string.Equals(item.ReasonCode, "grant-preparation-retired", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GrantConfirmationDoesNotPublishReusablePermissionBeforeTheTerminalAudit(bool hideDuringCommit)
    {
        var fixture = await Fixture.CreateInitializedAsync();
        await fixture.ViewModel.PrepareGrantChangeAsync(
            new(GrantChangeOperation.Add, BuiltInAction.LockMachine, ModelApprovalScope.Session));
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var token = TestContext.Current.CancellationToken;
        fixture.Audit.BeforeWrite = audit =>
        {
            if (audit.Category != SecurityAuditCategory.SecurityApproval
                || audit.Outcome != SecurityAuditOutcome.Succeeded) { return; }
            entered.TrySetResult();
            release.Wait(token);
        };
        var confirmation = fixture.ViewModel.ConfirmGrantChangeAsync();
        try
        {
            await entered.Task.WaitAsync(token);
            confirmation.IsCompleted.Should().BeFalse();
            fixture.ViewModel.IsGrantChangePending.Should().BeFalse();
            fixture.ViewModel.SessionAllowedModelActions.Should().BeEmpty();
            if (hideDuringCommit) { await fixture.ViewModel.HideApplicationAsync(); }
        }
        finally { release.Set(); }
        await confirmation;
        fixture.ViewModel.SessionAllowedModelActions.Should().HaveCount(hideDuringCommit ? 0 : 1);
        fixture.Session.LockCalls.Should().Be(0);
    }

    [Fact]
    public async Task RequiredTerminalAuditFailureCannotPublishAGrantOrAdmitLaterActions()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        await fixture.ViewModel.PrepareGrantChangeAsync(
            new(GrantChangeOperation.Add, BuiltInAction.LockMachine, ModelApprovalScope.Session));
        fixture.Audit.BeforeWrite = audit =>
        {
            if (audit.Outcome == SecurityAuditOutcome.Succeeded)
            {
                throw new IOException("Fixture required terminal audit failure.");
            }
        };
        var confirmation = () => fixture.ViewModel.ConfirmGrantChangeAsync();
        await confirmation.Should().ThrowAsync<IOException>();
        fixture.ViewModel.SessionAllowedModelActions.Should().BeEmpty();
        fixture.ViewModel.CanRevealPrivatePresentation.Should().BeFalse();
        await fixture.RunAsync("lock the machine");
        fixture.Session.LockCalls.Should().Be(0);
    }

    [Fact]
    public async Task ConsentWithdrawalClosesCaptureBeforeSlowPersistenceAndTheLatestChoiceWins()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        await fixture.ViewModel.BeginPushToTalkAsync();
        fixture.Voice.IsListening.Should().BeTrue();
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var token = TestContext.Current.CancellationToken;
        fixture.VoiceConsent.BeforeSave = consent =>
        {
            if (!consent) { return; }
            entered.TrySetResult();
            release.Wait(token);
        };
        var granting = fixture.ViewModel.SetVoiceConsentAsync(true);
        Task withdrawal;
        try
        {
            await entered.Task.WaitAsync(token);
            withdrawal = fixture.ViewModel.SetVoiceConsentAsync(false);
            fixture.ViewModel.HasVoiceConsent.Should().BeFalse();
            fixture.Voice.IsListening.Should().BeFalse();
            fixture.Voice.StopCalls.Should().BeGreaterThan(0);
            withdrawal.IsCompleted.Should().BeFalse();
            fixture.ViewModel.ShowSettings();
        }
        finally { release.Set(); }
        await Task.WhenAll(granting, withdrawal);
        fixture.VoiceConsent.Consent.Should().BeFalse();
        fixture.ViewModel.HasVoiceConsent.Should().BeFalse();
        fixture.ViewModel.IsVoiceEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task CancellingDuringRequestedPowerAuditCannotPublishTheProposalAfterTheReceipt()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var token = TestContext.Current.CancellationToken;
        fixture.Audit.BeforeWrite = audit =>
        {
            if (!string.Equals(audit.ActionId, "power.shutdown", StringComparison.Ordinal)
                || audit.Outcome != SecurityAuditOutcome.Requested) { return; }
            entered.TrySetResult();
            release.Wait(token);
        };
        var command = fixture.Catalog.GetCommands("Kora").Single(item => item.Action == BuiltInAction.ProposeShutdown);
        var preparing = fixture.ViewModel.ExecuteAsync(command, SecurityAuditInitiator.TypedCommand);
        try
        {
            await entered.Task.WaitAsync(token);
            preparing.IsCompleted.Should().BeFalse();
            await fixture.ViewModel.CancelCurrentTaskAsync();
        }
        finally { release.Set(); }
        await preparing;
        await fixture.RunAsync("what power action is pending");
        fixture.ViewModel.ResponseTitle.Should().Be("No power action is pending.");
    }
}
