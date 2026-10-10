using AwesomeAssertions;

using Kora.Core.Hosting;

namespace Kora.Core.UnitTests.Hosting;

public sealed class SessionQueuePolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 0, 0, 0, TimeSpan.Zero);
    private static readonly Guid Run = Guid.NewGuid();

    [Theory]
    [InlineData(1, false)]
    [InlineData(5, false)]
    [InlineData(60, false)]
    [InlineData(1, true)]
    [InlineData(60, true)]
    public void FutureAdmissionCapturesActiveBudgetWithoutChangingPendingClock(int minutes, bool lifetimeFormat)
    {
        var pending = Entry(new(Guid.NewGuid()), 1);
        if (lifetimeFormat)
        {
            pending = pending with { RecordVersion = 2, PendingLifetimeMinutes = 120, ExpiresAt = Now.AddMinutes(120) };
        }
        var admitted = pending.Admit(new(2), 2, Now.AddSeconds(1), minutes);
        admitted.RecordVersion.Should().Be(3);
        admitted.ActiveBudgetMinutes.Should().Be(minutes);
        admitted.AdmittedAt.Should().Be(Now.AddSeconds(1));
        admitted.ActiveDeadlineAt.Should().Be(Now.AddSeconds(1).AddMinutes(minutes));
        admitted.EnqueuedAt.Should().Be(pending.EnqueuedAt);
        admitted.ExpiresAt.Should().Be(pending.ExpiresAt);
        admitted.PendingLifetimeMinutes.Should().Be(lifetimeFormat ? 120 : 30);
        admitted.Validate();
        foreach (var state in new[] { SessionQueueState.Succeeded, SessionQueueState.Failed, SessionQueueState.Unknown })
        {
            (admitted with { Revision = new(3), State = state }).Validate();
        }
        admitted.Invoking(item => item.Admit(new(3), 3, Now, 1)).Should().Throw<InvalidOperationException>();
        (pending with { DispatchOrder = 1 }).Invoking(item => item.Admit(new(2), 2, Now, 1)).Should().Throw<InvalidOperationException>();
        foreach (var malformed in new[]
        {
            admitted with { ActiveBudgetMinutes = null }, admitted with { AdmittedAt = null },
            admitted with { ActiveDeadlineAt = null }, admitted with { AdmittedAt = Now.AddTicks(-1) },
            admitted with { AdmittedAt = Now.ToOffset(TimeSpan.FromHours(1)) },
            admitted with { DispatchOrder = 1 }, admitted with { Revision = new(1) },
            admitted with { State = SessionQueueState.Pending }, admitted with { State = SessionQueueState.Interrupted },
            admitted with { ActiveBudgetMinutes = 0 }, admitted with { ActiveBudgetMinutes = 61 },
            admitted with { ActiveDeadlineAt = admitted.ActiveDeadlineAt!.Value.AddTicks(1) },
            admitted with { ActiveDeadlineAt = admitted.ActiveDeadlineAt!.Value.ToOffset(TimeSpan.FromHours(1)) },
            admitted with { RecordVersion = 2 }, admitted with { RecordVersion = null, PendingLifetimeMinutes = null },
            admitted with { AdmittedAt = DateTimeOffset.MaxValue, ActiveDeadlineAt = DateTimeOffset.MaxValue },
            pending with { ActiveBudgetMinutes = 5 }, pending with { AdmittedAt = Now }, pending with { ActiveDeadlineAt = Now },
        })
        {
            malformed.Invoking(item => item.Validate()).Should().Throw<InvalidDataException>();
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(30)]
    [InlineData(120)]
    public void Captured_lifetime_has_exact_expiry_edge_independent_of_current_limits(int minutes)
    {
        var entry = Entry(new(Guid.NewGuid()), 1) with
        {
            RecordVersion = SessionQueueEntry.CapturedLifetimeRecordVersion, PendingLifetimeMinutes = minutes,
            ExpiresAt = Now.AddMinutes(minutes),
        };
        var tasks = new Dictionary<HostId<TaskIdentity>, HostTaskState>();
        entry.Validate();
        SessionQueuePolicy.SelectReady([entry], tasks, Run, entry.ExpiresAt.AddTicks(-1), 4,
            new(pendingLifetimeMinutes: minutes == 1 ? 120 : 1)).Should().Be(entry);
        SessionQueuePolicy.SelectReady([entry], tasks, Run, entry.ExpiresAt, 4, new()).Should().BeNull();
        SessionQueuePolicy.Eligibility(entry, [entry], tasks, Run, entry.ExpiresAt, 4, new())
            .Should().Be(SessionQueueEligibility.Expired);
        (entry with { ExpiresAt = entry.ExpiresAt.AddTicks(1) }).Invoking(item => item.Validate()).Should().Throw<InvalidDataException>();
        SessionQueuePolicy.ActiveDeadline.Should().Be(TimeSpan.FromMinutes(5));
    }

    [Theory]
    [InlineData(null, 1)]
    [InlineData(1, null)]
    [InlineData(1, 30)]
    [InlineData(2, null)]
    [InlineData(3, 30)]
    [InlineData(0, 30)]
    [InlineData(2, 0)]
    [InlineData(2, 121)]
    public void Unknown_partial_or_invalid_captured_format_never_reinterprets_legacy_deadlines(int? version, int? minutes)
    {
        var entry = Entry(new(Guid.NewGuid()), 1) with { RecordVersion = version, PendingLifetimeMinutes = minutes };
        entry.Invoking(item => item.Validate()).Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void Round_robin_is_FIFO_and_bounds_starvation_to_one_turn_per_ready_session()
    {
        var sessions = Enumerable.Range(0, 3).Select(_ => new HostId<SessionIdentity>(Guid.NewGuid())).ToArray();
        var entries = Enumerable.Range(0, 12).Select(index => Entry(sessions[index % 3], index + 1)).ToList();
        var selectedSessions = new List<HostId<SessionIdentity>>();
        for (var turn = 0; turn < 12; turn++)
        {
            var next = Select(entries)!;
            next.Position.Should().Be(turn + 1);
            selectedSessions.Add(next.Request.SessionId);
            entries[entries.IndexOf(next)] = next with { State = SessionQueueState.Succeeded, DispatchOrder = 100 + turn };
        }
        selectedSessions.Should().Equal(Enumerable.Range(0, 12).Select(index => sessions[index % 3]));
        Select(entries).Should().BeNull();
    }

    [Fact]
    public void Global_and_session_slots_block_admission_without_bypassing_FIFO_or_dependency()
    {
        var session = new HostId<SessionIdentity>(Guid.NewGuid());
        var other = new HostId<SessionIdentity>(Guid.NewGuid());
        var first = Entry(session, 1);
        var next = Entry(session, 2);
        var unrelated = Entry(other, 3);
        Select([first with { State = SessionQueueState.Running }, next, unrelated]).Should().BeNull();
        Select([first with { State = SessionQueueState.Running }, next, unrelated], slots: 2).Should().Be(unrelated);
        Select([first with { State = SessionQueueState.Unknown }, next, unrelated]).Should().Be(unrelated);
        var dependent = first with { Dependency = new(Guid.NewGuid()) };
        Select([dependent, next, unrelated]).Should().Be(unrelated);
        foreach (var state in Enum.GetValues<HostTaskState>())
        {
            var states = new Dictionary<HostId<TaskIdentity>, HostTaskState> { [dependent.Dependency!.Value] = state };
            SessionQueuePolicy.SelectReady([dependent, next], states, Run, Now, 4, new())
                .Should().Be(state == HostTaskState.Succeeded ? dependent : null);
        }
    }

    [Fact]
    public void Expiry_run_and_admission_changes_block_the_head_and_cannot_replay_or_gain_priority()
    {
        var session = new HostId<SessionIdentity>(Guid.NewGuid());
        var first = Entry(session, 1);
        foreach (var blocked in new[] { first with { RunId = Guid.NewGuid() }, first with { AdmissionRevision = 5 },
            first with { EnqueuedAt = Now.AddMinutes(-30), ExpiresAt = Now } })
        {
            Select([blocked, Entry(session, 2)]).Should().BeNull();
        }
        var other = Entry(new(Guid.NewGuid()), 3) with { DispatchOrder = 10 };
        Select([first with { DispatchOrder = 10 }, other]).Should().Be(first with { DispatchOrder = 10 });
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(11, 1)]
    [InlineData(1, 0)]
    [InlineData(1, 3)]
    public void Limits_reject_unqualified_capacity(int pending, int slots)
    {
        var create = () => new SessionQueueLimits(pending, slots);
        create.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Policy_and_record_validation_fail_closed_on_missing_or_malformed_identity()
    {
        var entry = Entry(new(Guid.NewGuid()), 1);
        entry.Validate();
        entry.IsPending.Should().BeTrue();
        entry.IsCurrent.Should().BeFalse();
        new SessionQueueLimits(2, 2).PendingPerSession.Should().Be(2);
        foreach (var malformed in new[]
        {
            entry with { Request = null! }, entry with { Generation = default }, entry with { Revision = default },
            entry with { Position = 0 }, entry with { RunId = Guid.Empty }, entry with { AdmissionRevision = -1 },
            entry with { DispatchOrder = -1 }, entry with { State = (SessionQueueState)99 },
            entry with { ExpiresAt = Now }, entry with { Request = HostRequest.Create(RequestOrigin.HostSystem) },
            entry with { Dependency = entry.Request.TaskId }, entry with { Dependency = default(HostId<TaskIdentity>) },
        })
        {
            var validate = malformed.Validate;
            validate.Should().Throw<Exception>();
        }
        var noEntries = () => SessionQueuePolicy.SelectReady(null!, new Dictionary<HostId<TaskIdentity>, HostTaskState>(), Run, Now, 4, new());
        noEntries.Should().Throw<ArgumentNullException>();
        var noTasks = () => SessionQueuePolicy.SelectReady([], null!, Run, Now, 4, new());
        noTasks.Should().Throw<ArgumentNullException>();
        var noLimits = () => SessionQueuePolicy.SelectReady([], new Dictionary<HostId<TaskIdentity>, HostTaskState>(), Run, Now, 4, null!);
        noLimits.Should().Throw<ArgumentNullException>();
    }

    private static SessionQueueEntry? Select(IReadOnlyList<SessionQueueEntry> entries, int slots = 1) =>
        SessionQueuePolicy.SelectReady(entries, new Dictionary<HostId<TaskIdentity>, HostTaskState>(), Run, Now, 4, new(executionSlots: slots));

    [Fact]
    public void Presentation_eligibility_uses_the_dispatch_policy_and_names_every_fail_closed_reason()
    {
        var head = Entry(new(Guid.NewGuid()), 2);
        var other = Entry(new(Guid.NewGuid()), 1);
        var states = new Dictionary<HostId<TaskIdentity>, HostTaskState>();
        SessionQueueEligibility Observe(SessionQueueEntry entry, IReadOnlyList<SessionQueueEntry>? rows = null,
            bool active = true, bool blocked = false, bool unknown = false) =>
            SessionQueuePolicy.Eligibility(entry, rows ?? [entry], states, Run, Now, 4, new(),
                active, blocked, unknown);
        Observe(head).Should().Be(SessionQueueEligibility.Ready);
        Observe(head with { State = SessionQueueState.Unknown }).Should().Be(SessionQueueEligibility.UnknownQuarantine);
        Observe(head with { State = SessionQueueState.Interrupted }).Should().Be(SessionQueueEligibility.InterruptedNoReplay);
        Observe(head with { RunId = Guid.NewGuid() }).Should().Be(SessionQueueEligibility.InterruptedNoReplay);
        Observe(head with { State = SessionQueueState.Running }).Should().Be(SessionQueueEligibility.Current);
        Observe(head with { State = SessionQueueState.Running, RunId = Guid.NewGuid() }).Should().Be(SessionQueueEligibility.UnknownQuarantine);
        Observe(head with { State = SessionQueueState.Expired }).Should().Be(SessionQueueEligibility.Expired);
        Observe(head with { EnqueuedAt = Now.AddMinutes(-30), ExpiresAt = Now }).Should().Be(SessionQueueEligibility.Expired);
        Observe(head with { State = SessionQueueState.Cancelled }).Should().Be(SessionQueueEligibility.Completed);
        Observe(head with { State = SessionQueueState.Succeeded, RunId = Guid.NewGuid() }).Should().Be(SessionQueueEligibility.Completed);
        Observe(head, active: false).Should().Be(SessionQueueEligibility.SessionDone);
        Observe(head, unknown: true).Should().Be(SessionQueueEligibility.UnknownQuarantine);
        Observe(head, [head, head with { State = SessionQueueState.Unknown }]).Should().Be(SessionQueueEligibility.UnknownQuarantine);
        Observe(head, blocked: true).Should().Be(SessionQueueEligibility.UnclassifiedWorkOrWait);
        Observe(head with { AdmissionRevision = 9 }).Should().Be(SessionQueueEligibility.AdmissionChanged);
        Observe(head, [head, head with { State = SessionQueueState.Running }]).Should().Be(SessionQueueEligibility.SessionCurrent);
        Observe(head, [head, head with { Position = 1 }]).Should().Be(SessionQueueEligibility.EarlierPendingEntry);
        var dependent = head with { Dependency = new(Guid.NewGuid()) };
        Observe(dependent).Should().Be(SessionQueueEligibility.DependencyNotSucceeded);
        states[dependent.Dependency!.Value] = HostTaskState.Unknown;
        Observe(dependent).Should().Be(SessionQueueEligibility.DependencyNotSucceeded);
        states[dependent.Dependency.Value] = HostTaskState.Succeeded;
        Observe(dependent).Should().Be(SessionQueueEligibility.Ready);
        Observe(head, [head, other with { State = SessionQueueState.Running }]).Should().Be(SessionQueueEligibility.GlobalCapacity);
        Observe(head, [head, other with { State = SessionQueueState.Running, RunId = Guid.NewGuid() }]).Should().Be(SessionQueueEligibility.Ready);
        Observe(head, [head, head with { Position = 3 }, other]).Should().Be(SessionQueueEligibility.Ready);
        Observe(head, [head, other with { State = SessionQueueState.Unknown }]).Should().Be(SessionQueueEligibility.Ready);
        var malformed = () => Observe(head with { State = (SessionQueueState)99 });
        malformed.Should().Throw<InvalidDataException>();
        var noEntry = () => SessionQueuePolicy.Eligibility(null!, [], states, Run, Now, 4, new());
        noEntry.Should().Throw<ArgumentNullException>();
        var noRows = () => SessionQueuePolicy.Eligibility(head, null!, states, Run, Now, 4, new());
        noRows.Should().Throw<ArgumentNullException>();
        var noStates = () => SessionQueuePolicy.Eligibility(head, [], null!, Run, Now, 4, new());
        noStates.Should().Throw<ArgumentNullException>();
        var noLimits = () => SessionQueuePolicy.Eligibility(head, [], states, Run, Now, 4, null!);
        noLimits.Should().Throw<ArgumentNullException>();
    }

    private static SessionQueueEntry Entry(HostId<SessionIdentity> session, long position) =>
        new(new(new(Guid.NewGuid()), session, new(Guid.NewGuid()), RequestOrigin.LocalUi), new(1), new(1),
            position, SessionQueueState.Pending, Run, 4, Now, Now.AddMinutes(30));
}
