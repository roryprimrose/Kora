using AwesomeAssertions;
using Kora.Core.Configuration;
using Kora.Core.Diagnostics;

namespace Kora.Core.UnitTests.Diagnostics;

public sealed class AuditRetentionImpactTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 15, 0, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(30)]
    [InlineData(365)]
    public void Valid_bounds_zero_and_newly_due_ranges_are_exact(int days)
    {
        var impact = new AuditRetentionImpact(new(days), Now);
        var empty = impact.Finish(AuditImpactSource.RequiredAuthority, "snapshot");
        empty.Aggregate.Should().Be(new AuditImpactAggregate(0, null, null, null, null));
        AuditRetentionImpact.Validate(empty, empty.Source, new(days), Now);
        impact.Add(Now, Now.AddDays(days), 1).Should().BeTrue();
        if (days == 365)
        {
            // A 365-day proposal cannot shorten any valid original deadline.
            return;
        }
    }

    [Fact]
    public void Out_of_order_equal_affected_already_due_and_not_due_records_do_not_disclose_content()
    {
        var impact = new AuditRetentionImpact(new(30), Now);
        foreach (var age in new[] { 40, 50, 35, 40, 35, 50 })
        {
            impact.Add(Now.AddDays(-age), Now.AddDays(90 - age), 12).Should().BeTrue();
        }
        impact.Add(Now.AddDays(-100), Now.AddDays(-10), 1).Should().BeTrue();
        impact.Add(Now.AddDays(-5), Now.AddDays(85), 1).Should().BeTrue();
        var result = impact.Finish(AuditImpactSource.DiagnosticProjection, "opaque");
        result.Aggregate.Should().Be(new AuditImpactAggregate(6, Now.AddDays(-50), Now.AddDays(-35),
            Now.AddDays(-20), Now.AddDays(-5)));
        AuditRetentionImpact.Validate(result, result.Source, new(30), Now);
        typeof(AuditImpactAggregate).GetProperties().Select(p => p.Name).Should().BeEquivalentTo(
            "AffectedCount", "OldestCommittedUtc", "NewestCommittedUtc", "EarliestDueUtc", "LatestDueUtc");
    }

    [Fact]
    public void Finite_rows_and_encoded_bytes_are_incomplete_without_partial_counts()
    {
        var rows = new AuditRetentionImpact(new(30), Now);
        for (var i = 0; i < AuditRetentionImpact.MaximumRows; i++) { rows.Add(Now, Now.AddDays(90), 0).Should().BeTrue(); }
        rows.Add(Now, Now.AddDays(90), 0).Should().BeFalse();
        rows.Add(Now, Now.AddDays(90), 0).Should().BeFalse();
        var result = rows.Finish(AuditImpactSource.RequiredAuthority, "opaque");
        result.Status.Should().Be(AuditImpactStatus.Incomplete);
        result.Aggregate.Should().BeNull();
        AuditRetentionImpact.Validate(result, result.Source, new(30), Now);
        var bytes = new AuditRetentionImpact(new(30), Now);
        bytes.Add(Now, Now.AddDays(90), AuditRetentionImpact.MaximumEncodedBytes).Should().BeTrue();
        bytes.Add(Now, Now.AddDays(90), 1).Should().BeFalse();
        bytes.Finish(AuditImpactSource.RequiredAuthority, "opaque").Aggregate.Should().BeNull();
    }

    [Fact]
    public void Unknown_invalid_and_future_committed_metadata_fails_closed()
    {
        var invalid = () => new AuditRetentionImpact(default, Now);
        invalid.Should().Throw<ArgumentOutOfRangeException>();
        var impact = new AuditRetentionImpact(new(30), Now);
        var negative = () => impact.Add(Now, Now.AddDays(90), -1);
        negative.Should().Throw<ArgumentOutOfRangeException>();
        foreach (var due in new[] { Now.AddDays(29), Now.AddDays(366), Now.AddDays(30).AddTicks(1) })
        {
            var action = () => impact.Add(Now, due, 1);
            action.Should().Throw<InvalidDataException>();
        }
        var future = () => impact.Add(Now.AddTicks(1), Now.AddDays(90).AddTicks(1), 1);
        future.Should().Throw<InvalidDataException>();
        var source = () => impact.Finish((AuditImpactSource)99, "opaque");
        source.Should().Throw<ArgumentOutOfRangeException>();
        var snapshot = () => impact.Finish(AuditImpactSource.RequiredAuthority, " ");
        snapshot.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Unavailable_unknown_partial_and_malformed_aggregates_never_validate_as_success()
    {
        var zero = new AuditImpactAggregate(0, null, null, null, null);
        var positive = new AuditImpactAggregate(1, Now.AddDays(-40), Now.AddDays(-35), Now.AddDays(-10), Now.AddDays(-5));
        var valid = new AuditImpactResult(AuditImpactSource.RequiredAuthority, AuditImpactStatus.Complete, positive, "stamp");
        AuditRetentionImpact.Validate(valid, valid.Source, new(30), Now);
        AuditRetentionImpact.Validate(new(valid.Source, AuditImpactStatus.Unavailable, null, null), valid.Source, new(30), Now);
        var bad = new List<AuditImpactResult>
        {
            valid with { Source = AuditImpactSource.DiagnosticProjection },
            valid with { Status = (AuditImpactStatus)99 },
            valid with { Status = AuditImpactStatus.Incomplete },
            new(valid.Source, AuditImpactStatus.Unavailable, null, "stamp"),
            valid with { Snapshot = null }, valid with { Aggregate = null },
            valid with { Aggregate = positive with { AffectedCount = -1 } },
            valid with { Aggregate = positive with { AffectedCount = 4097 } },
        };
        foreach (var value in new[] { zero with { OldestCommittedUtc = Now }, zero with { NewestCommittedUtc = Now },
            zero with { EarliestDueUtc = Now }, zero with { LatestDueUtc = Now },
            positive with { OldestCommittedUtc = null }, positive with { NewestCommittedUtc = null },
            positive with { OldestCommittedUtc = Now }, positive with { EarliestDueUtc = null },
            positive with { LatestDueUtc = null },
            positive with { OldestCommittedUtc = Now.AddDays(-5), NewestCommittedUtc = Now,
                EarliestDueUtc = Now.AddDays(25), LatestDueUtc = Now.AddDays(30) } })
        {
            bad.Add(valid with { Aggregate = value });
        }
        foreach (var result in bad)
        {
            var action = () => AuditRetentionImpact.Validate(result, valid.Source, new(30), Now);
            action.Should().Throw<InvalidDataException>();
        }
    }
}
