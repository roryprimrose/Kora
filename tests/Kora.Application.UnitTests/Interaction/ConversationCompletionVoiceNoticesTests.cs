using AwesomeAssertions;

using Kora.Application.Communication;
using Kora.Application.Interaction;
using Kora.Core.Auditing;
using Kora.Core.Communication;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Core.Voice;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.Interaction;

[Collection("Host tracing")]
public sealed class ConversationCompletionVoiceNoticesTests
{
    [Theory]
    [InlineData(CallState.Active, false)]
    [InlineData(CallState.Active, true)]
    [InlineData(CallState.Suspected, false)]
    [InlineData(CallState.Suspected, true)]
    [InlineData(CallState.Unknown, false)]
    [InlineData(CallState.Unknown, true)]
    public async Task ConsentedCompletionCannotConsumePendingOrConfirmedRequestedCallSpeech(CallState state, bool confirmed)
    {
        await using var f = new Fixture();
        await f.Enable();
        f.Automatic.Set(state);
        var observation = f.Calls.Current;
        CallSpeechReview review;
        using (var input = f.Native())
        {
            review = new(f.Calls, f.Source.Time, new(Guid.NewGuid(), 1, observation.Revision),
                "Exact requested response.", f.Output, () => true);
        }
        using var ownedReview = review;
        using var confirmation = f.Native();
        using var lease = confirmed ? review.Confirm() : null;
        await f.Deliver(await f.Observe());
        f.Notices.Enabled.Should().BeTrue();
        f.Speech.Requests.Should().Be(0);
        f.Source.Audits.Should().NotContain(item => item.Event.ActionId == "completion-notice.voice");
        review.Available.Should().Be(!confirmed);
        using var requested = lease ?? review.Confirm();
        requested.IsCurrent.Should().BeTrue();
        var starts = 0;
        Task Start() { starts++; return Task.CompletedTask; }
        await f.Calls.StartReviewedSpeech(requested, requested.Text, Start, () => true);
        await f.Calls.StartReviewedSpeech(requested, requested.Text, Start, () => true);
        starts.Should().Be(1);
        f.Calls.Current.Should().Be(observation);
        f.Calls.Current.SuppressSpeech.Should().BeTrue();
    }

    [Theory]
    [InlineData("existing")]
    [InlineData("capacity")]
    [InlineData("post-check")]
    public async Task EnablingExcludesAlreadyCurrentSourcesAndCannotRetainUnconfirmedConsent(string change)
    {
        await using var f = new Fixture();
        if (change is "capacity")
        {
            await f.Deliver(new(Enumerable.Range(0, LocalEventBrokerState.MaximumReceipts)
                .Select(_ => new LocalEventView(f.Completion(), LocalEventReason.CategoryLimit, null)).ToArray(), 0, LocalEventReason.Eligible));
        }
        if (change is "post-check") { f.Source.AfterCurrent = () => throw new IOException("injected final source recheck"); }
        using (var root = f.Native())
        {
            var enable = () => f.Notices.ChangeNativeAsync(f.Source.Session, new(1), true,
                f.Notices.Revision, () => true, () => true, f.Token);
            if (change is not "existing")
            {
                await enable.Should().ThrowAsync<Exception>();
                f.Notices.Enabled.Should().BeFalse();
                f.Speech.Requests.Should().Be(0);
                return;
            }
            await enable();
        }
        await f.Deliver(await f.Observe());
        f.Speech.Requests.Should().Be(0);
        f.Source.Time.Now = f.Source.Time.Now.AddMinutes(1);
        f.Source.Events = [f.Completion()];
        await f.Deliver(await f.Observe());
        f.Speech.Played.Should().Equal(ConversationCompletionVoiceNotices.Statement);
    }

    [Fact]
    public async Task UnqualifiedProviderCannotUseOrdinaryPlaybackAsAnAdmissionFallback()
    {
        ISpeechPlaybackService speech = new UnqualifiedSpeech();
        var admitted = false;
        var play = () => speech.SpeakAdmittedAsync("fixed", new("voice", "Voice", "en-US", SpeechVoiceGender.Neutral),
            new("output", "Output"), (start, _) => { admitted = true; start(); return Task.CompletedTask; }, TestContext.Current.CancellationToken);
        await play.Should().ThrowAsync<NotSupportedException>();
        admitted.Should().BeFalse();
    }

