using AwesomeAssertions;
using Kora.Core.Diagnostics;

namespace Kora.Core.UnitTests.Diagnostics;

public sealed class EvidenceRetentionPolicyTests
{
    [Theory]
    [InlineData(29)]
    [InlineData(366)]
    public void Audit_retention_rejects_out_of_contract_limits(int days)
    {
        var action = () => new EvidenceRetentionPolicy(days);
        action.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(30)]
    [InlineData(90)]
    [InlineData(365)]
    public void Retention_is_utc_independent_and_shortening_requires_apply_now(int days)
    {
        var committed = new DateTimeOffset(2026, 10, 5, 10, 0, 0, TimeSpan.FromHours(11));
        var policy = new EvidenceRetentionPolicy(days);
        policy.DiagnosticDue(committed).Should().Be(committed.ToUniversalTime().AddDays(30));
        policy.AuditDue(committed).Should().Be(committed.ToUniversalTime().AddDays(days));
        var prior = committed.AddDays(365);
        policy.ExistingAuditDue(committed, prior, applyNow: false).Should().Be(prior);
        policy.ExistingAuditDue(committed, prior, applyNow: true).Should().Be(policy.AuditDue(committed));
    }
}
