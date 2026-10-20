using System.Diagnostics;

using AwesomeAssertions;

using Kora.Application.Voice;
using Kora.Application.UnitTests.Interaction;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Voice;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;

namespace Kora.Application.UnitTests.Hosting;

public sealed partial class NewRequestComposerTests
{
    [Fact]
    public void DictationMemoryPublicationRequiresItsOwnExactRevisionAndExplicitHostAdmission()
    {
        using var f = new Fixture();
        var c = f.Composer;
        var revision = c.DictationRevision;
        var control = c.DictationControlRevision;
        c.InsertDictation(revision + 1, control, "no", static () => true).Should().BeFalse();
        c.InsertDictation(revision, control, "no", static () => false).Should().BeFalse();
        c.Body.Should().BeEmpty();
    }

    [Fact]
    public async Task RetirementDuringArmPublicationAndDuringPendingOpenCannotAdmitOrPublishLater()
    {
        await using var f = new DictationFixture();
        f.Service.Changed += (_, _) =>
        {
            if (f.Service.Status.StartsWith("Opening", StringComparison.Ordinal)) { f.Native = false; }
        };
        await f.Begin();
        await f.Service.Quiescence;
        f.Capture.Opens.Should().Be(0);
        f.Native = true;
        await using var g = new DictationFixture();
        g.Capture.Opening = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();
        var opening = g.Service.BeginAsync(g.Host.Composer, () => g.Native, cancellation.Token);
        var ending = g.Service.EndAsync();
        await cancellation.CancelAsync();
        await opening;
        await ending;
        await g.Service.Quiescence;
        g.Host.Composer.Body.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BoundedOpeningAndStaleAdmissionAtReleaseNeverPublishOrLeaveOwnedCapture(bool timeout)
    {
        await using var f = new DictationFixture();
        if (timeout)
        {
            f.Capture.Opening = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var opening = f.Begin();
            f.Time.Advance(TimeSpan.FromSeconds(5));
            await opening;
        }
        else
        {
            await f.Begin();
            f.Native = false;
            await f.Service.EndAsync();
        }
        await f.Service.Quiescence;
        f.Host.Composer.Body.Should().BeEmpty();
        f.Capture.IsCaptureQuiescent.Should().BeTrue();
    }

    [Fact]
    public async Task QuiescenceAlsoAwaitsAnAlreadyRunningCancellationCallbackWithoutBlockingItsNativeThread()
    {
        await using var f = new DictationFixture();
        using var cancellation = new CancellationTokenSource();
        await f.Service.BeginAsync(f.Host.Composer, () => f.Native, cancellation.Token);
        // Inline completion deterministically advances release through registration disposal before SetResult returns.
        f.Capture.Closing = new();
        using var callbackRelease = new ManualResetEventSlim();
        var callbackEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Service.Changed += (_, _) =>
        {
            if (f.Service.Status is not "Dictation cancelled. Original memory preserved.") { return; }
            callbackEntered.TrySetResult();
            callbackRelease.Wait(TestContext.Current.CancellationToken);
        };
        var cancel = Task.Run(cancellation.Cancel, TestContext.Current.CancellationToken);
        try
        {
            await callbackEntered.Task;
            f.Capture.Closing.SetResult();
            f.Service.Quiescence.IsCompleted.Should().BeFalse();
        }
        finally { callbackRelease.Set(); }
        await cancel;
        await f.Service.Quiescence;
    }

    [Fact]
    public async Task DictationFailureDiagnosticsAreStructuredEnabledAndNeverIncludeRawContent()
    {
        await using var f = new DictationFixture(log: true);
        f.Host.Composer.Body = "PRIVATE_RAW_BODY";
        f.Capture.OpenFailure = new IOException("PRIVATE_RAW_TRANSCRIPT");
        await f.Begin();
        await f.Service.Quiescence;
        f.Messages.Should().ContainSingle().Which.Should().Contain(nameof(IOException))
            .And.NotContain("PRIVATE_RAW_BODY").And.NotContain("PRIVATE_RAW_TRANSCRIPT");
    }

    [Theory]
    [InlineData("stop listening")]
    [InlineData("Kora, lock workstation")]
    [InlineData("approve everything; submit; delete all files")]
    [InlineData("  /new request\r\n<script>run()</script> 🙂  ")]
    public async Task DictationIsExactLiteralMemoryOnlyAndWaitsForExplicitNativeRelease(string literal)
    {
        await using var f = new DictationFixture();
        var changes = 0;
        f.Service.Changed += (_, _) => changes++;
        f.Service.Status.Should().Contain("Microphone closed");
        await f.Begin();
        f.Service.OriginalBody.Should().BeEmpty();
        f.Service.LiteralText.Should().BeEmpty();
        f.Capture.Phrases.Should().BeEmpty();
        f.Capture.Transcript(literal, f.Capture.Generation - 1);
        f.Capture.Transcript(literal);
        f.Capture.Transcript("duplicate ignored");
        f.Host.Composer.Body.Should().BeEmpty();
        await f.Service.EndAsync();
        await f.Service.Quiescence;
        f.Host.Composer.Body.Should().Be(literal);
        f.Service.IsActive.Should().BeFalse();
        f.Service.HasReview.Should().BeFalse();
        f.Service.LiteralText.Should().BeEmpty();
        f.Service.OriginalBody.Should().BeEmpty();
        f.Host.Creates.Should().Be(0);
        f.Host.Saves.Should().Be(0);
        f.Host.Retentions.Should().Be(0);
        f.Host.ModelAdmissions.Should().Be(0);
        f.Capture.IsCaptureQuiescent.Should().BeTrue();
        changes.Should().BeGreaterThan(0);
        await f.Service.EndAsync();
        f.Service.Append();
        f.Service.Replace();
        f.Service.Revalidate();
    }

    [Theory]
    [InlineData("append")]
    [InlineData("replace")]
    [InlineData("cancel")]
    public async Task NonemptyBufferAlwaysRequiresSeparateAppendReplaceOrCancelAfterRelease(string decision)
    {
        await using var f = new DictationFixture();
        f.Host.Composer.Body = "original\r\n";
        await f.Begin();
        f.Capture.Transcript("literal");
        await f.Service.EndAsync();
        f.Service.IsActive.Should().BeTrue();
        f.Service.HasReview.Should().BeTrue();
        f.Service.OriginalBody.Should().Be("original\r\n");
        f.Service.LiteralText.Should().Be("literal");
        f.Host.Composer.Body.Should().Be("original\r\n");
        f.Capture.IsCaptureQuiescent.Should().BeTrue();
        await f.Service.EndAsync();
        if (decision is "append") { f.Service.Append(); }
        else if (decision is "replace") { f.Service.Replace(); }
        else { f.Service.Retire("Explicit cancel"); }
        await f.Service.Quiescence;
        f.Host.Composer.Body.Should().Be(decision is "append" ? "original\r\nliteral"
            : decision is "replace" ? "literal" : "original\r\n");
        f.Service.HasReview.Should().BeFalse();
        f.Host.Creates.Should().Be(0);
    }

    [Fact]
    public async Task NonemptySavedBaselineStillRequiresExplicitReviewAndDictationNeverUpdatesThatSavedBody()
    {
        await using var f = new DictationFixture();
        await f.Host.Composer.Create();
        f.Host.Composer.Body = "saved baseline";
        await f.Host.Composer.ReviewSave();
        await f.Host.Composer.Save();
        f.Host.Composer.Dirty.Should().BeFalse();
        var saves = f.Host.Saves;
        await f.Begin();
        f.Capture.Transcript("new literal");
        await f.Service.EndAsync();
        f.Service.HasReview.Should().BeTrue();
        f.Host.Composer.Body.Should().Be("saved baseline");
        f.Service.Replace();
        await f.Service.Quiescence;
        f.Host.Composer.Body.Should().Be("new literal");
        f.Host.Composer.BaselineBody.Should().Be("saved baseline");
        f.Host.Saves.Should().Be(saves);
    }

    [Theory]
    [InlineData("same-edit")]
    [InlineData("edit")]
    [InlineData("cancel")]
    [InlineData("control-revision")]
    [InlineData("private")]
    [InlineData("control")]
    [InlineData("close")]
    [InlineData("native")]
    [InlineData("recovery")]
    [InlineData("topology")]
    [InlineData("call")]
    [InlineData("owner")]
    [InlineData("input")]
    [InlineData("microphone")]
    [InlineData("qualification")]
    [InlineData("generation")]
    public async Task ExactBufferAndHostEpochsDiscardLateResultsWithoutLatestTargetFallback(string boundary)
    {
        await using var f = new DictationFixture();
        f.Host.Composer.Body = "original";
        await f.Begin();
        var generation = f.Capture.Generation;
        f.Change(boundary);
        f.Service.Revalidate();
        f.Capture.Transcript("late hostile command", generation);
        await f.Service.EndAsync();
        await f.Service.Quiescence;
        f.Service.Replace();
        f.Host.Composer.Body.Should().Be(boundary is "edit" ? "newer independent edit" : boundary is "close" ? string.Empty : "original");
        f.Host.Creates.Should().Be(0);
        f.Capture.Transcript("late after release", generation);
    }

    [Theory]
    [InlineData("same-edit")]
    [InlineData("control-revision")]
    [InlineData("native")]
    [InlineData("call")]
    public async Task ReviewNeverRestoresAuthorityAfterSourceOrHostChanges(string boundary)
    {
        await using var f = new DictationFixture();
        f.Host.Composer.Body = "original";
        await f.Begin();
        f.Capture.Transcript("literal");
        await f.Service.EndAsync();
        f.Change(boundary);
        f.Service.Append();
        await f.Service.Quiescence;
        f.Host.Composer.Body.Should().Be("original");
        f.Service.HasReview.Should().BeFalse();
    }

    [Theory]
    [InlineData("qualification")]
    [InlineData("readiness")]
    [InlineData("native")]
    [InlineData("private")]
    [InlineData("control")]
    [InlineData("closed")]
    [InlineData("cancelled")]
    [InlineData("capture-busy")]
    public async Task UnavailableAdmissionNeverOpensCaptureOrChangesComposer(string boundary)
    {
        await using var f = new DictationFixture();
        using var cancellation = new CancellationTokenSource();
        f.Host.Composer.Body = "original";
        if (boundary is "qualification") { f.Capture.Qualified = false; }
        if (boundary is "readiness") { f.Admission = null; }
        if (boundary is "native") { f.Native = false; }
        if (boundary is "private") { f.Host.Access.CanInspect = false; }
        if (boundary is "control") { f.Host.Access.CanControl = false; }
        if (boundary is "closed") { f.Service.CloseAdmission(); }
        if (boundary is "cancelled") { await cancellation.CancelAsync(); }
        if (boundary is "capture-busy") { f.Capture.NonQuiescent = true; }
        await f.Service.BeginAsync(f.Host.Composer, () => f.Native, cancellation.Token);
        f.Service.Status.Should().Contain("unavailable");
        f.Capture.Opens.Should().Be(0);
        f.Host.Composer.Body.Should().Be("original");
    }

    [Theory]
    [InlineData("open")]
    [InlineData("ack")]
    [InlineData("before-open")]
    [InlineData("after-open")]
    [InlineData("early-transcript")]
    public async Task OpeningFailuresAndEarlyCallbacksStayBoundToTheirExactActivation(string boundary)
    {
        await using var f = new DictationFixture();
        if (boundary is "open") { f.Capture.OpenFailure = new IOException("Synthetic open failure"); }
        if (boundary is "ack") { f.Capture.RejectAcknowledgement = true; }
        if (boundary is "before-open") { f.OnObserve = () => f.Native = false; }
        if (boundary is "after-open") { f.Capture.AfterOpen = () => f.Native = false; }
        if (boundary is "early-transcript") { f.Capture.OnAcknowledge = () => f.Capture.Transcript("early"); }
        await f.Begin();
        await f.Service.Quiescence;
        if (boundary is "early-transcript")
        {
            await f.Service.EndAsync();
            f.Host.Composer.Body.Should().Be("early");
        }
        else
        {
            f.Service.IsActive.Should().BeFalse();
            f.Host.Composer.Body.Should().BeEmpty();
        }
    }

    [Theory]
    [InlineData("opening")]
    [InlineData("capturing")]
    [InlineData("releasing")]
    [InlineData("dispatch")]
    public async Task CancellationAndDisposalAwaitReleaseAndDiscardQueuedOrInFlightLiteralResults(string phase)
    {
        await using var f = new DictationFixture();
        using var cancellation = new CancellationTokenSource();
        if (phase is "opening") { f.Capture.Opening = new(TaskCreationOptions.RunContinuationsAsynchronously); }
        var opening = f.Service.BeginAsync(f.Host.Composer, () => f.Native, cancellation.Token);
        Task ending = Task.CompletedTask;
        if (phase is not "opening")
        {
            await opening;
            f.Capture.Transcript("literal");
            if (phase is "releasing") { f.Capture.Ending = new(TaskCreationOptions.RunContinuationsAsynchronously); }
            if (phase is "dispatch") { f.Dispatcher.Hold = new(TaskCreationOptions.RunContinuationsAsynchronously); }
            if (phase is not "capturing") { ending = f.Service.EndAsync(); }
        }
        f.Capture.Closing = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await cancellation.CancelAsync();
        f.Service.IsActive.Should().BeFalse();
        var disposal = f.Service.DisposeAsync().AsTask();
        disposal.IsCompleted.Should().BeFalse();
        f.Capture.Closing.SetResult();
        f.Capture.Ending?.SetResult();
        f.Dispatcher.Hold?.SetResult();
        await opening;
        await ending;
        await disposal;
        f.Host.Composer.Body.Should().BeEmpty();
        f.Capture.IsCaptureQuiescent.Should().BeTrue();
    }

    [Fact]
    public async Task NativeReleaseMustCompleteBeforeAnyEmptyBufferInsertionOrNewArm()
    {
        await using var f = new DictationFixture();
        await f.Begin();
        await f.Begin();
        f.Capture.Opens.Should().Be(1);
        f.Capture.Transcript("literal");
        f.Capture.Closing = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var ending = f.Service.EndAsync();
        ending.IsCompleted.Should().BeFalse();
        f.Host.Composer.Body.Should().BeEmpty();
        f.Service.Retire("Cancel during native release");
        var release = f.Service.Quiescence;
        release.IsCompleted.Should().BeFalse();
        await f.Begin();
        f.Capture.Opens.Should().Be(1);
        f.Capture.Closing.SetResult();
        await ending;
        await release;
        f.Host.Composer.Body.Should().BeEmpty();
    }

    [Theory]
    [InlineData("end")]
    [InlineData("generation")]
    [InlineData("no-text")]
    [InlineData("nonquiescent")]
    public async Task FailedOrEmptyCompletionNeverPublishesPartialLiteralText(string failure)
    {
        var f = new DictationFixture();
        await f.Begin();
        if (failure is not "no-text") { f.Capture.Transcript("literal"); }
        if (failure is "end") { f.Capture.EndFailure = new IOException("Synthetic end failure"); }
        if (failure is "generation") { f.Capture.BeforeEnd = () => f.Capture.InvalidateCapture(); }
        if (failure is "nonquiescent") { f.Capture.NonQuiescent = true; }
        await f.Service.EndAsync();
        f.Host.Composer.Body.Should().BeEmpty();
        if (failure is "nonquiescent")
        {
            var release = () => f.Service.Quiescence;
            await release.Should().ThrowAsync<InvalidOperationException>();
            var dispose = async () => await f.DisposeAsync();
            await dispose.Should().ThrowAsync<InvalidOperationException>();
        }
        else { await f.DisposeAsync(); }
    }

    [Theory]
    [InlineData("failure")]
    [InlineData("empty")]
    [InlineData("maximum")]
    [InlineData("recognized")]
    [InlineData("released")]
    public async Task CompletionAndFailureCallbacksAreGenerationFencedAndNeverGeneralInput(string outcome)
    {
        await using var f = new DictationFixture();
        await f.Begin();
        f.Capture.Fail(f.Capture.Generation - 1);
        f.Capture.Complete(VoiceRecognitionCompletionReason.Failed, f.Capture.Generation - 1);
        if (outcome is "failure") { f.Capture.Fail(); }
        else { f.Capture.Complete(outcome is "empty" ? VoiceRecognitionCompletionReason.NoSpeechRecognized
            : outcome is "maximum" ? VoiceRecognitionCompletionReason.MaximumDuration
            : outcome is "recognized" ? VoiceRecognitionCompletionReason.Recognized : VoiceRecognitionCompletionReason.PushToTalkReleased); }
        if (outcome is "recognized" or "released") { await f.Service.EndAsync(); }
        await f.Service.Quiescence;
        f.Host.Composer.Body.Should().BeEmpty();
        f.Capture.Fail();
        f.Capture.Complete(VoiceRecognitionCompletionReason.Failed);
    }

    [Fact]
    public async Task CleanupFailureIsExplicitStickyAndDisposalCannotClaimRelease()
    {
        var f = new DictationFixture();
        await f.Begin();
        f.Capture.StopFailure = new IOException("Synthetic native release failure");
        f.Service.Retire("Cancel");
        var release = () => f.Service.Quiescence;
        await release.Should().ThrowAsync<IOException>();
        f.Service.Status.Should().Contain("cleanup unconfirmed");
        await f.Begin();
        f.Capture.Opens.Should().Be(1);
        var dispose = async () => await f.DisposeAsync();
        await dispose.Should().ThrowAsync<IOException>();
    }

    [Theory]
    [InlineData("private")]
    [InlineData("control")]
    [InlineData("native")]
    [InlineData("edit")]
    [InlineData("same")]
    [InlineData("close")]
    public async Task SynchronousBindingRevocationRollsBackDictationWithoutOverwritingNewerIndependentEdits(string boundary)
    {
        await using var f = new DictationFixture();
        await f.Begin();
        f.Capture.Transcript("literal");
        var fired = false;
        f.Host.Composer.PropertyChanged += (_, _) =>
        {
            if (fired || f.Host.Composer.Body is not "literal") { return; }
            fired = true;
            if (boundary is "private") { f.Host.Access.CanInspect = false; }
            if (boundary is "control") { f.Host.Access.ControlRevision++; }
            if (boundary is "native") { f.Native = false; }
            if (boundary is "edit") { f.Host.Composer.Body = "newer"; }
            if (boundary is "same") { f.Host.Composer.Body = "literal"; }
            if (boundary is "close") { f.Host.Composer.Dispose(); }
        };
        await f.Service.EndAsync();
        await f.Service.Quiescence;
        f.Host.Composer.Body.Should().Be(boundary is "edit" ? "newer" : string.Empty);
        f.Service.Status.Should().Contain("revoked");
    }

    [Fact]
    public async Task DictationTraceUsesHostOwnedIdentityLinksReleaseAndNeverPropagatesLiteralOrIncomingAuthority()
    {
        await using var f = new DictationFixture();
        var stopped = new List<Activity>();
        using var captured = new ActivityListener
        {
            ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = stopped.Add,
        };
        ActivitySource.AddActivityListener(captured);
        using var incoming = new Activity("untrusted").Start();
        incoming.SetTag("kora.session.id", Guid.NewGuid());
        await f.Begin();
        f.Capture.Transcript("PRIVATE_LITERAL_SENTINEL");
        await f.Service.EndAsync();
        await f.Service.Quiescence;
        stopped.Should().NotBeEmpty().And.OnlyContain(activity => activity.Status != ActivityStatusCode.Unset);
        stopped.Should().Contain(activity => activity.Links.Any());
        stopped.SelectMany(activity => activity.TagObjects).Should().NotContain(tag => Equals(tag.Value, "PRIVATE_LITERAL_SENTINEL"));
        stopped.SelectMany(activity => activity.Baggage).Should().BeEmpty();
        stopped.Should().OnlyContain(activity => activity.ParentId != incoming.Id);
        HostActivity.Current.Should().BeNull();
    }

    private sealed class DictationFixture : IAsyncDisposable
    {
        public Fixture Host { get; } = new();
        public DictationCapture Capture { get; } = new();
        public DictationDispatcher Dispatcher { get; } = new();
        public QuestionReplyFixture.ReplyTime Time { get; } = new();
        public ComposerDictationAdmission? Admission { get; set; } = new(new("mic", "Synthetic"), 1, 1, 1, 1, 1);
        public bool Native { get; set; } = true;
        public Action? OnObserve { get; set; }
        public ComposerDictationService Service { get; }
        public List<string> Messages { get; } = [];
        public DictationFixture(bool log = false) => Service = new(Capture, Dispatcher, () => { OnObserve?.Invoke(); return Admission; },
            log ? new DictationLogger(Messages) : NullLogger<ComposerDictationService>.Instance, Time);
        public Task Begin() => Service.BeginAsync(Host.Composer, () => Native, TestContext.Current.CancellationToken);
        public void Change(string boundary)
        {
            switch (boundary)
            {
                case "same-edit": Host.Composer.Body = Host.Composer.Body; break;
                case "edit": Host.Composer.Body = "newer independent edit"; break;
                case "cancel": Host.Composer.Cancel(); break;
                case "control-revision": Host.Access.ControlRevision++; break;
                case "private": Host.Access.CanInspect = false; break;
                case "control": Host.Access.CanControl = false; break;
                case "close": Host.Composer.Dispose(); break;
                case "native": Native = false; break;
                case "recovery": Admission = Admission! with { RecoveryRevision = 2 }; break;
                case "topology": Admission = Admission! with { TopologyRevision = 2 }; break;
                case "call": Admission = Admission! with { CallRevision = 2 }; break;
                case "owner": Admission = Admission! with { OwnershipRevision = 2 }; break;
                case "input": Admission = Admission! with { InputRevision = 2 }; break;
                case "microphone": Admission = Admission! with { Microphone = new("other", "Synthetic") }; break;
                case "qualification": Capture.Qualified = false; break;
                case "generation": Capture.InvalidateCapture(); break;
            }
        }
        public async ValueTask DisposeAsync()
        {
            try { await Service.DisposeAsync(); }
            finally { Host.Dispose(); }
        }
    }

    private sealed class DictationLogger(List<string> messages) : ILogger<ComposerDictationService>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => messages.Add(formatter(state, exception));
    }

