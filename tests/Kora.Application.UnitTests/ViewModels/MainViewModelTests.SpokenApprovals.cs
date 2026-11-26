using AwesomeAssertions;
using Kora.Core.Communication;
using Kora.Core.Auditing;
using Kora.Core.Commands;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Authorization.SessionPermission;
using Kora.Core.Voice;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AutomaticSpokenControlWithoutLiveSelectionStillRequiresExplicitReview(bool released)
    {
        var f = await Fixture.CreateInitializedAsync();
        var store = BindPermissionCommands(f);
        var id = store.Session.Authority.SessionId.Value.ToString("D");
        await f.RunAsync("session permission review " + id + " approve-all");
        await f.RunAsync("session permission confirm " + id);
        if (released)
        {
            var selector = BindReleasedPermissionTarget(f.ViewModel.SessionControlWorkspace!, store.Session);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            selector.IsAlive.Should().BeFalse();
        }
        await f.ViewModel.ReviewSpokenApprovalsCommand.ExecuteAsync();
        f.CallPreferences.SavedSettings.Should().BeNull();
        store.AutomaticAudits.Should().BeEmpty();
        f.ViewModel.SpokenApprovalsStatus.Should().Contain("Review:");
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static WeakReference BindReleasedPermissionTarget(
        Kora.Application.Hosting.SessionWorkspaceService workspace, Kora.Core.Storage.SessionWorkspaceEntry entry)
    {
        Func<Kora.Core.Storage.SessionWorkspaceEntry?> read = () => entry;
        workspace.BindPermissionControlTarget(read);
        return new(read);
    }

    [Theory]
    [InlineData("success")]
    [InlineData("native-parent")]
    [InlineData("audit-failure")]
    [InlineData("retired")]
    [InlineData("target-after-read")]
    public async Task ApproveAllSpokenApprovalControlSkipsConfirmationOnlyWithCurrentAuditedSession(string scenario)
    {
        var f = await Fixture.CreateInitializedAsync();
        var store = BindPermissionCommands(f);
        await f.RunAsync("session permission review " + store.Session.Authority.SessionId.Value.ToString("D") + " approve-all");
        await f.RunAsync("session permission confirm " + store.Session.Authority.SessionId.Value.ToString("D"));
        store.FailAutomaticAudit = scenario is "audit-failure";
        store.RetireAfterAutomaticAudit = scenario is "retired";
        Kora.Core.Storage.SessionWorkspaceEntry? target = store.Session;
        Func<Kora.Core.Storage.SessionWorkspaceEntry?> selected = () => target;
        f.ViewModel.SessionControlWorkspace!.BindPermissionControlTarget(selected);
        f.ViewModel.CurrentPermissionControlTarget.Should().Be(store.Session);
        var reads = 0;
        store.AfterPermissionRead = () =>
        {
            if (scenario is "target-after-read" && ++reads == 2) { target = null; }
        };
        using var parent = scenario is "native-parent" ? HostActivity.BeginRoot(
            new(new(Guid.NewGuid()), store.Session.Authority.SessionId, new(Guid.NewGuid()), RequestOrigin.LocalUi),
            HostActivityLayer.Application, HostOperation.Policy) : null;
        var review = () => f.ViewModel.ReviewSpokenApprovalsCommand.ExecuteAsync();
        if (scenario is "success" or "native-parent")
        {
            await review();
            f.CallPreferences.SavedSettings!.AllowSpokenApprovalsDuringCalls.Should().BeTrue();
            var audit = store.AutomaticAudits.Should().ContainSingle().Subject;
            audit.Policy.Level.Should().Be(SessionPermissionLevel.ApproveAll);
            audit.PromptCategory.Should().Be("call-spoken-approval-control");
            audit.ContentDigest.Should().HaveLength(64);
            audit.GrantsEvaluated.Should().BeFalse();
        }
        else
        {
            if (scenario is "audit-failure") { await review.Should().ThrowAsync<IOException>(); }
            else { await review(); }
            f.CallPreferences.SavedSettings.Should().BeNull();
            f.ViewModel.SpokenApprovalsStatus.Should().Contain("Off");
        }
        GC.KeepAlive(selected);
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
