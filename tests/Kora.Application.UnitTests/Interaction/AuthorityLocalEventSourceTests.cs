using System.Diagnostics;

using AwesomeAssertions;

using Kora.Application.Interaction;
using Kora.Application.Maintenance;
using Kora.Core;
using Kora.Core.Auditing;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Core.Maintenance;
using Kora.Core.Storage;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.Interaction;

[Collection("Host tracing")]
public sealed class AuthorityLocalEventSourceTests
{
    [Theory]
    [InlineData(SessionQueueState.Pending, SessionQueueEligibility.Ready, LocalEventType.Queued)]
    [InlineData(SessionQueueState.Running, SessionQueueEligibility.Current, LocalEventType.Current)]
    [InlineData(SessionQueueState.Succeeded, SessionQueueEligibility.Completed, LocalEventType.Completed)]
    [InlineData(SessionQueueState.Failed, SessionQueueEligibility.Completed, LocalEventType.Failed)]
    [InlineData(SessionQueueState.Unknown, SessionQueueEligibility.UnknownQuarantine, LocalEventType.Unknown)]
    [InlineData(SessionQueueState.Pending, SessionQueueEligibility.DependencyNotSucceeded, LocalEventType.Blocked)]
    [InlineData(SessionQueueState.Pending, SessionQueueEligibility.UnclassifiedWorkOrWait, LocalEventType.Blocked)]
    [InlineData(SessionQueueState.Pending, SessionQueueEligibility.UnknownQuarantine, LocalEventType.Blocked)]
    public void Only_authoritative_fixed_profile_work_states_become_content_free_events(
        SessionQueueState state, SessionQueueEligibility eligibility, LocalEventType type)
    {
        using var f = new Fixture();
        var snapshot = f.Snapshot(state, eligibility);
        var item = AuthorityLocalEventSource.FromWork(snapshot, f.Time.Now).Single();
        item.Type.Should().Be(type);
        item.SourceRevision.Should().Be(snapshot.QueueRecords[0].Entry.Revision.Value);
        item.SessionId.Should().Be(f.Request.SessionId);
        item.TaskId.Should().Be(f.Request.TaskId);
        item.ExpiresAt.Should().Be(snapshot.QueueRecords[0].Entry.ExpiresAt);
        item.Validate();
    }

    [Theory]
    [InlineData("done")]
    [InlineData("session")]
    [InlineData("generation")]
    [InlineData("expired")]
    [InlineData("future")]
    [InlineData("removed")]
    [InlineData("cancelled")]
    [InlineData("interrupted")]
    [InlineData("admission-retired")]
    [InlineData("later-head")]
    [InlineData("current-retired")]
    public void Ineligible_retired_expired_and_restart_states_are_not_fabricated_current_events(string boundary)
    {
        using var f = new Fixture();
        var snapshot = f.Snapshot();
        var row = snapshot.QueueRecords[0];
        snapshot = boundary switch
        {
            "done" => snapshot with { Session = new(snapshot.Session.Authority with { IsActive = false }, null) },
            "session" => snapshot with { QueueRecords = [row with { Entry = row.Entry with { Request = HostRequest.Create(RequestOrigin.LocalUi) } }] },
            "generation" => snapshot with { QueueRecords = [row with { Entry = row.Entry with { Generation = new(2) } }] },
            "expired" => snapshot with { QueueRecords = [row with { Entry = row.Entry with { EnqueuedAt = f.Time.Now.AddMinutes(-30), ExpiresAt = f.Time.Now } }] },
            "future" => snapshot with { QueueRecords = [row with { Entry = row.Entry with { EnqueuedAt = f.Time.Now.AddSeconds(1), ExpiresAt = f.Time.Now.AddSeconds(1).AddMinutes(30) } }] },
            "removed" => snapshot with { QueueRecords = [row with { Entry = row.Entry with { State = SessionQueueState.Removed } }] },
            "cancelled" => snapshot with { QueueRecords = [row with { Entry = row.Entry with { State = SessionQueueState.Cancelled } }] },
            "interrupted" => snapshot with { QueueRecords = [row with { Entry = row.Entry with { State = SessionQueueState.Interrupted } }] },
            "admission-retired" => snapshot with { QueueRecords = [row with { Eligibility = SessionQueueEligibility.AdmissionChanged }] },
            "later-head" => snapshot with { QueueRecords = [row with { Eligibility = SessionQueueEligibility.EarlierPendingEntry }] },
            _ => snapshot with { QueueRecords = [row with { Entry = row.Entry with { State = SessionQueueState.Running }, Eligibility = SessionQueueEligibility.UnknownQuarantine }] },
        };
        AuthorityLocalEventSource.FromWork(snapshot, f.Time.Now).Should().BeEmpty();
    }

