using System.Diagnostics;

using AwesomeAssertions;

using Kora.Application.Diagnostics;
using Kora.Core.Diagnostics;

namespace Kora.Application.UnitTests.Diagnostics;

public sealed partial class DurableEvidenceQueryTests
{
    [Fact]
    public async Task TraceMetadataIsSourceCompatibleAndEveryPredicateBindsTheCursor()
    {
        using var listener = Listen();
        using var host = Root();
        var fixture = new Fixture();
        fixture.Reader.Batch = Batch(2);
        var query = new EvidenceQuery
        {
            Limit = 1, TraceSource = "Kora.Core",
            TraceSourceVersionAvailability = TraceSourceVersionAvailability.Exact,
            TraceSourceVersion = "1", ActivityName = "request",
            ActivityKind = ActivityKind.Internal, TraceFlags = ActivityTraceFlags.None,
        };
        var cursor = (await fixture.Service.QueryAsync(query, null, TestContext.Current.CancellationToken)).Cursor;
        EvidenceQuery[] changed =
        [
            query with { TraceSource = null }, query with { TraceSource = "kora.core" },
            query with { TraceSourceVersion = "2" },
            query with { TraceSourceVersion = null, TraceSourceVersionAvailability = TraceSourceVersionAvailability.Any },
            query with { TraceSourceVersion = null, TraceSourceVersionAvailability = TraceSourceVersionAvailability.Absent },
            query with { TraceSourceVersion = null, TraceSourceVersionAvailability = TraceSourceVersionAvailability.Present },
            query with { ActivityName = null }, query with { ActivityName = "Request" },
            query with { ActivityKind = null }, query with { ActivityKind = ActivityKind.Client },
            query with { TraceFlags = null }, query with { TraceFlags = ActivityTraceFlags.Recorded },
        ];
        foreach (var value in changed)
        {
            var read = () => fixture.Service.QueryAsync(value, cursor, TestContext.Current.CancellationToken).AsTask();
            await read.Should().ThrowAsync<InvalidDataException>();
        }
        await fixture.Service.QueryAsync(query, cursor, TestContext.Current.CancellationToken);
        foreach (var source in Enum.GetValues<EvidenceSource>())
        foreach (var availability in Enum.GetValues<TraceSourceVersionAvailability>())
        foreach (var kind in Enum.GetValues<ActivityKind>())
        foreach (var flags in Enum.GetValues<ActivityTraceFlags>())
        {
            DurableEvidenceQuery.Validate(query with
            {
                Source = source, TraceSourceVersionAvailability = availability,
                TraceSourceVersion = availability == TraceSourceVersionAvailability.Exact ? new string('v', 128) : null,
                TraceSource = new string('s', 128), ActivityName = new string('n', 128),
                ActivityKind = kind, TraceFlags = flags,
            });
        }
    }

    [Fact]
    public async Task TracePredicateAdmissionFailsBeforeReadingForAllInvalidTextAndTypedStates()
    {
        using var listener = Listen();
        using var host = Root();
        var fixture = new Fixture();
        foreach (var text in new[] { "", " ", " x", "x ", "x\n", "x\0y", "\ud800", new string('x', 129) })
        {
            EvidenceQuery[] queries =
            [
                new() { TraceSource = text }, new() { ActivityName = text },
                new() { TraceSourceVersionAvailability = TraceSourceVersionAvailability.Exact, TraceSourceVersion = text },
            ];
            foreach (var query in queries)
            {
                var read = () => fixture.Service.QueryAsync(query, null, TestContext.Current.CancellationToken).AsTask();
                await read.Should().ThrowAsync<ArgumentException>();
            }
        }
        EvidenceQuery[] invalid =
        [
            new() { ActivityKind = (ActivityKind)99 }, new() { TraceFlags = (ActivityTraceFlags)2 },
            new() { TraceSourceVersionAvailability = (TraceSourceVersionAvailability)99 },
            new() { TraceSourceVersionAvailability = TraceSourceVersionAvailability.Exact },
            new() { TraceSourceVersion = "1" },
            new() { TraceSourceVersionAvailability = TraceSourceVersionAvailability.Absent, TraceSourceVersion = "1" },
            new() { TraceSourceVersionAvailability = TraceSourceVersionAvailability.Present, TraceSourceVersion = "1" },
        ];
        foreach (var query in invalid)
        {
            var read = () => fixture.Service.QueryAsync(query, null, TestContext.Current.CancellationToken).AsTask();
            await read.Should().ThrowAsync<ArgumentException>();
        }
        fixture.Reader.Calls.Should().Be(0);
    }
}
