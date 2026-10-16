using System.Diagnostics;

using AwesomeAssertions;

using Kora.Core.Diagnostics;

namespace Kora.Core.UnitTests.Diagnostics;

public sealed class EvidenceMetadataPredicateTests
{
    [Fact]
    public void ExactDiagnosticNamesAndInclusiveSequenceBoundsComposeWithEveryRetainedPredicate()
    {
        var record = new EvidenceRecord(new(EvidenceSource.Audit, new(Guid.NewGuid())), null, null,
            EvidenceSegmentStatus.Present, null, null, null, null, null, null, null, null,
            new Dictionary<string, EvidenceValue>(StringComparer.Ordinal), null, HostOperationOutcome.Completed, []);
        foreach (var eventName in new[] { null, "Event.Name", "event.name" })
        foreach (var exceptionType in new[] { null, "System.IOException", "system.ioexception" })
        foreach (var sequence in new long?[] { null, 1, 2, 3, long.MaxValue })
        foreach (var from in new long?[] { null, 1, 2, 3, long.MaxValue })
        foreach (var until in new long?[] { null, 1, 2, 3, long.MaxValue })
        foreach (var omitted in new[] { false, true })
        {
            var candidate = record with
            {
                EventName = eventName, ExceptionType = exceptionType, AuditSequence = sequence, ContentOmitted = omitted,
            };
            var query = new EvidenceQuery
            {
                EventName = "Event.Name", ExceptionType = "System.IOException",
                FromAuditSequence = from, UntilAuditSequence = until,
                Outcome = HostOperationOutcome.Completed, ContentAvailability = EvidenceContentAvailability.Present,
            };
            query.MatchesRetainedMetadata(candidate).Should().Be(
                string.Equals(eventName, "Event.Name", StringComparison.Ordinal)
                && string.Equals(exceptionType, "System.IOException", StringComparison.Ordinal)
                && (from is null || sequence >= from) && (until is null || sequence <= until) && !omitted);
            (query with { EventName = null, ExceptionType = null }).MatchesRetainedMetadata(candidate).Should().Be(
                (from is null || sequence >= from) && (until is null || sequence <= until) && !omitted);
            new EvidenceQuery().MatchesRetainedMetadata(candidate).Should().BeTrue();
        }
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("0123456789abcdef", true)]
    [InlineData("0000000000000001", true)]
    [InlineData("0000000000000000", false)]
    [InlineData("0123456789ABCDEF", false)]
    [InlineData("0123456789abcdeg", false)]
    [InlineData("0123456789abcde", false)]
    [InlineData("0123456789abcdef0", false)]
    [InlineData(" 0123456789abcdef", false)]
    [InlineData("0123456789abcdef ", false)]
    [InlineData("0123456789abcde\n", false)]
    [InlineData("0123456789abcde\ud800", false)]
    public void SingleW3CPolicyRejectsRepairAndNoncanonicalIds(string? value, bool valid) =>
        TraceSnapshot.IsValidW3CId(value, 16).Should().Be(valid);

    [Fact]
    public void EveryOutcomeNullParentAndContentDiscriminatorComposeExactly()
    {
        var parent = "0123456789abcdef";
        var record = new EvidenceRecord(new(EvidenceSource.Span, new(Guid.NewGuid())), null, null,
            EvidenceSegmentStatus.Present, null,
            new(new string('a', 32), new string('b', 16), parent, ActivityTraceFlags.Recorded,
                "Kora.Core", "1", "request", ActivityKind.Internal), null, null, null, null, null, null,
            new Dictionary<string, EvidenceValue>(StringComparer.Ordinal), null, null, []);
        HostOperationOutcome?[] outcomes = [null, .. Enum.GetValues<HostOperationOutcome>().Select(value => (HostOperationOutcome?)value)];
        foreach (var retained in outcomes)
        foreach (var queried in outcomes)
        foreach (var omitted in new[] { false, true })
        foreach (var content in Enum.GetValues<EvidenceContentAvailability>())
        foreach (var queriedParent in new[] { null, parent, new string('c', 16) })
        {
            var query = new EvidenceQuery { ParentSpanId = queriedParent, Outcome = queried, ContentAvailability = content };
            var matches = (queriedParent is null || string.Equals(queriedParent, parent, StringComparison.Ordinal))
                && (queried is null || queried == retained)
                && (content == EvidenceContentAvailability.Any || omitted == (content == EvidenceContentAvailability.Omitted));
            query.MatchesRetainedMetadata(record with { Outcome = retained, ContentOmitted = omitted }).Should().Be(matches);
        }
        new EvidenceQuery { ParentSpanId = parent }.MatchesRetainedMetadata(record with { Trace = null }).Should().BeFalse();
        new EvidenceQuery { ParentSpanId = parent }.MatchesRetainedMetadata(record with { Trace = record.Trace! with { ParentSpanId = null } }).Should().BeFalse();
        new EvidenceQuery().MatchesRetainedMetadata(record with { Trace = null }).Should().BeTrue();
        var invalid = () => new EvidenceQuery { ContentAvailability = (EvidenceContentAvailability)99 }.MatchesRetainedMetadata(record);
        invalid.Should().Throw<InvalidOperationException>();
    }
}
