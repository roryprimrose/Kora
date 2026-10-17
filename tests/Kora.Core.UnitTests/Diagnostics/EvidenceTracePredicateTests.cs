using System.Diagnostics;
using System.Text.Json;

using AwesomeAssertions;

using Kora.Core.Diagnostics;

namespace Kora.Core.UnitTests.Diagnostics;

public sealed class EvidenceTracePredicateTests
{
    private static readonly TraceSnapshot Trace = new(new string('a', 32), new string('b', 16), null,
        ActivityTraceFlags.None, "Kora.Core", null, "request", ActivityKind.Internal);

    private static EvidenceRecord Record(TraceSnapshot? trace) => new(
        new(EvidenceSource.Log, new(Guid.NewGuid())), null, null, EvidenceSegmentStatus.Present,
        null, trace, null, null, null, null, null, null,
        new Dictionary<string, EvidenceValue>(StringComparer.Ordinal), null, null, []);

    [Fact]
    public void TracePredicatesComposeExactlyAndNeverInventAnAbsentTrace()
    {
        foreach (var version in new[] { null, "1", "V" })
        foreach (var source in new[] { "Kora.Core", "Kora.Application", "Kora.Windows", "Kora.Desktop" })
        foreach (var name in new[] { "request", "Request" })
        foreach (var kind in Enum.GetValues<ActivityKind>())
        foreach (var flags in Enum.GetValues<ActivityTraceFlags>())
        foreach (var availability in Enum.GetValues<TraceSourceVersionAvailability>())
        foreach (var parent in new[] { null, new string('c', 16) })
        foreach (var parentKind in Enum.GetValues<TraceParentKind>())
        {
            var record = Record(Trace with { SourceVersion = version, Source = source, Name = name, Kind = kind, Flags = flags, ParentSpanId = parent });
            var query = new EvidenceQuery
            {
                TraceSource = "Kora.Core", ActivityName = "request", ActivityKind = ActivityKind.Internal,
                TraceFlags = ActivityTraceFlags.None, TraceSourceVersionAvailability = availability,
                TraceSourceVersion = availability == TraceSourceVersionAvailability.Exact ? "V" : null,
                TraceParentKind = parentKind,
            };
            var versionMatches = availability switch
            {
                TraceSourceVersionAvailability.Any => true,
                TraceSourceVersionAvailability.Absent => version is null,
                TraceSourceVersionAvailability.Present => version is not null,
                _ => string.Equals(version, "V", StringComparison.Ordinal),
            };
            query.MatchesRetainedMetadata(record).Should().Be(
                string.Equals(source, "Kora.Core", StringComparison.Ordinal)
                && string.Equals(name, "request", StringComparison.Ordinal) && kind == ActivityKind.Internal
                && flags == ActivityTraceFlags.None && versionMatches
                && (parentKind == TraceParentKind.Any || (parentKind == TraceParentKind.ObservedRoot) == (parent is null)));
            new EvidenceQuery().MatchesRetainedMetadata(record).Should().BeTrue();
        }
        EvidenceQuery[] predicates =
        [
            new() { TraceSource = "Kora.Core" }, new() { ActivityName = "request" },
            new() { ActivityKind = ActivityKind.Internal }, new() { TraceFlags = ActivityTraceFlags.None },
            new() { TraceSourceVersionAvailability = TraceSourceVersionAvailability.Absent },
            new() { TraceSourceVersionAvailability = TraceSourceVersionAvailability.Present },
            new() { TraceSourceVersionAvailability = TraceSourceVersionAvailability.Exact, TraceSourceVersion = "1" },
            new() { TraceParentKind = TraceParentKind.ObservedRoot },
            new() { TraceParentKind = TraceParentKind.ObservedChild },
        ];
        foreach (var query in predicates) { query.MatchesRetainedMetadata(Record(null)).Should().BeFalse(); }
        new EvidenceQuery().MatchesRetainedMetadata(Record(null)).Should().BeTrue();
        var invalid = () => new EvidenceQuery
        {
            TraceSourceVersionAvailability = (TraceSourceVersionAvailability)99,
        }.MatchesRetainedMetadata(Record(Trace));
        invalid.Should().Throw<InvalidOperationException>();
        var invalidParentKind = () => new EvidenceQuery { TraceParentKind = (TraceParentKind)99 }
            .MatchesRetainedMetadata(Record(null));
        invalidParentKind.Should().Throw<InvalidOperationException>();
        new EvidenceQuery { TraceSourceVersionAvailability = TraceSourceVersionAvailability.Exact, TraceSourceVersion = "1" }
            .MatchesRetainedMetadata(Record(Trace with { SourceVersion = "1" })).Should().BeTrue();
    }

