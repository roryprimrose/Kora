using AwesomeAssertions;

using Kora.Application.Voice;
using Kora.Core.Auditing;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Voice;
using Kora.Tools.Speech;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Fact]
    public async Task WakeSpeechInterruptionUsesExactSessionActionOnlyAfterNativeCaptureJoins()
    {
        var stage = new HandsFreeStage { IsAvailable = false, InterruptionAvailable = true };
        HandsFreeVoiceRecognitionService? decorated = null;
        var f = CreateHandsFreeFixture(stage, value => decorated = value);
        await using var voice = decorated!;
        await f.ViewModel.InitializeAsync();
        f.ViewModel.DefaultResponseMode = ResponseOutputMode.Hybrid;
        var (_, _, read) = await StartInterruptibleAnswerAsync(preparedFixture: f);
        var capture = await stage.NextAsync();
        var stops = f.TextToSpeech.StopCalls;
        capture.HoldClose = true;
        capture.Wake.SetResult(new(capture.Binding.Generation, 12, true));
        capture.Result.SetResult(new("stop speaking", 1, capture.Binding.Generation));
        await capture.Closing.Task.WaitAsync(TestContext.Current.CancellationToken);
        f.TextToSpeech.StopCalls.Should().Be(stops);
        f.TextToSpeech.IsSpeaking.Should().BeTrue();
        read.IsCompleted.Should().BeFalse();
        capture.ReleaseClose.SetResult();
        (await read.WaitAsync(TestContext.Current.CancellationToken)).Should().Contain("interrupted");
        f.ViewModel.ReadCurrentResponseStatus.Should().Contain("Exact session speech interrupted");
        f.TextToSpeech.IsSpeaking.Should().BeFalse();
        voice.IsCaptureQuiescent.Should().BeTrue();
        f.Reasoner.Requests.Should().BeEmpty();
        f.Audit.Events.Should().ContainSingle(item => item.ActionId == "speech.interrupt"
            && item.Outcome == SecurityAuditOutcome.Succeeded && item.Initiator == SecurityAuditInitiator.VoiceCommand);
        await f.ViewModel.DisposeAsync();
    }

    [Theory]
    [InlineData("non-command")]
    [InlineData("focus")]
    [InlineData("audit-focus")]
    [InlineData("qualification")]
    [InlineData("audit-qualification")]
    [InlineData("audit-capture")]
    public async Task WakeSpeechInterruptionNeverPromotesOtherTextOrRetargetsRetiredPlayback(string loss)
    {
        var stage = new HandsFreeStage { IsAvailable = false, InterruptionAvailable = true };
        HandsFreeVoiceRecognitionService? decorated = null;
        var f = CreateHandsFreeFixture(stage, value => decorated = value);
        await using var voice = decorated!;
        await f.ViewModel.InitializeAsync();
        f.ViewModel.DefaultResponseMode = ResponseOutputMode.Hybrid;
        var (_, target, read) = await StartInterruptibleAnswerAsync(preparedFixture: f);
        var capture = await stage.NextAsync();
        var stops = f.TextToSpeech.StopCalls;
        var dispatched = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (loss is "non-command")
        { f.Dispatcher.BeforeInvoke = () => dispatched.TrySetResult(); }
        if (loss is "focus") { target.Retire(); }
        if (loss is "qualification") { stage.InterruptionAvailable = false; }
        if (loss is "audit-focus" or "audit-qualification" or "audit-capture")
        {
            f.Audit.BeforeWrite = audit =>
            {
                if (string.Equals(audit.ActionId, "speech.interrupt", StringComparison.Ordinal)
                    && audit.Outcome == SecurityAuditOutcome.Requested)
                {
                    if (loss is "audit-focus") { target.Retire(); }
                    if (loss is "audit-qualification") { stage.InterruptionAvailable = false; }
                    if (loss is "audit-capture") { f.Voice.PreventQuiescence = true; }
                    dispatched.TrySetResult();
                }
            };
        }
        capture.Wake.SetResult(new(capture.Binding.Generation, 12, true));
        capture.Result.SetResult(new(loss is "non-command" ? "lock windows" : "stop speaking", 1, capture.Binding.Generation));
        await capture.Closing.Task.WaitAsync(TestContext.Current.CancellationToken);
        if (loss is "non-command" or "audit-focus" or "audit-qualification" or "audit-capture")
        { await dispatched.Task.WaitAsync(TestContext.Current.CancellationToken); }
        f.TextToSpeech.SpeakGate!.TrySetResult();
        await read.WaitAsync(TestContext.Current.CancellationToken);
        f.TextToSpeech.StopCalls.Should().Be(stops);
        f.Session.LockCalls.Should().Be(0);
        f.Reasoner.Requests.Should().BeEmpty();
        f.Audit.Events.Should().NotContain(item => item.ActionId == "speech.interrupt" && item.Outcome == SecurityAuditOutcome.Succeeded);
        f.Voice.PreventQuiescence = false;
        await f.ViewModel.DisposeAsync();
    }

    [Fact]
    public async Task WakeSpeechInterruptionDoesNotOpenWithoutTheOriginalForegroundSession()
    {
        var stage = new HandsFreeStage { IsAvailable = false, InterruptionAvailable = true };
        HandsFreeVoiceRecognitionService? decorated = null;
        var f = CreateHandsFreeFixture(stage, value => decorated = value);
        await using var voice = decorated!;
        await f.ViewModel.InitializeAsync();
        f.ViewModel.DefaultResponseMode = ResponseOutputMode.Hybrid;
        f.Audit.BeforeWrite = audit =>
        {
            if (string.Equals(audit.ActionId, SpeechReadOnce.AuditActionId, StringComparison.Ordinal)
                && audit.Outcome == SecurityAuditOutcome.Requested)
            { f.ViewModel.PublishForegroundConversationTarget(null); }
        };
        var (_, _, read) = await StartInterruptibleAnswerAsync(preparedFixture: f);
        stage.OpenCount.Should().Be(0);
        voice.IsCaptureQuiescent.Should().BeTrue();
        f.TextToSpeech.IsSpeaking.Should().BeTrue();
        f.TextToSpeech.SpeakGate!.TrySetResult();
        (await read.WaitAsync(TestContext.Current.CancellationToken)).Should().Contain("completed once");
        f.Audit.Events.Should().NotContain(item => item.ActionId == "speech.interrupt");
        f.Reasoner.Requests.Should().BeEmpty();
        await f.ViewModel.DisposeAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WakeSpeechInterruptionNeverPermitsUnrelatedSpeechAndHostDisposalJoinsItsNativeClose(bool auditFailure)
    {
        var stage = new HandsFreeStage { IsAvailable = false, InterruptionAvailable = true };
        HandsFreeVoiceRecognitionService? decorated = null;
        var f = CreateHandsFreeFixture(stage, value => decorated = value);
        await using var voice = decorated!;
        await f.ViewModel.InitializeAsync();
        f.ViewModel.DefaultResponseMode = ResponseOutputMode.Hybrid;
        HostRequest? request = null;
        var (_, _, read) = await StartInterruptibleAnswerAsync(preparedFixture: f, captureRequest: value => request = value);
        var capture = await stage.NextAsync();
        capture.HoldClose = true;
        if (auditFailure)
        {
            f.Audit.BeforeWrite = audit =>
            {
                if (string.Equals(audit.ActionId, SpeechReadOnce.AuditActionId, StringComparison.Ordinal)
                    && audit.Outcome != SecurityAuditOutcome.Requested)
                {
                    f.Audit.BeforeWrite = null;
                    throw new NotSupportedException("Unexpected audit contract failure.");
                }
            };
        }
        f.ViewModel.ListeningStatus.Should().Contain("original retained playback only");
        f.ViewModel.IsSpeechResponseEnabled.Should().BeFalse("unrelated callers cannot use the exact-playback exception");
        using (HostActivity.BeginRoot(request!, HostActivityLayer.Application, HostOperation.Request))
        { f.ViewModel.IsSpeechResponseEnabled.Should().BeTrue(); }
        f.TextToSpeech.SpeakGate!.TrySetResult();
        await capture.Closing.Task.WaitAsync(TestContext.Current.CancellationToken);
        using (HostActivity.BeginRoot(request!, HostActivityLayer.Application, HostOperation.Request))
        { f.ViewModel.IsSpeechResponseEnabled.Should().BeFalse("playback has ended while capture is still joining"); }
        var disposing = f.ViewModel.DisposeAsync().AsTask();
        disposing.IsCompleted.Should().BeFalse();
        read.IsCompleted.Should().BeFalse();
        capture.ReleaseClose.TrySetResult();
        if (auditFailure)
        {
            Func<Task> run = () => read;
            await run.Should().ThrowAsync<NotSupportedException>();
            f.Audit.Events.Should().NotContain(item => item.ActionId == SpeechReadOnce.AuditActionId
                && item.Outcome == SecurityAuditOutcome.Succeeded);
            Func<Task> dispose = () => disposing;
            await dispose.Should().ThrowAsync<NotSupportedException>();
        }
        else { await Task.WhenAll(disposing, read); }
        voice.IsCaptureQuiescent.Should().BeTrue();
        f.Audit.Events.Should().NotContain(item => item.ActionId == "speech.interrupt");
    }

    [Fact]
    public async Task WakeSpeechInterruptionFailedNativeReleaseReportsRecoveryWithoutSuccessfulInterruption()
    {
        var stage = new HandsFreeStage { IsAvailable = false, InterruptionAvailable = true };
        HandsFreeVoiceRecognitionService? voice = null;
        var f = CreateHandsFreeFixture(stage, value => voice = value);
        await f.ViewModel.InitializeAsync();
        f.ViewModel.DefaultResponseMode = ResponseOutputMode.Hybrid;
        var (_, _, read) = await StartInterruptibleAnswerAsync(preparedFixture: f);
        var capture = await stage.NextAsync();
        capture.FailClose = true;
        capture.Wake.SetResult(new(capture.Binding.Generation, 12, true));
        capture.Result.SetResult(new("stop speaking", 1, capture.Binding.Generation));
        await capture.Closing.Task.WaitAsync(TestContext.Current.CancellationToken);
        f.TextToSpeech.SpeakGate!.TrySetResult();
        (await read).Should().Contain("unconfirmed").And.NotContain("completed once");
        f.ViewModel.ReadCurrentResponseStatus.Should().Contain("unconfirmed");
        f.ViewModel.IsVoiceEnabled.Should().BeFalse();
        voice!.IsCaptureQuiescent.Should().BeFalse();
        f.Audit.Events.Should().NotContain(item => item.ActionId == "speech.interrupt" && item.Outcome == SecurityAuditOutcome.Succeeded);
        await f.ViewModel.DisposeAsync();
        await FluentActions.Awaiting(() => voice.DisposeAsync().AsTask()).Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task WakeSpeechInterruptionUnqualifiedAdapterKeepsPlaybackAndCaptureUnchanged()
    {
        var stage = new HandsFreeStage { IsAvailable = false };
        HandsFreeVoiceRecognitionService? decorated = null;
        var f = CreateHandsFreeFixture(stage, value => decorated = value);
        await using var voice = decorated!;
        await f.ViewModel.InitializeAsync();
        f.ViewModel.DefaultResponseMode = ResponseOutputMode.Hybrid;
        var (_, _, read) = await StartInterruptibleAnswerAsync(preparedFixture: f);
        stage.OpenCount.Should().Be(0);
        f.TextToSpeech.SpeakGate!.TrySetResult();
        (await read).Should().Contain("completed once");
        f.Audit.Events.Should().NotContain(item => item.ActionId == "speech.interrupt");
        await f.ViewModel.DisposeAsync();
    }
}
