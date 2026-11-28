using AwesomeAssertions;

using Kora.Application.ViewModels;
using Kora.Core.Communication;
using Kora.Core.Configuration;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    private static readonly ConversationCheckInTarget CheckInTarget = new(
        new(Guid.NewGuid(), HostRequest.Create(RequestOrigin.LocalUi), new(1), new(2), DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch.AddSeconds(15)),
        new(Guid.NewGuid(), new OrdinaryTurnDeadline(5), TimeSpan.FromMinutes(5)));

    private sealed class CheckInPort
    {
        internal List<(ConversationCheckInChoice Choice, RequestOrigin Origin)> Answers { get; } = [];
        internal IReadOnlyList<ConversationCheckInTarget> Targets { get; set; } = [CheckInTarget];
        internal bool Current { get; set; } = true;
        internal Func<Task<ConversationExecutionStatus>>? Answer { get; set; }
        internal int Captures { get; private set; }
        internal MainViewModel.TurnCheckInPort Port => new(() => { Captures++; return Targets; }, _ => Current, (target, choice, admitted, origin) =>
        {
            target.Should().Be(CheckInTarget);
            admitted().Should().BeTrue();
            Answers.Add((choice, origin));
            return Answer?.Invoke() ?? Task.FromResult(new ConversationExecutionStatus(target.Execution,
                choice.Operation == ConversationCheckInOperation.Stop ? ConversationExecutionPhase.Cancelling
                    : ConversationExecutionPhase.Running));
        });
    }

    [Theory]
    [InlineData("keep going", ConversationCheckInOperation.KeepGoing, null, "keep going for another 5 minutes")]
    [InlineData("Kora, keep going for 1 minute", ConversationCheckInOperation.KeepGoingFor, 1, "keep going for 1 minute")]
    [InlineData("keep going for 12 minutes", ConversationCheckInOperation.KeepGoingFor, 12, "keep going for 12 minutes")]
    [InlineData("keep going until done", ConversationCheckInOperation.KeepRunning, null, "without more check-ins")]
    [InlineData("stop this turn", ConversationCheckInOperation.Stop, null, "stop this turn")]
    public async Task TypedCheckInRepliesAnswerOnlyTheSingleCurrentCheckIn(string command,
        ConversationCheckInOperation operation, int? minutes, string readback)
    {
        var f = new Fixture();
        var port = new CheckInPort();
        var transcripts = new List<string>();
        f.ViewModel.PropertyChanged += (_, args) =>
        {
            if (string.Equals(args.PropertyName, nameof(f.ViewModel.Transcript), StringComparison.Ordinal)) { transcripts.Add(f.ViewModel.Transcript); }
        };
        f.ViewModel.BindTurnCheckIns(port.Port);
        await f.RunAsync(command);
        var answer = port.Answers.Should().ContainSingle().Subject;
        answer.Choice.Operation.Should().Be(operation);
        answer.Choice.Minutes?.Minutes.Should().Be(minutes);
        answer.Origin.Should().Be(RequestOrigin.LocalUi);
        transcripts.Should().Contain(text => text.Contains(readback, StringComparison.Ordinal)
            && text.Contains(CheckInTarget.Execution.Request.TaskId.Value.ToString("D"), StringComparison.Ordinal));
        f.ViewModel.Transcript.Should().Contain(operation == ConversationCheckInOperation.Stop ? "Stop requested" : "Check-in answered");
        f.TextToSpeech.SpokenText.Should().BeNull("typed replies are not read aloud");
        f.ViewModel.Dispose();
    }

    [Theory]
    [InlineData("unbound")]
    [InlineData("none")]
    [InlineData("ambiguous")]
    [InlineData("stale")]
    [InlineData("malformed")]
    [InlineData("failure")]
    public async Task MissingAmbiguousStaleMalformedOrFailedCheckInRepliesAreRefused(string boundary)
    {
        var f = new Fixture();
        var port = new CheckInPort
        {
            Targets = boundary switch { "none" => [], "ambiguous" => [CheckInTarget, CheckInTarget], _ => [CheckInTarget] },
            Current = boundary is not "stale",
            Answer = boundary is "failure" ? () => throw new InvalidOperationException("Synthetic stale check-in") : null,
        };
        if (boundary is not "unbound") { f.ViewModel.BindTurnCheckIns(port.Port); }
        await f.RunAsync(boundary is "malformed" ? "keep going for 90 minutes" : "keep going");
        f.ViewModel.Transcript.Should().Contain("refused").And.Contain("continues unchanged");
        if (boundary is not "failure") { port.Answers.Should().BeEmpty(); }
        f.ViewModel.Dispose();
    }

    [Theory]
    [InlineData("Kora, keep going", true)]
    [InlineData("keep going", false)]
    public async Task ActivatedVoiceRequiresTheNameAndReadsBackBeforeAnswering(string command, bool accepted)
    {
        var f = await ReplyHostAsync();
        var port = new CheckInPort();
        var original = port.Port;
        f.ViewModel.BindTurnCheckIns(original with
        {
            AnswerAsync = (target, choice, admitted, origin) =>
            {
                f.TextToSpeech.SpokenText.Should().Contain("keep going for another 5 minutes");
                f.TextToSpeech.IsSpeaking.Should().BeFalse();
                return original.AnswerAsync(target, choice, admitted, origin);
            },
        });
        await f.RaiseActivatedTranscriptAsync(command, 1);
        port.Answers.Select(answer => answer.Origin).Should().Equal(accepted ? [RequestOrigin.ActivatedVoice] : []);
        f.ViewModel.Dispose();
    }

    [Fact]
    public async Task CheckInWithdrawnDuringSpokenReadbackIsNotAnswered()
    {
        var f = await ReplyHostAsync();
        var port = new CheckInPort();
        f.ViewModel.BindTurnCheckIns(port.Port);
        var speech = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.TextToSpeech.SpeakStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        f.TextToSpeech.BeforeSpeak = () => f.TextToSpeech.SpeakGate = speech;
        var reply = f.RaiseActivatedTranscriptAsync("Kora, stop this turn", 1);
        try
        {
            await f.TextToSpeech.SpeakStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            port.Current = false;
        }
        finally
        {
            speech.TrySetResult();
            await reply;
        }
        port.Answers.Should().BeEmpty();
        f.ViewModel.Transcript.Should().Contain("refused");
        f.ViewModel.Dispose();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProtectedCallSpokenCheckInRepliesFollowTheSpokenApprovalPolicy(bool allowSpokenApprovals)
    {
        var f = new Fixture(enableQuestionReplies: true);
        f.VoiceConsent.Consent = true;
        f.Voice.Microphones = [new("mic", "Synthetic headset")];
        f.Voice.DefaultMicrophoneId = "mic";
        f.CallPreferences.Settings = new CallAwareSettings(true, true, allowSpokenApprovals);
        f.ViewModel.BindVoiceOwnershipGate(static () => true);
        await f.ViewModel.InitializeAsync();
        f.CallState.SetState(CallState.Active);
        await f.ViewModel.CallClosureTask;
        f.ViewModel.IsProtectedCall.Should().BeTrue();
        var port = new CheckInPort();
        f.ViewModel.BindTurnCheckIns(port.Port);
        await f.RaiseActivatedTranscriptAsync("Kora, keep going", 1);
        // Admission follows calls.allowSpokenApprovals; the exact spoken readback itself still respects call speech policy.
        port.Captures.Should().Be(allowSpokenApprovals ? 1 : 0);
        port.Answers.Should().BeEmpty("no readback was spoken during the active call, so nothing was answered");
        f.ViewModel.Dispose();
    }

    [Fact]
    public void CheckInPortRequiresTheCanonicalOwner() =>
        FluentActions.Invoking(() => MainViewModel.TurnCheckInPort.For(null!)).Should().Throw<ArgumentNullException>();

    [Fact]
    public void BindingANullCheckInPortIsRejected()
    {
        var f = new Fixture();
        f.ViewModel.Invoking(model => model.BindTurnCheckIns(null!)).Should().Throw<ArgumentNullException>();
        f.ViewModel.Dispose();
    }
}
