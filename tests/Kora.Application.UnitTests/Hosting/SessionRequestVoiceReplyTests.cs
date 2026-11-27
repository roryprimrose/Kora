using AwesomeAssertions;

using Kora.Application.Hosting;
using Kora.Application.Hosting.SessionRouting;
using Kora.Application.UnitTests.Interaction;
using Kora.Application.Voice;
using Kora.Core.Hosting;
using Kora.Core.Voice;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.Hosting;

[Collection("Host tracing")]
public sealed class SessionRequestVoiceReplyTests
{
    [Theory]
    [InlineData("complete")]
    [InlineData("cancel")]
    [InlineData("native")]
    [InlineData("mismatch")]
    [InlineData("speech-failure")]
    [InlineData("after-playback")]
    public async Task ApproveAllReadbackHasOnlyCancelDuringPlaybackAndJoinsBeforeAutomaticSend(string outcome)
    {
        await using var f = new QuestionReplyFixture();
        var playback = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var speaking = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var exact = true;
        var request = new SessionRoutingVoiceRequest("  exact\r\noriginal  ", HostRequest.Create(RequestOrigin.ActivatedVoice),
            static () => true, f.Capture, release: HandsFreeVoiceContinuation.CaptureRelease.Preserved);
        await using var reply = SessionRequestVoiceReply.Create(request, "Build", () => exact, () => f.Admission,
            async (text, eligible, token) =>
            {
                text.Should().Be("Sending to Build:   exact\r\noriginal  . Say cancel to stop.");
                eligible().Should().BeTrue();
                f.Capture.IsListening.Should().BeTrue();
                speaking.SetResult();
                if (outcome is "speech-failure") { throw new IOException("Synthetic"); }
                await playback.Task.WaitAsync(token);
                if (outcome is "after-playback") { exact = false; }
            }, NullLogger.Instance, submitAfterReadback: true);
        reply.Spec.Options.Select(option => option.Label).Should().Equal("cancel");
        var operation = reply.StartAsync();
        await speaking.Task;
        f.Capture.Closing = new(TaskCreationOptions.RunContinuationsAsynchronously);
        if (outcome is "complete" or "after-playback") { playback.SetResult(); }
        if (outcome is "cancel") { f.Capture.Transcript("cancel"); }
        if (outcome is "mismatch") { f.Capture.Transcript("send"); }
        if (outcome is "native") { reply.Cancel(); }
        await f.Capture.StopStarted.Task;
        if (outcome is not "speech-failure") { operation.IsCompleted.Should().BeFalse(); }
        f.Capture.Closing.SetResult();
        var answer = await operation;
        if (outcome is "complete") { answer!.Choice.Should().Be(SessionRoutingVoiceReply.SessionRoutingVoiceChoice.Submit); }
        else { answer.Should().BeNull(); }
        f.Capture.IsCaptureQuiescent.Should().BeTrue();
    }

    [Theory]
    [InlineData("send", SessionRoutingVoiceReply.SessionRoutingVoiceChoice.Submit)]
    [InlineData("cancel", SessionRoutingVoiceReply.SessionRoutingVoiceChoice.Cancel)]
    public async Task ExactReadbackFinishesBeforeOneReplyAndPhysicalReleasePrecedesItsAnswer(
        string spoken, SessionRoutingVoiceReply.SessionRoutingVoiceChoice expected)
    {
        await using var f = new QuestionReplyFixture();
        var request = new SessionRoutingVoiceRequest("  exact\r\noriginal  ", HostRequest.Create(RequestOrigin.ActivatedVoice),
            static () => true, f.Capture, release: HandsFreeVoiceContinuation.CaptureRelease.Preserved);
        var playback = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var opened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        string? text = null;
        await using var reply = SessionRequestVoiceReply.Create(request, "Build", static () => true, () => f.Admission,
            async (value, eligible, token) =>
            {
                text = value;
                eligible().Should().BeTrue();
                await playback.Task.WaitAsync(token);
            }, NullLogger.Instance);
        reply.Changed += (_, _) =>
        {
            if (reply.Status.StartsWith("Speak exactly", StringComparison.Ordinal)) { opened.TrySetResult(); }
        };
        var operation = reply.StartAsync();
        text.Should().Be("Send to Build:   exact\r\noriginal  ?");
        f.Capture.Opens.Should().Be(0);
        f.Capture.Transcript("send");
        operation.IsCompleted.Should().BeFalse();
        playback.SetResult();
        await opened.Task;
        f.Capture.Closing = new(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Capture.Transcript(spoken);
        await f.Capture.StopStarted.Task;
        operation.IsCompleted.Should().BeFalse();
        f.Capture.Closing.SetResult();
        (await operation)!.Choice.Should().Be(expected);
        f.Capture.Opens.Should().Be(1);
        f.Capture.Stops.Should().Be(1);
        f.Capture.IsCaptureQuiescent.Should().BeTrue();
        await FluentActions.Awaiting(() => reply.StartAsync()).Should().ThrowAsync<InvalidOperationException>();
    }

    [Theory]
    [InlineData("mismatch")]
    [InlineData("timeout")]
    [InlineData("privacy")]
    [InlineData("target")]
    [InlineData("playback")]
    public async Task FailureOrChangedAdmissionNeverReturnsSend(string boundary)
    {
        await using var f = new QuestionReplyFixture();
        var exact = true;
        var request = new SessionRoutingVoiceRequest("original", HostRequest.Create(RequestOrigin.ActivatedVoice),
            static () => true, f.Capture, release: HandsFreeVoiceContinuation.CaptureRelease.Preserved);
        await using var reply = SessionRequestVoiceReply.Create(request, "Build", () => exact, () => f.Admission,
            (_, _, _) => boundary is "playback" ? Task.FromException(new IOException("Synthetic playback failure")) : Task.CompletedTask,
            NullLogger.Instance);
        var operation = reply.StartAsync();
        if (boundary is "mismatch") { f.Capture.Transcript("send please"); }
        if (boundary is "timeout") { f.Capture.Complete(); }
        if (boundary is "privacy") { f.Admission = null; reply.Revalidate(); }
        if (boundary is "target") { exact = false; reply.Revalidate(); }
        (await operation).Should().BeNull();
        f.Capture.IsCaptureQuiescent.Should().BeTrue();
        f.Capture.Transcript("send");
        request.IsCurrent.Should().BeFalse();
    }

    [Fact]
    public async Task LocalUiCannotCreateAnAutomaticSendReply()
    {
        await using var f = new QuestionReplyFixture();
        var request = new SessionRoutingVoiceRequest("original", HostRequest.Create(RequestOrigin.LocalUi),
            static () => true, f.Capture);
        FluentActions.Invoking(() => SessionRequestVoiceReply.Create(request, "Build", static () => true,
            () => f.Admission, (_, _, _) => Task.CompletedTask, NullLogger.Instance))
            .Should().Throw<InvalidOperationException>();
        f.Capture.Opens.Should().Be(0);
    }
}
