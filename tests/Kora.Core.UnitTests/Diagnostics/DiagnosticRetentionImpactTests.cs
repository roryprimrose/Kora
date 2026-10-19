using AwesomeAssertions;
using Kora.Core.Configuration;
using Kora.Core.Diagnostics;

namespace Kora.Core.UnitTests.Diagnostics;

public sealed class DiagnosticRetentionImpactTests
{
    private static readonly DateTimeOffset Cutoff = new(2026, 10, 19, 0, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(1)]
    [InlineData(365)]
    public void Domain_extremes_exact_cutoff_and_original_deadline_exclusion(int proposed)
    {
        var committed = Cutoff.AddDays(-proposed);
        var impact = new DiagnosticRetentionImpact(new(proposed), Cutoff);
        impact.Admit(0).Should().BeTrue();
        if (proposed == 1)
        {
            impact.Add(committed, committed.AddDays(2));
            impact.Finish(DiagnosticImpactSource.OrdinaryLogs).Aggregate.Should()
                .Be(new DiagnosticImpactAggregate(1, committed, committed, Cutoff, Cutoff));
        }
        else
        {
            impact.Add(committed, committed.AddDays(365));
            impact.Finish(DiagnosticImpactSource.Spans).Aggregate.Should()
                .Be(new DiagnosticImpactAggregate(0, null, null, null, null));
        }
        DiagnosticRetentionDays.IsValidDeadline(committed, committed.AddDays(proposed)).Should().BeTrue();
    }

    [Fact]
    public void Only_newly_due_counts_not_observed_age_already_due_or_not_yet_due_and_ranges_are_UTC()
    {
        var impact = new DiagnosticRetentionImpact(new(1), Cutoff);
        impact.Add(Cutoff.AddDays(-30), Cutoff); // Already due.
        impact.Add(Cutoff.AddDays(-1).AddTicks(1), Cutoff.AddDays(29).AddTicks(1));
        impact.Add(Cutoff.AddDays(-5), Cutoff.AddDays(25));
        impact.Add(Cutoff.AddDays(-2).ToOffset(TimeSpan.FromHours(11)), Cutoff.AddDays(28));
        impact.Add(Cutoff.AddDays(-7), Cutoff.AddDays(23));
        impact.Add(Cutoff.AddDays(-3), Cutoff.AddDays(27));
        var result = impact.Finish(DiagnosticImpactSource.OrdinaryLogs);
        result.Aggregate.Should().Be(new DiagnosticImpactAggregate(4, Cutoff.AddDays(-7), Cutoff.AddDays(-2),
            Cutoff.AddDays(-6), Cutoff.AddDays(-1)));
        DiagnosticRetentionImpact.Validate(Snapshot(result), new(1), Cutoff);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(366)]
    [InlineData(-1)]
    public void Invalid_integral_original_deadlines_are_not_ignored_even_for_irrelevant_rows(int days)
    {
        var committed = Cutoff.AddDays(-400);
        var impact = new DiagnosticRetentionImpact(new(1), Cutoff);
        impact.Invoking(value => value.Add(committed, committed.AddDays(days))).Should().Throw<InvalidDataException>();
        DiagnosticRetentionDays.IsValidDeadline(committed, committed.AddDays(days)).Should().BeFalse();
    }

