using System.Text.Json;

using AwesomeAssertions;

using Kora.Core.Storage;

namespace Kora.Core.UnitTests.Storage;

public sealed class SessionAttachmentPageOrderingTests
{
    [Fact]
    public void OnlySupportedMetadataOrdersAreOffered()
    {
        Enum.GetValues<SessionAttachmentPageOrder>().Should().Equal(SessionAttachmentPageOrder.Source,
            SessionAttachmentPageOrder.RetainedBytesAscending, SessionAttachmentPageOrder.RetainedBytesDescending);
    }

    [Fact]
    public void DefaultPreservesExactSourceOrderAndAllObservationMetadata()
    {
        var page = Page();
        SessionAttachmentPageOrdering.Apply(page).Should().Equal(page.Records);
        page.Next.Should().Be("YQ==." + new string('a', 64));
        page.ScannedRows.Should().Be(5);
        page.ScanLimitReached.Should().BeFalse();
        page.AuditScannedRows.Should().Be(4096);
        JsonSerializer.SerializeToUtf8Bytes(page).Length.Should().BeLessThan(SessionAttachmentPage.MaximumBytes);
    }

    [Theory]
    [InlineData(SessionAttachmentPageOrder.RetainedBytesAscending, "31245")]
    [InlineData(SessionAttachmentPageOrder.RetainedBytesDescending, "12345")]
    public void OrdersOnlyAdmittedProjectionWithCanonicalSessionTiesAndUnknownLast(
        SessionAttachmentPageOrder order, string expected)
    {
        var page = Page();
        var original = JsonSerializer.Serialize(page);
        var rows = SessionAttachmentPageOrdering.Apply(page, order);
        string.Concat(rows.Select(row => row.Session.Value.ToString("D")[0])).Should().Be(expected);
        rows.Single(row => row.Session.Value.ToString("D")[0] == '4').RetainedStoredBytes.Should().BeNull();
        rows[^1].RetainedStoredBytes.Should().BeNull();
        rows[^1].RetainedDigest.Should().BeNull();
        rows[^1].RetainedObservationSource.Should().BeNull();
        JsonSerializer.Serialize(page).Should().Be(original);
    }

    [Theory]
    [InlineData("row")]
    [InlineData("duplicate")]
    [InlineData("budget")]
    [InlineData("authority")]
    [InlineData("query")]
    public void ValidatesCompletePageBeforeAnyOrderingIncludingSource(string boundary)
    {
        var page = Page();
        var invalid = boundary switch
        {
            "row" => page with { Records = [page.Records[0], page.Records[1] with { StoredBytes = -1 }] },
            "duplicate" => page with { Records = [page.Records[0], page.Records[0]] },
            "budget" => page with { Records = [.. Enumerable.Repeat(page.Records[0], 17)] },
            "authority" => page with { AuthorityDigest = new string('0', 64) },
            _ => page with { Query = new(false, Guid.NewGuid().ToString("D")) },
        };
        foreach (var order in Enum.GetValues<SessionAttachmentPageOrder>())
        {
            ((Action)(() => SessionAttachmentPageOrdering.Apply(invalid, order))).Should().Throw<InvalidDataException>();
        }
    }

    [Fact]
    public void RejectsNullOrUnknownOrderAndAcceptsEmptyAuthorityContinuation()
    {
        ((Action)(() => SessionAttachmentPageOrdering.Apply(null!))).Should().Throw<ArgumentNullException>();
        ((Action)(() => SessionAttachmentPageOrdering.Apply(Page(), (SessionAttachmentPageOrder)99)))
            .Should().Throw<InvalidDataException>();
        var pending = Page() with { Records = [], ScannedRows = 0, ScanLimitReached = false, AuthorityValidationPending = true };
        foreach (var order in Enum.GetValues<SessionAttachmentPageOrder>())
        {
            SessionAttachmentPageOrdering.Apply(pending, order).Should().BeEmpty();
        }
    }

    [Fact]
    public void MaximumBoundedPagePreservesRowsAndByteBudgetInEveryOrder()
    {
        var page = Page();
        var maximum = page with { Records = [.. Enumerable.Range(0, SessionAttachmentPage.MaximumRecords)
            .Select(index => page.Records[0] with { Session = new(Guid.Parse($"00000000-0000-0000-0000-{index + 1:D12}")) })],
            ScannedRows = SessionAttachmentPage.MaximumScannedRows, ScanLimitReached = true };
        foreach (var order in Enum.GetValues<SessionAttachmentPageOrder>())
        {
            var rows = SessionAttachmentPageOrdering.Apply(maximum, order);
            rows.Should().HaveCount(SessionAttachmentPage.MaximumRecords);
            rows.Should().BeEquivalentTo(maximum.Records);
            JsonSerializer.SerializeToUtf8Bytes(rows).Length.Should().BeLessThan(SessionAttachmentPage.MaximumBytes);
        }
    }

    private static SessionAttachmentPage Page()
    {
        var row = SessionAttachmentSummaryTests.Summary();
        SessionAttachmentSummary With(int id, long bytes, AttachmentCopyStatus copy = AttachmentCopyStatus.Confirmed,
            AttachmentSessionState state = AttachmentSessionState.Active) =>
            row with { Session = new(Guid.Parse($"{id}0000000-0000-0000-0000-000000000000")), StoredBytes = bytes,
                CopyStatus = copy, SessionState = state };
        return new(new(true), [With(5, 0, AttachmentCopyStatus.Unavailable, AttachmentSessionState.Removed),
            With(2, 10, AttachmentCopyStatus.Held), With(4, 0, AttachmentCopyStatus.Unavailable),
            With(1, 10), With(3, 0)], "YQ==." + new string('a', 64), DateTimeOffset.UnixEpoch,
            4096, new string('a', 64), 5, false, 4096);
    }
}
