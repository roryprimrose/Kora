using System.Collections.Immutable;
using System.Text.Json;

using AwesomeAssertions;

using Kora.Core.Authorization;
using Kora.Core.Hosting;

namespace Kora.Core.UnitTests.Authorization;

public sealed class ExactGrantHistoryTests
{
    [Fact]
    public void CompleteSerializedPageIncludesAllBoundsAndExplicitEvidenceLimitationsWithoutInventingMissingRows()
    {
        var page = new ExactGrantHistoryPage(null, ImmutableArray<ExactGrantHistoryEntry>.Empty,
            null, DateTimeOffset.UnixEpoch, 0, new string('0', 64), 0, 0, false);
        using var json = JsonDocument.Parse(ExactGrantHistoryPage.Serialize(page));
        json.RootElement.GetProperty(nameof(ExactGrantHistoryPage.Limitations)).GetString().Should()
            .Contain("not proof an action executed").And.Contain("not missing use rows").And.Contain("not forensic");
        page.Records.Should().BeEmpty();
        page.Current.Should().BeNull();
        page.Next.Should().BeNull();
        page.AuditCeiling.Should().Be(0);
        page.AuditCeilingDigest.Should().HaveLength(64);
        page.ScannedThrough.Should().Be(0);
        page.ScannedRows.Should().Be(0);
        page.ObservedAt.Should().Be(DateTimeOffset.UnixEpoch);
        page.ScanLimitReached.Should().BeFalse();
        ExactGrantHistoryPage.MaximumRecords.Should().Be(50);
        ExactGrantHistoryPage.MaximumBytes.Should().Be(65536);
        ExactGrantHistoryPage.MaximumScannedRows.Should().Be(4096);
        ExactGrantHistoryPage.RequestSeconds.Should().Be(5);
        ExactGrantHistoryPage.SnapshotMinutes.Should().Be(15);
    }
}