    [Theory]
    [InlineData("valid")]
    [InlineData("expired")]
    [InlineData("status")]
    [InlineData("generation")]
    [InlineData("session")]
    [InlineData("missing-task")]
    [InlineData("retired-source")]
    [InlineData("profile")]
    [InlineData("task-state")]
    [InlineData("question-key")]
    [InlineData("origin")]
    public void Pending_attention_is_bound_to_the_exact_genuine_local_version_wait_and_never_answers_it(string boundary)
    {
        using var f = new Fixture();
        var key = new HostQuestionKey(f.Request, new(Guid.NewGuid()), new(1));
        var record = new HostQuestionRecord(key, LocalVersionWait.CreateSpec(), new(1), f.Time.Now.AddMinutes(10));
        var observation = new HostTaskObservation(new(f.Request, new(1), HostTaskState.IntentRecorded), new(1), LocalVersionWait.Source, true, record);
        var question = new SessionWorkQuestion(key, new(1), QuestionStatus.Pending, QuestionKind.SingleChoice, null, record.ExpiresAt);
        var systemKey = new HostQuestionKey(new(f.Request.RequestId, f.Request.SessionId, f.Request.TaskId, RequestOrigin.HostSystem),
            key.QuestionId, key.Revision);
        var snapshot = f.Snapshot() with { QueueRecords = [], Tasks = [observation], PendingQuestions = [question] };
        snapshot = boundary switch
        {
            "expired" => snapshot with { PendingQuestions = [question with { ExpiresAt = f.Time.Now }] },
            "status" => snapshot with { PendingQuestions = [question with { Status = QuestionStatus.Cancelled }] },
            "generation" => snapshot with { PendingQuestions = [question with { Generation = new(2) }] },
            "session" => snapshot with { PendingQuestions = [question with { Key = new(HostRequest.Create(RequestOrigin.LocalUi), key.QuestionId, key.Revision) }] },
            "missing-task" => snapshot with { Tasks = [] },
            "retired-source" => snapshot with { Tasks = [observation with { CurrentSource = false }] },
            "profile" => snapshot with { Tasks = [observation with { Source = "unclassified" }] },
            "task-state" => snapshot with { Tasks = [observation with { Task = new(f.Request, new(2), HostTaskState.Unknown) }] },
            "question-key" => snapshot with { Tasks = [observation with { Question = null }] },
            "origin" => snapshot with
            {
                Tasks = [observation with { Question = record with { Key = systemKey } }],
                PendingQuestions = [question with { Key = systemKey }],
            },
            _ => snapshot,
        };
        var events = AuthorityLocalEventSource.FromWork(snapshot, f.Time.Now);
        if (boundary is "valid")
        {
            events.Should().ContainSingle().Which.Type.Should().Be(LocalEventType.UserAttention);
            events[0].SubjectId.Should().Be(key.QuestionId.Value);
        }
        else { events.Should().BeEmpty(); }
        record.Status.Should().Be(QuestionStatus.Pending);
        observation.Task.State.Should().Be(HostTaskState.IntentRecorded);
    }