    [Fact]
    public async Task BoundedSpeechWatermarksHoldWithoutEvictingOrPersistingAnotherPreference()
    {
        await using var f = new Fixture();
        var entries = Enumerable.Range(0, LocalEventBrokerState.MaximumReceipts + 1)
            .Select(_ => new LocalEventView(f.Completion(), LocalEventReason.CategoryLimit, null)).ToArray();
        await f.Deliver(new(entries, 0, LocalEventReason.Eligible));
        f.Notices.Outcome.Should().Be(ConversationCompletionVoiceNotices.DeliveryOutcome.CapacityHeld);
        f.Speech.Requests.Should().Be(0);
        f.Source.Saves.Should().Be(0);
    }

    [Theory]
    [InlineData("expiry")]
    [InlineData("native-after-request")]
    [InlineData("native-after-terminal")]
    [InlineData("voice-after-terminal")]
    [InlineData("quiet")]
    [InlineData("closed")]
    public async Task ConsentAndDeadlineRevalidationRetainNoAuthorityAfterLateChanges(string change)
    {
        await using var f = new Fixture();
        if (change is "expiry")
        {
            await f.Enable();
            var snapshot = await f.Observe();
            f.Source.Time.Now = f.Source.Time.Now.AddHours(1);
            await f.Deliver(snapshot);
            f.Notices.Outcome.Should().Be(ConversationCompletionVoiceNotices.DeliveryOutcome.Skipped);
            return;
        }
        var native = true;
        if (change is "closed") { await f.Notices.DisposeAsync(); }
        if (change is "quiet")
        {
            using var quiet = f.Native();
            await f.Broker.ChangeRoutineQuietNativeAsync(f.Source.Session, true, f.Broker.RoutineQuiet.Revision, () => true, f.Token);
        }
        f.Source.OnAudit = record =>
        {
            if (record.ActionId is not "completion-notice.voice.enable") { return; }
            if (change is "native-after-request" && record.Outcome == SecurityAuditOutcome.Requested
                || change is "native-after-terminal" && record.Outcome == SecurityAuditOutcome.Succeeded) { native = false; }
            if (change is "voice-after-terminal" && record.Outcome == SecurityAuditOutcome.Succeeded) { f.VoiceCurrent = false; }
        };
        using var root = f.Native();
        var enable = () => f.Notices.ChangeNativeAsync(f.Source.Session, new(1), true,
            f.Notices.Revision, () => f.VoiceCurrent, () => native, f.Token);
        await enable.Should().ThrowAsync<InvalidOperationException>();
        f.Notices.Enabled.Should().BeFalse();
    }

