using AwesomeAssertions;
using System.Reflection;
using Kora.Application.Presentation;
using Kora.Application.ViewModels;
using Kora.Core;
using Kora.Core.Auditing;
using Kora.Core.Commands;
using Kora.Core.Communication;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    private static async Task<Fixture> CreateModelActionReviewFixtureAsync(bool voice = false)
    {
        var f = voice ? CreateVoicePrivacyFixture() : new Fixture();
        f.Probe.Status = new("local.inference", "Local model", DependencyReadiness.Ready, "Synthetic ready seam.");
        f.Reasoner.Action = BuiltInAction.LockMachine;
        await f.ViewModel.InitializeAsync();
        f.ViewModel.BindModelActionReviewPresentation(f.ViewModel.IsModelActionReviewCurrent);
        return f;
    }

    private static async Task<ModelActionReviewTarget> ProposeModelActionReviewAsync(Fixture f)
    {
        await f.RunAsync("please secure this workstation");
        await f.ViewModel.ActiveReasoningTask!;
        return f.ViewModel.CaptureModelActionReview()!;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExactModelActionReviewActivationRequiresASeparateOriginalDecision(bool reject)
    {
        var f = await CreateModelActionReviewFixtureAsync();
        f.ViewModel.CanConfirmModelActionReview.Should().BeFalse();
        f.ViewModel.CanConfirmReusableModelActionReview.Should().BeFalse();
        f.ViewModel.CanRejectModelActionReview.Should().BeFalse();
        var target = await ProposeModelActionReviewAsync(f);
        f.ViewModel.CanConfirmModelActionReview.Should().BeTrue();
        f.ViewModel.CanConfirmReusableModelActionReview.Should().BeTrue();
        f.ViewModel.CanRejectModelActionReview.Should().BeTrue();
        target.Should().NotBeNull();
        target.Request.SessionId.Validate();
        target.Epoch.Should().BePositive();
        f.ViewModel.ModelActionReviews.Should().Equal(target);
        f.ViewModel.PresenceState.Should().Be(AssistantState.Waiting);
        f.ViewModel.CanAutoHidePresence.Should().BeFalse();
        var audits = f.Audit.Events.Count;
        var requests = f.Reasoner.Requests.Count;
        f.WindowActions.Clear();

        using (HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi),
            HostActivityLayer.Application, HostOperation.Request))
        {
            f.ViewModel.ActivateModelActionReview(target).Should().BeTrue();
            f.ViewModel.ActivateModelActionReview(target).Should().BeTrue();
        }

        f.ViewModel.ResponseBody.Should().Contain("Lock the current Windows session.");
        f.ViewModel.ModelActionReviewStatus.Should().Contain("Confirm or reject separately");
        f.ViewModel.PendingModelActionReview.Should().BeSameAs(target);
        f.Audit.Events.Should().HaveCount(audits);
        f.Reasoner.Requests.Should().HaveCount(requests);
        f.Session.LockCalls.Should().Be(0);
        f.ApprovalPreferences.Preferences.AlwaysAllowedActions.Should().BeEmpty();
        f.ViewModel.SessionAllowedModelActions.Should().BeEmpty();
        f.WindowActions.Should().BeEmpty("exact activation uses its bound native presenter, not generic Show");

        if (reject) { await f.ViewModel.RejectModelActionReviewAsync(target); }
        else { await f.ViewModel.ConfirmModelActionReviewAsync(target, ModelApprovalScope.Once); }

        f.Session.LockCalls.Should().Be(reject ? 0 : 1);
        f.ViewModel.PendingModelActionReview.Should().BeNull();
        f.ViewModel.ModelActionReviews.Should().BeEmpty();
        f.ViewModel.IsModelActionApprovalPending.Should().BeFalse();
        f.ViewModel.PresenceState.Should().Be(f.ViewModel.State);
        f.Audit.Events.Should().Contain(audit => audit.CorrelationId == target.Audit.CorrelationId
            && audit.Outcome == (reject ? SecurityAuditOutcome.Cancelled : SecurityAuditOutcome.Succeeded));
        f.ViewModel.ActivateModelActionReview(target).Should().BeFalse();
        await f.ViewModel.ConfirmModelActionReviewAsync(target, ModelApprovalScope.Always);
        await f.ViewModel.RejectModelActionReviewAsync(target);
        f.Session.LockCalls.Should().Be(reject ? 0 : 1);
        f.ApprovalPreferences.Preferences.AlwaysAllowedActions.Should().BeEmpty();
    }

    [Fact]
    public async Task ExactModelActionReviewReplacementAndForeignOwnerNeverRetarget()
    {
        var f = await CreateModelActionReviewFixtureAsync();
        var first = await ProposeModelActionReviewAsync(f);
        var second = await ProposeModelActionReviewAsync(f);
        second.Should().NotBeSameAs(first);
        second.Epoch.Should().BeGreaterThan(first.Epoch);
        var originalBody = f.ViewModel.ResponseBody;
        var other = await CreateModelActionReviewFixtureAsync();
        var foreign = await ProposeModelActionReviewAsync(other);
        foreach (var stale in new[] { first, foreign })
        {
            f.ViewModel.ActivateModelActionReview(stale).Should().BeFalse();
            await f.ViewModel.ConfirmModelActionReviewAsync(stale, ModelApprovalScope.Once);
            await f.ViewModel.RejectModelActionReviewAsync(stale);
        }
        f.ViewModel.ResponseBody.Should().Be(originalBody);
        f.ViewModel.PendingModelActionReview.Should().BeSameAs(second);
        other.ViewModel.PendingModelActionReview.Should().BeSameAs(foreign);
        f.Session.LockCalls.Should().Be(0);
        other.Session.LockCalls.Should().Be(0);
        other.ViewModel.ModelActionReviewStatus.Should().NotContain("unavailable");
        f.ViewModel.ActivateModelActionReview(second).Should().BeTrue();
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("binding")]
    [InlineData("revision")]
    [InlineData("locked")]
    [InlineData("dismissed")]
    [InlineData("disposed")]
    public async Task ExactModelActionReviewWithdrawnHostCannotRevealOrDecide(string withdrawal)
    {
        var f = await CreateModelActionReviewFixtureAsync();
        long owner = 1;
        f.ViewModel.BindCallOwnershipGate(static () => true, () => owner);
        var target = await ProposeModelActionReviewAsync(f);
        switch (withdrawal)
        {
            case "owner": f.ViewModel.BindCallOwnershipGate(static () => false); break;
            case "binding": f.ViewModel.BindCallOwnershipGate(static () => true); break;
            case "revision": owner++; break;
            case "locked": f.Session.IsUnlocked = false; break;
            case "call": f.CallState.SetState(CallState.Active); await f.ViewModel.CallClosureTask; break;
            case "dismissed": await f.ViewModel.HideApplicationAsync(); break;
            case "disposed": await f.ViewModel.DisposeAsync(); break;
        }
        f.WindowActions.Clear();
        var body = f.ViewModel.ResponseBody;
        f.ViewModel.CaptureModelActionReview().Should().BeNull();
        f.ViewModel.ActivateModelActionReview(target).Should().BeFalse();
        await f.ViewModel.ConfirmModelActionReviewAsync(target, ModelApprovalScope.Always);
        await f.ViewModel.RejectModelActionReviewAsync(target);
        f.ViewModel.ResponseBody.Should().Be(body);
        f.WindowActions.Should().BeEmpty();
        f.Session.LockCalls.Should().Be(0);
        f.ApprovalPreferences.Preferences.AlwaysAllowedActions.Should().BeEmpty();
        f.ViewModel.ModelActionReviewStatus.Should().Contain("unavailable");
    }

    [Fact]
    public async Task ExactModelActionReviewRequiresAnOriginalEligibleChannelAndNativePresenter()
    {
        var f = await CreateModelActionReviewFixtureAsync();
        f.ViewModel.ActivateModelActionReview(null).Should().BeFalse();
        var target = await ProposeModelActionReviewAsync(f);
        f.ViewModel.BindModelActionReviewPresentation(null);
        f.ViewModel.ActivateModelActionReview(target).Should().BeFalse();
        f.ViewModel.BindModelActionReviewPresentation(static _ => false);
        f.ViewModel.ActivateModelActionReview(target).Should().BeFalse();
        f.ViewModel.BindModelActionReviewPresentation(static _ => throw new InvalidOperationException("Synthetic native surface unavailable."));
        f.ViewModel.ActivateModelActionReview(target).Should().BeFalse();
        f.ViewModel.BindModelActionReviewPresentation(f.ViewModel.IsModelActionReviewCurrent);
        using (HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.HostSystem),
            HostActivityLayer.Application, HostOperation.Request))
        {
            f.ViewModel.ActivateModelActionReview(target).Should().BeFalse();
            await f.ViewModel.ConfirmModelActionReviewAsync(target, ModelApprovalScope.Once);
            await f.ViewModel.RejectModelActionReviewAsync(target);
        }
        f.Session.LockCalls.Should().Be(0);
        f.ViewModel.PendingModelActionReview.Should().BeSameAs(target);
        await f.RunAsync("Kora, review pending action");
        f.ViewModel.ModelActionReviewStatus.Should().Contain("opened");
        f.ViewModel.PendingModelActionReview.Should().BeSameAs(target);
    }

    [Fact]
    public async Task ExactModelActionReviewActivatedVoiceUsesTheCapturedTargetNotForegroundSelection()
    {
        var f = await CreateModelActionReviewFixtureAsync(voice: true);
        await f.RaiseActivatedTranscriptAsync("Kora, please secure this workstation", 1);
        await f.ViewModel.ActiveReasoningTask!;
        var target = f.ViewModel.CaptureModelActionReview()!;
        target.Should().NotBeNull();
        await f.RaiseActivatedTranscriptAsync("Kora, review pending action", 1);
        f.ViewModel.ModelActionReviewStatus.Should().Contain("opened");
        f.ViewModel.PendingModelActionReview.Should().BeSameAs(target);
        f.Session.LockCalls.Should().Be(0);
        await f.RaiseActivatedTranscriptAsync("Kora, approve once", 1);
        f.Session.LockCalls.Should().Be(1);
        f.ViewModel.PendingModelActionReview.Should().BeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExactModelActionReviewAndNamedSkillChoiceKeepIndependentAuthority(bool voice)
    {
        var f = await CreateModelActionReviewFixtureAsync(voice);
        var target = await ProposeModelActionReviewAsync(f);
        var requests = f.Reasoner.Requests.Count;
        var calls = 0;
        f.ViewModel.BindNamedSkillChoiceCapture(() => static () => false);
        f.ViewModel.NamedSkillInvocationRequested += (command, origin, eligible) =>
        {
            command.Action.Should().Be("Choice");
            command.Choice.Should().Be(1);
            origin.Should().Be(voice ? RequestOrigin.ActivatedVoice : RequestOrigin.LocalUi);
            eligible().Should().BeTrue();
            calls++;
            return Task.CompletedTask;
        };
        async Task Run(string command)
        {
            if (voice) { await f.RaiseActivatedTranscriptAsync("Kora, " + command, 1); }
            else { await f.RunAsync(command); }
        }

        await Run("review pending action");
        f.ViewModel.ModelActionReviewStatus.Should().Contain("opened");
        f.ViewModel.PendingModelActionReview.Should().BeSameAs(target);
        f.ViewModel.BindNamedSkillChoiceCapture(() => static () => true);
        await Run("skill choice 1");
        calls.Should().Be(0, "a current skill choice cannot replace or decide a pending action");
        f.ViewModel.PendingModelActionReview.Should().BeSameAs(target);
        f.Session.LockCalls.Should().Be(0);
        f.Audit.Events.Should().NotContain(audit => audit.CorrelationId == target.Audit.CorrelationId
            && audit.Outcome == SecurityAuditOutcome.Succeeded);

        await f.ViewModel.RejectModelActionReviewAsync(target);
        await Run("skill choice 1");
        calls.Should().Be(1);
        f.ViewModel.PendingModelActionReview.Should().BeNull();
        f.ViewModel.ActivateModelActionReview(target).Should().BeFalse();
        f.Reasoner.Requests.Should().HaveCount(requests);
        f.Session.LockCalls.Should().Be(0);
        f.ApprovalPreferences.Preferences.AlwaysAllowedActions.Should().BeEmpty();
        f.ViewModel.SessionAllowedModelActions.Should().BeEmpty();
    }

    [Theory]
    [InlineData("Kora, skill choice 1")]
    [InlineData("Kora, review pending action")]
    public async Task ExactModelActionReviewQueuedSkillInputCannotAdoptANewReview(string text)
    {
        var f = await CreateModelActionReviewFixtureAsync(voice: true);
        var generation = 1;
        f.ViewModel.BindNamedSkillChoiceCapture(() =>
        {
            var captured = generation;
            return () => captured == generation;
        });
        var calls = 0;
        f.ViewModel.NamedSkillInvocationRequested += (_, _, _) => { calls++; return Task.CompletedTask; };
        await f.ViewModel.BeginPushToTalkAsync();
        await f.ViewModel.EndPushToTalkAsync();
        ModelActionReviewTarget? target = null;
        f.Dispatcher.BeforeInvokeAsync = async () =>
        {
            generation++;
            target = await ProposeModelActionReviewAsync(f);
        };

        await f.Voice.RaiseTranscriptAsync(text, 1);
        target.Should().NotBeNull();
        calls.Should().Be(0);
        f.ViewModel.PendingModelActionReview.Should().BeSameAs(target);
        f.ViewModel.ModelActionReviewStatus.Should().NotContain("opened");
        f.Session.LockCalls.Should().Be(0);
        f.Reasoner.Requests.Should().ContainSingle();
        f.Audit.Events.Should().NotContain(audit => audit.CorrelationId == target!.Audit.CorrelationId
            && audit.Outcome == SecurityAuditOutcome.Succeeded);
        f.ApprovalPreferences.Preferences.AlwaysAllowedActions.Should().BeEmpty();
        f.ViewModel.SessionAllowedModelActions.Should().BeEmpty();
        await f.ViewModel.RejectModelActionReviewAsync(target);
        f.ViewModel.PendingModelActionReview.Should().BeNull();
    }

    [Theory]
    [InlineData("Kora, review pending action")]
    [InlineData("Kora, approve once")]
    [InlineData("Kora, reject")]
    public async Task ExactModelActionReviewQueuedVoiceCannotFollowAReplacement(string text)
    {
        var f = await CreateModelActionReviewFixtureAsync(voice: true);
        var first = await ProposeModelActionReviewAsync(f);
        await f.ViewModel.BeginPushToTalkAsync();
        await f.ViewModel.EndPushToTalkAsync();
        f.Dispatcher.BeforeInvokeAsync = async () =>
        {
            await f.RunAsync("another request to secure the workstation");
            await f.ViewModel.ActiveReasoningTask!;
        };
        await f.Voice.RaiseTranscriptAsync(text, 1);
        await f.ViewModel.ActiveReasoningTask!;
        var second = f.ViewModel.CaptureModelActionReview();
        second.Should().NotBeNull().And.NotBeSameAs(first);
        f.ViewModel.ModelActionReviewStatus.Should().Contain("unavailable");
        f.Session.LockCalls.Should().Be(0);
        f.ViewModel.IsModelActionApprovalPending.Should().BeTrue();
    }

    [Fact]
    public async Task ExactModelActionReviewMissingVoiceActivationDoesNotSupersedeTheDecision()
    {
        var f = await CreateModelActionReviewFixtureAsync(voice: true);
        var target = await ProposeModelActionReviewAsync(f);
        await f.RaiseActivatedTranscriptAsync("review pending action", 1);
        f.ViewModel.ModelActionReviewStatus.Should().Contain("unavailable");
        f.ViewModel.PendingModelActionReview.Should().BeSameAs(target);
        f.Session.LockCalls.Should().Be(0);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task ExactModelActionReviewRevalidatesAfterAwaitedPromptStop(bool reject, bool lockHost)
    {
        var f = await CreateModelActionReviewFixtureAsync();
        f.TextToSpeech.SpeakGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await f.RunAsync("please secure this workstation");
        await f.TextToSpeech.SpeakStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
        var target = f.ViewModel.CaptureModelActionReview()!;
        f.TextToSpeech.StopGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var decision = reject ? f.ViewModel.RejectModelActionReviewAsync(target)
            : f.ViewModel.ConfirmModelActionReviewAsync(target, ModelApprovalScope.Once);
        await f.TextToSpeech.StopStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
        if (reject) { await f.ViewModel.RejectModelActionReviewAsync(target); }
        else { await f.ViewModel.ConfirmModelActionReviewAsync(target, ModelApprovalScope.Once); }
        if (lockHost) { f.Session.IsUnlocked = false; }
        else { f.ViewModel.BindCallOwnershipGate(static () => false); }
        f.TextToSpeech.StopGate.SetResult();
        await decision;
        await f.ViewModel.ActiveReasoningTask!;
        f.Session.LockCalls.Should().Be(0);
        f.ViewModel.ModelActionReviewStatus.Should().Contain("unavailable");
        f.Audit.Events.Should().NotContain(audit => audit.CorrelationId == target.Audit.CorrelationId
            && audit.Outcome == SecurityAuditOutcome.Succeeded);
    }

    [Theory]
    [InlineData(ModelApprovalScope.Once)]
    [InlineData(ModelApprovalScope.Session)]
    [InlineData(ModelApprovalScope.Always)]
    public async Task ExactModelActionReviewNativeConfirmationReusesOriginalScopeCommand(ModelApprovalScope scope)
    {
        var f = await CreateModelActionReviewFixtureAsync();
        f.CallState.SetState(CallState.Clear);
        await f.ViewModel.CallClosureTask;
        var target = await ProposeModelActionReviewAsync(f);
        var sessionGrantObserved = false;
        f.ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(MainViewModel.SessionAllowedModelActions)
                && f.ViewModel.SessionAllowedModelActions.Any(command => command.Action == BuiltInAction.LockMachine))
            { sessionGrantObserved = true; }
        };
        await FluentActions.Awaiting(() => f.ViewModel.ConfirmModelActionReviewAsync(target, (ModelApprovalScope)99))
            .Should().ThrowAsync<ArgumentOutOfRangeException>();
        await f.ViewModel.ConfirmModelActionReviewAsync(target, scope);
        f.Session.LockCalls.Should().Be(1);
        f.ViewModel.IsModelActionApprovalPending.Should().BeFalse();
        sessionGrantObserved.Should().Be(scope == ModelApprovalScope.Session);
        f.ViewModel.SessionAllowedModelActions.Should().BeEmpty("locking Windows retires session grants through the original lifecycle");
        f.ApprovalPreferences.Preferences.AlwaysAllowedActions.Count.Should().Be(scope == ModelApprovalScope.Always ? 1 : 0);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task ExactModelActionReviewNativePresentationFailuresRemainExplicit(int kind)
    {
        var f = await CreateModelActionReviewFixtureAsync();
        var target = await ProposeModelActionReviewAsync(f);
        Exception failure = kind switch
        {
            0 => new IOException("Synthetic native presentation I/O failure."),
            1 => new UnauthorizedAccessException("Synthetic native presentation denial."),
            _ => new ArgumentException("Synthetic unexpected presenter contract failure."),
        };
        f.ViewModel.BindModelActionReviewPresentation(_ => throw failure);
        if (kind == 2)
        { FluentActions.Invoking(() => f.ViewModel.ActivateModelActionReview(target)).Should().Throw<ArgumentException>(); }
        else
        {
            f.ViewModel.ActivateModelActionReview(target).Should().BeFalse();
            f.ViewModel.ModelActionReviewStatus.Should().Contain("unavailable");
        }
        f.Session.LockCalls.Should().Be(0);
        f.ViewModel.PendingModelActionReview.Should().BeSameAs(target);
    }

    [Theory]
    [InlineData("name-unavailable")]
    [InlineData("initiator")]
    [InlineData("origin")]
    [InlineData("eligibility")]
    [InlineData("call-revision")]
    [InlineData("name-revision")]
    public async Task ExactModelActionReviewCapturedInputAdmissionCannotBePromoted(string change)
    {
        var f = await CreateModelActionReviewFixtureAsync();
        var target = await ProposeModelActionReviewAsync(f);
        var capture = typeof(MainViewModel).GetMethod("CaptureManualCallInput", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var input = capture.Invoke(f.ViewModel, [RequestOrigin.LocalUi, f.ViewModel.CallPolicyRevision, null, false])!;
        var initiator = SecurityAuditInitiator.TypedCommand;
        if (change is "name-unavailable")
        { typeof(MainViewModel).GetField("isAssistantNameAvailable", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(f.ViewModel, false); }
        if (change is "initiator") { initiator = SecurityAuditInitiator.ModelSuggestion; }
        if (change is "eligibility") { f.Session.IsUnlocked = false; }
        if (change is "call-revision") { f.CallState.SetState(CallState.Active); await f.ViewModel.CallClosureTask; }
        if (change is "name-revision")
        { input.GetType().GetProperty("AssistantNameRevision", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(input, -1L); }
        using var activity = HostActivity.BeginRoot(HostRequest.Create(change is "origin" ? RequestOrigin.HostSystem : RequestOrigin.LocalUi),
            HostActivityLayer.Application, HostOperation.Request);
        // The private routing seam permits adversarial host-envelope combinations that native/voice entry points cannot mint.
        var route = typeof(MainViewModel).GetMethod("HandleTranscriptAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        object?[] arguments = ["Kora, review pending action", 1f, initiator, input,
            .. route.GetParameters().Skip(4).Select(_ => Type.Missing)];
        await (Task)route.Invoke(f.ViewModel, arguments)!;
        f.ViewModel.ModelActionReviewStatus.Should().Contain("unavailable");
        f.ViewModel.PendingModelActionReview.Should().BeSameAs(target);
        f.Session.LockCalls.Should().Be(0);
    }

    [Theory]
    [InlineData("modelActionReviewEpoch", -1L)]
    [InlineData("pendingModelActionAudit", null)]
    [InlineData("pendingModelAction", null)]
    public async Task ExactModelActionReviewIncoherentLegacyOwnerStateFailsClosed(string field, object? replacement)
    {
        var f = await CreateModelActionReviewFixtureAsync();
        var target = await ProposeModelActionReviewAsync(f);
        // Supplemental invariant corruption, not the operation/authority proof supplied by the public-route tests.
        typeof(MainViewModel).GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(f.ViewModel, replacement);
        f.ViewModel.IsModelActionReviewCurrent(target).Should().BeFalse();
        f.ViewModel.ActivateModelActionReview(target).Should().BeFalse();
        f.Session.LockCalls.Should().Be(0);
    }

    [Fact]
    public async Task ExactModelActionReviewArtifactAndReentrantPresentationAreRevalidated()
    {
        var f = await CreateModelActionReviewFixtureAsync();
        var target = await ProposeModelActionReviewAsync(f);
        var field = typeof(MainViewModel).GetField("pendingModelActionArtifact", BindingFlags.NonPublic | BindingFlags.Instance)!;
        field.SetValue(f.ViewModel, new LocalModelArtifact("missing", Kora.Core.Artifacts.ArtifactKind.Skill,
            "Synthetic", "synthetic", "1", new string('0', 64), "Synthetic source"));
        f.ViewModel.IsModelActionReviewCurrent(target).Should().BeFalse();
        field.SetValue(f.ViewModel, null);
        f.ViewModel.BindModelActionReviewPresentation(_ =>
        {
            f.ViewModel.BindCallOwnershipGate(static () => false);
            return true;
        });
        f.ViewModel.ActivateModelActionReview(target).Should().BeFalse();
        f.Session.LockCalls.Should().Be(0);
    }

    [Fact]
    public async Task ExactModelActionReviewClarificationRequiresItsOriginalHostContinuation()
    {
        var f = await CreateModelActionReviewFixtureAsync();
        f.Reasoner.Action = null;
        f.Reasoner.Question = new("Which?", ["One", "Two"]);
        await f.RunAsync("choose");
        await f.ViewModel.ActiveReasoningTask!;
        // A missing legacy continuation must be unavailable, never a fabricated session or action review.
        typeof(MainViewModel).GetField("pendingModelQuestionContinuation", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(f.ViewModel, null);
        await f.ViewModel.SelectModelQuestionChoiceAsync(f.ViewModel.ModelQuestionChoices[0]);
        f.ViewModel.ResponseBody.Should().Contain("original host request is unavailable");
        f.Reasoner.Requests.Should().ContainSingle();
        f.ViewModel.PendingModelActionReview.Should().BeNull();
    }

    [Theory]
    [InlineData("Kora, review pending action")]
    [InlineData("Kora, approve once")]
    public async Task ExactModelActionReviewRetiredVoiceGenerationCannotRevealOrDecide(string text)
    {
        var f = await CreateModelActionReviewFixtureAsync(voice: true);
        var target = await ProposeModelActionReviewAsync(f);
        await f.ViewModel.BeginPushToTalkAsync();
        await f.ViewModel.EndPushToTalkAsync();
        f.Dispatcher.BeforeInvoke = () => f.Voice.InvalidateCapture();
        await f.Voice.RaiseTranscriptAsync(text, 1);
        f.ViewModel.ModelActionReviewStatus.Should().Contain("unavailable");
        f.ViewModel.PendingModelActionReview.Should().BeSameAs(target);
        f.Session.LockCalls.Should().Be(0);
    }
}
