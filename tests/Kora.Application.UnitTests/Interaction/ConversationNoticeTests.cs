using System.Diagnostics;

using AwesomeAssertions;

using Kora.Application.Interaction;
using Kora.Application.Maintenance;
using Kora.Core;
using Kora.Core.Auditing;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Core.Interaction.ConversationNotices;
using Kora.Core.Maintenance;
using Kora.Core.Storage;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.Interaction;

[Collection("Host tracing")]
public sealed class ConversationNoticeTests
{
    [Fact]
    public async Task RealAuthoritySuccessProjectionCompletesTheConsentedSpeechOperationAndKeepsPassiveVisualReceipt()
    {
        using var f = new Fixture();
        await using var broker = f.Broker();
        using var calls = new Kora.Application.Communication.CallCommunicationPolicy(new ConversationCompletionVoiceNoticesTests.Automatic());
        var speech = new ConversationCompletionVoiceNoticesTests.Speech();
        await using var notices = new ConversationCompletionVoiceNotices(broker, calls, speech, f, NullLogger.Instance);
        using (var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Desktop, HostOperation.Policy))
        { await notices.ChangeNativeAsync(f.Request.SessionId, new(1), true, notices.Revision, () => true, () => true, f.Token); }
        f.Request = new(new(Guid.NewGuid()), f.Request.SessionId, new(Guid.NewGuid()), RequestOrigin.LocalUi);
        f.Terminal(ModelTurnOutcome.Succeeded, HostTaskState.Succeeded);
        var snapshot = await f.Observe(broker);
        await notices.DeliverAsync(snapshot, () => true,
            new("voice", "Voice", "en-US", Kora.Core.Voice.SpeechVoiceGender.Neutral), new("output", "Output"), f.Token);
        speech.Played.Should().Equal(ConversationCompletionVoiceNotices.Statement);
        notices.Outcome.Should().Be(ConversationCompletionVoiceNotices.DeliveryOutcome.Completed);
        f.Current.Tasks.Single().Task.State.Should().Be(HostTaskState.Succeeded);
        (await f.Observe(broker)).Events.Single().Reason.Should().Be(LocalEventReason.PresentedNoReplay);
        f.Saved!.Budgets.Single().Count.Should().Be(1);
    }

    [Theory]
    [InlineData(ModelTurnOutcome.Succeeded, HostTaskState.Succeeded, LocalEventType.Completed)]
    [InlineData(ModelTurnOutcome.Unavailable, HostTaskState.Unavailable, LocalEventType.Failed)]
    [InlineData(ModelTurnOutcome.Denied, HostTaskState.Denied, LocalEventType.Failed)]
    [InlineData(ModelTurnOutcome.Unknown, HostTaskState.Unknown, LocalEventType.Unknown)]
    public async Task RealSourceBrokerCoalescesTerminalsWithoutRenewingExpiryOrAuthority(
        ModelTurnOutcome outcome, HostTaskState state, LocalEventType type)
    {
        using var f = new Fixture();
        f.Terminal(outcome, state);
        await using var broker = f.Broker();
        var original = f.Current;
        var first = (await f.Observe(broker)).Events.Single();
        first.Event.Type.Should().Be(type);
        first.Reason.Should().Be(LocalEventReason.Eligible);
        first.Event.ExpiresAt.Should().Be(f.Time.Now + ConversationNoticeAuthority.Lifetime);
        f.Time.Now = f.Time.Now.AddMinutes(1);
        var second = (await f.Observe(broker)).Events.Single();
        second.Event.ExpiresAt.Should().Be(first.Event.ExpiresAt);
        second.Reason.Should().Be(LocalEventReason.PresentedNoReplay);
        var reviewed = await broker.ExecuteAsync(new(LocalEventOperation.Review, second.Event.Id, second.Event.Revision),
            RequestOrigin.LocalUi, () => true, f.Token);
        reviewed.Event.Should().Be(second.Event);
        var deferred = await broker.ExecuteAsync(new(LocalEventOperation.Defer, second.Event.Id, second.Event.Revision),
            RequestOrigin.LocalUi, () => true, f.Token);
        deferred.DeferredUntil.Should().Be(f.Time.Now.AddMinutes(15));
        var dismissed = await broker.ExecuteAsync(new(LocalEventOperation.Dismiss, deferred.Event.Id, deferred.Event.Revision),
            RequestOrigin.LocalUi, () => true, f.Token);
        dismissed.Reason.Should().Be(LocalEventReason.Dismissed);
        f.Current.Should().Be(original);
        f.Time.Now = first.Event.ExpiresAt;
        (await f.Observe(broker)).Events.Should().BeEmpty();
    }

    [Theory]
    [InlineData("valid", true)]
    [InlineData("text", true)]
    [InlineData("multiple-choice", true)]
    [InlineData("omitted", false)]
    [InlineData("generation", false)]
    [InlineData("request", false)]
    [InlineData("revision", false)]
    [InlineData("status", false)]
    [InlineData("channel", false)]
    [InlineData("kind", false)]
    [InlineData("expiry", false)]
    [InlineData("elapsed", false)]
    public void AttentionRequiresExactLiveClarificationNotModelText(string fault, bool admitted)
    {
        using var f = new Fixture();
        f.Question(fault switch
        {
            "text" => QuestionKind.Text,
            "multiple-choice" => QuestionKind.MultipleChoice,
            _ => QuestionKind.SingleChoice,
        });
        var question = f.Current.PendingQuestions.Single();
        var changed = fault switch
        {
            "generation" => question with { Generation = new(2) },
            "request" => question with { Key = new(HostRequest.Create(RequestOrigin.LocalUi), question.Key.QuestionId, question.Key.Revision) },
            "revision" => question with { Key = question.Key.Next() },
            "status" => question with { Status = QuestionStatus.Answered },
            "channel" => question with { AnswerChannel = RequestOrigin.LocalUi },
            "kind" => question with { Kind = (QuestionKind)(-1) },
            "expiry" => question with { ExpiresAt = question.ExpiresAt.AddTicks(1) },
            _ => question,
        };
        f.Current = f.Current with { PendingQuestions = fault is "omitted" ? [] : [changed], OmittedQuestions = 1 };
        if (fault is "elapsed") { f.Time.Now = question.ExpiresAt; }
        var events = AuthorityLocalEventSource.ConversationEvents(f.Current, f.Time.Now, DateTimeOffset.MinValue);
        events.Count.Should().Be(admitted ? 1 : 0);
        if (admitted)
        {
            events.Single().Source.Should().Be(LocalEventSource.ConversationQuestion);
            events.Single().SubjectId.Should().Be(question.Key.QuestionId.Value);
            events.Single().ExpiresAt.Should().Be(question.ExpiresAt);
        }
    }

    [Theory]
    [InlineData("done")]
    [InlineData("source")]
    [InlineData("generation")]
    [InlineData("notice-generation")]
    [InlineData("session")]
    [InlineData("future")]
    [InlineData("cutoff")]
    [InlineData("expired")]
    [InlineData("missing")]
    public void RetiredUnclassifiedAndOmittedSourcesNeverInferSuccess(string fault)
    {
        using var f = new Fixture();
        var task = f.Current.Tasks.Single();
        var notice = task.ConversationNotice!;
        f.Current = fault switch
        {
            "done" => f.Current with { Session = new(f.Current.Session.Authority with { IsActive = false }, null) },
            "source" => f.Current with { Tasks = [task with { Source = "model text says complete" }] },
            "generation" => f.Current with { Tasks = [task with { Generation = new(2) }] },
            "notice-generation" => f.Current with { Tasks = [task with { ConversationNotice = notice with { Generation = new(2) } }] },
            "session" => f.Current with { Session = new(f.Current.Session.Authority with { SessionId = new(Guid.NewGuid()) }, null) },
            "future" => f.Current with { Tasks = [task with { ConversationNotice = notice with { SubmittedAt = f.Time.Now.AddTicks(1) } }] },
            "missing" => f.Current with { Tasks = [task with { ConversationNotice = null }], OmittedTasks = 50 },
            _ => f.Current,
        };
        if (fault is "expired") { f.Time.Now += ConversationNoticeAuthority.Lifetime; }
        var cutoff = fault is "cutoff" ? notice.SubmittedAt : DateTimeOffset.MinValue;
        AuthorityLocalEventSource.ConversationEvents(f.Current, f.Time.Now, cutoff).Should().BeEmpty();
    }

    [Theory]
    [InlineData("source")]
    [InlineData("privacy")]
    [InlineData("call")]
    [InlineData("delete")]
    public async Task RevalidationRejectsLateActionsAndEpochReopenCannotReplayEarlierSources(string change)
    {
        using var f = new Fixture();
        await using var broker = f.Broker();
        var first = (await f.Observe(broker)).Events.Single().Event;
        f.Time.Now = f.Time.Now.AddTicks(1);
        if (change is "privacy" or "call")
        {
            f.ControlRevision++;
            f.CanControl = false;
            var closed = () => f.Observe(broker);
            await closed.Should().ThrowAsync<InvalidOperationException>();
            f.CanControl = true;
        }
        else if (change is "delete") { f.Current = f.Current with { Tasks = [] }; }
        else { f.Current = f.Current with { Tasks = [f.Current.Tasks[0] with { Source = "unclassified" }] }; }
        var stale = () => broker.ExecuteAsync(new(LocalEventOperation.Review, first.Id, first.Revision),
            RequestOrigin.LocalUi, () => true, f.Token);
        if (change is "privacy" or "call") { await stale.Should().ThrowAsync<InvalidOperationException>(); }
        else { (await stale()).Reason.Should().Be(LocalEventReason.SourceUnavailable); }
        (await f.Observe(broker)).Events.Should().BeEmpty();
        if (change is "privacy" or "call")
        {
            f.Time.Now = f.Time.Now.AddTicks(1);
            f.Terminal(ModelTurnOutcome.Succeeded, HostTaskState.Succeeded);
            (await f.Observe(broker)).Events.Single().Event.Type.Should().Be(LocalEventType.Completed);
        }
    }

    [Fact]
    public async Task ClarificationTerminalTransitionKeepsQuestionSeparateAndSharesFatigueBudget()
    {
        using var f = new Fixture();
        f.Question();
        await using var broker = f.Broker();
        var question = (await f.Observe(broker)).Events.Single().Event;
        var defer = await broker.ExecuteAsync(new(LocalEventOperation.Defer, question.Id, question.Revision),
            RequestOrigin.LocalUi, () => true, f.Token);
        defer.DeferredUntil.Should().Be(question.ExpiresAt);
        f.Current.PendingQuestions.Single().Status.Should().Be(QuestionStatus.Pending);
        f.Terminal(ModelTurnOutcome.Succeeded, HostTaskState.Succeeded);
        var complete = (await f.Observe(broker)).Events.Single().Event;
        complete.Source.Should().Be(LocalEventSource.ConversationTask);
        complete.Id.Should().NotBe(question.Id);
        for (var index = 0; index < 3; index++)
        {
            f.Time.Now = f.Time.Now.AddMinutes(1);
            f.Request = new(new(Guid.NewGuid()), f.Request.SessionId, new(Guid.NewGuid()), RequestOrigin.LocalUi);
            f.Terminal(ModelTurnOutcome.Succeeded, HostTaskState.Succeeded);
            var observed = (await f.Observe(broker)).Events.Single();
            observed.Reason.Should().Be(index < 2 ? LocalEventReason.Eligible : LocalEventReason.CategoryLimit);
        }
        f.Saved!.Budgets.Single(item => item.Category == LocalEventCategory.Work).Count.Should().Be(3);
        f.Saved.Budgets.Single(item => item.Category == LocalEventCategory.Attention).Count.Should().Be(1);
        LocalEventBrokerState.Deserialize(LocalEventBrokerState.Serialize(f.Saved)).Should().BeEquivalentTo(f.Saved);
    }

    private sealed class Fixture : ISessionWorkStore, ISessionWorkspaceAccess, IReleaseMetadataClient,
        ICanonicalReleasePageOpener, IUiDispatcher, ISecurityAuditLog, ILocalEventStateStore, IDisposable
    {
        private readonly ActivityListener listener = new()
        {
            ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };
        private readonly MaintenanceViewModel maintenance;
        private readonly AuthorityLocalEventSource source;
        internal Fixture()
        {
            ActivitySource.AddActivityListener(listener);
            maintenance = new(this, new AssemblyApplicationInfo(() => "1.0.0"), ReleaseArchitecture.X64,
                this, Time, this, this, NullLogger<MaintenanceViewModel>.Instance);
            maintenance.BindGate(() => CanControl);
            source = new(this, this, maintenance, new(), Time);
            Terminal(ModelTurnOutcome.Succeeded, HostTaskState.Succeeded);
        }
        internal LocalEventBrokerTests.Clock Time { get; } = new();
        internal HostRequest Request { get; set; } = HostRequest.Create(RequestOrigin.LocalUi);
        internal SessionWorkSnapshot Current { get; set; } = null!;
        internal LocalEventBrokerState? Saved { get; private set; }
        internal CancellationToken Token => TestContext.Current.CancellationToken;
        public bool CanInspect => CanControl;
        public bool CanControl { get; set; } = true;
        public long ControlRevision { get; set; }
        internal void Terminal(ModelTurnOutcome outcome, HostTaskState state)
        {
            var task = new HostTaskObservation(new(Request, new(3), state), new(1), ConversationNoticeAuthority.Source, false, null)
            { ConversationNotice = new(Request, new(1), 3, new(Guid.NewGuid()), Time.Now, outcome, ModelTurnReason.ProviderOutcome, null, null) };
            Current = Snapshot(task);
        }
        internal void Question(QuestionKind kind = QuestionKind.SingleChoice)
        {
            var key = new HostQuestionKey(Request, new(Guid.NewGuid()), new(1));
            var task = new HostTaskObservation(new(Request, new(2), HostTaskState.DispatchRecorded), new(1),
                ConversationNoticeAuthority.Source, true, null)
            { ConversationNotice = new(Request, new(1), 2, new(Guid.NewGuid()), Time.Now, null, null, key, Time.Now.AddSeconds(15)) };
            Current = Snapshot(task) with { PendingQuestions = [new(key, new(1), QuestionStatus.Pending,
                kind, null, Time.Now.AddSeconds(15))] };
        }
        private SessionWorkSnapshot Snapshot(HostTaskObservation task) =>
            new(new(new(Request.SessionId, new(1), true), null), 1, Time.Now, new(Request.SessionId, new(1), 0, []),
                0, 10, 1, [], 0, [task], 0, [], 0);
        internal LocalEventBroker Broker() => new(source, this, this, Time, this, NullLogger<LocalEventBroker>.Instance);
        internal Task<LocalEventSnapshot> Observe(LocalEventBroker broker) => broker.ObserveAsync(Request.SessionId, () => true, Token);
        public ValueTask<SessionWorkSnapshot> ReadWorkAsync(HostId<SessionIdentity> session, long revision, SessionQueueLimits limits, CancellationToken token) =>
            ValueTask.FromResult(Current);
        public ValueTask<T> WithCurrentWorkAsync<T>(HostId<SessionIdentity> session, long revision, SessionQueueLimits limits,
            Func<SessionWorkSnapshot, T> observation, CancellationToken token) => ValueTask.FromResult(observation(Current));
        public LocalEventBrokerState? Load() => Saved;
        public void Save(LocalEventBrokerState state) { state.Validate(); Saved = state; }
        public void BeginWrite() { }
        public void ConfirmWrite() { }
        public void Write(SecurityAuditEvent entry) => HostActivity.RequireCurrent().CorrelationId.Should().Be(entry.CorrelationId);
        public Task<ReleaseCheck> CheckAsync(ReleaseChannel channel, string version, ReleaseArchitecture architecture, CancellationToken token) =>
            throw new InvalidOperationException("No notice network operations.");
        public Task OpenAsync(ReleaseVersion version, CancellationToken token) => throw new InvalidOperationException("No notice navigation.");
        public Task InvokeAsync(Func<Task> action) => action();
        public void Post(Action action) => action();
        public void Dispose() { maintenance.Dispose(); listener.Dispose(); }
    }
}