    [Theory]
    [InlineData("no-start")]
    [InlineData("retired-at-terminal")]
    [InlineData("unjoined")]
    [InlineData("audit-request")]
    [InlineData("audit-terminal")]
    [InlineData("gate-update")]
    [InlineData("cancel-unjoined")]
    public async Task TerminalReportsActualOutputRatherThanEnqueueOrCancellation(string change)
    {
        await using var f = new Fixture();
        await f.Enable();
        var snapshot = await f.Observe();
        f.Speech.SkipAdmission = change is "no-start";
        f.Speech.ReturnUnjoined = change is "unjoined" or "cancel-unjoined";
        f.Speech.CancelAfterStart = change is "cancel-unjoined";
        if (change is "retired-at-terminal") { f.Speech.BeforeReturn = () => f.HostCurrent = false; }
        if (change.StartsWith("audit-", StringComparison.Ordinal))
        {
            f.Source.OnAudit = record =>
            {
                if (record.ActionId is "completion-notice.voice" && record.Outcome ==
                    (change is "audit-request" ? SecurityAuditOutcome.Requested : SecurityAuditOutcome.Succeeded))
                { throw new IOException("injected audit failure"); }
            };
        }
        if (change is "gate-update") { f.Source.BeforeCurrent = () => f.Automatic.SetSilently(CallState.Unknown); }
        await f.Deliver(snapshot);
        f.Notices.Outcome.Should().Be(change switch
        {
            "no-start" or "gate-update" => ConversationCompletionVoiceNotices.DeliveryOutcome.Skipped,
            "retired-at-terminal" => ConversationCompletionVoiceNotices.DeliveryOutcome.CancelledJoined,
            _ => ConversationCompletionVoiceNotices.DeliveryOutcome.Unconfirmed,
        });
        if (change is "unjoined" or "cancel-unjoined") { f.Speech.CompleteUnjoined(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConsentRejectsWrongSessionAndAlreadyStoppedOriginalInput(bool stopped)
    {
        await using var f = new Fixture();
        using var root = f.Native();
        if (stopped) { root.Activity!.Stop(); }
        var operation = () => f.Notices.ChangeNativeAsync(stopped ? f.Source.Session : new(Guid.NewGuid()),
            new(1), true, f.Notices.Revision, () => true, () => true, f.Token);
        await operation.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task FailureDiagnosticsUseOnlyTypedFailureAndHostCorrelationNotPrivateExceptionText()
    {
        var logger = new NoticeLogger();
        await using var f = new Fixture(logger);
        await f.Enable();
        var snapshot = await f.Observe();
        using var root = f.Native();
        f.Source.BeforeRead = _ => throw new IOException("private-task-title-answer-secret");
        await f.Deliver(snapshot);
        logger.Messages.Should().ContainSingle().Which.Should().Contain(nameof(IOException))
            .And.NotContain("private-task-title-answer-secret");
        f.Notices.Outcome.Should().Be(ConversationCompletionVoiceNotices.DeliveryOutcome.Unconfirmed);
    }

    [Theory]
    [InlineData("off")]
    [InlineData("no-voice")]
    [InlineData("no-gap")]
    [InlineData("no-output")]
    [InlineData("no-voice-output")]
    [InlineData("session")]
    [InlineData("generation")]
    [InlineData("failure")]
    [InlineData("queue")]
    public async Task DisabledOrIneligibleOperationHasNoSpeechEffects(string change)
    {
        await using var f = new Fixture();
        if (change is not "off") { await f.Enable(); }
        if (change is "no-voice") { f.VoiceCurrent = false; }
        if (change is "no-gap") { f.HostCurrent = false; }
        if (change is "session")
        {
            f.Source.Session = new(Guid.NewGuid());
            f.Source.Events = [f.Completion()];
        }
        if (change is "generation") { f.Source.Events = [f.Completion() with { Generation = 2 }]; }
        if (change is "failure") { f.Source.Events = [f.Completion() with { Type = LocalEventType.Failed }]; }
        if (change is "queue") { f.Source.Events = [f.Source.Event(LocalEventType.Completed)]; }
        var snapshot = await f.Observe();
        var before = f.Source.Audits.Count;
        await f.Notices.DeliverAsync(snapshot, () => f.HostCurrent,
            change is "no-voice-output" ? null : f.Voice, change is "no-output" ? null : f.Output, f.Token);
        f.Speech.Requests.Should().Be(0);
        f.Source.Audits.Count.Should().Be(before);
        f.Speech.Invalidations.Should().Be(0);
        f.Source.Saved!.Budgets.Single().Count.Should().Be(1);
    }

    [Fact]
    public async Task FirstVisualCompletionSpeaksFixedStatementOnceWithoutChangingVisualOrFatigueReceipts()
    {
        await using var f = new Fixture();
        await f.Enable();
        var first = await f.Observe();
        var saved = LocalEventBrokerState.Serialize(f.Source.Saved!);
        await f.Deliver(first);
        f.Notices.Outcome.Should().Be(ConversationCompletionVoiceNotices.DeliveryOutcome.Completed);
        f.Speech.Played.Should().Equal(ConversationCompletionVoiceNotices.Statement);
        f.Source.Saved.Should().NotBeNull();
        LocalEventBrokerState.Serialize(f.Source.Saved!).Should().Be(saved);
        await f.Deliver(first);
        await f.Deliver(await f.Observe());
        var target = first.Events.Single().Event;
        await f.Broker.ExecuteAsync(new(LocalEventOperation.Defer, target.Id, target.Revision),
            RequestOrigin.LocalUi, () => true, f.Token);
        f.Source.Time.Now = f.Source.Time.Now.AddMinutes(16);
        await f.Deliver(await f.Observe());
        f.Speech.Requests.Should().Be(1);
        f.Source.Audits.Where(item => item.Event.ActionId.StartsWith("completion-notice.voice", StringComparison.Ordinal))
            .Select(item => item.Event.Outcome).Should().Equal(SecurityAuditOutcome.Requested,
                SecurityAuditOutcome.Succeeded, SecurityAuditOutcome.Requested, SecurityAuditOutcome.Succeeded);
    }

    [Theory]
    [InlineData("privacy")]
    [InlineData("owner")]
    [InlineData("input")]
    [InlineData("prompt")]
    [InlineData("off")]
    [InlineData("call-active")]
    [InlineData("call-suspected")]
    [InlineData("call-unknown")]
    [InlineData("quiet")]
    [InlineData("retire")]
    [InlineData("dispose-broker")]
    public async Task RevocationCancelsButKeepsOwnershipUntilPhysicalJoinAndNeverReplays(string change)
    {
        await using var f = new Fixture();
        await f.Enable();
        f.Speech.HoldPhysical = true;
        var snapshot = await f.Observe();
        var active = f.Deliver(snapshot);
        await f.Speech.Started.Task;
        f.Notices.Outcome.Should().Be(ConversationCompletionVoiceNotices.DeliveryOutcome.QueuedNotDelivered);
        if (change is "off") { await f.Disable(); }
        else if (change.StartsWith("call-", StringComparison.Ordinal))
        {
            f.Automatic.Set(change switch { "call-active" => CallState.Active, "call-suspected" => CallState.Suspected, _ => CallState.Unknown });
            f.Notices.Revalidate();
        }
        else if (change is "quiet")
        {
            using var root = f.Native();
            await f.Broker.ChangeRoutineQuietNativeAsync(f.Source.Session, true, f.Broker.RoutineQuiet.Revision, () => true, f.Token);
        }
        else if (change is "retire") { await f.Broker.RetireSessionAsync(f.Source.Session, f.Token); }
        else if (change is "dispose-broker") { await f.Broker.DisposeAsync(); }
        else
        {
            if (change is "privacy" or "owner") { f.VoiceCurrent = false; }
            else { f.HostCurrent = false; }
            f.Notices.Revalidate();
        }
        f.Speech.Invalidations.Should().BeGreaterThan(0);
        active.IsCompleted.Should().BeFalse();
        var closing = f.Notices.DisposeAsync().AsTask();
        closing.IsCompleted.Should().BeFalse();
        f.Source.Audits.Should().NotContain(item => item.Event.ActionId == "completion-notice.voice"
            && item.Event.Outcome == SecurityAuditOutcome.Succeeded);
        f.Speech.Release.SetResult();
        await Task.WhenAll(active, closing);
        f.Notices.Outcome.Should().Be(ConversationCompletionVoiceNotices.DeliveryOutcome.CancelledJoined);
        f.Speech.IsSpeaking.Should().BeFalse();
        f.Automatic.Set(CallState.Clear);
        f.VoiceCurrent = f.HostCurrent = true;
        await f.Deliver(snapshot);
        f.Speech.Played.Should().ContainSingle();
    }

    [Theory]
    [InlineData("source")]
    [InlineData("expiry")]
    [InlineData("revision")]
    [InlineData("private-epoch")]
    [InlineData("call")]
    [InlineData("queued-off")]
    [InlineData("attention")]
    public async Task ActualQueuedStartRevalidatesSourceExpiryAndAuthority(string change)
    {
        await using var f = new Fixture();
        await f.Enable();
        f.Speech.HoldQueue = true;
        var snapshot = await f.Observe();
        var active = f.Deliver(snapshot);
        await f.Speech.Queued.Task;
        if (change is "source") { f.Source.Events = []; }
        if (change is "expiry") { f.Source.Time.Now = f.Source.Time.Now.AddMinutes(31); }
        if (change is "revision") { f.Source.Events = [f.Source.Events[0] with { SourceRevision = 4 }]; }
        if (change is "private-epoch") { f.Source.ControlRevision++; }
        if (change is "call") { f.Automatic.Set(CallState.Unknown); }
        if (change is "queued-off") { f.Notices.Retire(); }
        if (change is "attention") { f.Source.Events = [.. f.Source.Events, f.Source.Event(LocalEventType.UserAttention)]; }
        f.Speech.QueueRelease.SetResult();
        await active;
        f.Speech.Played.Should().BeEmpty();
        f.Notices.Outcome.Should().NotBe(ConversationCompletionVoiceNotices.DeliveryOutcome.Completed);
        f.Source.Audits.Should().NotContain(item => item.Event.ActionId == "completion-notice.voice"
            && item.Event.Outcome == SecurityAuditOutcome.Succeeded);
        f.Automatic.Set(CallState.Clear);
        await f.Deliver(snapshot);
        f.Speech.Requests.Should().Be(1);
    }

    [Fact]
    public async Task ConcurrentNewEventsAreBurnedNotQueuedAndOffObservationsCannotBecomeDeferredSpeech()
    {
        await using var f = new Fixture();
        var old = await f.Observe();
        await f.Deliver(old);
        await f.Enable();
        await f.Deliver(old);
        f.Speech.Requests.Should().Be(0);
        f.Source.Time.Now = f.Source.Time.Now.AddMinutes(1);
        f.Source.Events = [f.Completion()];
        f.Speech.HoldPhysical = true;
        var active = f.Deliver(await f.Observe());
        await f.Speech.Started.Task;
        f.Source.Time.Now = f.Source.Time.Now.AddMinutes(1);
        f.Source.Events = [f.Completion()];
        var second = await f.Observe();
        await f.Deliver(second);
        f.Speech.Release.SetResult();
        await active;
        await f.Deliver(second);
        f.Speech.Requests.Should().Be(1);
        var join = f.Notices.RetireAndJoinAsync();
        await join;
        f.Notices.Enabled.Should().BeFalse();
    }

    [Theory]
    [InlineData(RequestOrigin.HostSystem)]
    [InlineData(RequestOrigin.ActivatedVoice)]
    public async Task NonNativeConsentCannotManufactureProactiveSpeechAuthority(RequestOrigin origin)
    {
        await using var f = new Fixture();
        using var root = HostActivity.BeginRoot(new(new(Guid.NewGuid()), f.Source.Session, new(Guid.NewGuid()), origin),
            HostActivityLayer.Application, HostOperation.Policy);
        var change = () => f.Notices.ChangeNativeAsync(f.Source.Session, new(1), true, f.Notices.Revision, () => true, () => true, f.Token);
        await change.Should().ThrowAsync<InvalidOperationException>();
        f.Notices.Enabled.Should().BeFalse();
        f.Source.Audits.Should().BeEmpty();
    }

    [Theory]
    [InlineData("revision")]
    [InlineData("native")]
    [InlineData("voice")]
    [InlineData("call")]
    [InlineData("cancel")]
    [InlineData("audit")]
    public async Task ConsentFailuresRemainOffWithExplicitFailureAndNoSpeech(string change)
    {
        await using var f = new Fixture();
        using var root = f.Native();
        using var cancellation = new CancellationTokenSource();
        if (change is "cancel") { cancellation.Cancel(); }
        if (change is "call") { f.Automatic.Set(CallState.Unknown); }
        if (change is "audit") { f.Source.OnAudit = _ => throw new IOException("injected audit failure"); }
        var operation = () => f.Notices.ChangeNativeAsync(f.Source.Session, new(1), true,
            change is "revision" ? 99 : f.Notices.Revision, () => change is not "voice", () => change is not "native", cancellation.Token);
        await operation.Should().ThrowAsync<Exception>();
        f.Notices.Enabled.Should().BeFalse();
        f.Speech.Requests.Should().Be(0);
    }

    internal sealed class Speech : ISpeechPlaybackService
    {
        internal int Requests { get; private set; }
        internal int Invalidations { get; private set; }
        internal bool HoldQueue { get; set; }
        internal bool HoldPhysical { get; set; }
        internal bool SkipAdmission { get; set; }
        internal bool ReturnUnjoined { get; set; }
        internal bool CancelAfterStart { get; set; }
        internal Action? BeforeReturn { get; set; }
        internal List<string> Played { get; } = [];
        internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Queued { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource QueueRelease { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool IsSpeaking { get; private set; }
        public SpeechPlaybackFrame PlaybackFrame => SpeechPlaybackFrame.Inactive;
        public void InvalidateOutput() => Invalidations++;
        public Task SpeakAsync(string text, SpeechVoice voice, AudioOutputDevice outputDevice, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Unadmitted speech is forbidden.");
        public async Task SpeakAdmittedAsync(string text, SpeechVoice voice, AudioOutputDevice outputDevice,
            SpeechStartAdmission admission, CancellationToken cancellationToken = default)
        {
            Requests++;
            Queued.TrySetResult();
            if (HoldQueue) { await QueueRelease.Task.WaitAsync(cancellationToken); }
            if (SkipAdmission) { return; }
            try
            {
                await admission(() => { IsSpeaking = true; Played.Add(text); Started.TrySetResult(); }, cancellationToken);
                if (CancelAfterStart) { throw new OperationCanceledException("Cancellation is not a physical join."); }
                if (HoldPhysical) { await Release.Task; }
                cancellationToken.ThrowIfCancellationRequested();
                BeforeReturn?.Invoke();
            }

            finally { if (!ReturnUnjoined) { IsSpeaking = false; } }
        }
        internal void CompleteUnjoined() => IsSpeaking = false;
        public Task StopAsync(CancellationToken cancellationToken = default) => throw new InvalidOperationException("Do not stop another output owner.");
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class UnqualifiedSpeech : ISpeechPlaybackService
    {
        public bool IsSpeaking => false;
        public SpeechPlaybackFrame PlaybackFrame => SpeechPlaybackFrame.Inactive;
        public void InvalidateOutput() => throw new InvalidOperationException("No output was admitted.");
        public Task SpeakAsync(string text, SpeechVoice voice, AudioOutputDevice outputDevice, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("No fallback is permitted.");
        public Task StopAsync(CancellationToken cancellationToken = default) => throw new InvalidOperationException("No output was admitted.");
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    internal sealed class Automatic : ICallStateService
    {
        public event EventHandler<CallStateChangedEventArgs>? StateChanged;
        public CallState CurrentState { get; private set; } = CallState.Clear;
        internal void SetSilently(CallState state) => CurrentState = state;
        internal void Set(CallState state) { CurrentState = state; StateChanged?.Invoke(this, new(state)); }
    }

    private sealed class Fixture : IAsyncDisposable
    {
        internal LocalEventBrokerTests.Fixture Source { get; } = new();
        internal Speech Speech { get; } = new();
        internal Automatic Automatic { get; } = new();
        internal CallCommunicationPolicy Calls { get; }
        internal LocalEventBroker Broker { get; }
        internal ConversationCompletionVoiceNotices Notices { get; }
        internal SpeechVoice Voice { get; } = new("voice", "Voice", "en-US", SpeechVoiceGender.Neutral);
        internal AudioOutputDevice Output { get; } = new("output", "Output");
        internal bool VoiceCurrent { get; set; } = true;
        internal bool HostCurrent { get; set; } = true;
        internal CancellationToken Token => TestContext.Current.CancellationToken;
        internal Fixture(Microsoft.Extensions.Logging.ILogger? logger = null)
        {
            Source.Events = [Completion()];
            Broker = Source.Create();
            Calls = new(Automatic);
            Notices = new(Broker, Calls, Speech, Source, logger ?? NullLogger.Instance);
        }
        internal LocalEvent Completion()
        {
            var item = Source.Event(LocalEventType.Completed);
            return item with
            {
                Source = LocalEventSource.ConversationTask,
                QueueReason = null,
                Id = LocalEvent.Identity(LocalEventSource.ConversationTask, item.SessionId.Value, item.SubjectId),
                SourceRevision = 3,
                RelatedRevision = 3
            };
        }
        internal HostActivity Native() => HostActivity.BeginRoot(new(new(Guid.NewGuid()), Source.Session,
            new(Guid.NewGuid()), RequestOrigin.LocalUi), HostActivityLayer.Desktop, HostOperation.Policy);
        internal async Task Enable()
        {
            using var root = Native();
            await Notices.ChangeNativeAsync(Source.Session, new(1), true, Notices.Revision, () => VoiceCurrent, () => true, Token);
            Source.Events = [Completion()];
        }
        internal async Task Disable()
        {
            using var root = Native();
            await Notices.ChangeNativeAsync(Source.Session, new(1), false, Notices.Revision, () => VoiceCurrent, () => true, Token);
        }
        internal Task<LocalEventSnapshot> Observe() => Source.Observe(Broker);
        internal Task Deliver(LocalEventSnapshot snapshot) => Notices.DeliverAsync(snapshot, () => HostCurrent, Voice, Output, Token);
        public async ValueTask DisposeAsync()
        {
            Speech.Release.TrySetResult();
            Speech.QueueRelease.TrySetResult();
            await Notices.DisposeAsync();
            await Broker.DisposeAsync();
            Calls.Dispose();
            Source.Dispose();
        }
    }

    private sealed class NoticeLogger : Microsoft.Extensions.Logging.ILogger
    {
        internal List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;
        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId,
            TState state, Exception? exception, Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));
    }
}