    [Fact]
    public void Fractional_future_and_default_values_fail_closed_without_overflow()
    {
        var impact = new DiagnosticRetentionImpact(new(365), DateTimeOffset.MaxValue);
        impact.Add(DateTimeOffset.MaxValue.AddDays(-1), DateTimeOffset.MaxValue);
        impact.Finish(DiagnosticImpactSource.OwnedLinks).Aggregate!.NewlyDueCount.Should().Be(0);
        impact.Invoking(value => value.Add(Cutoff, Cutoff.AddDays(1).AddTicks(1))).Should().Throw<InvalidDataException>();
        var past = new DiagnosticRetentionImpact(new(1), Cutoff);
        past.Invoking(value => value.Add(Cutoff.AddTicks(1), Cutoff.AddDays(1).AddTicks(1)))
            .Should().Throw<InvalidDataException>();
        var invalid = () => new DiagnosticRetentionImpact(default, Cutoff);
        invalid.Should().Throw<ArgumentOutOfRangeException>();
        impact.Invoking(value => value.Admit(-1)).Should().Throw<ArgumentOutOfRangeException>();
        impact.Invoking(value => value.Finish((DiagnosticImpactSource)99)).Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Exact_row_and_byte_thresholds_are_complete_but_one_overflow_discards_all_partial_counts()
    {
        var rows = new DiagnosticRetentionImpact(new(1), Cutoff);
        for (var index = 0; index < DiagnosticRetentionImpact.MaximumRows; index++)
        {
            rows.Admit(1).Should().BeTrue();
            rows.Add(Cutoff.AddDays(-2), Cutoff.AddDays(28));
        }
        rows.Finish(DiagnosticImpactSource.Spans).Aggregate!.NewlyDueCount.Should().Be(DiagnosticRetentionImpact.MaximumRows);
        rows.Admit(0).Should().BeFalse();
        rows.Admit(0).Should().BeFalse();
        rows.Finish(DiagnosticImpactSource.Spans).Should()
            .Be(new DiagnosticImpactResult(DiagnosticImpactSource.Spans, DiagnosticImpactStatus.Incomplete, null));
        var bytes = new DiagnosticRetentionImpact(new(1), Cutoff);
        bytes.Admit(DiagnosticRetentionImpact.MaximumEncodedBytes).Should().BeTrue();
        bytes.Finish(DiagnosticImpactSource.OwnedLinks).Status.Should().Be(DiagnosticImpactStatus.Complete);
        bytes.Admit(1).Should().BeFalse();
        bytes.Finish(DiagnosticImpactSource.OwnedLinks).Aggregate.Should().BeNull();
        var oversized = new DiagnosticRetentionImpact(new(1), Cutoff);
        oversized.Admit(int.MaxValue).Should().BeFalse();
    }

    [Theory]
    [InlineData("source")]
    [InlineData("status")]
    [InlineData("partial")]
    [InlineData("missing")]
    [InlineData("negative")]
    [InlineData("excess")]
    [InlineData("zero-oldest")]
    [InlineData("zero-newest")]
    [InlineData("zero-first")]
    [InlineData("zero-last")]
    [InlineData("no-oldest")]
    [InlineData("no-newest")]
    [InlineData("no-first")]
    [InlineData("no-last")]
    [InlineData("reversed")]
    [InlineData("future")]
    [InlineData("not-due")]
    [InlineData("first")]
    [InlineData("last")]
    [InlineData("stamp")]
    [InlineData("empty-stamp")]
    [InlineData("whitespace-stamp")]
    [InlineData("links-status")]
    [InlineData("links-without-owner")]
    [InlineData("links-before-owner")]
    [InlineData("links-after-owner")]
    public void Invalid_reader_receipts_never_become_counts(string reason)
    {
        var zero = new DiagnosticImpactAggregate(0, null, null, null, null);
        var committed = Cutoff.AddDays(-2);
        var one = new DiagnosticImpactAggregate(1, committed, committed, committed.AddDays(1), committed.AddDays(1));
        var valid = new DiagnosticImpactResult(DiagnosticImpactSource.OrdinaryLogs, DiagnosticImpactStatus.Complete, zero);
        var result = reason switch
        {
            "source" => valid with { Source = DiagnosticImpactSource.Spans },
            "status" => valid with { Status = (DiagnosticImpactStatus)99 },
            "partial" => valid with { Status = DiagnosticImpactStatus.Incomplete },
            "missing" => valid with { Aggregate = null },
            "negative" => valid with { Aggregate = zero with { NewlyDueCount = -1 } },
            "excess" => valid with { Aggregate = zero with { NewlyDueCount = DiagnosticRetentionImpact.MaximumRows + 1 } },
            "zero-oldest" => valid with { Aggregate = zero with { OldestCommittedUtc = committed } },
            "zero-newest" => valid with { Aggregate = zero with { NewestCommittedUtc = committed } },
            "zero-first" => valid with { Aggregate = zero with { EarliestProposedDueUtc = committed } },
            "zero-last" => valid with { Aggregate = zero with { LatestProposedDueUtc = committed } },
            "no-oldest" => valid with { Aggregate = one with { OldestCommittedUtc = null } },
            "no-newest" => valid with { Aggregate = one with { NewestCommittedUtc = null } },
            "no-first" => valid with { Aggregate = one with { EarliestProposedDueUtc = null } },
            "no-last" => valid with { Aggregate = one with { LatestProposedDueUtc = null } },
            "reversed" => valid with { Aggregate = one with { OldestCommittedUtc = Cutoff } },
            "future" => valid with { Aggregate = one with { NewestCommittedUtc = Cutoff.AddTicks(1) } },
            "not-due" => valid with { Aggregate = one with { NewestCommittedUtc = Cutoff } },
            "first" => valid with { Aggregate = one with { EarliestProposedDueUtc = committed } },
            "last" => valid with { Aggregate = one with { LatestProposedDueUtc = committed } },
            _ => valid,
        };
        var snapshot = Snapshot(result);
        snapshot = reason switch
        {
            "stamp" => snapshot with { Snapshot = null },
            "empty-stamp" => snapshot with { Snapshot = string.Empty },
            "whitespace-stamp" => snapshot with { Snapshot = " " },
            "links-status" => snapshot with { Links = snapshot.Links with { Status = DiagnosticImpactStatus.Unavailable } },
            "links-without-owner" => snapshot with { Links = snapshot.Links with { Aggregate = one } },
            "links-before-owner" => snapshot with
            {
                Spans = snapshot.Spans with { Aggregate = one },
                Links = snapshot.Links with { Aggregate = one with
                {
                    OldestCommittedUtc = committed.AddDays(-1), EarliestProposedDueUtc = committed,
                } },
            },
            "links-after-owner" => snapshot with
            {
                Spans = snapshot.Spans with { Aggregate = one },
                Links = snapshot.Links with { Aggregate = one with
                {
                    NewestCommittedUtc = committed.AddDays(1), LatestProposedDueUtc = Cutoff,
                } },
            },
            _ => snapshot,
        };
        var action = () => DiagnosticRetentionImpact.Validate(snapshot, new(1), Cutoff);
        action.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void Separate_source_qualification_and_zero_ranges_are_truthful_and_policy_generation_detects_ABA()
    {
        DiagnosticRetentionImpact.Validate(DiagnosticImpactSnapshot.Unavailable, new(1), Cutoff);
        var complete = Snapshot(new(DiagnosticImpactSource.OrdinaryLogs, DiagnosticImpactStatus.Complete,
            new(0, null, null, null, null)));
        DiagnosticRetentionImpact.Validate(complete, new(1), Cutoff);
        DiagnosticRetentionImpact.Validate(complete with { Logs = complete.Logs with
        {
            Status = DiagnosticImpactStatus.Incomplete, Aggregate = null,
        } }, new(1), Cutoff);
        var policy = new DiagnosticRetentionPolicy();
        policy.Revision.Should().Be(0);
        policy.Activate(new(30));
        var revision = policy.Revision;
        policy.IsCurrent(new(30), revision).Should().BeTrue();
        policy.IsCurrent(new(1), revision).Should().BeFalse();
        policy.HoldUnavailable();
        policy.Activate(new(30));
        policy.Revision.Should().Be(revision + 2);
        policy.IsCurrent(new(30), revision).Should().BeFalse();
    }

    [Fact]
    public void Positive_owned_links_require_positive_qualified_owners_and_complete_snapshot_identity()
    {
        var committed = Cutoff.AddDays(-2);
        var affected = new DiagnosticImpactAggregate(1, committed, committed, committed.AddDays(1), committed.AddDays(1));
        var snapshot = new DiagnosticImpactSnapshot(
            new(DiagnosticImpactSource.OrdinaryLogs, DiagnosticImpactStatus.Unavailable, null),
            new(DiagnosticImpactSource.Spans, DiagnosticImpactStatus.Complete, affected),
            new(DiagnosticImpactSource.OwnedLinks, DiagnosticImpactStatus.Complete, affected), "qualified");
        DiagnosticRetentionImpact.Validate(snapshot, new(1), Cutoff);
        var missingIdentity = () => DiagnosticRetentionImpact.Validate(snapshot with { Snapshot = null }, new(1), Cutoff);
        missingIdentity.Should().Throw<InvalidDataException>();
        DiagnosticRetentionImpact.Validate(snapshot with
        {
            Spans = new(DiagnosticImpactSource.Spans, DiagnosticImpactStatus.Incomplete, null),
            Links = new(DiagnosticImpactSource.OwnedLinks, DiagnosticImpactStatus.Incomplete, null),
        }, new(1), Cutoff);
    }

    private static DiagnosticImpactSnapshot Snapshot(DiagnosticImpactResult logs) => new(logs,
        new(DiagnosticImpactSource.Spans, DiagnosticImpactStatus.Complete, new(0, null, null, null, null)),
        new(DiagnosticImpactSource.OwnedLinks, DiagnosticImpactStatus.Complete, new(0, null, null, null, null)), "private-stamp");
}
