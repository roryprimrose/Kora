using AwesomeAssertions;

using Kora.Application.Hosting.SessionRouting;
using Kora.Application.UnitTests.Interaction;
using Kora.Application.Voice;
using Kora.Core.Conversation.SessionRouting;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;

using Microsoft.Extensions.Logging.Abstractions;

using static Kora.Application.Hosting.SessionRouting.SessionRoutingVoiceReply;

namespace Kora.Application.UnitTests.Hosting;

[Collection("Host tracing")]
public sealed class SessionRoutingVoiceReplyTests
{
    [Theory]
    [InlineData("1", SessionRoutingVoiceChoice.Destination)]
    [InlineData("2", SessionRoutingVoiceChoice.Destination)]
    [InlineData("New", SessionRoutingVoiceChoice.New)]
    [InlineData("Cancel", SessionRoutingVoiceChoice.Cancel)]
    public async Task ExactPresentedChoiceCompletesOnlyAfterPhysicalRelease(string spoken, SessionRoutingVoiceChoice expected)
    {
        var f = new Fixture();
        await using var reply = f.Destinations();
        var changes = 0;
        reply.Changed += (_, _) => changes++;
        reply.Revalidate();
        reply.Spec.Options.Select(option => option.Label).Should().Equal("1", "2", "New", "Cancel");
        f.Capture.Opens.Should().Be(0);
        var operation = reply.StartAsync();
        reply.CanAnswerByVoice.Should().BeFalse();
        f.Request.IsCurrent.Should().BeFalse();
        f.Request.IsLive.Should().BeTrue();
        f.Capture.Closing = new(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Capture.Transcript(spoken);
        await f.Capture.StopStarted.Task;
        operation.IsCompleted.Should().BeFalse("a transcript is not a capture-release receipt");
        f.Capture.Transcript("1");
        f.Capture.Closing.SetResult();
        var result = (await operation)!;
        result.Choice.Should().Be(expected);
        if (expected == SessionRoutingVoiceChoice.Destination)
        { result.Destination.Should().BeSameAs(f.Review.Snapshot.Descriptors[int.Parse(spoken, System.Globalization.CultureInfo.InvariantCulture) - 1]); }
        else { result.Destination.Should().BeNull(); }
        f.Request.IsCurrent.Should().BeTrue();
        f.Capture.Stops.Should().Be(1);
        f.Capture.IsCaptureQuiescent.Should().BeTrue();
        reply.Revalidate();
        changes.Should().BeGreaterThan(0);
        await FluentActions.Awaiting(() => reply.StartAsync()).Should().ThrowAsync<InvalidOperationException>();
    }

    [Theory]
    [InlineData("Submit original request", SessionRoutingVoiceChoice.Submit)]
    [InlineData("Correct destination", SessionRoutingVoiceChoice.Correct)]
    [InlineData("Cancel", SessionRoutingVoiceChoice.Cancel)]
    public async Task ConfirmationHasAnIndependentClosedCaptureAndPreservesOriginal(string spoken, SessionRoutingVoiceChoice expected)
    {
        var f = new Fixture();
        await using (var choice = f.Destinations())
        {
            var first = choice.StartAsync();
            f.Capture.Transcript("2");
            (await first)!.Destination.Should().BeSameAs(f.Review.Snapshot.Descriptors[1]);
        }
        var selected = new SessionRoutingSelection(f.Review, f.Review.Snapshot.Descriptors[1].Entry);
        await using var confirmation = ForConfirmation(f.Request, f.Review, selected, () => f.Exact, () => f.Admission, NullLogger.Instance);
        f.Capture.Opens.Should().Be(1, "showing acknowledgment must not arm another microphone");
        confirmation.Spec.Options.Select(option => option.Label).Should().Equal("Submit original request", "Correct destination", "Cancel");
        var second = confirmation.StartAsync();
        f.Capture.Transcript(spoken);
        (await second)!.Choice.Should().Be(expected);
        f.Request.Body.Should().Be("  ORIGINAL\r\nunchanged  ");
        f.Request.Request.Origin.Should().Be(RequestOrigin.LocalUi);
        f.Capture.Opens.Should().Be(2);
        f.Capture.Stops.Should().Be(2);
    }

    [Fact]
    public async Task NativeCancellationNotifiesThenClosedReplyIgnoresRepeatedCancellation()
    {
        var f = new Fixture();
        await using var reply = f.Destinations();
        var changes = 0;
        reply.Changed += (_, _) => changes++;
        var operation = reply.StartAsync();
        reply.Cancel();
        reply.Revalidate();
        (await operation).Should().BeNull();
        var closedChanges = changes;
        reply.Cancel();
        reply.Revalidate();
        changes.Should().Be(closedChanges);
        f.Capture.IsCaptureQuiescent.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("option 1")]
    [InlineData("one")]
    [InlineData("3")]
    [InlineData("Last")]
    [InlineData("Same name")]
    [InlineData("Nova, 1")]
    [InlineData("1 and 2")]
    [InlineData("shutdown")]
    [InlineData("Submit original request")]
    public async Task InvalidOrUnpresentedTranscriptTerminatesWithoutFallback(string transcript)
    {
        var f = new Fixture();
        await using var reply = f.Destinations();
        var operation = reply.StartAsync();
        f.Capture.Transcript(transcript);
        (await operation).Should().BeNull();
        f.Request.IsCurrent.Should().BeFalse();
        reply.Status.Should().Contain("did not match");
        f.Capture.Stops.Should().Be(1);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("New")]
    [InlineData("submit")]
    [InlineData("yes")]
    public async Task ConfirmationCannotReuseOrdinalOrInferSubmit(string transcript)
    {
        var f = new Fixture();
        await using var reply = ForConfirmation(f.Request, f.Review, new(f.Review, null),
            () => f.Exact, () => f.Admission, NullLogger.Instance);
        var operation = reply.StartAsync();
        f.Capture.Transcript(transcript);
        (await operation).Should().BeNull();
        f.Request.IsCurrent.Should().BeFalse();
    }

    [Fact]
    public async Task ForeignGenerationIsIgnoredAndNativeWithdrawalJoinsDelayedOpening()
    {
        var f = new Fixture();
        f.Capture.Opening = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var reply = f.Destinations();
        var operation = reply.StartAsync();
        f.Capture.Transcript("1", 99);
        reply.Cancel();
        await reply.DisposeAsync();
        (await operation).Should().BeNull();
        f.Capture.IsCaptureQuiescent.Should().BeTrue();
        f.Capture.Opens.Should().Be(0);
        f.Capture.Stops.Should().Be(1);
        f.Request.IsCurrent.Should().BeTrue("native withdrawal retires only the reply, not a still-current original request");
    }

    [Theory]
    [InlineData("source")]
    [InlineData("question")]
    [InlineData("privacy")]
    [InlineData("generation")]
    public async Task RevocationAfterOpeningDeniesExactAnswerAndCannotRevive(string change)
    {
        var f = new Fixture();
        await using var reply = f.Destinations();
        var operation = reply.StartAsync();
        if (change is "source") { f.Source = false; }
        if (change is "question") { f.Exact = false; }
        if (change is "privacy") { f.Admission = f.Admission! with { TopologyRevision = 2 }; }
        if (change is "generation") { f.Capture.AdvanceGeneration(); }
        reply.Revalidate();
        f.Source = true;
        f.Exact = true;
        f.Admission = Fixture.OriginalAdmission;
        f.Capture.Transcript("1");
        (await operation).Should().BeNull();
        f.Request.IsCurrent.Should().BeFalse();
    }

    [Theory]
    [InlineData("source")]
    [InlineData("question")]
    [InlineData("generation")]
    [InlineData("quiescence")]
    public async Task RevocationDuringPhysicalReleaseCannotCommitARecognizedChoice(string change)
    {
        var f = new Fixture();
        f.Capture.Closing = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var reply = f.Destinations();
        var operation = reply.StartAsync();
        f.Capture.Transcript("1");
        await f.Capture.StopStarted.Task;
        if (change is "source") { f.Source = false; }
        if (change is "question") { f.Exact = false; }
        if (change is "generation") { f.Capture.AdvanceGeneration(); }
        if (change is "quiescence") { f.Capture.NonQuiescent = true; }
        f.Capture.Closing.SetResult();
        if (change is "generation" or "quiescence")
        {
            await FluentActions.Awaiting(() => operation).Should().ThrowAsync<InvalidOperationException>();
            await FluentActions.Awaiting(() => reply.DisposeAsync().AsTask()).Should().ThrowAsync<InvalidOperationException>();
        }
        else { (await operation).Should().BeNull(); await reply.DisposeAsync(); }
        f.Request.IsCurrent.Should().Be(change is "question",
            "changing the exact question retires that reply, but native correction may keep the original request");
    }

    [Theory]
    [InlineData("source")]
    [InlineData("admission")]
    [InlineData("release-admission")]
    [InlineData("transcript")]
    [InlineData("opening-events")]
    [InlineData("before-handover-events")]
    [InlineData("ack-admission")]
    public async Task AdmissionRacesAndOpeningCallbacksCannotAcquireOrSubmitOriginal(string change)
    {
        var f = new Fixture();
        var observations = 0;
        QuestionVoiceAdmission? Observe()
        {
            if (change is "ack-admission" && f.Capture.Opens == 1) { f.Admission = null; }
            if (observations == 2 && change is "before-handover-events")
            {
                f.Capture.Fail();
                f.Capture.Complete();
                f.Capture.Transcript("1");
            }
            if (++observations == 2)
            {
                if (change is "source") { f.Source = false; }
                if (change is "admission") { f.Admission = null; }
            }
            return f.Admission;
        }
        var reply = ForDestinations(f.Request, f.Review, f.Review.Candidates, () => f.Exact, Observe, NullLogger.Instance);
        if (change is "opening-events")
        {
            f.Capture.PreserveGenerationOnInvalidate = true;
            f.Capture.BeforeOpen = () =>
            {
                f.Capture.InvalidateCapture();
                f.Capture.Fail();
                f.Capture.Complete();
                f.Capture.Transcript("1");
            };
        }
        using var cause = HostActivity.BeginRoot(f.Request.Request, HostActivityLayer.Application, HostOperation.Request);
        var answer = reply.StartAsync();
        if (change is "release-admission")
        {
            f.Capture.Closing = new(TaskCreationOptions.RunContinuationsAsynchronously);
            f.Capture.Transcript("1");
            await f.Capture.StopStarted.Task;
            f.Admission = null;
            f.Capture.Fail();
            f.Capture.Complete();
            reply.Revalidate();
            f.Capture.Closing.SetResult();
        }
        if (change is "transcript") { f.Exact = false; f.Capture.Transcript("1"); }
        if (change is "opening-events" or "before-handover-events") { answer.IsCompleted.Should().BeFalse(); f.Capture.Transcript("Cancel"); }
        if (change is "opening-events" or "before-handover-events") { (await answer)!.Choice.Should().Be(SessionRoutingVoiceChoice.Cancel); }
        else { (await answer).Should().BeNull(); }
        await reply.DisposeAsync();
        if (change is not ("opening-events" or "before-handover-events")) { f.Request.IsCurrent.Should().BeFalse(); }
    }

    [Theory]
    [InlineData("failure")]
    [InlineData("completed")]
    [InlineData("foreign-failure")]
    [InlineData("foreign-completed")]
    public async Task RecognitionTerminalsAreBoundToTheExactGeneration(string terminal)
    {
        var f = new Fixture();
        await using var reply = f.Destinations();
        var operation = reply.StartAsync();
        if (terminal.Contains("failure", StringComparison.Ordinal)) { f.Capture.Fail(terminal.StartsWith("foreign", StringComparison.Ordinal) ? 99 : null); }
        else { f.Capture.Complete(terminal.StartsWith("foreign", StringComparison.Ordinal) ? 99 : null); }
        if (terminal.StartsWith("foreign", StringComparison.Ordinal))
        {
            operation.IsCompleted.Should().BeFalse();
            f.Capture.Transcript("New");
            (await operation)!.Choice.Should().Be(SessionRoutingVoiceChoice.New);
        }
        else { (await operation).Should().BeNull(); }
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("capture")]
    [InlineData("busy")]
    [InlineData("source")]
    [InlineData("question")]
    [InlineData("unknown-release")]
    public async Task UnavailableRepliesLeaveNativeRecoveryWithoutGuessing(string unavailable)
    {
        var f = new Fixture();
        if (unavailable is "missing") { f.Admission = null; }
        if (unavailable is "capture") { f.Request = new(f.Review.Body, f.Request.Request, () => f.Source, null); }
        if (unavailable is "unknown-release") { f.Request = new(f.Review.Body, f.Request.Request, () => f.Source, f.Capture); }
        if (unavailable is "busy") { f.Capture.NonQuiescent = true; }
        if (unavailable is "source") { f.Source = false; }
        if (unavailable is "question") { f.Exact = false; }
        await using var reply = f.Destinations();
        reply.CanAnswerByVoice.Should().BeFalse();
        if (unavailable is "capture" or "unknown-release") { f.Request.BeginHandover().Should().BeFalse(); }
        (await reply.StartAsync()).Should().BeNull();
        reply.Status.Should().Contain("unavailable");
        f.Capture.Opens.Should().Be(0);
        f.Capture.Stops.Should().Be(0);
    }

    [Theory]
    [InlineData("open")]
    [InlineData("ack")]
    [InlineData("stop")]
    [InlineData("fatal-stop")]
    public async Task CaptureFailuresNeverProduceAChoice(string failure)
    {
        var f = new Fixture();
        if (failure is "open") { f.Capture.OpenFailure = new IOException("Synthetic"); }
        if (failure is "ack") { f.Capture.RejectAcknowledgement = true; }
        if (failure is "stop") { f.Capture.StopFailure = new IOException("Synthetic"); }
#pragma warning disable CA2201 // Inject the fatal boundary deterministically without inducing real memory pressure.
        if (failure is "fatal-stop") { f.Capture.StopFailure = new OutOfMemoryException("Synthetic fatal release failure; no allocation failure."); }
#pragma warning restore CA2201
        var reply = f.Destinations();
        var operation = reply.StartAsync();
        if (failure is "fatal-stop")
        {
            f.Capture.Transcript("1");
            await FluentActions.Awaiting(() => operation).Should().ThrowAsync<OutOfMemoryException>();
            await FluentActions.Awaiting(() => reply.DisposeAsync().AsTask()).Should().ThrowAsync<OutOfMemoryException>();
        }
        else if (failure is "stop")
        {
            f.Capture.Transcript("1");
            await FluentActions.Awaiting(() => operation).Should().ThrowAsync<IOException>();
            await FluentActions.Awaiting(() => reply.DisposeAsync().AsTask()).Should().ThrowAsync<IOException>();
        }
        else { (await operation).Should().BeNull(); await reply.DisposeAsync(); }
        f.Request.IsCurrent.Should().BeFalse();
    }

    [Fact]
    public async Task CaptureFailureUsesEnabledTypedDiagnosticWithoutSuccessfulChoice()
    {
        var f = new Fixture();
        var logger = new EnabledRoutingLogger();
        f.Capture.OpenFailure = new IOException("Synthetic");
        await using var reply = ForDestinations(f.Request, f.Review, f.Review.Candidates, () => f.Exact, () => f.Admission, logger);
        (await reply.StartAsync()).Should().BeNull();
        logger.Events.Should().ContainSingle().Which.Should().Be(1);
        f.Request.IsCurrent.Should().BeFalse();
        f.Capture.IsCaptureQuiescent.Should().BeTrue();
    }

    [Fact]
    public async Task FatalOpeningPropagatesOnlyAfterJoiningItsCaptureWithoutAChoice()
    {
        var f = new Fixture();
#pragma warning disable CA2201 // Inject the fatal boundary deterministically without inducing real memory pressure.
        var failure = new OutOfMemoryException("Synthetic fatal opening; no memory pressure is induced.");
#pragma warning restore CA2201
        f.Capture.OpenFailure = failure;
        var reply = f.Destinations();
        await FluentActions.Awaiting(() => reply.StartAsync()).Should().ThrowAsync<OutOfMemoryException>().Where(exception => ReferenceEquals(exception, failure));
        f.Capture.IsCaptureQuiescent.Should().BeTrue();
        f.Capture.Stops.Should().Be(1);
        f.Capture.Opens.Should().Be(0);
        f.Capture.Transcript("1");
        await FluentActions.Awaiting(() => reply.DisposeAsync().AsTask()).Should().ThrowAsync<OutOfMemoryException>().Where(exception => ReferenceEquals(exception, failure));
    }

    private sealed class EnabledRoutingLogger : Microsoft.Extensions.Logging.ILogger
    {
        internal List<int> Events { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;
        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId,
            TState state, Exception? exception, Func<TState, Exception?, string> formatter) => Events.Add(eventId.Id);
    }

    [Fact]
    public void ForeignReconstructedSnapshotAndOriginalAreRejected()
    {
        var f = new Fixture();
        var rows = f.Review.Snapshot.Descriptors;
        FluentActions.Invoking(() => ForDestinations(f.Request, f.Review, [rows[0]],
            static () => true, () => f.Admission, NullLogger.Instance)).Should().Throw<InvalidOperationException>();
        FluentActions.Invoking(() => ForDestinations(f.Request, f.Review, [rows[1], rows[0]],
            static () => true, () => f.Admission, NullLogger.Instance)).Should().Throw<InvalidOperationException>();
        FluentActions.Invoking(() => ForDestinations(f.Request, f.Review, [rows[0] with { }],
            static () => true, () => f.Admission, NullLogger.Instance)).Should().Throw<InvalidOperationException>();
        FluentActions.Invoking(() => ForDestinations(f.Request, f.Review, [rows[0], rows[0]],
            static () => true, () => f.Admission, NullLogger.Instance)).Should().Throw<InvalidOperationException>();
        var other = new SessionRoutingVoiceRequest("changed", f.Request.Request, static () => true, f.Capture);
        FluentActions.Invoking(() => ForDestinations(other, f.Review, rows, static () => true, () => f.Admission, NullLogger.Instance))
            .Should().Throw<InvalidOperationException>();
        var foreignReview = new SessionRoutingReview(f.Review.Body, f.Review.Origin, f.Review.Snapshot,
            SessionRouteMatch.Clear, rows, f.Review.ExpiresAt, f.Review.Continue);
        FluentActions.Invoking(() => ForConfirmation(f.Request, f.Review, new(foreignReview, null),
            static () => true, () => f.Admission, NullLogger.Instance)).Should().Throw<InvalidOperationException>();
    }

    private sealed class Fixture
    {
        internal static QuestionVoiceAdmission OriginalAdmission => new(new("mic", "Synthetic"), 1, 1, 1);
        internal QuestionReplyFixture.ReplyCapture Capture { get; } = new();
        internal bool Source { get; set; } = true;
        internal bool Exact { get; set; } = true;
        internal QuestionVoiceAdmission? Admission { get; set; } = OriginalAdmission;
        internal SessionRoutingVoiceRequest Request { get; set; }
        internal SessionRoutingReview Review { get; }
        internal Fixture()
        {
            var original = HostRequest.Create(RequestOrigin.LocalUi);
            const string body = "  ORIGINAL\r\nunchanged  ";
            Request = new(body, original, () => Source, Capture, release: HandsFreeVoiceContinuation.CaptureRelease.Preserved);
            var first = HostRequest.Create(RequestOrigin.LocalUi).SessionId;
            var second = HostRequest.Create(RequestOrigin.LocalUi).SessionId;
            static SessionRouteDescriptor Row(HostId<SessionIdentity> id) =>
                new(new(new(id, new(1), true), new(id, new(1), new("Same name"))), Guid.NewGuid());
            var snapshot = new SessionRouteSnapshot(1, [Row(first), Row(second)]);
            Review = new(body, original.Origin, snapshot, SessionRouteMatch.Ambiguous, snapshot.Descriptors,
                DateTimeOffset.MaxValue, () => HostActivity.BeginRoot(original, HostActivityLayer.Application, HostOperation.Request));
        }
        internal SessionRoutingVoiceReply Destinations() =>
            ForDestinations(Request, Review, Review.Snapshot.Descriptors, () => Exact, () => Admission, NullLogger.Instance);
    }
}