    public static TheoryData<string?> InvalidText => new()
    {
        (string?)null, "", " ", " x", "x ", "\u00a0x", "x\u00a0", "x\n", "x\0y", "\ud800", new string('x', 129),
    };

    [Theory]
    [MemberData(nameof(InvalidText))]
    public void ExactPlainMetadataRejectsMalformedText(string? text) =>
        TraceSnapshot.IsValidMetadataText(text).Should().BeFalse();

    [Fact]
    public void CompleteTraceIsValidatedEvenWhenAnEarlierPredicateWouldExcludeIt()
    {
        TraceSnapshot[] invalid =
        [
            Trace with { TraceId = null! }, Trace with { TraceId = new string('0', 32) },
            Trace with { SpanId = "invalid" }, Trace with { ParentSpanId = "INVALID" },
            Trace with { Kind = (ActivityKind)99 }, Trace with { Flags = (ActivityTraceFlags)2 },
            Trace with { Source = null! }, Trace with { Source = "Other" },
            Trace with { SourceVersion = "" }, Trace with { SourceVersion = " 1" },
            Trace with { SourceVersion = "\ud800" }, Trace with { Name = null! },
            Trace with { Name = " request" }, Trace with { Name = new string('n', 129) },
        ];
        foreach (var trace in invalid)
        {
            foreach (var parentKind in Enum.GetValues<TraceParentKind>())
            {
                var match = () => new EvidenceQuery { TraceSource = "nonmatching", TraceParentKind = parentKind }
                    .MatchesRetainedMetadata(Record(trace));
                match.Should().Throw<InvalidDataException>();
            }
        }

        (Trace with { ParentSpanId = new string('c', 16), SourceVersion = new string('v', 128), Name = new string('n', 128) }).Validate();
        TraceSnapshot.IsValidMetadataText("Plain Unicode \u00e9 / metadata").Should().BeTrue();
    }

    [Theory]
    [InlineData(TraceParentKind.Any, true, true)]
    [InlineData(TraceParentKind.ObservedRoot, true, false)]
    [InlineData(TraceParentKind.ObservedChild, false, true)]
    public void ParentKindAndExactParentComposeWithoutRepairOrInference(TraceParentKind kind, bool root, bool child)
    {
        var parent = new string('c', 16);
        var query = new EvidenceQuery { TraceParentKind = kind };
        query.MatchesRetainedMetadata(Record(Trace)).Should().Be(root);
        query.MatchesRetainedMetadata(Record(Trace with { ParentSpanId = parent })).Should().Be(child);
        (query with { ParentSpanId = parent }).MatchesRetainedMetadata(Record(Trace)).Should().BeFalse();
        (query with { ParentSpanId = parent }).MatchesRetainedMetadata(Record(Trace with { ParentSpanId = parent })).Should().Be(child);
        (query with { ParentSpanId = new string('d', 16) }).MatchesRetainedMetadata(Record(Trace with { ParentSpanId = parent })).Should().BeFalse();
    }

    [Theory]
    [InlineData("TraceId")]
    [InlineData("SpanId")]
    [InlineData("ParentSpanId")]
    [InlineData("Flags")]
    [InlineData("Source")]
    [InlineData("SourceVersion")]
    [InlineData("Name")]
    [InlineData("Kind")]
    public void MissingSerializedMetadataIsUnknownRatherThanAnAbsentVersionOrDefaultEnum(string field)
    {
        var json = JsonSerializer.SerializeToNode(Trace)!;
        json.AsObject().Remove(field);
        var decode = () => JsonSerializer.Deserialize<TraceSnapshot>(json);
        decode.Should().Throw<JsonException>();
        JsonSerializer.Deserialize<TraceSnapshot>(JsonSerializer.Serialize(Trace)).Should().Be(Trace);
    }
}
