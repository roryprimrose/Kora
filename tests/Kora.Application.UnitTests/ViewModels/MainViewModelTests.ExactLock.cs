using System.Reflection;

using AwesomeAssertions;

using Kora.Core.Auditing;
using Kora.Core.Commands;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Skills;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Theory]
    [InlineData("Revision")]
    [InlineData("OwnershipBindingRevision")]
    [InlineData("AssistantNameRevision")]
    public async Task ExactLockRejectsIncoherentCapturedInputBeforeTransferringActivation(string property)
    {
        var fixture = await Fixture.CreateInitializedAsync();
        var capture = typeof(Kora.Application.ViewModels.MainViewModel).GetMethod("CaptureManualCallInput",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        var input = capture.Invoke(fixture.ViewModel, [RequestOrigin.LocalUi, fixture.ViewModel.CallPolicyRevision, null, false])!;
        var transfer = typeof(Kora.Application.ViewModels.MainViewModel).GetMethod("CaptureLockSource",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        ((Func<bool>)transfer.Invoke(fixture.ViewModel, [input])!).Invoke().Should().BeTrue();
        // Supplemental envelope corruption exercises states rejected before the public router can transfer activation.
        input.GetType().GetProperty(property, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
            .SetValue(input, -1L);
        var admitted = (Func<bool>)transfer.Invoke(fixture.ViewModel, [input])!;
        admitted().Should().BeFalse();
        fixture.Session.LockCalls.Should().Be(0);
        fixture.Audit.Events.Should().NotContain(item => item.ActionId == "session.lock");
    }

    private static void BindAdmittedLockResourceOwner(Fixture fixture) =>
        fixture.ViewModel.BindBundledActions(async (action, _, current) =>
        {
            action.Should().Be(FixedSessionAction.Lock);
            using var prepared = await fixture.ViewModel.PrepareBundledResourcesAsync(action, current,
                _ => throw new InvalidOperationException("Lock cannot request computer power."));
            await prepared!.StopAndJoinAsync();
            prepared.IsCurrent.Should().BeTrue();
            fixture.Session.LockCurrentSession().Should().BeTrue();
        });

    [Theory]
    [InlineData("typed", "none")]
    [InlineData("voice", "none")]
    [InlineData("native", "none")]
    [InlineData("model-typed", "none")]
    [InlineData("model-voice", "none")]
    [InlineData("model-typed", "session")]
    [InlineData("model-voice", "always")]
    public async Task ExactLockRoutesShareOneOwnerWithoutLegacyApprovalOrNativeFallback(string route, string legacy)
    {
        var fixture = new Fixture();
        fixture.Probe.Status = new("local.inference", "Local model", DependencyReadiness.Ready, "Synthetic ready seam.");
        fixture.Reasoner.Action = BuiltInAction.LockMachine;
        if (legacy is "always") { fixture.ApprovalPreferences.Save(new(true, [BuiltInAction.LockMachine])); }
        await fixture.ViewModel.InitializeAsync();
        if (legacy is "session")
        {
            await fixture.ViewModel.PrepareGrantChangeAsync(new(GrantChangeOperation.Add,
                BuiltInAction.LockMachine, ModelApprovalScope.Session));
            await fixture.ViewModel.ConfirmGrantChangeAsync();
        }
        var calls = 0;
        var joined = 0;
        var origin = route.Contains("voice", StringComparison.Ordinal)
            ? RequestOrigin.ActivatedVoice : RequestOrigin.LocalUi;
        fixture.ViewModel.BindBundledActions(async (action, initiator, current) =>
        {
            calls++;
            action.Should().Be(FixedSessionAction.Lock);
            current().Should().BeTrue();
            HostActivity.RequireCurrent().Request.Origin.Should().Be(origin);
            initiator.Should().Be(route.StartsWith("model", StringComparison.Ordinal)
                ? SecurityAuditInitiator.ModelSuggestion
                : route is "voice" ? SecurityAuditInitiator.VoiceCommand
                : route is "native" ? SecurityAuditInitiator.LocalUser : SecurityAuditInitiator.TypedCommand);
            fixture.Session.LockCalls.Should().Be(0);
            using var prepared = await fixture.ViewModel.PrepareBundledResourcesAsync(action, current,
                _ => throw new InvalidOperationException("Lock cannot request computer power."));
            await prepared!.StopAndJoinAsync();
            current().Should().BeTrue();
            joined++;
        });
        if (route is "native")
        {
            using var original = HostActivity.BeginRoot(HostRequest.Create(origin),
                HostActivityLayer.Application, HostOperation.Request);
            await fixture.ViewModel.ExecuteAsync(fixture.Catalog.GetCommands().Single(item => item.Action == BuiltInAction.LockMachine),
                SecurityAuditInitiator.LocalUser);
        }
        else if (route.Contains("voice", StringComparison.Ordinal))
        {
            await fixture.RaiseActivatedTranscriptAsync(route is "voice" ? "Kora, lock the machine" : "Kora, please secure this workstation", 1);
        }
        else { await fixture.RunAsync(route is "typed" ? "lock the machine" : "please secure this workstation"); }
        if (route.StartsWith("model", StringComparison.Ordinal))
        {
            await fixture.ViewModel.ActiveReasoningTask!.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        }
        calls.Should().Be(1);
        joined.Should().Be(1);
        fixture.Session.LockCalls.Should().Be(0);
        fixture.ViewModel.IsModelActionApprovalPending.Should().BeFalse();
        fixture.Audit.Events.Should().NotContain(item => item.ActionId == "model.action.lockmachine");
        fixture.ApprovalPreferences.Preferences.AlwaysAllowedActions.Should().HaveCount(legacy is "always" ? 1 : 0);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task MissingExactLockOwnerCannotUseNativeApiOrSavedNamedPermission(bool model, bool saved)
    {
        var fixture = new Fixture();
        fixture.Probe.Status = new("local.inference", "Local model", DependencyReadiness.Ready, "Synthetic ready seam.");
        fixture.Reasoner.Action = BuiltInAction.LockMachine;
        if (saved) { fixture.ApprovalPreferences.Save(new(true, [BuiltInAction.LockMachine])); }
        await fixture.ViewModel.InitializeAsync();
        await fixture.RunAsync(model ? "please secure this workstation" : "lock the machine");
        if (model) { await fixture.ViewModel.ActiveReasoningTask!; }
        fixture.ViewModel.ResponseBody.Should().Contain("Exact Lock admission is unavailable");
        fixture.Session.LockCalls.Should().Be(0);
        fixture.ViewModel.IsModelActionApprovalPending.Should().BeFalse();
        fixture.Audit.Events.Should().NotContain(item => item.ActionId == "session.lock"
            && item.Outcome == SecurityAuditOutcome.Succeeded);
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("owner-binding")]
    [InlineData("call")]
    [InlineData("privacy")]
    [InlineData("cancel")]
    public async Task ModelLockRetainsOriginalAuthorityAcrossInference(string change)
    {
        var fixture = new Fixture();
        fixture.Probe.Status = new("local.inference", "Local model", DependencyReadiness.Ready, "Synthetic ready seam.");
        fixture.Reasoner.Action = BuiltInAction.LockMachine;
        fixture.Reasoner.DecisionGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await fixture.ViewModel.InitializeAsync();
        var revision = 1L;
        fixture.ViewModel.BindCallOwnershipGate(() => true, () => revision);
        var calls = 0;
        fixture.ViewModel.BindBundledActions((_, _, _) => { calls++; return Task.CompletedTask; });
        await fixture.RunAsync("please secure this workstation");
        var running = fixture.ViewModel.ActiveReasoningTask!;
        calls.Should().Be(0);
        running.IsCompleted.Should().BeFalse();
        switch (change)
        {
            case "owner": revision++; break;
            case "owner-binding": fixture.ViewModel.BindCallOwnershipGate(() => true, () => revision); break;
            case "call": fixture.CallState.SetState(Kora.Core.Communication.CallState.Active); break;
            case "privacy":
                fixture.PrivacyObservation.Current = fixture.PrivacyObservation.Current with { TopologyRevision = 10 };
                break;
            case "cancel": await fixture.ViewModel.CancelCurrentTaskAsync(); break;
        }
        fixture.Reasoner.DecisionGate.TrySetResult();
        await running;
        calls.Should().Be(0);
        fixture.Session.LockCalls.Should().Be(0);
        fixture.ViewModel.IsModelActionApprovalPending.Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PrivacyClosureJoinsTheExactOwnerWithoutPublishingItsLateReply(bool fail)
    {
        var fixture = await Fixture.CreateInitializedAsync();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.ViewModel.BindBundledActions((_, _, _) => Task.CompletedTask, async () =>
        {
            entered.SetResult();
#pragma warning disable VSTHRD003 // The explicit barrier models the existing owner physically joining after cancellation.
            await release.Task;
#pragma warning restore VSTHRD003
            if (fail) { throw new IOException("Synthetic Lock resource join failure."); }
        });
        fixture.ViewModel.CloseForObservedPrivacyEvent("Synthetic locked session", true);
        await entered.Task.WaitAsync(TestContext.Current.CancellationToken);
        fixture.ViewModel.PrivacyClosureTask.IsCompleted.Should().BeFalse();
        fixture.Session.LockCalls.Should().Be(0);
        release.SetResult();
        await fixture.ViewModel.PrivacyClosureTask;
        fixture.ViewModel.ResponseTitle.Should().Be(fail ? "Audio privacy closure needs attention." : "Windows privacy recovery");
        fixture.Session.LockCalls.Should().Be(0);
    }
}
