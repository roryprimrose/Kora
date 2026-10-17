using AwesomeAssertions;

using Kora.Core.Diagnostics;

namespace Kora.Core.UnitTests.Diagnostics;

public sealed class EvidenceDuePredicateTests
{
    [Fact]
    public void InclusiveDueBoundsExcludeUnknownAndComposeWithRetainedMetadataIndependentlyOfCommitTime()
    {
        var edge = DateTimeOffset.UnixEpoch;
        var record = new EvidenceRecord(new(EvidenceSource.Log, new(Guid.NewGuid())), edge.AddDays(-30), null,
            EvidenceSegmentStatus.Present, null, null, null, null, null, null, null, null,
            new Dictionary<string, EvidenceValue>(StringComparer.Ordinal), null, HostOperationOutcome.Completed, [],
            EventName: "Event", ExceptionType: "System.IOException", AuditSequence: 2);
        DateTimeOffset?[] values = [null, DateTimeOffset.MinValue, edge.AddTicks(-1), edge, edge.AddTicks(1), DateTimeOffset.MaxValue];
        foreach (var due in values)
        foreach (var from in values)
        foreach (var until in values)
        foreach (var matchesEvent in new[] { false, true })
        {
            var query = new EvidenceQuery
            {
                FromDueUtc = from, UntilDueUtc = until, FromUtc = edge.AddDays(-31), UntilUtc = edge.AddDays(-29),
                EventName = matchesEvent ? "Event" : "Other", ExceptionType = "System.IOException",
                Outcome = HostOperationOutcome.Completed, FromAuditSequence = 2, UntilAuditSequence = 2,
                ContentAvailability = EvidenceContentAvailability.Present,
            };
            query.MatchesRetainedMetadata(record with { DueUtc = due }).Should().Be(
                matchesEvent && (from is null || due >= from) && (until is null || due <= until));
            new EvidenceQuery().MatchesRetainedMetadata(record with { DueUtc = due }).Should().BeTrue();
        }
    }
}
