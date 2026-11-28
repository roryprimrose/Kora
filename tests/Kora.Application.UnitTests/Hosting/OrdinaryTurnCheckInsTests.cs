using AwesomeAssertions;

using Kora.Application.Hosting;
using Kora.Core.Auditing;
using Kora.Core.Configuration;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;

namespace Kora.Application.UnitTests.Hosting;

public sealed class OrdinaryTurnCheckInsTests
{
    private static readonly HostRequest Request = HostRequest.Create(RequestOrigin.LocalUi);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void StopReceiptClosesAPendingCheckInOrFollowsTheTurnsOwnRetirement(bool stillPending)
    {
        using var harness = new Harness();
        var checkIn = harness.Issue();
        var complete = harness.CheckIns.BeginStop(checkIn.Id, SecurityAuditInitiator.VoiceCommand);
        if (!stillPending) { harness.CheckIns.Dispose(); }
        complete(true);
        harness.CheckIns.Dispose();
        harness.Audit.Events.Select(item => (item.ActionId, item.Outcome, item.ReasonCode)).Should().Equal(stillPending
            ? [("conversation.check-in", SecurityAuditOutcome.Requested, null),
                ("conversation.check-in-answer", SecurityAuditOutcome.Requested, "stop"),
                ("conversation.check-in-answer", SecurityAuditOutcome.Succeeded, "stop"),
                ("conversation.check-in", SecurityAuditOutcome.Succeeded, "stop")]
            : [("conversation.check-in", SecurityAuditOutcome.Requested, null),
                ("conversation.check-in-answer", SecurityAuditOutcome.Requested, "stop"),
                ("conversation.check-in", SecurityAuditOutcome.Cancelled, "turn-ended"),
                ("conversation.check-in-answer", SecurityAuditOutcome.Succeeded, "stop")]);
        harness.CheckIns.Pending.Should().BeNull();
        harness.Clock.Advance(TimeSpan.FromHours(1));
        harness.Fire();
        harness.Presented.Should().ContainSingle("a stopped or retired schedule never posts again");
    }

    [Fact]
    public void RefusedStopKeepsTheExactCheckInPendingAndAnswerable()
    {
        using var harness = new Harness();
        var checkIn = harness.Issue();
        harness.CheckIns.BeginStop(checkIn.Id, SecurityAuditInitiator.LocalUser)(false);
        harness.CheckIns.Pending.Should().Be(checkIn);
        harness.Audit.Events.Last().Outcome.Should().Be(SecurityAuditOutcome.Failed);
        harness.CheckIns.Answer(checkIn.Id, new(ConversationCheckInOperation.KeepGoing), SecurityAuditInitiator.LocalUser);
        harness.CheckIns.Pending.Should().BeNull();
    }

    [Fact]
    public void StaleOrStopAnswersAreRefusedBeforeAnyAudit()
    {
        using var harness = new Harness();
        harness.CheckIns.Invoking(value => value.Answer(Guid.NewGuid(), new(ConversationCheckInOperation.KeepGoing),
            SecurityAuditInitiator.LocalUser)).Should().Throw<InvalidOperationException>("nothing is pending yet");
        var checkIn = harness.Issue();
        harness.CheckIns.Invoking(value => value.Answer(checkIn.Id, new(ConversationCheckInOperation.Stop),
            SecurityAuditInitiator.LocalUser)).Should().Throw<InvalidOperationException>();
        harness.CheckIns.Invoking(value => value.Answer(Guid.NewGuid(), new(ConversationCheckInOperation.KeepGoing),
            SecurityAuditInitiator.LocalUser)).Should().Throw<InvalidOperationException>();
        harness.CheckIns.Invoking(value => value.BeginStop(Guid.NewGuid(), SecurityAuditInitiator.LocalUser))
            .Should().Throw<InvalidOperationException>();
        harness.Audit.Events.Should().ContainSingle();
    }

    [Fact]
    public void ResumeAfterTheIntervalAlreadyPassedFiresImmediatelyAndPendingCheckInsIgnoreWaits()
    {
        using var harness = new Harness();
        harness.Clock.Advance(TimeSpan.FromMinutes(2));
        using (harness.Lifetime.BeginHostWait()) { harness.Timer.Due.Should().Be(Timeout.InfiniteTimeSpan); }
        harness.Timer.Due.Should().Be(TimeSpan.Zero);
        harness.Fire();
        var checkIn = harness.CheckIns.Pending!;
        checkIn.ActiveElapsed.Should().Be(TimeSpan.FromMinutes(2));
        var changes = harness.Timer.Changes;
        using (harness.Lifetime.BeginHostWait()) { }
        harness.Timer.Changes.Should().Be(changes, "a pending check-in is not rescheduled by host waits");
        harness.Fire();
        harness.Presented.Should().ContainSingle("one check-in at a time");
    }

    [Fact]
    public void MissingAuditFailsClosedOnceAndNeverPresents()
    {
        using var harness = new Harness(audit: false);
        harness.Clock.Advance(TimeSpan.FromMinutes(1));
        harness.Fire();
        harness.Fire();
        harness.Unavailable.Should().Be(1);
        harness.Presented.Should().BeEmpty();
        harness.CheckIns.Pending.Should().BeNull();
    }

    private sealed class Harness : IDisposable
    {
        private readonly Kora.Core.Diagnostics.HostActivity root =
            Kora.Core.Diagnostics.HostActivity.BeginRoot(Request, Kora.Core.Diagnostics.HostActivityLayer.Application,
                Kora.Core.Diagnostics.HostOperation.Request);

        internal Harness(bool audit = true)
        {
            Lifetime = new(Request, Clock);
            Lifetime.UseCheckIns();
            CheckIns = new(Lifetime, new OrdinaryTurnDeadline(1), Clock, audit ? Audit : null, Guid.NewGuid(),
                Presented.Add, () => Unavailable++);
        }

        internal Clock Clock { get; } = new();
        internal ModelTurnLifetime Lifetime { get; }
        internal AuditLog Audit { get; } = new();
        internal List<ConversationCheckIn?> Presented { get; } = [];
        internal int Unavailable { get; private set; }
        internal OrdinaryTurnCheckIns CheckIns { get; }
        internal ManualTimer Timer => Clock.Timers.Single();
        internal void Fire() => Timer.Fire();

        internal ConversationCheckIn Issue()
        {
            Clock.Advance(TimeSpan.FromMinutes(1));
            Fire();
            return CheckIns.Pending!;
        }

        public void Dispose()
        {
            CheckIns.Dispose();
            root.Complete(Kora.Core.Diagnostics.HostOperationOutcome.Completed);
            root.Dispose();
        }
    }

    internal sealed class AuditLog : ISecurityAuditLog
    {
        internal List<SecurityAuditEvent> Events { get; } = [];
        public void Write(SecurityAuditEvent auditEvent) => Events.Add(auditEvent);
    }

    internal sealed class Clock : TimeProvider
    {
        private long timestamp;
        internal List<ManualTimer> Timers { get; } = [];
        public override long GetTimestamp() => timestamp;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        internal void Advance(TimeSpan amount) => timestamp += amount.Ticks;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ManualTimer(callback, state) { Due = dueTime };
            Timers.Add(timer);
            return timer;
        }
    }

    internal sealed class ManualTimer(TimerCallback callback, object? state) : ITimer
    {
        internal TimeSpan Due { get; set; }
        internal int Changes { get; private set; }
        internal void Fire() => callback(state);
        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            Due = dueTime;
            Changes++;
            return true;
        }
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
