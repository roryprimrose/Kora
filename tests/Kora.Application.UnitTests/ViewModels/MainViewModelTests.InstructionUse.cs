using AwesomeAssertions;

using Kora.Core.Commands;
using Kora.Application.Hosting;
using Kora.Application.ViewModels;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    private const string InstructionRequest = "instructions request review \"example\" in \"Work\" --   original request  ";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InstructionUseDispatchPreservesOriginalChannelBodyAndHostPendingIdentity(bool voice)
    {
        var f = await Fixture.CreateInitializedAsync();
        await f.ViewModel.SetAssistantNameAsync("Nova", Kora.Core.Auditing.SecurityAuditInitiator.LocalUser);
        var id = Guid.NewGuid();
        var calls = new List<string>();
        f.ViewModel.InstructionUseRequested += (command, origin, eligible, expected) =>
        {
            eligible().Should().BeTrue();
            origin.Should().Be(voice ? RequestOrigin.ActivatedVoice : RequestOrigin.LocalUi);
            HostActivity.RequireCurrent().Request.Origin.Should().Be(origin);
            calls.Add(command.Action);
            if (command.Action is "Review")
            {
                expected.Should().BeNull();
                command.Body.Should().Be("  original request  ");
                return Task.FromResult<Guid?>(id);
            }
            expected.Should().Be(id);
            return Task.FromResult<Guid?>(null);
        };
        foreach (var text in new[] { InstructionRequest, "instructions request confirm" })
        {
            if (voice) { await f.RaiseActivatedTranscriptAsync("Nova, " + text, 1); }
            else { await f.RunAsync(text); }
        }
        calls.Should().Equal("Review", "Confirm");
        await f.RunAsync("instructions request confirm");
        calls.Should().HaveCount(2);
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData("generation")]
    [InlineData("owner")]
    [InlineData("binding")]
    [InlineData("call")]
    public async Task InstructionUseVoiceKeepsOriginalPreQueueEligibilityEvenAfterARestrictiveStateIsRestored(string change)
    {
        var f = await Fixture.CreateInitializedAsync();
        long owner = 1;
        f.ViewModel.BindSkillReviewNavigation(null, () => owner);
        var calls = 0;
        f.ViewModel.InstructionUseRequested += (_, _, _, _) => { calls++; return Task.FromResult<Guid?>(null); };
        await f.ViewModel.BeginPushToTalkAsync();
        await f.ViewModel.EndPushToTalkAsync();
        f.Dispatcher.BeforeInvoke = () =>
        {
            if (change is "generation") { f.Voice.InvalidateCapture(); }
            if (change is "owner") { owner++; }
            if (change is "binding") { f.ViewModel.BindSkillReviewNavigation(null, () => owner); }
            if (change is "call")
            {
                f.ViewModel.EnableManualCallCommand.ExecuteAsync().IsCompletedSuccessfully.Should().BeTrue();
                f.ViewModel.ClearManualCallCommand.ExecuteAsync().IsCompletedSuccessfully.Should().BeTrue();
            }
        };
        await f.Voice.RaiseTranscriptAsync("Kora, " + InstructionRequest, 1);
        calls.Should().Be(0);
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task InstructionUseDoesNotTreatUnboundMalformedModelOrUnprefixedVoiceAsAuthority()
    {
        var f = await Fixture.CreateInitializedAsync();
        await f.RunAsync(InstructionRequest);
        f.ViewModel.ResponseBody.Should().Contain("unavailable");
        var calls = 0;
        f.ViewModel.InstructionUseRequested += (_, _, _, _) => { calls++; return Task.FromResult<Guid?>(null); };
        await f.RunAsync("instructions request confirm");
        await f.RunAsync("instructions request review broken");
        await f.RaiseActivatedTranscriptAsync(InstructionRequest, 1);
        using (HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.HostSystem), HostActivityLayer.Application, HostOperation.Request))
        { await f.RunAsync(InstructionRequest); }
        calls.Should().Be(0);
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InstructionUseCannotReplaceAnUnrelatedQuestionOrPermissionReview(bool question)
    {
        var f = new Fixture();
        f.Probe.Status = new("local.inference", "Local", Kora.Core.Dependencies.DependencyReadiness.Ready, "ready");
        f.Reasoner.Action = question ? null : BuiltInAction.LockMachine;
        if (question) { f.Reasoner.Question = new("Keep this exact question?", ["one", "two"]); }
        await f.ViewModel.InitializeAsync();
        await f.RunAsync("please do the work");
        await f.ViewModel.ActiveReasoningTask!;
        var calls = 0;
        f.ViewModel.InstructionUseRequested += (_, _, _, _) => { calls++; return Task.FromResult<Guid?>(null); };
        var body = f.ViewModel.ResponseBody;
        await f.RunAsync(InstructionRequest);
        calls.Should().Be(0);
        f.ViewModel.ResponseBody.Should().Be(body);
        f.ViewModel.IsResponseInteractionPending.Should().BeTrue();
    }

    [Fact]
    public async Task NativeRetirementPreventsTypedConfirmationAndExitJoinsTheInstructionOwner()
    {
        var f = await Fixture.CreateInitializedAsync();
        var id = Guid.NewGuid();
        var calls = 0;
        f.ViewModel.InstructionUseRequested += (_, _, _, _) => { calls++; return Task.FromResult<Guid?>(id); };
        await f.RunAsync(InstructionRequest);
        f.ViewModel.RetireInstructionUseReview(Guid.NewGuid());
        f.ViewModel.RetireInstructionUseReview(null);
        f.ViewModel.RetireInstructionUseReview(id);
        f.ViewModel.RetireInstructionUseReview(Guid.NewGuid());
        f.ViewModel.RetireInstructionUseReview(null);
        await f.RunAsync("instructions request confirm");
        calls.Should().Be(1);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.ViewModel.BindInstructionUseShutdown(() => release.Task);
        var exit = f.ViewModel.ExitAsync();
        exit.IsCompleted.Should().BeFalse();
        release.SetResult();
        await exit;
        f.ViewModel.BindInstructionUseShutdown(null);
    }

    [Fact]
    public async Task QueuedVoiceConfirmationCannotConfirmAReviewPresentedAfterThatInputArrived()
    {
        var f = await Fixture.CreateInitializedAsync();
        var calls = new List<string>();
        f.ViewModel.InstructionUseRequested += (command, _, _, _) =>
        { calls.Add(command.Action); return Task.FromResult<Guid?>(Guid.NewGuid()); };
        await f.ViewModel.BeginPushToTalkAsync();
        await f.ViewModel.EndPushToTalkAsync();
        f.Dispatcher.BeforeInvoke = () =>
        {
            using var native = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi),
                HostActivityLayer.Application, HostOperation.Request);
            f.RunAsync(InstructionRequest).IsCompletedSuccessfully.Should().BeTrue();
        };
        await f.Voice.RaiseTranscriptAsync("Kora, instructions request confirm", 1);
        calls.Should().Equal("Review");
        f.ViewModel.ResponseBody.Should().Contain("require original private typed input");
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OriginalActivatedVoiceReachesActualBrokerAndRetainedLocalAnswerOnlyAfterSeparateTypedConfirmation(bool revoke)
    {
        var f = await Fixture.CreateInitializedAsync();
        using var host = new Dependencies.ModelTurnHostTests.InstructionUseHarness();
        await using var workflow = host.Workflow;
        InstructionUseReview? review = null;
        Func<bool>? original = null;
        f.ViewModel.InstructionUseRequested += async (command, origin, eligible, expected) =>
        {
            if (command.Action is "Review")
            {
                original = eligible;
                review = await workflow.ReviewAsync(command.Source!, command.Target!, command.Body!, origin, eligible, TestContext.Current.CancellationToken);
                return review.Id;
            }
            var receipt = await workflow.ConfirmAsync(expected!.Value, eligible, TestContext.Current.CancellationToken);
            receipt.Message.AssistantResponse!.Body.Should().Be("answer");
            return null;
        };
        await f.RaiseActivatedTranscriptAsync("Kora, " + InstructionRequest, 1);
        review.Should().NotBeNull();
        review!.Text.Should().Contain(host.SourceText).And.Contain("  original request  ").And.Contain("ActivatedVoice");
        host.RetainCalls.Should().Be(0);
        host.ModelCalls.Should().Be(0);
        if (revoke)
        {
            await f.ViewModel.EnableManualCallCommand.ExecuteAsync();
            await f.ViewModel.ClearManualCallCommand.ExecuteAsync();
            original!().Should().BeFalse();
            await workflow.Invoking(w => w.ConfirmAsync(review.Id, () => true, TestContext.Current.CancellationToken)).Should().ThrowAsync<InvalidOperationException>();
            host.RetainCalls.Should().Be(0);
            host.ModelCalls.Should().Be(0);
        }
        else
        {
            await f.RunAsync("instructions request confirm");
            host.Retained!.Body.Should().Be("  original request  ");
            host.Retained.Request.Origin.Should().Be(RequestOrigin.ActivatedVoice);
            host.Retained.AssistantResponse!.Body.Should().Be("answer");
            host.RetainCalls.Should().Be(1);
            host.ModelCalls.Should().Be(1);
        }
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InstructionUseConsumerFailsClosedWithoutTheOriginalQueuedVoiceSnapshot(bool missingEligibility)
    {
        var f = await Fixture.CreateInitializedAsync();
        var calls = 0;
        f.ViewModel.InstructionUseRequested += (_, _, _, _) => { calls++; return Task.FromResult<Guid?>(null); };
        var method = typeof(MainViewModel).GetMethod("ExecuteInstructionEditorCommand",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        object? snapshot = null;
        if (missingEligibility)
        {
            var capture = typeof(MainViewModel).GetMethod("CaptureManualCallInput",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            snapshot = capture.Invoke(f.ViewModel, [RequestOrigin.ActivatedVoice, f.ViewModel.CallPolicyRevision, null, false]);
        }
        // The public voice producer always supplies both values. Exercise the defensive consumer seam without fabricating a transcript event.
        await HostRequestRunner.RunAsync(RequestOrigin.ActivatedVoice,
            () => (Task)method.Invoke(f.ViewModel, [InstructionRequest, snapshot, missingEligibility ? null : (Func<bool>)(() => true)])!);
        calls.Should().Be(0);
        f.ViewModel.ResponseBody.Should().Contain("Original queued voice admission is unavailable");
        f.Reasoner.Requests.Should().BeEmpty();
    }
}
