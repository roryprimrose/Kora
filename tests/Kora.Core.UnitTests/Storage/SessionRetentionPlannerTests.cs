using AwesomeAssertions;

using Kora.Core.Configuration;
using Kora.Core.Storage;

namespace Kora.Core.UnitTests.Storage;

public sealed class SessionRetentionPlannerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 18, 0, 0, 0, TimeSpan.Zero);
    private static SessionRetentionState Clock(int age = 5) =>
        new(new(Guid.NewGuid()), Now.AddDays(-age), Now.AddDays(10 - age), Now.AddDays(30 - age), false, false);

    [Fact]
    public void CompleteZeroAndAllExclusionsAreContentFreeAndDoNotChangeRecordedClocks()
    {
        var planner = new SessionRetentionPlanner(new(1, 3), Now);
        var empty = planner.Finish("source");
        empty.Aggregate.Should().Be(new SessionRetentionPlanAggregate(0, 0, 0, 0, 0, 0, 0, 0, 0));
        SessionRetentionPlanner.Validate(empty);
        var clock = Clock();
        planner.Add(clock with { Perpetual = true }, 0, true, true, 1).Should().BeTrue();
        planner.Add(clock, 0, true, true, 1).Should().BeTrue();
        planner.Add(clock, 0, false, true, 1).Should().BeTrue();
        planner.Add(clock with { Purged = true }, 2, true, true, 1).Should().BeTrue();
        var result = planner.Finish("source");
        result.Aggregate.Should().Be(new SessionRetentionPlanAggregate(4, 0, 0, 0, 0, 1, 1, 1, 1));
        SessionRetentionPlanner.Validate(result);
        clock.Should().Be(Clock() with { SessionId = clock.SessionId });
        typeof(SessionRetentionPlanAggregate).GetProperties().Should().OnlyContain(p => p.PropertyType == typeof(int));
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(1, 1, 0)]
    [InlineData(2, 1, 0)]
    [InlineData(3, 0, 1)]
    [InlineData(5, 0, 1)]
    [InlineData(30, 0, 0)]
    public void InclusiveCutoffAndDeletePrecedenceProduceExactNewlyDueCounts(int age, int archive, int delete)
    {
        var planner = new SessionRetentionPlanner(new(1, 3), Now);
        planner.Add(Clock(age), 0, false, false, 1);
        var result = planner.Finish("source");
        result.Aggregate!.NewlyArchiveDue.Should().Be(archive);
        result.Aggregate.NewlyDeleteDue.Should().Be(delete);
        SessionRetentionPlanner.Validate(result);
    }

    [Fact]
    public void StoredDeadlinesAreSeparateFromHypotheticalRecomputationAndArchivedSessionsDoNotRearchive()
    {
        var planner = new SessionRetentionPlanner(new(1, 3), Now);
        planner.Add(Clock(10), 0, false, false, 1);
        planner.Add(Clock(30), 0, false, false, 1);
        planner.Add(Clock(2), 1, false, false, 1);
        var value = planner.Finish("source").Aggregate!;
        value.RecordedArchiveDue.Should().Be(1);
        value.RecordedDeleteDue.Should().Be(1);
        value.NewlyArchiveDue.Should().Be(0);
        value.NewlyDeleteDue.Should().Be(1);
        var extending = new SessionRetentionPlanner(new(20, 60), Now);
        extending.Add(Clock(10), 0, false, false, 1);
        extending.Finish("source").Aggregate!.NewlyDeleteDue.Should().Be(0);
        extending.Finish("source").Aggregate!.NewlyArchiveDue.Should().Be(0);
    }

    [Fact]
    public void ExactCandidateAndByteLimitsCompleteButOneMoreIsIncompleteWithoutPartialAggregates()
    {
        var rows = new SessionRetentionPlanner(new(1, 3), Now);
        for (var i = 0; i < SessionRetentionPlanner.MaximumCandidates; i++)
        {
            rows.AdmitCandidate(0).Should().BeTrue();
        }
        rows.Finish("source").Status.Should().Be(SessionRetentionPlanStatus.Complete);
        rows.AdmitCandidate(0).Should().BeFalse();
        rows.AdmitCandidate(0).Should().BeFalse();
        var limited = rows.Finish("source");
        limited.Status.Should().Be(SessionRetentionPlanStatus.Incomplete);
        limited.Aggregate.Should().BeNull();
        SessionRetentionPlanner.Validate(limited);
        var bytes = new SessionRetentionPlanner(new(1, 3), Now);
        bytes.Add(Clock(), 0, false, false, SessionRetentionPlanner.MaximumEncodedBytes).Should().BeTrue();
        bytes.Finish("source").Status.Should().Be(SessionRetentionPlanStatus.Complete);
        bytes.Add(Clock(), 0, false, false, 1).Should().BeFalse();
        bytes.Finish("source").Aggregate.Should().BeNull();
        new SessionRetentionPlanner(new(1, 3), Now).Finish("source", exhausted: true).Aggregate.Should().BeNull();
    }

    [Theory]
    [InlineData("id")]
    [InlineData("lifecycle-negative")]
    [InlineData("lifecycle-unknown")]
    [InlineData("purged-active")]
    [InlineData("offset-last")]
    [InlineData("offset-archive")]
    [InlineData("offset-delete")]
    [InlineData("future")]
    [InlineData("fraction-archive")]
    [InlineData("fraction-delete")]
    [InlineData("archive-negative")]
    [InlineData("contradictory")]
    [InlineData("delete-too-late")]
    public void InvalidCandidatesFailBeforeExclusionEvenAfterExhaustion(string reason)
    {
        var planner = new SessionRetentionPlanner(new(1, 3), Now);
        planner.AdmitCandidate(SessionRetentionPlanner.MaximumEncodedBytes);
        var clock = Clock() with { Perpetual = true };
        var lifecycle = 0;
        clock = reason switch
        {
            "id" => clock with { SessionId = default },
            "purged-active" => clock with { Purged = true },
            "offset-last" => clock with { LastMeaningfulActivity = clock.LastMeaningfulActivity.ToOffset(TimeSpan.FromHours(1)) },
            "offset-archive" => clock with { ArchiveDue = clock.ArchiveDue.ToOffset(TimeSpan.FromHours(1)) },
            "offset-delete" => clock with { DeleteDue = clock.DeleteDue.ToOffset(TimeSpan.FromHours(1)) },
            "future" => clock with { LastMeaningfulActivity = Now.AddTicks(1) },
            "fraction-archive" => clock with { ArchiveDue = clock.ArchiveDue.AddTicks(1) },
            "fraction-delete" => clock with { DeleteDue = clock.DeleteDue.AddTicks(1) },
            "archive-negative" => clock with { ArchiveDue = clock.LastMeaningfulActivity.AddDays(-1) },
            "contradictory" => clock with { DeleteDue = clock.ArchiveDue },
            "delete-too-late" => clock with { DeleteDue = clock.LastMeaningfulActivity.AddDays(366) },
            _ => clock,
        };
        if (reason == "lifecycle-negative") { lifecycle = -1; }
        if (reason == "lifecycle-unknown") { lifecycle = 3; }
        var action = () => planner.Add(clock, lifecycle, true, true, 1);
        action.Should().Throw<Exception>();
        planner.Finish("source").Aggregate!.Sessions.Should().Be(0);
    }

    [Fact]
    public void DeadlineOverflowAndInvalidProposalsAndArgumentsFailExplicitly()
    {
        var invalid = () => new SessionRetentionPlanner(default, Now);
        invalid.Should().Throw<ArgumentOutOfRangeException>();
        var planner = new SessionRetentionPlanner(new(1, 365), DateTimeOffset.MaxValue);
        var last = DateTimeOffset.MaxValue.AddDays(-30);
        var clock = new SessionRetentionState(new(Guid.NewGuid()), last, last.AddDays(1), last.AddDays(3), true, true);
        var overflow = () => planner.Add(clock, 2, true, true, 1);
        overflow.Should().Throw<InvalidDataException>();
        var negative = () => planner.AdmitCandidate(-1);
        negative.Should().Throw<ArgumentOutOfRangeException>();
        var missing = () => planner.Add(null!, 0, false, false, 1);
        missing.Should().Throw<ArgumentNullException>();
        var snapshot = () => planner.Finish(" ");
        snapshot.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void InvalidResultShapesAndCountsNeverValidateAsSuccess()
    {
        var zero = new SessionRetentionPlanAggregate(0, 0, 0, 0, 0, 0, 0, 0, 0);
        var valid = new SessionRetentionPlanResult(SessionRetentionPlanStatus.Complete, zero, "source");
        SessionRetentionPlanner.Validate(valid);
        SessionRetentionPlanner.Validate(new(SessionRetentionPlanStatus.Unavailable, null, null));
        SessionRetentionPlanner.Validate(new(SessionRetentionPlanStatus.Incomplete, null, "source"));
        var values = new List<SessionRetentionPlanResult>
        {
            valid with { Status = (SessionRetentionPlanStatus)99 },
            valid with { Status = SessionRetentionPlanStatus.Incomplete },
            valid with { Status = SessionRetentionPlanStatus.Unavailable },
            valid with { Snapshot = null }, valid with { Aggregate = null },
            new(SessionRetentionPlanStatus.Unavailable, null, "source"),
            new(SessionRetentionPlanStatus.Incomplete, null, ""),
        };
        foreach (var counts in new[]
        {
            zero with { Sessions = -1 }, zero with { Sessions = 4097 },
            zero with { NewlyArchiveDue = -1 }, zero with { NewlyDeleteDue = -1 },
            zero with { RecordedArchiveDue = -1 }, zero with { RecordedDeleteDue = -1 },
            zero with { Kept = -1 }, zero with { WorkHeld = -1 }, zero with { UnknownHold = -1 },
            zero with { Removed = -1 }, zero with { NewlyArchiveDue = 1 }, zero with { RecordedArchiveDue = 1 },
            zero with { Sessions = 1, Kept = 1, NewlyDeleteDue = 1 },
            zero with { Sessions = 1, Removed = 1, RecordedDeleteDue = 1 },
        })
        {
            values.Add(valid with { Aggregate = counts });
        }
        foreach (var value in values)
        {
            var action = () => SessionRetentionPlanner.Validate(value);
            action.Should().Throw<InvalidDataException>();
        }
        var missing = () => SessionRetentionPlanner.Validate(null!);
        missing.Should().Throw<ArgumentNullException>();
    }
}
