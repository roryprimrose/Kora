using AwesomeAssertions;

using Kora.Core.Auditing;
using Kora.Core.Authorization;

namespace Kora.Core.UnitTests.Authorization;

public sealed class ExactGrantHistoryQueryTests
{
    [Theory]
    [InlineData("Requested", SecurityAuditOutcome.Requested)]
    [InlineData("Succeeded", SecurityAuditOutcome.Succeeded)]
    [InlineData("Failed", SecurityAuditOutcome.Failed)]
    [InlineData("Denied", SecurityAuditOutcome.Denied)]
    [InlineData("Cancelled", SecurityAuditOutcome.Cancelled)]
    [InlineData("Unknown", SecurityAuditOutcome.Unknown)]
    public void DefinedOutcomeAndExactReasonMatchOnlyTheCommittedTypedEvent(string input, SecurityAuditOutcome outcome)
    {
        var query = ExactGrantHistoryQuery.FromInput(input, "policy-denied");
        var audit = Audit(outcome, "policy-denied");
        query.Outcome.Should().Be(outcome);
        query.ReasonCode.Should().Be("policy-denied");
        query.Matches(audit).Should().BeTrue();
        query.Matches(audit.WithOutcome(outcome, "other-reason")).Should().BeFalse();
        query.Matches(audit.WithOutcome(outcome)).Should().BeFalse();
        var other = outcome == SecurityAuditOutcome.Succeeded ? SecurityAuditOutcome.Requested : SecurityAuditOutcome.Succeeded;
        query.Matches(audit.WithOutcome(other, "policy-denied")).Should().BeFalse();
        new ExactGrantHistoryQuery(outcome).Matches(audit).Should().BeTrue();
        new ExactGrantHistoryQuery(reasonCode: "policy-denied").Matches(audit).Should().BeTrue();
        query.Digest.Should().HaveLength(64);
        query.Should().Be(new ExactGrantHistoryQuery(outcome, "policy-denied"));
        query.Digest.Should().NotBe(ExactGrantHistoryQuery.Unfiltered.Digest);
        query.Digest.Should().NotBe(new ExactGrantHistoryQuery(other, "policy-denied").Digest);
        query.Digest.Should().NotBe(new ExactGrantHistoryQuery(outcome, "other-reason").Digest);
    }

    [Fact]
    public void AnyPreservesTypedEventsWithOrWithoutReasonAndOnlyEmptyInputMeansAny()
    {
        ExactGrantHistoryQuery.FromInput("Any", "").Should().Be(ExactGrantHistoryQuery.Unfiltered);
        foreach (var outcome in Enum.GetValues<SecurityAuditOutcome>())
        {
            ExactGrantHistoryQuery.Unfiltered.Matches(Audit(outcome, null)).Should().BeTrue();
            ExactGrantHistoryQuery.Unfiltered.Matches(Audit(outcome, "reason.0-")).Should().BeTrue();
        }
        new ExactGrantHistoryQuery(reasonCode: new string('a', 128)).ReasonCode.Should().HaveLength(128);
        new ExactGrantHistoryQuery(reasonCode: ".").ReasonCode.Should().Be(".");
        var nullAudit = () => ExactGrantHistoryQuery.Unfiltered.Matches(null!);
        nullAudit.Should().Throw<ArgumentNullException>();
        var nullReason = () => ExactGrantHistoryQuery.FromInput("Any", null!);
        nullReason.Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("any")]
    [InlineData("succeeded")]
    [InlineData(" Succeeded")]
    [InlineData("Succeeded ")]
    [InlineData("1")]
    [InlineData("999")]
    public void UnknownOrNoncanonicalOutcomeNeverBecomesAny(string? outcome)
    {
        var parse = () => ExactGrantHistoryQuery.FromInput(outcome!, "");
        parse.Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(6)]
    [InlineData(int.MaxValue)]
    public void InvalidTypedEnumFailsClosed(int value)
    {
        var create = () => new ExactGrantHistoryQuery((SecurityAuditOutcome)value);
        create.Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("reason ")]
    [InlineData(" reason")]
    [InlineData("Reason")]
    [InlineData("reason_code")]
    [InlineData("reason\n")]
    [InlineData("r\u00e9ason")]
    public void ReasonUsesTheAuthoritativeAuditPolicyWithoutNormalization(string reason)
    {
        var query = () => new ExactGrantHistoryQuery(reasonCode: reason);
        var audit = () => Audit(SecurityAuditOutcome.Denied, reason);
        query.Should().Throw<ArgumentException>();
        audit.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void OverlongReasonIsRejectedByBothEventAndQuery()
    {
        var query = () => ExactGrantHistoryQuery.FromInput("Any", new string('a', 129));
        query.Should().Throw<ArgumentException>();
    }

    private static SecurityAuditEvent Audit(SecurityAuditOutcome outcome, string? reason) =>
        new(Guid.NewGuid(), SecurityAuditCategory.SecurityApproval, "approval.revoke-exact", outcome,
            SecurityAuditInitiator.LocalUser, "task", reasonCode: reason);
}
