using System.Collections.Immutable;
using System.Text.Json;

using AwesomeAssertions;

using Kora.Core.Storage;

namespace Kora.Core.UnitTests.Storage;

public sealed class SessionAttachmentPageTests
{
    [Theory]
    [InlineData("query")]
    [InlineData("default")]
    [InlineData("records")]
    [InlineData("scanned-few")]
    [InlineData("scanned-large")]
    [InlineData("all-omitted")]
    [InlineData("utc")]
    [InlineData("ceiling")]
    [InlineData("digest-null")]
    [InlineData("digest-length")]
    [InlineData("digest-character")]
    [InlineData("zero-head")]
    [InlineData("zero-digest")]
    [InlineData("next-empty")]
    [InlineData("next-large")]
    [InlineData("limit-no-next")]
    [InlineData("limit-few")]
    [InlineData("audit-negative")]
    [InlineData("audit-large")]
    [InlineData("audit-ceiling")]
    [InlineData("pending-no-next")]
    [InlineData("pending-slots")]
    [InlineData("pending-limit")]
    [InlineData("pending-audit")]
    [InlineData("next-no-scan")]
    [InlineData("duplicate")]
    [InlineData("null-row")]
    [InlineData("row")]
    [InlineData("profile")]
    [InlineData("source")]
    [InlineData("removed")]
    public void ValidateRejectsMalformedPageScopeBoundsAndAvailability(string boundary)
    {
        var summary = SessionAttachmentSummaryTests.Summary();
        var page = new SessionAttachmentPage(new(), [summary], null, DateTimeOffset.UnixEpoch, 10000, new string('a', 64), 1, false);
        var invalid = boundary switch
        {
            "query" => page with { Query = null! },
            "default" => page with { Records = default },
            "records" => page with { Records = [.. Enumerable.Repeat(summary, 17)], ScannedRows = 17 },
            "scanned-few" => page with { ScannedRows = 0 },
            "scanned-large" => page with { ScannedRows = 65 },
            "all-omitted" => page with { Query = new(true), ScannedRows = 2 },
            "utc" => page with { ObservedAt = page.ObservedAt.ToOffset(TimeSpan.FromHours(1)) },
            "ceiling" => page with { AuthorityCeiling = -1 },
            "digest-null" => page with { AuthorityDigest = null! },
            "digest-length" => page with { AuthorityDigest = "x" },
            "digest-character" => page with { AuthorityDigest = new string('z', 64) },
            "zero-head" => page with { AuthorityCeiling = 0 },
            "zero-digest" => page with { AuthorityDigest = new string('0', 64) },
            "next-empty" => page with { Next = string.Empty },
            "next-large" => page with { Next = new string('a', 4097) },
            "limit-no-next" => page with { ScanLimitReached = true, ScannedRows = 64 },
            "limit-few" => page with { ScanLimitReached = true, Next = "opaque" },
            "audit-negative" => page with { AuditScannedRows = -1 },
            "audit-large" => page with { AuditScannedRows = 4097 },
            "audit-ceiling" => page with { AuthorityCeiling = 1, AuditScannedRows = 2 },
            "pending-no-next" => page with { AuthorityValidationPending = true },
            "pending-slots" => page with { AuthorityValidationPending = true, Next = "opaque", AuditScannedRows = 4096 },
            "pending-limit" => page with { Records = [], ScannedRows = 0, ScanLimitReached = true, AuthorityValidationPending = true, Next = "opaque", AuditScannedRows = 4096 },
            "pending-audit" => page with { Records = [], ScannedRows = 0, AuthorityValidationPending = true, Next = "opaque" },
            "next-no-scan" => page with { Records = [], ScannedRows = 0, Next = "opaque" },
            "duplicate" => page with { Records = [summary, summary], ScannedRows = 2 },
            "null-row" => page with { Records = [null!] },
            "row" => page with { Records = [summary with { StoredBytes = -1 }] },
            "profile" => page with { Records = [summary, summary with { Session = new(Guid.NewGuid()), Profile = new(Guid.NewGuid()) }], ScannedRows = 2 },
            "source" => page with { Records = [summary, summary with { Session = new(Guid.NewGuid()), ObservationSource = Guid.NewGuid() }], ScannedRows = 2 },
            _ => page with { Records = [summary with { CopyStatus = AttachmentCopyStatus.Unavailable, StoredBytes = 0 }] },
        };
        invalid.Invoking(item => item.Validate()).Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void ValidMaximumPageIsContentFreeAndBelowByteBoundWhileEmptyScansMayContinue()
    {
        var exact = SessionAttachmentSummaryTests.Summary();
        var records = ImmutableArray.CreateRange(Enumerable.Range(0, 16)
            .Select(_ => exact with { Session = new(Guid.NewGuid()) }));
        var maximum = new SessionAttachmentPage(new(), records, Cursor(3000),
            DateTimeOffset.MaxValue.ToUniversalTime(), long.MaxValue, "0123456789abcdef" + new string('f', 48), 64, true, 4096);
        maximum.Validate();
        (maximum with { Query = new(true) }).Validate();
        JsonSerializer.SerializeToUtf8Bytes(maximum).Length.Should().BeLessThan(SessionAttachmentPage.MaximumBytes);
        JsonSerializer.Serialize(maximum).Should().NotContain("private content").And.NotContain("Synthetic").And.NotContain("CanonicalPath");
        (maximum with { Records = [], ScannedRows = 64 }).Validate();
        (maximum with { Records = [], ScannedRows = 0, ScanLimitReached = false, AuthorityValidationPending = true }).Validate();
        (maximum with { Next = null, ScannedRows = 16, ScanLimitReached = false }).Validate();
        (maximum with { Query = new(true), Records = [exact with { CopyStatus = AttachmentCopyStatus.Unavailable, StoredBytes = 0 }],
            Next = null, ScannedRows = 1, ScanLimitReached = false }).Validate();
        new SessionAttachmentPage(new(), [], null, DateTimeOffset.UnixEpoch, 0, new string('0', 64), 0, false).Validate();
    }

    [Theory]
    [InlineData("length")]
    [InlineData("parts")]
    [InlineData("empty")]
    [InlineData("signature")]
    [InlineData("uppercase")]
    [InlineData("base64")]
    [InlineData("noncanonical")]
    public void ValidateCursorRejectsMalformedOrNoncanonicalOpaqueShape(string boundary)
    {
        var token = boundary switch
        {
            "length" => Cursor(4000),
            "parts" => "not-a-cursor",
            "empty" => "." + new string('a', 64),
            "signature" => "YQ==.a",
            "uppercase" => "YQ==." + new string('A', 64),
            "base64" => "%." + new string('a', 64),
            _ => " YQ==." + new string('a', 64),
        };
        ((Action)(() => SessionAttachmentPage.ValidateCursor(token))).Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void ValidateCursorRejectsNullAndAcceptsCanonicalBoundedToken()
    {
        ((Action)(() => SessionAttachmentPage.ValidateCursor(null!))).Should().Throw<ArgumentNullException>();
        SessionAttachmentPage.ValidateCursor(Cursor());
    }

    private static string Cursor(int bytes = 2) => Convert.ToBase64String(new byte[bytes]) + "." + new string('a', 64);
}