    [Fact]
    public async Task Cached_maintenance_is_only_an_observation_no_check_open_review_consent_or_model_side_effect()
    {
        using var f = new Fixture();
        var source = f.Create();
        (await source.ReadAsync(f.Request.SessionId, f.Token)).Should().BeEmpty();
        f.State.NetworkEnabled = true;
        await f.State.CheckAsync();
        var checks = f.Checks;
        var events = await source.ReadAsync(f.Request.SessionId, f.Token);
        events.Should().ContainSingle().Which.Type.Should().Be(LocalEventType.MaintenanceAvailable);
        var observed = events[0];
        observed.RelatedRevision.Should().BeGreaterThan(0);
        f.OnCurrent = () =>
        {
            var cached = f.State.ReadLocalEvent(f.Request.SessionId)!;
            var bound = cached with { RelatedRevision = cached.Generation, Generation = f.Current.Session.Authority.Generation.Value };
            bound.Should().Be(observed);
            bound.SameSource(observed).Should().BeTrue();
            AuthorityLocalEventSource.FromWork(f.Current, f.Time.Now).Should().BeEmpty();
        };
        (await source.WithCurrentAsync(f.Request.SessionId, events, () => 42, f.Token)).Should().Be(42);
        f.Checks.Should().Be(checks);
        f.State.CanOpen.Should().BeFalse();
        using (var host = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Application, HostOperation.Request))
        {
            var target = f.State.CaptureCachedTarget();
            f.State.ApplyCached(MaintenanceCommand.Review, target, host.Request, () => true, f.Token);
            f.State.ApplyCached(MaintenanceCommand.Snooze, f.State.CaptureCachedTarget(), host.Request, () => true, f.Token);
        }
        (await source.ReadAsync(f.Request.SessionId, f.Token)).Should().BeEmpty();
        f.Time.Now = observed.ExpiresAt;
        f.State.PrivacyClosed();
        (await source.ReadAsync(f.Request.SessionId, f.Token)).Should().BeEmpty();
        f.Checks.Should().Be(checks);
    }

    [Theory]
    [InlineData("up-to-date")]
    [InlineData("future")]
    [InlineData("sample-rollback")]
    [InlineData("snooze-expired")]
    public async Task Cached_availability_requires_present_verification_and_original_snooze_deadlines_without_refreshing_the_cache(string boundary)
    {
        using var f = new Fixture();
        if (boundary is "up-to-date") { f.Availability = ReleaseAvailability.UpToDate; }
        f.State.NetworkEnabled = true;
        await f.State.CheckAsync();
        var verified = f.Time.Now;
        if (boundary is "future") { f.Time.Now = verified.AddSeconds(-1); }
        if (boundary is "snooze-expired")
        {
            using var host = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Application, HostOperation.Request);
            f.State.ApplyCached(MaintenanceCommand.Review, f.State.CaptureCachedTarget(), host.Request, () => true, f.Token);
            f.State.ApplyCached(MaintenanceCommand.Snooze, f.State.CaptureCachedTarget(), host.Request, () => true, f.Token);
            f.Time.Now = verified.AddHours(24);
            await f.State.CheckAsync();
        }
        var checks = f.Checks;
        if (boundary is "sample-rollback")
        {
            var samples = new Queue<DateTimeOffset>([verified.Add(MaintenanceViewModel.Freshness), verified]);
            f.Time.ReadUtc = samples.Dequeue;
            f.State.ReadLocalEvent(f.Request.SessionId).Should().BeNull();
            samples.Should().BeEmpty();
            f.Time.ReadUtc = null;
        }
        else
        {
            var events = await f.Create().ReadAsync(f.Request.SessionId, f.Token);
            events.Count.Should().Be(boundary is "snooze-expired" ? 1 : 0);
        }
        f.Checks.Should().Be(checks);
    }

    [Theory]
    [InlineData("entry")]
    [InlineData("owner")]
    [InlineData("revision")]
    [InlineData("session")]
    [InlineData("deadline")]
    [InlineData("rollback")]
    [InlineData("cancel")]
    public async Task Source_lease_rechecks_fences_revisions_subject_and_clock_at_the_immediate_presentation_boundary(string boundary)
    {
        using var f = new Fixture();
        f.Current = f.Snapshot();
        var source = f.Create();
        var events = await source.ReadAsync(f.Request.SessionId, f.Token);
        using var cancellation = new CancellationTokenSource();
        f.OnCurrent = () =>
        {
            if (boundary is "entry") { f.Current = f.Current with { QueueRecords = [] }; }
            if (boundary is "owner") { f.CanControl = false; }
            if (boundary is "revision") { f.ControlRevision++; }
            if (boundary is "session") { f.Current = f.Current with { Session = new(f.Current.Session.Authority with { SessionId = new(Guid.NewGuid()) }, null) }; }
        };
        var current = () => source.WithCurrentAsync(f.Request.SessionId, events, () =>
        {
            if (boundary is "deadline") { f.Time.Now = events[0].ExpiresAt; }
            if (boundary is "rollback") { f.Time.Now = f.Time.Now.AddMinutes(-1); }
            if (boundary is "cancel") { cancellation.Cancel(); }
            return true;
        }, cancellation.Token);
        await current.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task Missing_atomic_store_and_closed_or_changing_inspection_admission_do_not_default()
    {
        using var f = new Fixture();
        var source = f.Create();
        f.CanControl = false;
        Func<Task> denied = async () => await source.ReadAsync(f.Request.SessionId, f.Token);
        await denied.Should().ThrowAsync<InvalidOperationException>();
        f.CanControl = true;
        f.OnRead = () => f.ControlRevision++;
        await denied.Should().ThrowAsync<InvalidOperationException>();
        f.OnRead = null;
        f.Current = f.Snapshot() with { Session = new(f.Current.Session.Authority with { SessionId = new(Guid.NewGuid()) }, null) };
        await denied.Should().ThrowAsync<InvalidDataException>();
        f.Current = f.Snapshot();
        ISessionWorkStore missing = new MissingStore();
        var missingRead = () => missing.WithCurrentWorkAsync(f.Request.SessionId, 0, new(), _ => true, f.Token).AsTask();
        await missingRead.Should().ThrowAsync<InvalidOperationException>();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Confirmed_queue_configuration_feeds_event_observation_and_changed_revision_refuses_presentation(bool retire)
    {
        using var f = new Fixture();
        await using var configuration = new Kora.Application.UnitTests.Configuration.SessionQueueConfigurationTestFixture();
        await configuration.Refresh();
        await configuration.Set("2", Kora.Core.Configuration.SessionQueueOption.ExecutionSlots);
        var source = f.Create(configuration.Service);
        var events = await source.ReadAsync(f.Request.SessionId, f.Token);
        f.ObservedLimits!.ExecutionSlots.Should().Be(2);
        (await source.WithCurrentAsync(f.Request.SessionId, events, () => true, f.Token)).Should().BeTrue();
        f.OnCurrent = () =>
        {
            if (retire) { configuration.Service.HoldUnavailable(); }
            else { configuration.PreferencesStore.Value = new(); }
        };
        var observe = () => source.WithCurrentAsync(f.Request.SessionId, events, () => true, f.Token);
        await observe.Should().ThrowAsync<InvalidOperationException>();
    }

    private sealed class Fixture : ISessionWorkStore, ISessionWorkspaceAccess, IReleaseMetadataClient, ICanonicalReleasePageOpener, IUiDispatcher, ISecurityAuditLog, IDisposable
    {
        private readonly ActivityListener listener = new()
        {
            ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };
        internal Fixture()
        {
            ActivitySource.AddActivityListener(listener);
            State = new(this, new AssemblyApplicationInfo(() => "1.0.0"), ReleaseArchitecture.X64, this, Time,
                this, this, NullLogger<MaintenanceViewModel>.Instance, () => 0);
            State.BindGate(() => CanControl);
            Current = Snapshot() with { QueueRecords = [] };
        }
        internal Clock Time { get; } = new();
        internal HostRequest Request { get; } = HostRequest.Create(RequestOrigin.LocalUi);
        internal MaintenanceViewModel State { get; }
        internal SessionWorkSnapshot Current { get; set; }
        internal Action? OnCurrent { get; set; }
        internal Action? OnRead { get; set; }
        internal int Checks { get; private set; }
        internal ReleaseAvailability Availability { get; set; } = ReleaseAvailability.Available;
        internal CancellationToken Token => TestContext.Current.CancellationToken;
        public bool CanInspect => CanControl;
        public bool CanControl { get; set; } = true;
        public long ControlRevision { get; set; }
        internal Kora.Core.Hosting.SessionQueueLimits? ObservedLimits { get; private set; }
        internal AuthorityLocalEventSource Create(Kora.Application.Configuration.SessionQueueConfigurationService? configuration = null) => new(this, this, State, new(), Time, configuration);
        internal SessionWorkSnapshot Snapshot(SessionQueueState state = SessionQueueState.Pending, SessionQueueEligibility eligibility = SessionQueueEligibility.Ready)
        {
            var entry = new SessionQueueEntry(Request, new(1), new(1), 1, state, Guid.NewGuid(), 0, Time.Now, Time.Now.AddMinutes(30));
            return new(new(new(Request.SessionId, new(1), true), null), 1, Time.Now,
                new(Request.SessionId, new(1), 0, [entry]), 1, 10, 1, [new(entry, eligibility, null)], 0, [], 0, [], 0);
        }
        public ValueTask<SessionWorkSnapshot> ReadWorkAsync(HostId<SessionIdentity> session, long admissionRevision,
            SessionQueueLimits limits, CancellationToken token) { ObservedLimits = limits; OnRead?.Invoke(); return ValueTask.FromResult(Current); }
        public ValueTask<T> WithCurrentWorkAsync<T>(HostId<SessionIdentity> session, long admissionRevision, SessionQueueLimits limits,
            Func<SessionWorkSnapshot, T> observation, CancellationToken token) { OnCurrent?.Invoke(); return ValueTask.FromResult(observation(Current)); }
        public Task<ReleaseCheck> CheckAsync(ReleaseChannel channel, string currentVersion, ReleaseArchitecture architecture, CancellationToken cancellationToken)
        {
            Checks++;
            var release = new ReleaseMetadata(42, ReleaseVersion.Parse("2.0.0"), new string('a', 40), ReleaseArchitecture.X64,
                [new(1, "Kora-2.0.0-win-x64.zip", 123, new string('b', 64))]);
            return Task.FromResult(new ReleaseCheck(Availability, "verified", Time.Now, Time.Now, release));
        }
        public Task OpenAsync(ReleaseVersion version, CancellationToken cancellationToken) => throw new InvalidOperationException("Broker cannot navigate.");
        public Task InvokeAsync(Func<Task> action) => action();
        public void Post(Action action) => action();
        public void Write(SecurityAuditEvent entry) { HostActivity.RequireCurrent().Should().NotBeNull(); }
        public void Dispose() { State.Dispose(); listener.Dispose(); }
    }
    private sealed class MissingStore : ISessionWorkStore
    {
        public ValueTask<SessionWorkSnapshot> ReadWorkAsync(HostId<SessionIdentity> session, long admissionRevision,
            SessionQueueLimits limits, CancellationToken token) => throw new InvalidOperationException("Unavailable.");
    }
    private sealed class Clock : TimeProvider
    {
        internal DateTimeOffset Now { get; set; } = new(2026, 10, 4, 1, 55, 0, TimeSpan.Zero);
        internal Func<DateTimeOffset>? ReadUtc { get; set; }
        public override DateTimeOffset GetUtcNow() => ReadUtc?.Invoke() ?? Now;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) => new Timer();
        private sealed class Timer : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;
            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