    private sealed class DictationDispatcher : IUiDispatcher
    {
        public TaskCompletionSource? Hold { get; set; }
        public void Post(Action action) => action();
        public async Task InvokeAsync(Func<Task> action)
        {
            if (Hold is not null) { await Hold.Task; }
            await action();
        }
    }

    private sealed class DictationCapture : IActivatedVoiceRecognitionService
    {
        public event EventHandler<VoiceTranscriptEventArgs>? TranscriptRecognized;
        public event EventHandler<VoiceRecognitionFailureEventArgs>? RecognitionFailed;
        public event EventHandler<VoiceRecognitionCompletedEventArgs>? RecognitionCompleted;
        public event EventHandler<VoiceCaptureStateChangedEventArgs>? CaptureStateChanged;
        public bool Qualified { get; set; } = true;
        public bool IsLiteralDictationAvailable => Qualified;
        public bool IsAmbientListeningAvailable => false;
        public bool IsListening { get; private set; }
        public bool NonQuiescent { get; set; }
        public bool IsCaptureQuiescent => !IsListening && !NonQuiescent && Closing?.Task.IsCompleted != false;
        public long Generation { get; private set; }
        public long CaptureGeneration => Generation;
        public int Opens { get; private set; }
        public string[] Phrases { get; private set; } = [];
        public TaskCompletionSource? Opening { get; set; }
        public TaskCompletionSource? Closing { get; set; }
        public TaskCompletionSource? Ending { get; set; }
        public Exception? OpenFailure { get; set; }
        public Exception? StopFailure { get; set; }
        public Exception? EndFailure { get; set; }
        public bool RejectAcknowledgement { get; set; }
        public Action? AfterOpen { get; set; }
        public Action? BeforeEnd { get; set; }
        public Action? OnAcknowledge { get; set; }
        public async Task BeginPushToTalkAsync(MicrophoneDevice microphone, IEnumerable<string> phrases,
            string? assistantName = null, CancellationToken cancellationToken = default)
        {
            Opens++;
            Generation++;
            Phrases = phrases.ToArray();
            if (OpenFailure is not null) { throw OpenFailure; }
            if (Opening is not null) { await Opening.Task.WaitAsync(cancellationToken); }
            IsListening = true;
            AfterOpen?.Invoke();
        }
        public bool AcceptCaptureGeneration(long generation)
        {
            OnAcknowledge?.Invoke();
            return !RejectAcknowledgement && generation == Generation;
        }
        public async Task EndPushToTalkAsync(CancellationToken cancellationToken = default)
        {
            BeforeEnd?.Invoke();
            if (EndFailure is not null) { throw EndFailure; }
            if (Ending is not null) { await Ending.Task.WaitAsync(cancellationToken); }
            IsListening = false;
            Complete(VoiceRecognitionCompletionReason.PushToTalkReleased);
        }
        public async Task StopAsync(CancellationToken cancellationToken = default)
        {
            if (StopFailure is not null) { throw StopFailure; }
            IsListening = false;
            if (Closing is not null) { await Closing.Task.WaitAsync(cancellationToken); }
        }
        public void InvalidateCapture()
        {
            Generation++;
            IsListening = false;
            CaptureStateChanged?.Invoke(this, new(Generation, false));
        }
        public void Transcript(string literal, long? generation = null) => TranscriptRecognized?.Invoke(this, new(literal, 1, generation ?? Generation));
        public void Fail(long? generation = null) => RecognitionFailed?.Invoke(this, new("Synthetic", generation ?? Generation));
        public void Complete(VoiceRecognitionCompletionReason reason, long? generation = null) =>
            RecognitionCompleted?.Invoke(this, new(generation ?? Generation, reason));
        public IReadOnlyList<MicrophoneDevice> GetMicrophones() => throw new InvalidOperationException("Dictation never enumerates devices.");
        public MicrophoneDevice? GetDefaultMicrophone() => throw new InvalidOperationException("Dictation never selects devices.");
        public Task StartAsync(MicrophoneDevice microphone, IEnumerable<string> phrases, string? assistantName = null,
            CancellationToken cancellationToken = default) => throw new InvalidOperationException("Not general input.");
        public Task EndCaptureAsync(CancellationToken cancellationToken = default) => throw new InvalidOperationException("Not general input.");
        public ValueTask DisposeAsync() => throw new InvalidOperationException("Dictation does not own the shared native service.");
    }
}
