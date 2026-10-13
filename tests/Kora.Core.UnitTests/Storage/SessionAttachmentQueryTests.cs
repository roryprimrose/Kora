using System.Text.Json;

using AwesomeAssertions;

using Kora.Core.Storage;

namespace Kora.Core.UnitTests.Storage;

public sealed class SessionAttachmentQueryTests
{
    [Theory]
    [InlineData("")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("ABCDEFAB-1234-5678-9012-abcdefabcdef")]
    [InlineData("abcdefab123456789012abcdefabcdef")]
    [InlineData("{abcdefab-1234-5678-9012-abcdefabcdef}")]
    [InlineData(" abcdefab-1234-5678-9012-abcdefabcdef")]
    [InlineData("abcdefab-1234-5678-9012-abcdefabcdef\n")]
    [InlineData("abcdefab-1234-5678-9012-abcdefabcdef\u200D")]
    public void ExactQueryRejectsNoncanonicalInputWithoutBrowseFallback(string value)
    {
        ((Action)(() => _ = new SessionAttachmentQuery(false, value))).Should().Throw<InvalidDataException>();
        ((Action)(() => _ = new SessionAttachmentQuery(true, value))).Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void OversizedExactQueryUsesAuthoritativeIdPolicy()
    {
        ((Action)(() => _ = new SessionAttachmentQuery(false, new string('a', 10000))))
            .Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void QueryRoundTripsRemovalFlagAndExactFilterWithoutSearchingOtherHeaders(bool includeRemoved, bool exact)
    {
        var row = SessionAttachmentSummaryTests.Summary();
        var text = exact ? row.Session.Value.ToString("D") : null;
        var query = new SessionAttachmentQuery(includeRemoved, text);
        query.IncludeRemoved.Should().Be(includeRemoved);
        query.ExactSessionId.Should().Be(text);
        query.ExactId.Should().Be(exact ? row.Session.Value : null);
        var restored = JsonSerializer.Deserialize<SessionAttachmentQuery>(JsonSerializer.Serialize(query));
        restored.Should().Be(query);
        query.Matches(row).Should().BeTrue();
        query.Matches(row with { Session = new(Guid.NewGuid()) }).Should().Be(!exact);
        query.Matches(row with { CopyStatus = AttachmentCopyStatus.Unavailable, StoredBytes = 0 })
            .Should().Be(includeRemoved);
        query.Matches(row with { CopyStatus = AttachmentCopyStatus.Held }).Should().BeTrue();
        (query == new SessionAttachmentQuery(!includeRemoved, text)).Should().BeFalse();
        (query == new SessionAttachmentQuery(includeRemoved, Guid.NewGuid().ToString("D"))).Should().BeFalse();
    }

    [Fact]
    public void ExactPageAllowsNonmatchingScansButRejectsDisclosureOfDifferentSession()
    {
        var row = SessionAttachmentSummaryTests.Summary();
        var query = new SessionAttachmentQuery(true, row.Session.Value.ToString("D"));
        var page = new SessionAttachmentPage(query, [row], null, DateTimeOffset.UnixEpoch,
            1, new string('a', 64), 64, false);
        page.Validate();
        (page with { Records = [] }).Validate();
        (page with { Records = [row with { Session = new(Guid.NewGuid()) }] })
            .Invoking(value => value.Validate()).Should().Throw<InvalidDataException>();
    }
}
