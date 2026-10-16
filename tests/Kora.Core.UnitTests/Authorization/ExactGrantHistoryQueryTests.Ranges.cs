using System.Globalization;

using AwesomeAssertions;

using Kora.Core.Auditing;
using Kora.Core.Authorization;
using Kora.Core.Diagnostics;

namespace Kora.Core.UnitTests.Authorization;

public sealed partial class ExactGrantHistoryQueryTests
{
    [Fact]
    public void RetentionAndInclusiveRangesComposeWithCommittedAuditOnlyAndBindEveryBound()
    {
        ExactGrantHistoryQuery.RetentionOptions.Should().Equal("Any", "Present", "ExpiredButPresent");
        var row = Audit(SecurityAuditOutcome.Denied, "exact-reason", SecurityAuditInitiator.System);
        var from = row.CommittedAt;
        var due = row.DueAt;
        var requested = row.Audit.WithOutcome(SecurityAuditOutcome.Requested, "requested-only");
        row = row with { Provenance = row.Provenance with { RequestedAudit = requested } };
        var queries = new[]
        {
            new ExactGrantHistoryQuery(retention: row.Retention),
            new ExactGrantHistoryQuery(committedFromUtc: from),
            new ExactGrantHistoryQuery(committedUntilUtc: from),
            new ExactGrantHistoryQuery(dueFromUtc: due),
            new ExactGrantHistoryQuery(dueUntilUtc: due),
            new ExactGrantHistoryQuery(SecurityAuditOutcome.Denied, "exact-reason", SecurityAuditInitiator.System,
                row.Retention, from, from, due, due),
        };
        var digests = new HashSet<string>(StringComparer.Ordinal) { ExactGrantHistoryQuery.Unfiltered.Digest };
        foreach (var query in queries)
        {
            query.Matches(row).Should().BeTrue();
            digests.Add(query.Digest).Should().BeTrue();
            ExactGrantHistoryQuery.FromInput(query.Outcome?.ToString() ?? "Any", query.ReasonCode ?? "",
                query.Initiator?.ToString() ?? "Any", query.Retention?.ToString() ?? "Any",
                Text(query.CommittedFromUtc), Text(query.CommittedUntilUtc), Text(query.DueFromUtc), Text(query.DueUntilUtc))
                .Should().Be(query);
        }
        var all = queries[^1];
        all.CommittedFromUtc.Should().Be(from);
        all.CommittedUntilUtc.Should().Be(from);
        all.DueFromUtc.Should().Be(due);
        all.DueUntilUtc.Should().Be(due);
        all.Retention.Should().Be(row.Retention);
        foreach (var status in new[] { EvidenceSegmentStatus.Present, EvidenceSegmentStatus.ExpiredButPresent })
        {
            var query = new ExactGrantHistoryQuery(retention: status);
            foreach (var actual in new[] { EvidenceSegmentStatus.Present, EvidenceSegmentStatus.ExpiredButPresent })
            { query.Matches(row with { Retention = actual }).Should().Be(status == actual); }
        }
        new ExactGrantHistoryQuery(SecurityAuditOutcome.Requested, "requested-only").Matches(row).Should().BeFalse();
        new ExactGrantHistoryQuery(committedFromUtc: from.AddTicks(1)).Matches(row).Should().BeFalse();
        new ExactGrantHistoryQuery(committedUntilUtc: from.AddTicks(-1)).Matches(row).Should().BeFalse();
        new ExactGrantHistoryQuery(dueFromUtc: due.AddTicks(1)).Matches(row).Should().BeFalse();
        new ExactGrantHistoryQuery(dueUntilUtc: due.AddTicks(-1)).Matches(row).Should().BeFalse();
        all.Matches(row with { CommittedAt = from.AddTicks(-1) }).Should().BeFalse();
        all.Matches(row with { CommittedAt = from.AddTicks(1) }).Should().BeFalse();
        all.Matches(row with { DueAt = due.AddTicks(-1) }).Should().BeFalse();
        all.Matches(row with { DueAt = due.AddTicks(1) }).Should().BeFalse();
        new ExactGrantHistoryQuery(committedFromUtc: DateTimeOffset.MinValue, committedUntilUtc: DateTimeOffset.MaxValue,
            dueFromUtc: DateTimeOffset.MinValue, dueUntilUtc: DateTimeOffset.MaxValue).Matches(row).Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("any")]
    [InlineData("present")]
    [InlineData(" Present")]
    [InlineData("Present ")]
    [InlineData("0")]
    [InlineData("MissingOrRemoved")]
    [InlineData("RetentionUnknown")]
    [InlineData("Unavailable")]
    public void NoncanonicalOrIncompleteRetentionLabelsFailClosed(string? input)
    {
        var parse = () => ExactGrantHistoryQuery.FromInput("Any", "", retention: input!);
        parse.Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(int.MaxValue)]
    public void IncompleteOrUndefinedRetentionCannotBeAdmitted(int input)
    {
        var create = () => new ExactGrantHistoryQuery(retention: (EvidenceSegmentStatus)input);
        create.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void AllBoundsRejectNoncanonicalTextOffsetsAndInversionsWithoutRepair()
    {
        var utc = DateTimeOffset.UnixEpoch;
        var invalidText = new string?[] { null, " ", "1970-01-01T00:00:00Z", "1970-01-01T00:00:00.0000000Z",
            "1970-01-01T00:00:00.0000000-00:00", "1970-01-01T01:00:00.0000000+01:00",
            "1970-01-01T00:00:00.000000+00:00", "1970-01-01T00:00:00.0000000+00:00 ", "bad" };
        for (var index = 0; index < 4; index++)
        {
            foreach (var text in invalidText)
            {
                var bounds = new[] { "", "", "", "" };
                bounds[index] = text!;
                var parse = () => ExactGrantHistoryQuery.FromInput("Any", "", "Any", "Any",
                    bounds[0], bounds[1], bounds[2], bounds[3]);
                if (text is null) { parse.Should().Throw<ArgumentNullException>(); }
                else { parse.Should().Throw<InvalidDataException>(); }
            }
            var typed = new DateTimeOffset?[4];
            typed[index] = utc.ToOffset(TimeSpan.FromHours(1));
            var create = () => new ExactGrantHistoryQuery(committedFromUtc: typed[0], committedUntilUtc: typed[1],
                dueFromUtc: typed[2], dueUntilUtc: typed[3]);
            create.Should().Throw<InvalidDataException>();
            typed[index] = utc;
            create().Should().NotBe(ExactGrantHistoryQuery.Unfiltered);
        }
        var committed = () => new ExactGrantHistoryQuery(committedFromUtc: utc.AddTicks(1), committedUntilUtc: utc);
        var due = () => new ExactGrantHistoryQuery(dueFromUtc: utc.AddTicks(1), dueUntilUtc: utc);
        committed.Should().Throw<InvalidDataException>();
        due.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void InvalidProjectedCommittedMetadataIsValidatedEvenWhenOutcomeWouldExcludeIt()
    {
        var row = Audit(SecurityAuditOutcome.Succeeded, null);
        var query = new ExactGrantHistoryQuery(SecurityAuditOutcome.Denied);
        foreach (var invalid in new[]
        {
            row with { Audit = null! },
            row with { CommittedAt = row.CommittedAt.ToOffset(TimeSpan.FromHours(1)) },
            row with { DueAt = row.DueAt.ToOffset(TimeSpan.FromHours(-1)) },
            row with { DueAt = row.CommittedAt.AddTicks(-1) },
            row with { Retention = EvidenceSegmentStatus.MissingOrRemoved },
            row with { Retention = EvidenceSegmentStatus.RetentionUnknown },
            row with { Retention = EvidenceSegmentStatus.Unavailable },
            row with { Retention = (EvidenceSegmentStatus)999 },
        })
        {
            var match = () => query.Matches(invalid);
            match.Should().Throw<InvalidDataException>();
        }
    }

    private static string Text(DateTimeOffset? value) => value?.ToString("O", CultureInfo.InvariantCulture) ?? "";
}
