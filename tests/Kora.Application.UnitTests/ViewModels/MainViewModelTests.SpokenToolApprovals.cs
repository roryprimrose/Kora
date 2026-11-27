using AwesomeAssertions;
using Kora.Application.UnitTests.Interaction;
using Kora.Application.ViewModels;
using Kora.Core.Commands;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using SpokenToolApprovalTarget = Kora.Application.ViewModels.MainViewModel.SpokenToolApprovalTarget;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    private static SpokenToolApprovalTarget ApprovalTarget(InteractionFixture host, HostQuestionRecord question,
        Func<bool>? current = null) => new(question, current ?? (static () => true),
        (channel, admitted) => host.RunAsync(() => host.Authorization.ReviewReplyAsync(question,
            channel, admitted, CancellationToken.None)),
        (reply, channel, admitted) => host.RunAsync(() => reply.Decision == SpokenApprovalReply.Operation.Deny
            ? host.Authorization.CancelReplyAsync(question, channel, admitted, CancellationToken.None)
            : host.Authorization.ApproveAsync(question,
                new([reply.Decision == SpokenApprovalReply.Operation.Once ? "once" : "session"]),
                channel, admitted, CancellationToken.None)));

    [Theory]
    [InlineData("approve once", HostInteractionOutcome.Approved)]
    [InlineData("approve for session", HostInteractionOutcome.Approved)]
    [InlineData("deny", HostInteractionOutcome.Cancelled)]
    public async Task TypedApprovalEquivalentAnswersOnlyTheCapturedLiveCard(string command, HostInteractionOutcome outcome)
    {
        var f = new Fixture();
        using var host = new InteractionFixture();
        var question = (await host.RunAsync(() => host.Authorization.PresentAsync(host.Request, CancellationToken.None))).Question!;
        f.ViewModel.BindSpokenToolApprovals(() => [ApprovalTarget(host, question)]);
        await f.RunAsync(command);
        host.Store.Commits.Last().Decision.Outcome.Should().Be(outcome);
        host.Store.Snapshot.Questions.Single().AnswerChannel.Should().Be(RequestOrigin.LocalUi);
        f.TextToSpeech.SpokenText.Should().BeNull();
        f.ClipboardReader.Calls.Should().Be(0);
        f.ViewModel.Dispose();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task MissingAndAmbiguousCardsNeverReachAnAnswerOrModel(int count)
    {
        var f = new Fixture();
        using var host = new InteractionFixture();
        var question = (await host.RunAsync(() => host.Authorization.PresentAsync(host.Request, CancellationToken.None))).Question!;
        f.ViewModel.BindSpokenToolApprovals(() => Enumerable.Repeat(ApprovalTarget(host, question), count).ToArray());
        await f.RunAsync("Kora, approve once");
        host.Store.Snapshot.Grants.Should().BeEmpty();
        host.Store.Snapshot.Questions.Single().Status.Should().Be(QuestionStatus.Pending);
        f.ViewModel.Transcript.Should().Contain("refused");
        f.ViewModel.Dispose();
    }

    [Theory]
    [InlineData("approve once")]
    [InlineData("Kora, approve always")]
    public async Task VoiceRequiresActivationAndNeverApprovesAlways(string command)
    {
        var f = await ReplyHostAsync();
        using var host = new InteractionFixture();
        var question = (await host.RunAsync(() => host.Authorization.PresentAsync(host.Request, CancellationToken.None))).Question!;
        f.ViewModel.BindSpokenToolApprovals(() => [ApprovalTarget(host, question)]);
        await f.RaiseActivatedTranscriptAsync(command, 1);
        host.Store.Snapshot.Grants.Should().BeEmpty();
        host.Store.Snapshot.Questions.Single().Status.Should().Be(QuestionStatus.Pending);
        f.ViewModel.Dispose();
    }

    [Theory]
    [InlineData("Kora, approve once", SpokenApprovalReply.Operation.Once)]
    [InlineData("Kora, approve for session", SpokenApprovalReply.Operation.Session)]
    [InlineData("Kora, deny", SpokenApprovalReply.Operation.Deny)]
    public async Task ActivatedVoiceReadsBackBeforeCommittingTheExactAnswer(string command, SpokenApprovalReply.Operation decision)
    {
        var f = await ReplyHostAsync();
        using var host = new InteractionFixture();
        var question = (await host.RunAsync(() => host.Authorization.PresentAsync(host.Request, CancellationToken.None))).Question!;
        var answered = false;
        var original = ApprovalTarget(host, question);
        f.ViewModel.BindSpokenToolApprovals(() => [original with
        {
            AnswerAsync = async (reply, channel, admitted) =>
            {
                f.TextToSpeech.SpokenText.Should().Contain(question.Spec.Text).And.Contain(question.Proposal!.Binding.ActionId);
                f.TextToSpeech.IsSpeaking.Should().BeFalse();
                reply.Decision.Should().Be(decision);
                answered = true;
                return await original.AnswerAsync(reply, channel, admitted);
            },
        }]);
        await f.RaiseActivatedTranscriptAsync(command, 1);
        answered.Should().BeTrue();
        host.Store.Snapshot.Questions.Single().AnswerChannel.Should().Be(RequestOrigin.ActivatedVoice);
        f.ViewModel.Dispose();
    }

    [Fact]
    public async Task TargetWithdrawalDuringReadbackDoesNotAnswerOrRetarget()
    {
        var f = await ReplyHostAsync();
        using var host = new InteractionFixture();
        var question = (await host.RunAsync(() => host.Authorization.PresentAsync(host.Request, CancellationToken.None))).Question!;
        var current = true;
        f.ViewModel.BindSpokenToolApprovals(() => [ApprovalTarget(host, question, () => current)]);
        var speech = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.TextToSpeech.SpeakStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        f.TextToSpeech.BeforeSpeak = () => f.TextToSpeech.SpeakGate = speech;
        var reply = f.RaiseActivatedTranscriptAsync("Kora, approve once", 1);
        try
        {
            await f.TextToSpeech.SpeakStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            f.TextToSpeech.SpokenText.Should().Contain(question.Spec.Text);
            current = false;
        }
        finally
        {
            speech.TrySetResult();
            await reply;
        }
        host.Store.Snapshot.Grants.Should().BeEmpty();
        host.Store.Snapshot.Questions.Single().Status.Should().Be(QuestionStatus.Pending);
        f.ViewModel.Transcript.Should().Contain("refused");
        f.ViewModel.Dispose();
    }

    [Theory]
    [InlineData("unbound")]
    [InlineData("closed")]
    [InlineData("native-only")]
    public async Task UnboundClosedAndNativeOnlyTargetsCannotConsumeAnAnswer(string boundary)
    {
        var f = new Fixture();
        using var host = new InteractionFixture();
        var question = (await host.RunAsync(() => host.Authorization.PresentAsync(host.Request, CancellationToken.None))).Question!;
        if (boundary is "native-only")
        {
            question = question with { Spec = new("Native disclosure", QuestionKind.SingleChoice,
                [new("once", "Once")], purpose: "authorization", sourceId: "local-web-page") };
        }
        if (boundary is not "unbound")
        { f.ViewModel.BindSpokenToolApprovals(() => [ApprovalTarget(host, question, () => boundary is not "closed")]); }
        await f.RunAsync("approve once");
        f.ViewModel.Transcript.Should().Contain("refused");
        host.Store.Snapshot.Grants.Should().BeEmpty();
        f.ViewModel.Dispose();
    }

    [Theory]
    [InlineData("admission")]
    [InlineData("target")]
    [InlineData("denied")]
    [InlineData("foreign")]
    public async Task ReviewWithdrawalOrMismatchCannotApproveAnyCard(string boundary)
    {
        var f = new Fixture();
        using var host = new InteractionFixture();
        var question = (await host.RunAsync(() => host.Authorization.PresentAsync(host.Request, CancellationToken.None))).Question!;
        var current = true;
        var target = ApprovalTarget(host, question, () => current) with
        {
            ReviewAsync = (_, _) =>
            {
                if (boundary is "admission") { f.ViewModel.BindSpokenToolApprovals(static () => []); }
                if (boundary is "target") { current = false; }
                return Task.FromResult(new HostInteractionDecision(
                    boundary is "denied" ? HostInteractionOutcome.Denied : HostInteractionOutcome.Presented,
                    "synthetic-review", boundary is "foreign" ? question with { Key = question.Key.Next() } : question));
            },
        };
        f.ViewModel.BindSpokenToolApprovals(() => [target]);
        await f.RunAsync("approve once");
        host.Store.Snapshot.Grants.Should().BeEmpty();
        f.ViewModel.Transcript.Should().Contain("refused");
        f.ViewModel.Dispose();
    }

    [Theory]
    [InlineData("admission")]
    [InlineData("target")]
    public async Task AdmissionOrTargetWithdrawalWhenReviewIsDisplayedPreventsCommit(string boundary)
    {
        var f = new Fixture();
        using var host = new InteractionFixture();
        var question = (await host.RunAsync(() => host.Authorization.PresentAsync(host.Request, CancellationToken.None))).Question!;
        var current = true;
        f.ViewModel.BindSpokenToolApprovals(() => [ApprovalTarget(host, question, () => current)]);
        f.ViewModel.PropertyChanged += (_, args) =>
        {
            if (!string.Equals(args.PropertyName, nameof(f.ViewModel.Transcript), StringComparison.Ordinal)
                || !f.ViewModel.Transcript.StartsWith(question.Spec.Text, StringComparison.Ordinal)) { return; }
            if (boundary is "target") { current = false; }
            else { f.ViewModel.BindSpokenToolApprovals(static () => []); }
        };
        await f.RunAsync("approve once");
        host.Store.Snapshot.Grants.Should().BeEmpty();
        f.ViewModel.Transcript.Should().Contain("refused");
        f.ViewModel.Dispose();
    }

    [Fact]
    public async Task CommitRefusalIsNotPresentedAsSuccessfulApproval()
    {
        var f = new Fixture();
        using var host = new InteractionFixture();
        var question = (await host.RunAsync(() => host.Authorization.PresentAsync(host.Request, CancellationToken.None))).Question!;
        f.ViewModel.BindSpokenToolApprovals(() => [ApprovalTarget(host, question) with
        {
            AnswerAsync = (_, _, _) => Task.FromResult(new HostInteractionDecision(HostInteractionOutcome.Denied, "synthetic-denial")),
        }]);
        await f.RunAsync("approve once");
        f.ViewModel.Transcript.Should().Contain("refused");
        host.Store.Snapshot.Grants.Should().BeEmpty();
        f.ViewModel.Dispose();
    }

    [Theory]
    [InlineData("invalid-operation")]
    [InlineData("io")]
    [InlineData("access")]
    [InlineData("cancelled")]
    [InlineData("argument")]
    public async Task CaptureFailuresAreExplicitAndNeverCreateAuthority(string failure)
    {
        var f = new Fixture();
        f.ViewModel.BindSpokenToolApprovals(() => throw (failure switch
        {
            "invalid-operation" => new InvalidOperationException("Synthetic owner unavailable"),
            "io" => new IOException("Synthetic storage unavailable"),
            "access" => new UnauthorizedAccessException("Synthetic access withdrawn"),
            "cancelled" => new OperationCanceledException("Synthetic source retired"),
            _ => new ArgumentException("Synthetic invalid target"),
        }));
        await f.RunAsync("approve once");
        f.ViewModel.Transcript.Should().Contain("refused");
        f.ViewModel.Dispose();
    }
}
