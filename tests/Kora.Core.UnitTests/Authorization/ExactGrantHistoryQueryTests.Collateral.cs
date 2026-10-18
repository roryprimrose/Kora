using System.Globalization;

using AwesomeAssertions;

using Kora.Core.Auditing;
using Kora.Core.Authorization;

namespace Kora.Core.UnitTests.Authorization;

public sealed partial class ExactGrantHistoryQueryTests
{
    [Fact]
    public void CollateralBoundsAreInclusiveIndependentAndComposeWithEveryCommittedPredicate()
    {
        var row = Audit(SecurityAuditOutcome.Denied, "denied") with { OtherChanges = 2, OtherRequestedGrants = 3 };
        var query = new ExactGrantHistoryQuery(row.Audit.Outcome, row.Audit.ReasonCode, row.Audit.Initiator,
            row.Retention, row.CommittedAt, row.CommittedAt, row.DueAt, row.DueAt,
            row.Audit.CorrelationId, ExactGrantHistoryApproval.Absent, sessionId: row.Request.SessionId.Value,
            requestId: row.Request.RequestId.Value, taskId: row.Request.TaskId.Value,
            minimumOtherChanges: 2, maximumOtherChanges: 2, minimumOtherRequestedGrants: 3, maximumOtherRequestedGrants: 3);
        query.Matches(row).Should().BeTrue();
        query.MinimumOtherChanges.Should().Be(2);
        query.MaximumOtherChanges.Should().Be(2);
        query.MinimumOtherRequestedGrants.Should().Be(3);
        query.MaximumOtherRequestedGrants.Should().Be(3);
        query.Matches(row with { OtherChanges = 1 }).Should().BeFalse();
        query.Matches(row with { OtherChanges = 3 }).Should().BeFalse();
        query.Matches(row with { OtherRequestedGrants = 2 }).Should().BeFalse();
        query.Matches(row with { OtherRequestedGrants = 4 }).Should().BeFalse();
        query.Matches(row with { Audit = row.Audit.WithOutcome(SecurityAuditOutcome.Succeeded) }).Should().BeFalse();
        foreach (var value in new[] { 0, 1, int.MaxValue })
        {
            var text = value.ToString(CultureInfo.InvariantCulture);
            var exact = ExactGrantHistoryQuery.FromInput("Any", "", minimumOtherChanges: text, maximumOtherChanges: text,
                minimumOtherRequestedGrants: text, maximumOtherRequestedGrants: text);
            exact.Should().Be(new ExactGrantHistoryQuery(minimumOtherChanges: value, maximumOtherChanges: value,
                minimumOtherRequestedGrants: value, maximumOtherRequestedGrants: value));
            exact.Matches(row with { OtherChanges = value, OtherRequestedGrants = value }).Should().BeTrue();
            ExactGrantHistoryQuery.Unfiltered.Matches(row with { OtherChanges = value, OtherRequestedGrants = value }).Should().BeTrue();
        }
        ExactGrantHistoryQuery.FromInput("Any", "").Should().Be(new ExactGrantHistoryQuery());
        new ExactGrantHistoryQuery(minimumOtherChanges: 0, maximumOtherChanges: int.MaxValue,
            minimumOtherRequestedGrants: 0, maximumOtherRequestedGrants: int.MaxValue).Matches(row).Should().BeTrue();
    }

    [Fact]
    public void EveryCollateralBoundHasIndependentDigestAndStrictInputAndTypedValidation()
    {
        var digests = new HashSet<string>(StringComparer.Ordinal) { ExactGrantHistoryQuery.Unfiltered.Digest };
        for (var field = 0; field < 4; field++)
        {
            foreach (var value in new[] { 0, 1, int.MaxValue })
            {
                var bounds = new int?[4];
                bounds[field] = value;
                var query = CollateralQuery(bounds);
                digests.Add(query.Digest).Should().BeTrue();
                var text = new[] { "", "", "", "" };
                text[field] = value.ToString(CultureInfo.InvariantCulture);
                ParseCollateral(text).Should().Be(query);
            }
            foreach (var invalid in new string?[] { null, "-1", "+1", " 1", "1 ", "\t", "\n", "Any", "any",
                "1.0", "1,000", "1e1", "\u0661", "2147483648", "9223372036854775807", "9223372036854775808" })
            {
                var bounds = new[] { "", "", "", "" };
                bounds[field] = invalid!;
                var parse = () => ParseCollateral(bounds);
                if (invalid is null) { parse.Should().Throw<ArgumentNullException>(); }
                else { parse.Should().Throw<InvalidDataException>(); }
            }
            var typed = new int?[4];
            typed[field] = -1;
            var create = () => CollateralQuery(typed);
            create.Should().Throw<InvalidDataException>();
        }
        var changes = () => new ExactGrantHistoryQuery(minimumOtherChanges: 1, maximumOtherChanges: 0);
        var requested = () => new ExactGrantHistoryQuery(minimumOtherRequestedGrants: 1, maximumOtherRequestedGrants: 0);
        changes.Should().Throw<InvalidDataException>();
        requested.Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    [InlineData(int.MinValue, int.MinValue)]
    public void InvalidProjectedCountsFailBeforeAnyPredicateCanExcludeThem(int changes, int requested)
    {
        var row = Audit(SecurityAuditOutcome.Denied, null) with { OtherChanges = changes, OtherRequestedGrants = requested };
        var match = () => new ExactGrantHistoryQuery(SecurityAuditOutcome.Succeeded, minimumOtherChanges: int.MaxValue).Matches(row);
        match.Should().Throw<InvalidDataException>();
    }

    private static ExactGrantHistoryQuery CollateralQuery(int?[] bounds) =>
        new(minimumOtherChanges: bounds[0], maximumOtherChanges: bounds[1],
            minimumOtherRequestedGrants: bounds[2], maximumOtherRequestedGrants: bounds[3]);

    private static ExactGrantHistoryQuery ParseCollateral(string[] bounds) =>
        ExactGrantHistoryQuery.FromInput("Any", "", minimumOtherChanges: bounds[0], maximumOtherChanges: bounds[1],
            minimumOtherRequestedGrants: bounds[2], maximumOtherRequestedGrants: bounds[3]);
}
