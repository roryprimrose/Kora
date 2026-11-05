using AwesomeAssertions;
using Kora.Core.Auditing;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Theory]
    [InlineData(SecurityAuditOutcome.Requested)]
    [InlineData(SecurityAuditOutcome.Succeeded)]
    public async Task CallSpeechTypedCancellationRetiresHeldPreparationWithoutReleasingOriginalCommand(
        SecurityAuditOutcome phase)
    {
        var f = await CreateCallSpeechResponse();
        var native = new ResponsePresenter(f.ViewModel);
        f.ViewModel.BindCurrentResponsePresentation(native);
        var call = f.ViewModel.CallObservation;
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var token = TestContext.Current.CancellationToken;
        f.Audit.BeforeWrite = audit =>
        {
            if (audit.ActionId is not "speech.call-once.review" || audit.Outcome != phase) { return; }
            entered.TrySetResult();
            release.Wait(token);
        };
        var preparing = f.RunAsync("review call speech");
        try
        {
            await entered.Task.WaitAsync(token);
            await AssertCallSpeechTypedReentryRefused(f);
            f.ViewModel.CommandText = "cancel call speech";
            f.ViewModel.RunTypedCommand.CanExecute(null).Should().BeTrue();
            await f.ViewModel.RunTypedCommand.ExecuteAsync();
            f.ViewModel.CallSpeechStatus.Should().Contain("cancelled");
            f.ViewModel.CanConfirmCallSpeech.Should().BeFalse();
            preparing.IsCompleted.Should().BeFalse();
            await AssertCallSpeechTypedReentryRefused(f);
            f.ViewModel.CanReviewCallSpeech.Should().BeFalse();
        }
        finally
        {
            f.Audit.BeforeWrite = null;
            release.Set();
            await preparing;
        }

        native.Calls.Should().Be(phase == SecurityAuditOutcome.Requested ? 0 : 1);
        f.ViewModel.CanConfirmCallSpeech.Should().BeFalse();
        await f.RunAsync("confirm call speech");
        f.TextToSpeech.SpeakCalls.Should().Be(0);
        f.Audit.Events.Should().NotContain(item => string.Equals(item.ActionId, "speech.call-once", StringComparison.Ordinal));
        await f.RunAsync("review call speech");
        f.ViewModel.CanConfirmCallSpeech.Should().BeTrue();
        await f.RunAsync("confirm call speech");
        await f.RunAsync("confirm call speech");
        f.TextToSpeech.SpeakCalls.Should().Be(1);
        f.ViewModel.CallObservation.Should().Be(call);
        f.CallPreferences.SavedSettings.Should().BeNull();
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task CallSpeechTypedCancellationJoinsIgnoredPlaybackAndRetainsBothCommandOwners()
    {
        var f = await CreateCallSpeechResponse();
        var native = new ResponsePresenter(f.ViewModel);
        f.ViewModel.BindCurrentResponsePresentation(native);
        var call = f.ViewModel.CallObservation;
        await f.RunAsync("review call speech");
        f.TextToSpeech.SpeakGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        f.TextToSpeech.IgnoreSpeakCancellation = true;
        f.TextToSpeech.HoldSpeechCompletionOnStop = true;
        f.TextToSpeech.StopGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.TextToSpeech.BeforeStop = () => stopped.TrySetResult();
        var playing = f.RunAsync("confirm call speech");
        Task? cancelling = null;
        try
        {
            await f.TextToSpeech.SpeakStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
            await AssertCallSpeechTypedReentryRefused(f);
            f.ViewModel.CommandText = "Kora, cancel call speech!";
            f.ViewModel.RunTypedCommand.CanExecute(null).Should().BeTrue();
            cancelling = f.ViewModel.RunTypedCommand.ExecuteAsync();
            await stopped.Task.WaitAsync(TestContext.Current.CancellationToken);
            cancelling.IsCompleted.Should().BeFalse();
            playing.IsCompleted.Should().BeFalse();
            f.ViewModel.IsCurrentResponseReadActive.Should().BeTrue();
            f.ViewModel.CanConfirmCallSpeech.Should().BeFalse();
            f.ViewModel.CanReviewCallSpeech.Should().BeFalse();
            await AssertCallSpeechTypedReentryRefused(f);
            f.TextToSpeech.SpeakCalls.Should().Be(1);
            native.Calls.Should().Be(1);
            f.TextToSpeech.SpeakGate.TrySetResult();
            await playing;
            cancelling.IsCompleted.Should().BeFalse("the stop operation still owns its typed command");
            await AssertCallSpeechTypedReentryRefused(f);
        }
        finally
        {
            f.TextToSpeech.SpeakGate.TrySetResult();
            f.TextToSpeech.StopGate.TrySetResult();
            await playing;
            if (cancelling is not null) { await cancelling; }
        }

        f.ViewModel.IsCurrentResponseReadActive.Should().BeFalse();
        f.ViewModel.CanConfirmCallSpeech.Should().BeFalse();
        f.Audit.Events.Should().NotContain(item => string.Equals(item.ActionId, "speech.call-once", StringComparison.Ordinal)
            && item.Outcome == SecurityAuditOutcome.Succeeded);
        await f.RunAsync("confirm call speech");
        f.TextToSpeech.SpeakCalls.Should().Be(1);
        await f.RunAsync("review call speech");
        f.ViewModel.CanConfirmCallSpeech.Should().BeTrue();
        await f.RunAsync("confirm call speech");
        await f.RunAsync("confirm call speech");
        f.TextToSpeech.SpeakCalls.Should().Be(2);
        f.Audit.Events.Where(item => string.Equals(item.ActionId, "speech.call-once", StringComparison.Ordinal)).Select(item => item.Outcome)
            .Should().Equal(SecurityAuditOutcome.Requested, SecurityAuditOutcome.Cancelled,
                SecurityAuditOutcome.Requested, SecurityAuditOutcome.Succeeded);
        f.ViewModel.CallObservation.Should().Be(call);
        f.CallPreferences.SavedSettings.Should().BeNull();
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task CallSpeechTypedCancellationPreservesOwnedSkillCloseUseReentry()
    {
        var f = await Fixture.CreateInitializedAsync();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var operations = new List<string>();
        f.ViewModel.BindOwnedSkillAuthoring(async (command, authority) =>
        {
            authority.IsCurrent().Should().BeTrue();
            operations.Add(command.Operation);
            if (command.Operation is "use")
            {
                entered.TrySetResult();
                await release.Task;
            }
        }, null);
        var usingSkill = f.RunAsync("owned skill use example 1.0.0");
        try
        {
            await entered.Task.WaitAsync(TestContext.Current.CancellationToken);
            await AssertCallSpeechTypedReentryRefused(f);
            await f.ViewModel.RunTypedCommand.ExecuteAsync();
            operations.Should().Equal("use", "close-use");
            usingSkill.IsCompleted.Should().BeFalse();
            await AssertCallSpeechTypedReentryRefused(f);
        }
        finally
        {
            release.TrySetResult();
            await usingSkill;
        }
        f.Reasoner.Requests.Should().BeEmpty();
    }

    private static async Task AssertCallSpeechTypedReentryRefused(Fixture f)
    {
        foreach (var command in new[]
        {
            "review call speech", "confirm call speech", "call speech status",
            "cancel call speech extra", "cancel call", "cancel call speech\n",
            "help", "session list", "turn on manual call protection",
        })
        {
            f.ViewModel.CommandText = command;
            f.ViewModel.RunTypedCommand.CanExecute(null).Should().BeFalse(command);
            await f.ViewModel.RunTypedCommand.ExecuteAsync();
        }
        f.ViewModel.CommandText = "owned skill close-use";
        f.ViewModel.RunTypedCommand.CanExecute(null).Should().BeTrue("the existing narrow owned-skill close-use route is preserved");
    }
}
