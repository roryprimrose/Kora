using AwesomeAssertions;
using System.Diagnostics;
using Kora.Application.Voice;
using Kora.Core.Hosting;
using Kora.Core.Voice;
using Microsoft.Extensions.Logging;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Fact]
    public async Task ReviewedCaptureCompletesNormallyWithoutAnActivityListener()
    {
        hostListener.Dispose();
        await using var h = new ReviewedCaptureHarness();
        await h.Service.BeginAsync(() => h.Native);
        h.Fixture.Voice.RaiseTranscript("original without tracing", 1);
        await h.Service.FinishAsync();
        await h.Service.ConfirmAsync(h.Service.Revision);
        await h.Service.Quiescence;
        h.Text.Should().Be("original without tracing");
        h.Origin.Should().Be(RequestOrigin.ActivatedVoice);
        h.Calls.Should().Be(1);
    }

    [Fact]
    public async Task ReviewedCaptureAdmitsOnlyTheExactFinalTextAndFreshRevision()
    {
        await using var h = new ReviewedCaptureHarness();
        await h.Service.BeginAsync(() => h.Native);
        var lateTerminal = h.Fixture.Voice.CaptureReviewedTerminalCallback();
        var generation = h.Fixture.Voice.Generation;
        h.Fixture.Voice.RaiseTranscriptForGeneration(generation - 1);
        h.Fixture.Voice.RaiseTranscript("  Original \"lock the machine\"\r\n\tbody  ", 0.9f);
        h.Fixture.Voice.RaiseTranscript("duplicate", 1);
        h.Service.Candidate.Should().BeEmpty();
        h.Calls.Should().Be(0);
        await h.Service.FinishAsync();
        h.Fixture.Voice.PublishCompletion(new(generation, VoiceRecognitionCompletionReason.PushToTalkReleased));
        h.Service.Candidate.Should().Be("  Original \"lock the machine\"\r\n\tbody  ");
        var stale = h.Service.Revision;
        h.Service.Edit(stale, "corrected \"stop listening\"").Should().BeTrue();
        h.Service.Edit(stale, "stale overwrite").Should().BeFalse();
        h.Service.Candidate.Should().Be("corrected \"stop listening\"");
        await h.Service.ConfirmAsync(stale);
        h.Calls.Should().Be(0);
        await h.Service.ConfirmAsync(h.Service.Revision);
        h.Calls.Should().Be(1);
        h.Text.Should().Be("corrected \"stop listening\"");
        h.Origin.Should().Be(RequestOrigin.ActivatedVoice);
        h.Service.Candidate.Should().BeEmpty();
        h.Service.IsActive.Should().BeFalse();
        await h.Service.FinishAsync();
        await h.Service.ConfirmAsync(0);
        h.Calls.Should().Be(1);
        await h.Service.Quiescence;
        lateTerminal();
        await h.Service.BeginAsync(() => h.Native);
        lateTerminal();
        h.Fixture.Voice.RaiseTranscript("new original", 1);
        await h.Service.FinishAsync();
        h.Service.Edit(stale, "old turn").Should().BeFalse();
        await h.Service.ConfirmAsync(stale);
        h.Service.Candidate.Should().Be("new original");
        h.Calls.Should().Be(1);
    }

    [Theory]
    [InlineData("before-open")]
    [InlineData("before-end")]
    [InlineData("after-consume")]
    public async Task ReviewedCaptureRevocationAtPhaseEntryNeverPublishesOrDispatchesAndLogsNoContent(string phase)
    {
        await using var h = new ReviewedCaptureHarness();
        var started = 0;
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name is "Kora.Application",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStarted = _ =>
            {
                started++;
                if (phase is "before-open" && started == 1 || phase is "before-end" && started == 2)
                { h.Current = false; }
            },
        };
        ActivitySource.AddActivityListener(listener);
        var prior = Activity.Current;
        Activity.Current = null;
        try { await h.Service.BeginAsync(() => h.Native); }
        finally { Activity.Current = prior; }
        if (phase is not "before-open")
        {
            h.Fixture.Voice.RaiseTranscript("PRIVATE_RECOGNITION_CONTENT", 1);
            await h.Service.FinishAsync();
        }
        if (phase is "after-consume")
        {
            h.Clock.BeforeDispose = () => h.Current = false;
            await h.Service.ConfirmAsync(h.Service.Revision);
        }
        await h.Service.Quiescence;
        h.Service.IsActive.Should().BeFalse();
        h.Calls.Should().Be(0);
        h.Logger.Entries.Should().NotBeEmpty();
        h.Logger.Entries.Should().NotContain(text => text.Contains("PRIVATE_RECOGNITION_CONTENT", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("missing-target")]
    [InlineData("stale-target")]
    [InlineData("native")]
    [InlineData("closed")]
    [InlineData("capture-busy")]
    public async Task ReviewedCaptureUnavailableNeverOpensMicrophone(string denial)
    {
        await using var h = new ReviewedCaptureHarness();
        if (denial is "missing-target") { h.TargetAvailable = false; }
        if (denial is "stale-target") { h.Current = false; }
        if (denial is "native") { h.Native = false; }
        if (denial is "closed") { h.Service.CloseAdmission(); }
        if (denial is "capture-busy") { h.Fixture.Voice.PreventQuiescence = true; }
        await h.Service.BeginAsync(() => h.Native);
        h.Fixture.Voice.StartCalls.Should().Be(0);
        h.Service.Status.Should().Contain("unavailable");
        h.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData("open-fails")]
    [InlineData("acknowledgement")]
    [InlineData("revoked-open")]
    [InlineData("no-final")]
    [InlineData("end-fails")]
    [InlineData("generation")]
    [InlineData("publication")]
    [InlineData("unconfirmed")]
    public async Task ReviewedCaptureFailureNeverPublishesOrDispatchesAPartialCandidate(string failure)
    {
        var h = new ReviewedCaptureHarness();
        try
        {
            if (failure is "open-fails") { h.Fixture.Voice.StartException = new IOException("fixture"); }
            if (failure is "acknowledgement") { h.Fixture.Voice.RejectAcknowledgement = true; }
            if (failure is "revoked-open") { h.Fixture.Voice.AfterStart = () => h.Current = false; }
            await h.Service.BeginAsync(() => h.Native);
            if (h.Service.IsActive)
            {
                if (failure is not "no-final") { h.Fixture.Voice.RaiseTranscript("original", 1); }
                if (failure is "end-fails") { h.Fixture.Voice.EndException = new IOException("fixture"); }
                if (failure is "generation") { h.Fixture.Voice.BeforeEnd = h.Fixture.Voice.AdvanceGeneration; }
                if (failure is "publication") { h.Fixture.Dispatcher.BeforeInvoke = () => h.Current = false; }
                if (failure is "unconfirmed") { h.Fixture.Voice.PreventQuiescence = true; }
                await h.Service.FinishAsync();
            }
            h.Service.HasReview.Should().BeFalse();
            h.Service.Candidate.Should().BeEmpty();
            h.Calls.Should().Be(0);
            if (failure is "unconfirmed")
            {
                await h.Service.Invoking(s => s.Quiescence).Should().ThrowAsync<InvalidOperationException>();
                h.Service.Status.Should().Contain("unconfirmed");
                h.Fixture.Voice.PreventQuiescence = false;
                await h.Service.BeginAsync(static () => true);
                h.Service.IsActive.Should().BeFalse();
            }
            else { await h.Service.Quiescence; }
        }
        finally
        {
            h.Fixture.Voice.PreventQuiescence = false;
            h.Fixture.ViewModel.Dispose();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReviewedCaptureCancellationWaitsForAnUncooperativeOpenAndDiscardsAcknowledgedLateText(bool earlyRelease)
    {
        await using var h = new ReviewedCaptureHarness();
        h.Fixture.Voice.StartGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Fixture.Voice.IgnoreStartCancellation = true;
        var opening = h.Service.BeginAsync(() => h.Native);
        opening.IsCompleted.Should().BeFalse();
        if (earlyRelease) { await h.Service.FinishAsync(); }
        else { h.Service.Retire("test cancel"); }
        var release = h.Service.Quiescence;
        release.IsCompleted.Should().BeFalse();
        await h.Service.BeginAsync(() => h.Native);
        h.Fixture.Voice.StartCalls.Should().Be(1);
        h.Fixture.Voice.StartGate.SetResult();
        await opening;
        await release;
        h.Fixture.Voice.RaiseTranscript("late lock the machine", 1);
        h.Service.Candidate.Should().BeEmpty();
        h.Fixture.Voice.IsCaptureQuiescent.Should().BeTrue();
        h.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData("deadline")]
    [InlineData("native")]
    [InlineData("host")]
    [InlineData("capture")]
    [InlineData("failure")]
    [InlineData("completion")]
    [InlineData("secret-final")]
    public async Task ReviewedCaptureRevocationClearsVolatileTextAndJoinsWithoutDispatch(string loss)
    {
        await using var h = new ReviewedCaptureHarness();
        h.Fixture.Voice.EarlyTranscript = loss is "secret-final" ? "api_key=FixtureCredential123" : "original";
        await h.Service.BeginAsync(() => h.Native);
        if (loss is not "secret-final") { await h.Service.FinishAsync(); }
        var revision = h.Service.Revision;
        switch (loss)
        {
            case "deadline": h.Clock.Expire(); break;
            case "native": h.Native = false; h.Service.Revalidate(); break;
            case "host": h.Current = false; h.Service.Revalidate(); break;
            case "capture":
                h.Fixture.Voice.AdvanceGeneration();
                h.Fixture.Voice.PublishCaptureState(new(h.Fixture.Voice.Generation, false));
                break;
            case "failure": h.Fixture.Voice.RaiseFailure("private fixture text"); break;
            case "completion":
                h.Fixture.Voice.PublishCompletion(new(h.Fixture.Voice.Generation, VoiceRecognitionCompletionReason.EmptySpeechTimeout));
                break;
        }
        await h.Service.ConfirmAsync(revision);
        await h.Service.Quiescence;
        h.Service.Candidate.Should().BeEmpty();
        h.Service.HasReview.Should().BeFalse();
        h.Calls.Should().Be(0);
        h.Clock.DueTime.Should().Be(VoiceInputLimits.MaximumCommandDuration);
        h.Clock.Disposed.Should().BeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReviewedCaptureDispatchFailureOrChangedFinalFenceNeverReplays(bool revoke)
    {
        await using var h = new ReviewedCaptureHarness();
        await h.Service.BeginAsync(() => h.Native);
        h.Fixture.Voice.RaiseTranscript("original", 1);
        h.Fixture.Voice.PublishCompletion(new(h.Fixture.Voice.Generation + 1, VoiceRecognitionCompletionReason.Failed));
        h.Fixture.Voice.PublishCompletion(new(h.Fixture.Voice.Generation, VoiceRecognitionCompletionReason.Recognized));
        await h.Service.FinishAsync();
        if (revoke) { h.Native = false; }
        else { h.ThrowDispatch = true; }
        await h.Service.ConfirmAsync(h.Service.Revision);
        await h.Service.Quiescence;
        h.Service.Candidate.Should().BeEmpty();
        h.Calls.Should().Be(0);
    }

    private sealed class ReviewedCaptureHarness : IAsyncDisposable
    {
        internal Fixture Fixture { get; } = new();
        internal ReviewedClock Clock { get; } = new();
        internal ReviewedCaptureLogger Logger { get; } = new();
        internal bool Current { get; set; } = true;
        internal bool Native { get; set; } = true;
        internal bool TargetAvailable { get; set; } = true;
        internal bool ThrowDispatch { get; set; }
        internal int Calls { get; private set; }
        internal string? Text { get; private set; }
        internal RequestOrigin? Origin { get; private set; }
        internal ReviewedVoiceCommandCapture Service { get; }
        internal ReviewedCaptureHarness()
        {
            var target = new ReviewedVoiceCommandTarget(new("synthetic", "Fixture"), "Kora", () => Current,
                (text, request, exact) =>
                {
                    if (ThrowDispatch || !exact()) { throw new InvalidOperationException("fixture"); }
                    Calls++;
                    Text = text.Transcript;
                    Origin = request.Origin;
                    return Task.CompletedTask;
                });
            Service = new(Fixture.Voice, Fixture.Dispatcher, () => TargetAvailable ? target : null,
                Logger, Clock);
        }
        public async ValueTask DisposeAsync()
        {
            await Service.DisposeAsync();
            Fixture.ViewModel.Dispose();
        }
    }

    private sealed class ReviewedClock : TimeProvider
    {
        private TimerCallback? callback;
        private object? state;
        internal TimeSpan DueTime { get; private set; }
        internal bool Disposed { get; private set; }
        internal Action? BeforeDispose { get; set; }
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            this.callback = callback;
            this.state = state;
            DueTime = dueTime;
            return new Timer(this);
        }
        internal void Expire() => callback!(state);
        private sealed class Timer(ReviewedClock owner) : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => throw new NotSupportedException();
            public void Dispose()
            {
                var before = owner.BeforeDispose;
                owner.BeforeDispose = null;
                before?.Invoke();
                owner.Disposed = true;
            }
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }

    private sealed class ReviewedCaptureLogger : ILogger<ReviewedVoiceCommandCapture>
    {
        internal List<string> Entries { get; } = [];
        public bool IsEnabled(LogLevel logLevel) => true;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Entries.Add(formatter(state, exception));
    }

    private sealed partial class FakeVoiceRecognitionService
    {
        internal Action CaptureReviewedTerminalCallback()
        {
            var failure = RecognitionFailed;
            var completed = RecognitionCompleted;
            var generation = Generation;
            return () =>
            {
                failure?.Invoke(this, new("PRIVATE_LATE_FAILURE", generation, VoiceRecognitionFailureReason.CaptureFailure));
                completed?.Invoke(this, new(generation, VoiceRecognitionCompletionReason.Failed));
            };
        }
    }
}
