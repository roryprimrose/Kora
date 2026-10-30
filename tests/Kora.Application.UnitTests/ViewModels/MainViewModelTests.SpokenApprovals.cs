using AwesomeAssertions;
using Kora.Core.Communication;
using Kora.Core.Auditing;
using Kora.Core.Commands;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Voice;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Fact]
    public async Task Model_dispatch_without_host_admission_or_during_preference_write_cannot_execute()
    {
        var f = await Fixture.CreateInitializedAsync();
        var command = f.ViewModel.Commands.Single(item => item.Action == BuiltInAction.LockMachine);
        await f.ViewModel.ExecuteAsync(command, SecurityAuditInitiator.ModelSuggestion);
        f.Session.LockCalls.Should().Be(0);
        Task? reentry = null;
        f.Audit.BeforeWrite = audit =>
        {
            if (audit.Category == SecurityAuditCategory.ConfigurationWrite && reentry is null)
            { reentry = f.ViewModel.ExecuteAsync(command, SecurityAuditInitiator.ModelSuggestion, f.ViewModel.CallPolicyRevision); }
        };
        await f.ViewModel.PrepareGrantChangeAsync(new(GrantChangeOperation.Add, BuiltInAction.LockMachine, ModelApprovalScope.Always));
        await f.ViewModel.ConfirmGrantChangeAsync();
        if (reentry is not null) { await reentry; }
        f.Session.LockCalls.Should().Be(0);
        f.Audit.BeforeWrite = null;
        f.ApprovalPreferences.SaveFailure = new IOException("Synthetic preference failure.");
        await f.ViewModel.RevokeModelActionApprovalAsync(BuiltInAction.LockMachine, ModelApprovalScope.Always);
        await f.ViewModel.ExecuteAsync(command, SecurityAuditInitiator.ModelSuggestion, f.ViewModel.CallPolicyRevision);
        f.Session.LockCalls.Should().Be(0);
    }
    [Fact]
    public async Task Rejected_spoken_action_approval_retains_the_native_pending_decision_without_replay()
    {
        var f = new Fixture();
        f.Voice.Microphones = [new MicrophoneDevice("fixture", "Synthetic microphone")];
        f.Voice.DefaultMicrophoneId = "fixture";
        f.Probe.Status = new("local.inference", "Local model", DependencyReadiness.Ready, "Ready");
        f.Reasoner.Action = BuiltInAction.EnableHostedModels;
        await f.ViewModel.InitializeAsync();
        f.CallState.SetState(CallState.Active);
        await f.ViewModel.CallClosureTask;
        await f.RunAsync("please secure this computer");
        await f.ViewModel.ActiveReasoningTask!;
        f.ViewModel.IsModelActionApprovalPending.Should().BeTrue();
        await f.RaiseActivatedTranscriptAsync("Kora, approve once", 0.9f);
        f.ViewModel.HostedModelsEnabled.Should().Be(false);
        f.Session.LockCalls.Should().Be(0);
        f.ViewModel.IsModelActionApprovalPending.Should().BeTrue();
        f.ViewModel.ResponseTitle.Should().Contain("Spoken approvals disabled");
        await f.ViewModel.ApproveModelActionCommand.ExecuteAsync();
        f.ViewModel.HostedModelsEnabled.Should().Be(true);
        f.Session.LockCalls.Should().Be(0);
        f.ApprovalPreferences.Preferences.AlwaysAllowedActions.Should().BeEmpty();
    }
    [Fact]
    public async Task Spoken_approval_setting_requires_fresh_native_review_and_explicit_downgrade_confirmation()
    {
        var f = await Fixture.CreateInitializedAsync();
        f.ViewModel.SpokenApprovalsStatus.Should().Contain("Off");
        await FluentActions.Awaiting(() => f.ViewModel.ConfirmSpokenApprovalsCommand.ExecuteAsync())
            .Should().ThrowAsync<InvalidOperationException>();
        await f.ViewModel.ReviewSpokenApprovalsCommand.ExecuteAsync();
        f.ViewModel.SpokenApprovalsStatus.Should().Contain("downgrade");
        f.CallPreferences.SavedSettings.Should().BeNull();
        await f.ViewModel.CancelSpokenApprovalsCommand.ExecuteAsync();
        f.ViewModel.CancelSpokenApprovalsCommand.Should().BeSameAs(f.ViewModel.CancelSpokenApprovalsCommand);
        f.ViewModel.SpokenApprovalsStatus.Should().Contain("Off");
        f.CallState.SetState(CallState.Active);
        await f.ViewModel.CallClosureTask;
        await f.ViewModel.ReviewSpokenApprovalsCommand.ExecuteAsync();
        await f.ViewModel.ConfirmSpokenApprovalsCommand.ExecuteAsync();
        f.CallPreferences.SavedSettings!.AllowSpokenApprovalsDuringCalls.Should().BeTrue();
        f.ViewModel.SpokenApprovalsStatus.Should().Contain("On");
        await f.ViewModel.ReviewSpokenApprovalsCommand.ExecuteAsync();
        f.ViewModel.SpokenApprovalsStatus.Should().Contain("disable");
        await f.ViewModel.ConfirmSpokenApprovalsCommand.ExecuteAsync();
        f.CallPreferences.SavedSettings.AllowSpokenApprovalsDuringCalls.Should().BeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Spoken_approval_review_cannot_survive_changed_policy_privacy_or_ownership(int change)
    {
        var f = await Fixture.CreateInitializedAsync();
        await f.ViewModel.ReviewSpokenApprovalsCommand.ExecuteAsync();
        if (change == 0) { f.CallState.SetState(CallState.Unknown); await f.ViewModel.CallClosureTask; }
        if (change == 1) { f.Session.IsUnlocked = false; }
        if (change == 2) { f.ViewModel.BindCallOwnershipGate(static () => false); }
        await FluentActions.Awaiting(() => f.ViewModel.ConfirmSpokenApprovalsCommand.ExecuteAsync())
            .Should().ThrowAsync<InvalidOperationException>();
        f.CallPreferences.SavedSettings.Should().BeNull();
    }

    [Theory]
    [InlineData(RequestOrigin.ActivatedVoice)]
    [InlineData(RequestOrigin.HostSystem)]
    public async Task Spoken_approval_control_never_relabels_ambient_origin(RequestOrigin origin)
    {
        var f = await Fixture.CreateInitializedAsync();
        using var ambient = HostActivity.BeginRoot(HostRequest.Create(origin), HostActivityLayer.Application, HostOperation.Request);
        await FluentActions.Awaiting(() => f.ViewModel.ReviewSpokenApprovalsCommand.ExecuteAsync())
            .Should().ThrowAsync<InvalidOperationException>();
        f.CallPreferences.SavedSettings.Should().BeNull();
    }

    [Fact]
    public async Task Invocation_completed_context_and_changed_owner_cannot_confirm_spoken_approval_changes()
    {
        var f = await Fixture.CreateInitializedAsync();
        var request = HostRequest.Create(RequestOrigin.LocalUi);
        using (var callback = HostActivity.BeginRoot(new(request.RequestId, request.SessionId, request.TaskId,
            RequestOrigin.LocalUi, new(Guid.NewGuid())), HostActivityLayer.Application, HostOperation.Tool))
        {
            await FluentActions.Awaiting(() => f.ViewModel.ReviewSpokenApprovalsCommand.ExecuteAsync())
                .Should().ThrowAsync<InvalidOperationException>();
        }
        using (var stopped = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request))
        {
            stopped.Activity!.Stop();
            await FluentActions.Awaiting(() => f.ViewModel.ReviewSpokenApprovalsCommand.ExecuteAsync())
                .Should().ThrowAsync<InvalidOperationException>();
        }
        long owner = 1;
        f.ViewModel.BindCallOwnershipGate(static () => true, () => owner);
        await f.ViewModel.ReviewSpokenApprovalsCommand.ExecuteAsync();
        owner++;
        await FluentActions.Awaiting(() => f.ViewModel.ConfirmSpokenApprovalsCommand.ExecuteAsync())
            .Should().ThrowAsync<InvalidOperationException>();
        f.ViewModel.AuthorizationPolicyGate.Should().NotBeNull();
        f.CallPreferences.SavedSettings.Should().BeNull();
    }
}
