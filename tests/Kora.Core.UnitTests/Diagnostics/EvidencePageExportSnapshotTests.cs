using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Kora.Core.Diagnostics;

namespace Kora.Core.UnitTests.Diagnostics;

public sealed class EvidencePageExportSnapshotTests
{
    [Fact]
    public void FreezesWholePageAndOmitsOnlyLiveCursor()
    {
        var unavailable = new List<EvidenceSource> { EvidenceSource.Session, EvidenceSource.Conversation };
        var page = new EvidencePage(EvidencePageStatus.Partial, [], "live-private-cursor", unavailable, EvidencePage.StorageDisclosure);
        var snapshot = new EvidencePageExportSnapshot(page);
        var expected = EvidencePageExportSnapshot.Serialize(page with { Cursor = null });
        snapshot.CopyBytes().Should().Equal(expected);
        snapshot.Text.Should().Be(Encoding.UTF8.GetString(expected)).And.NotContain("live-private-cursor");
        using var json = JsonDocument.Parse(snapshot.Text);
        json.RootElement.GetProperty("Cursor").ValueKind.Should().Be(JsonValueKind.Null);
        json.RootElement.GetProperty("UnavailableSources").GetArrayLength().Should().Be(2);
        unavailable.Clear();
        var copy = snapshot.CopyBytes();
        copy[0] = 0;
        snapshot.CopyBytes().Should().Equal(expected);
        EvidencePageExportSnapshot.ExclusionDisclosure.Should().Contain("cursor").And.Contain("provenance");
    }

    [Fact]
    public void EnforcesExactWholePageByteThresholdIncludingExcludedCursor()
    {
        var page = new EvidencePage(EvidencePageStatus.Available, [], null, [], "");
        var overhead = EvidencePageExportSnapshot.Serialize(page).Length;
        var maximum = page with { Disclosure = new string('x', EvidencePage.MaximumBytes - overhead) };
        EvidencePageExportSnapshot.Serialize(maximum).Length.Should().Be(EvidencePage.MaximumBytes);
        new EvidencePageExportSnapshot(maximum).CopyBytes().Length.Should().Be(EvidencePage.MaximumBytes);
        var tooLarge = () => new EvidencePageExportSnapshot(maximum with { Disclosure = maximum.Disclosure + "x" });
        tooLarge.Should().Throw<InvalidDataException>();
        var excludedOverflow = () => new EvidencePageExportSnapshot(maximum with { Cursor = new string('x', 10) });
        excludedOverflow.Should().Throw<InvalidDataException>();
        var missing = () => new EvidencePageExportSnapshot(null!);
        missing.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void RejectsTooManyRecordsWithoutTruncation()
    {
        var page = new EvidencePage(EvidencePageStatus.Available,
            Enumerable.Repeat<EvidenceRecord>(null!, EvidencePage.MaximumRecords + 1).ToArray(), null, [], "");
        var create = () => new EvidencePageExportSnapshot(page);
        create.Should().Throw<InvalidDataException>();
    }
}
