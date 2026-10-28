using AwesomeAssertions;

using Kora.Application.Hosting;
using Kora.Core.Auditing;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Tools.Speech;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    private static async Task<(Fixture Fixture, ForegroundConversationTarget Target, Task<string> Read)>
        StartInterruptibleAnswerAsync(bool qualified = true, bool ignoreCancellation = false)
    {
        var f = await CreateReadableResponseAsync();
        await f.RaiseActivatedTranscriptAsync("Kora, what can you do", 1);
        f.Voice.IsSpeechInterruptionAvailable = qualified;
        var store = new SessionCommandStore(f);
        var target = new ForegroundConversationTarget(store.Session, () => true);
        f.ViewModel.PublishForegroundConversationTarget(target);
        var original = HostRequest.Create(RequestOrigin.LocalUi);
        var request = new HostRequest(original.RequestId, store.Session.Authority.SessionId,
            original.TaskId, original.Origin, original.InvocationId);
        f.TextToSpeech.SpeakGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        f.TextToSpeech.SpeakStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        f.TextToSpeech.IgnoreSpeakCancellation = ignoreCancellation;
        async Task<string> Read()
        {
            using var activity = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request);
            return await f.ViewModel.ReadAnswerOnceAsync(SpeechAnswer(request), () => true, _ => Task.CompletedTask,
                TestContext.Current.CancellationToken);
        }
        var read = Read();
        await f.TextToSpeech.SpeakStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
        f.Reasoner.Requests.Clear();
        return (f, target, read);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SpeechInterruptionStopsOnlyOriginalSessionAndReportsJoinedInterruption(bool voice)
    {
        var (f, _, read) = await StartInterruptibleAnswerAsync();
        var stopCalls = f.TextToSpeech.StopCalls;
        if (voice)
        {
            await f.ViewModel.BeginPushToTalkAsync();
            f.TextToSpeech.StopCalls.Should().Be(stopCalls);
            f.TextToSpeech.IsSpeaking.Should().BeTrue();
            read.IsCompleted.Should().BeFalse();
            await f.ViewModel.EndPushToTalkAsync();
            await f.Voice.RaiseTranscriptAsync("Kora, stop speaking", 1);
        }
        else { await f.RunAsync("stop speaking"); }
        (await read).Should().Contain("interrupted").And.NotContain("completed once");
        f.ViewModel.ReadCurrentResponseStatus.Should().Contain("Exact session speech interrupted");
        f.ViewModel.IsCurrentResponseReadActive.Should().BeFalse();
        f.TextToSpeech.IsSpeaking.Should().BeFalse();
        f.Voice.IsCaptureQuiescent.Should().BeTrue();
        f.Reasoner.Requests.Should().BeEmpty();
        var audits = f.Audit.Events.Where(item => string.Equals(item.ActionId, "speech.interrupt", StringComparison.Ordinal)).ToArray();
        audits.Should().HaveCount(2);
        audits[0].Initiator.Should().Be(voice ? SecurityAuditInitiator.VoiceCommand : SecurityAuditInitiator.TypedCommand);
        audits[1].Outcome.Should().Be(SecurityAuditOutcome.Succeeded);
        f.Audit.Events.Should().NotContain(item => string.Equals(item.ActionId, SpeechReadOnce.AuditActionId, StringComparison.Ordinal)
            && item.Outcome == SecurityAuditOutcome.Succeeded);
    }

    [Theory]
    [InlineData("absent")]
    [InlineData("wrong-session")]
    [InlineData("focus-before-final")]
    [InlineData("focus-after-queue")]
    [InlineData("generation")]
    [InlineData("non-command")]
    [InlineData("unreleased-capture")]
    [InlineData("profile")]
    public async Task SpeechInterruptionNeverRetargetsOrCancelsUnrelatedWork(string loss)
    {
        var (f, target, read) = await StartInterruptibleAnswerAsync();
        if (loss is "absent") { f.ViewModel.PublishForegroundConversationTarget(null); }
        if (loss is "wrong-session")
        {
            var other = new SessionCommandStore(f);
            f.ViewModel.PublishForegroundConversationTarget(new(other.Session, () => true));
        }
        var calls = f.TextToSpeech.StopCalls;
        await f.ViewModel.BeginPushToTalkAsync();
        await f.ViewModel.EndPushToTalkAsync();
        if (loss is "focus-before-final")
        {
            target.Retire();
            f.ViewModel.PublishForegroundConversationTarget(new(target.Entry, () => true));
        }
        if (loss is "focus-after-queue") { f.Dispatcher.BeforeInvoke = target.Retire; }
        if (loss is "generation") { f.Voice.AdvanceGeneration(); }
        if (loss is "unreleased-capture") { f.Voice.PreventQuiescence = true; }
        if (loss is "profile") { f.Voice.IsSpeechInterruptionAvailable = false; }
        await f.Voice.RaiseTranscriptAsync(loss is "non-command" ? "Kora, cancel task" : "Kora, stop speaking", 1);
        f.TextToSpeech.StopCalls.Should().Be(calls);
        f.TextToSpeech.IsSpeaking.Should().BeTrue();
        read.IsCompleted.Should().BeFalse();
        f.Reasoner.Requests.Should().BeEmpty();
        f.Audit.Events.Should().NotContain(item => string.Equals(item.ActionId, "speech.interrupt", StringComparison.Ordinal)
            && item.Outcome == SecurityAuditOutcome.Succeeded);
        f.TextToSpeech.SpeakGate!.TrySetResult();
        await read;
    }

    [Fact]
    public async Task SpeechInterruptionUnqualifiedProfileNeverOpensCaptureOrStopsPlayback()
    {
        var (f, _, read) = await StartInterruptibleAnswerAsync(qualified: false);
        var starts = f.Voice.StartCalls;
        var stops = f.TextToSpeech.StopCalls;
        await f.ViewModel.BeginPushToTalkAsync();
        f.Voice.StartCalls.Should().Be(starts);
        f.TextToSpeech.StopCalls.Should().Be(stops);
        f.ViewModel.ReadCurrentResponseStatus.Should().Contain("unavailable");
        f.TextToSpeech.SpeakGate!.TrySetResult();
        await read;
    }

    [Fact]
    public async Task SpeechInterruptionWaitsForIgnoredCancellationAndSuppressesLateSuccess()
    {
        var (f, _, read) = await StartInterruptibleAnswerAsync(ignoreCancellation: true);
        f.TextToSpeech.HoldSpeechCompletionOnStop = true;
        f.TextToSpeech.StopCompleted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await f.ViewModel.BeginPushToTalkAsync();
        await f.ViewModel.EndPushToTalkAsync();
        var stop = f.Voice.RaiseTranscriptAsync("Kora, stop speaking", 1);
        await f.TextToSpeech.StopCompleted.Task.WaitAsync(TestContext.Current.CancellationToken);
        stop.IsCompleted.Should().BeFalse();
        read.IsCompleted.Should().BeFalse();
        f.ViewModel.IsCurrentResponseReadActive.Should().BeTrue();
        f.ViewModel.CanReadCurrentResponse.Should().BeFalse();
        f.Audit.Events.Should().NotContain(item => string.Equals(item.ActionId, "speech.interrupt", StringComparison.Ordinal)
            && item.Outcome == SecurityAuditOutcome.Succeeded);
        f.TextToSpeech.SpeakGate!.TrySetResult();
        await stop;
        (await read).Should().Contain("interrupted");
        f.ViewModel.IsCurrentResponseReadActive.Should().BeFalse();
        f.ViewModel.ReadCurrentResponseStatus.Should().Contain("released");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SpeechInterruptionRevalidatesAtTheCancellationFence(bool generation)
    {
        var (f, target, read) = await StartInterruptibleAnswerAsync();
        f.Audit.BeforeWrite = audit =>
        {
            if (string.Equals(audit.ActionId, "speech.interrupt", StringComparison.Ordinal)
                && audit.Outcome == SecurityAuditOutcome.Requested)
            {
                if (generation) { f.Voice.AdvanceGeneration(); }
                else { target.Retire(); }
            }
        };
        var stops = f.TextToSpeech.StopCalls;
        await f.ViewModel.BeginPushToTalkAsync();
        await f.ViewModel.EndPushToTalkAsync();
        await f.Voice.RaiseTranscriptAsync("Kora, stop speaking", 1);
        f.TextToSpeech.StopCalls.Should().Be(stops);
        read.IsCompleted.Should().BeFalse();
        f.Audit.Events.Last().Outcome.Should().Be(SecurityAuditOutcome.Cancelled);
        f.TextToSpeech.SpeakGate!.TrySetResult();
        await read;
    }

    [Theory]
    [InlineData("stop-failure")]
    [InlineData("unreleased-output")]
    [InlineData("read-audit")]
    public async Task SpeechInterruptionUnconfirmedOutputOrAuditIsNeverSuccess(string fault)
    {
        var (f, _, read) = await StartInterruptibleAnswerAsync();
        await f.ViewModel.BeginPushToTalkAsync();
        await f.ViewModel.EndPushToTalkAsync();
        if (fault is "stop-failure") { f.TextToSpeech.StopException = new InvalidOperationException("Synthetic release failure"); }
        if (fault is "unreleased-output") { f.TextToSpeech.RemainSpeakingOnStop = true; }
        if (fault is "read-audit")
        {
            f.Audit.BeforeWrite = audit =>
            {
                if (string.Equals(audit.ActionId, SpeechReadOnce.AuditActionId, StringComparison.Ordinal)
                    && audit.Outcome == SecurityAuditOutcome.Cancelled)
                { throw new IOException("Synthetic terminal audit failure"); }
            };
        }
        await f.Voice.RaiseTranscriptAsync("Kora, stop speaking", 1);
        await read;
        f.Audit.Events.Should().Contain(item => string.Equals(item.ActionId, "speech.interrupt", StringComparison.Ordinal)
            && item.Outcome == SecurityAuditOutcome.Failed);
        f.Audit.Events.Should().NotContain(item => string.Equals(item.ActionId, "speech.interrupt", StringComparison.Ordinal)
            && item.Outcome == SecurityAuditOutcome.Succeeded);
        f.TextToSpeech.StopException = null;
        f.TextToSpeech.RemainSpeakingOnStop = false;
        await f.ViewModel.StopSpeakingFromTrayAsync();
        f.TextToSpeech.IsSpeaking.Should().BeFalse();
    }

    [Fact]
    public async Task SpeechInterruptionQuarantinesNewSpeechUntilOutputStopJoins()
    {
        var (f, _, read) = await StartInterruptibleAnswerAsync();
        await f.ViewModel.BeginPushToTalkAsync();
        await f.ViewModel.EndPushToTalkAsync();
        f.TextToSpeech.StopGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var stopEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.TextToSpeech.BeforeStop = () => stopEntered.TrySetResult();
        var calls = f.TextToSpeech.SpeakCalls;
        var stop = f.Voice.RaiseTranscriptAsync("Kora, stop speaking", 1);
        try
        {
            await stopEntered.Task.WaitAsync(TestContext.Current.CancellationToken);
            stop.IsCompleted.Should().BeFalse();
            await f.RunAsync("what can you do");
            f.TextToSpeech.SpeakCalls.Should().Be(calls);
            f.ViewModel.PreviewVoiceCommand.CanExecute(null).Should().BeFalse();
        }
        finally
        {
            f.TextToSpeech.StopGate.TrySetResult();
            await Task.WhenAll(stop, read);
        }
        f.ViewModel.ReadCurrentResponseStatus.Should().Contain("released");
    }

    [Fact]
    public async Task SpeechInterruptionCannotStopReplacementPlaybackInTheSameSession()
    {
        var (f, target, originalRead) = await StartInterruptibleAnswerAsync();
        await f.ViewModel.BeginPushToTalkAsync();
        await f.ViewModel.EndPushToTalkAsync();
        f.TextToSpeech.SpeakGate!.TrySetResult();
        await originalRead;
        f.TextToSpeech.SpeakGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        f.TextToSpeech.SpeakStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var original = HostRequest.Create(RequestOrigin.LocalUi);
        var request = new HostRequest(original.RequestId, target.Entry.Authority.SessionId,
            original.TaskId, original.Origin, original.InvocationId);
        async Task<string> ReadReplacement()
        {
            using var activity = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request);
            return await f.ViewModel.ReadAnswerOnceAsync(SpeechAnswer(request, "Replacement answer."),
                () => true, _ => Task.CompletedTask, TestContext.Current.CancellationToken);
        }
        var replacement = ReadReplacement();
        await f.TextToSpeech.SpeakStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
        var stops = f.TextToSpeech.StopCalls;
        await f.Voice.RaiseTranscriptAsync("Kora, stop speaking", 1);
        f.TextToSpeech.StopCalls.Should().Be(stops);
        replacement.IsCompleted.Should().BeFalse();
        f.ViewModel.ReadCurrentResponseStatus.Should().Contain("refused");
        f.TextToSpeech.SpeakGate.TrySetResult();
        (await replacement).Should().Contain("completed once");
    }
}
