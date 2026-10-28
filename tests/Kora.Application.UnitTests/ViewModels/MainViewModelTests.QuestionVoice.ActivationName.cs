using AwesomeAssertions;
using Kora.Application.Interaction;
using Kora.Application.UnitTests.Interaction;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Core.Auditing;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Theory]
    [InlineData("First")]
    [InlineData("1")]
    [InlineData("Option 1")]
    [InlineData("Nova First")]
    [InlineData("Nova 1")]
    [InlineData("Nova, option 1")]
    public async Task Configured_name_choice_reply_uses_composed_observer_and_original_host_submission(string transcript)
    {
        var f = await ReplyHostAsync();
        await f.ViewModel.SetAssistantNameAsync("Nova");
        await f.ViewModel.EnableListeningFromTrayAsync(f.ViewModel.MicrophoneTopologyRevision);
        using var host = new InteractionFixture();
        var question = await host.QuestionAsync();
        await using var replies = f.ViewModel.BindQuestionVoiceReplies(host.Questions, host.Time,
            NullLogger<HostQuestionVoiceReplyService>.Instance)!;
        HostInteractionDecision? accepted = null;
        await replies.OpenAsync(question, static () => true, decision => accepted = decision, false,
            TestContext.Current.CancellationToken);
        f.Voice.IsListening.Should().BeTrue();
        f.ReplyVoice!.Speech();
        f.Voice.RaiseTranscript(transcript, 1);
        await replies.Quiescence;
        accepted.Should().NotBeNull();
        accepted!.Outcome.Should().Be(HostInteractionOutcome.Answered);
        accepted.Question!.Key.Should().Be(question.Key.Next());
        accepted.Question.Draft!.Choices.Should().Equal("a");
        accepted.Question.AnswerChannel.Should().Be(RequestOrigin.ActivatedVoice);
        host.Store.Snapshot.Questions.Single().Should().Be(accepted.Question);
        host.Store.Snapshot.Grants.Should().BeEmpty();
        f.Reasoner.Requests.Should().BeEmpty();
        f.Session.LockCalls.Should().Be(0);
        f.ClipboardReader.Calls.Should().Be(0);
        f.Voice.IsCaptureQuiescent.Should().BeTrue();
        f.Voice.StartCalls.Should().Be(1);
        f.ViewModel.Dispose();
    }

    [Theory]
    [InlineData("Stop", "Stop listening")]
    [InlineData("Stop", "Stop Stop listening")]
    [InlineData("Nova", "Stop listening")]
    [InlineData("Nova", "Nova Stop listening")]
    public async Task Privacy_reducing_stop_precedes_even_an_exact_literal_or_named_question_choice(string name, string transcript)
    {
        var f = await ReplyHostAsync();
        await f.ViewModel.SetAssistantNameAsync(name);
        await f.ViewModel.EnableListeningFromTrayAsync(f.ViewModel.MicrophoneTopologyRevision);
        using var host = new InteractionFixture();
        var question = await host.QuestionAsync(new("Choose", QuestionKind.SingleChoice,
            [new("literal", transcript), new("stop", "listening")]));
        await using var replies = f.ViewModel.BindQuestionVoiceReplies(host.Questions, host.Time,
            NullLogger<HostQuestionVoiceReplyService>.Instance)!;
        var accepted = false;
        await replies.OpenAsync(question, static () => true, _ => accepted = true, false,
            TestContext.Current.CancellationToken);
        f.Voice.RaiseTranscript(transcript, 1);
        await replies.Quiescence;
        accepted.Should().BeFalse();
        host.Store.Snapshot.Questions.Single().Status.Should().Be(QuestionStatus.Pending);
        host.Store.Snapshot.Questions.Single().Draft.Should().BeNull();
        f.ViewModel.IsVoiceEnabled.Should().BeFalse();
        f.Voice.IsCaptureQuiescent.Should().BeTrue();
        f.Voice.StartCalls.Should().Be(1);
        f.Audit.Events.Should().Contain(item => item.ActionId == "configuration.listening-disabled"
            && item.Initiator == SecurityAuditInitiator.VoiceCommand && item.Outcome == SecurityAuditOutcome.Succeeded);
        f.Reasoner.Requests.Should().BeEmpty();
        f.ViewModel.Dispose();
    }

    [Theory]
    [InlineData("Nova lock the machine", false)]
    [InlineData("  Nova,\tFirst\r\n", true)]
    public async Task Non_stop_choice_and_free_text_answers_are_schema_data_not_commands(string transcript, bool text)
    {
        var f = await ReplyHostAsync();
        await f.ViewModel.SetAssistantNameAsync("Nova");
        await f.ViewModel.EnableListeningFromTrayAsync(f.ViewModel.MicrophoneTopologyRevision);
        using var host = new InteractionFixture();
        var question = await host.QuestionAsync(text
            ? new("Explain", QuestionKind.Text, [], maximumTextLength: transcript.Length)
            : new("Choose", QuestionKind.SingleChoice, [new("lock", "lock the machine")]));
        await using var replies = f.ViewModel.BindQuestionVoiceReplies(host.Questions, host.Time,
            NullLogger<HostQuestionVoiceReplyService>.Instance)!;
        await replies.OpenAsync(question, static () => true, _ => { }, false, TestContext.Current.CancellationToken);
        f.Voice.RaiseTranscript(transcript, 1);
        await replies.Quiescence;
        var answered = host.Store.Snapshot.Questions.Single();
        answered.Status.Should().Be(QuestionStatus.Answered);
        if (text) { answered.Draft!.Text.Should().Be(transcript); }
        else { answered.Draft!.Choices.Should().Equal("lock"); }
        f.Session.LockCalls.Should().Be(0);
        f.Reasoner.Requests.Should().BeEmpty();
        f.ViewModel.IsVoiceEnabled.Should().BeTrue();
        f.Voice.IsCaptureQuiescent.Should().BeTrue();
        f.ViewModel.Dispose();
    }

    [Theory]
    [InlineData("playback")]
    [InlineData("capture")]
    [InlineData("dispatch")]
    public async Task Real_configured_name_change_retires_playback_capture_and_queued_delivery_without_rearm(string phase)
    {
        var f = await ReplyHostAsync();
        using var host = new InteractionFixture();
        var question = await host.QuestionAsync();
        await using var replies = f.ViewModel.BindQuestionVoiceReplies(host.Questions, host.Time,
            NullLogger<HostQuestionVoiceReplyService>.Instance)!;
        if (phase is "playback") { f.TextToSpeech.SpeakGate = new(TaskCreationOptions.RunContinuationsAsynchronously); }
        var opening = replies.OpenAsync(question, static () => true, _ => { }, phase is "playback",
            TestContext.Current.CancellationToken);
        if (phase is not "playback") { await opening; }
        var starts = f.Voice.StartCalls;
        var generation = f.Voice.Generation;
        f.Voice.StopGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Task? rename = null;
        if (phase is "dispatch")
        {
            f.Dispatcher.BeforeInvoke = () => rename = f.ViewModel.SetAssistantNameAsync("Nova");
            f.Voice.RaiseTranscript("Kora First", 1);
        }
        else { rename = f.ViewModel.SetAssistantNameAsync("Nova"); }
        replies.IsActive.Should().BeFalse();
        f.Voice.IsListening.Should().BeFalse();
        replies.Quiescence.IsCompleted.Should().BeFalse();
        f.Voice.RaiseTranscriptForGeneration(generation);
        f.TextToSpeech.SpeakGate?.TrySetResult();
        f.Voice.StopGate.SetResult();
        await rename!;
        await opening;
        await replies.Quiescence;
        host.Store.Snapshot.Questions.Single().Status.Should().Be(QuestionStatus.Pending);
        host.Store.Snapshot.Questions.Single().Draft.Should().BeNull();
        f.ViewModel.AssistantName.Should().Be("Nova");
        f.ViewModel.IsVoiceEnabled.Should().BeFalse();
        f.Voice.IsCaptureQuiescent.Should().BeTrue();
        f.Voice.RaiseTranscript("Kora First", 1);
        f.Voice.RaiseTranscript("Nova First", 1);
        f.Voice.StartCalls.Should().Be(starts);
        f.Reasoner.Requests.Should().BeEmpty();
        f.Session.LockCalls.Should().Be(0);
        f.ViewModel.Dispose();
    }
}
