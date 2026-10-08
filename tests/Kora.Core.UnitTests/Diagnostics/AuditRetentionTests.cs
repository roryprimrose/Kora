using AwesomeAssertions;
using Kora.Core.Configuration;
using Kora.Core.Diagnostics;

namespace Kora.Core.UnitTests.Diagnostics;

public sealed class AuditRetentionTests
{
    [Theory]
    [InlineData(30)]
    [InlineData(90)]
    [InlineData(365)]
    public void Exact_domain_future_snapshot_and_metadata_are_independent_of_grants_and_ordinary_retention(int days)
    {
        var value = AuditRetentionDays.Parse(days.ToString(System.Globalization.CultureInfo.InvariantCulture));
        value.Days.Should().Be(days);
        AuditRetentionDays.Default.Days.Should().Be(90);
        var committed = new DateTimeOffset(2026, 10, 8, 14, 0, 0, TimeSpan.FromHours(11));
        var policy = new AuditRetentionPolicy();
        policy.Effective.Should().BeNull();
        policy.Invoking(item => item.Due(committed)).Should().Throw<AuditRetentionUnavailableException>();
        policy.Activate(value);
        policy.Effective.Should().Be(value);
        policy.Due(committed).Should().Be(committed.ToUniversalTime().AddDays(days));
        AuditRetentionDays.IsValidDeadline(committed, policy.Due(committed)).Should().BeTrue();
        policy.HoldUnavailable();
        policy.Effective.Should().BeNull();
        policy.Invoking(item => item.Activate(default)).Should().Throw<ArgumentOutOfRangeException>();
        new EvidenceRetentionPolicy(days).AuditDays.Should().Be(days);
        DiagnosticRetentionDays.Default.Days.Should().Be(30);
        EvidenceRetentionPolicy.DailyFileDays.Should().Be(30);
        EvidenceRetentionPolicy.DailyFileCount.Should().Be(30);
    }

    [Theory]
    [InlineData("29")]
    [InlineData("366")]
    [InlineData("-1")]
    [InlineData("030")]
    [InlineData(" 30")]
    [InlineData("+30")]
    [InlineData("30.0")]
    [InlineData("ninety")]
    [InlineData("")]
    [InlineData("999999999999999")]
    public void Noncanonical_or_out_of_bound_input_never_becomes_a_policy(string text)
    {
        var action = () => AuditRetentionDays.Parse(text);
        action.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(29)]
    [InlineData(366)]
    [InlineData(30.5)]
    [InlineData(-1)]
    public void Invalid_existing_deadline_metadata_is_refused_not_rewritten(double days)
    {
        var committed = new DateTimeOffset(2026, 10, 8, 0, 0, 0, TimeSpan.Zero);
        AuditRetentionDays.IsValidDeadline(committed, committed.AddDays(days)).Should().BeFalse();
    }

    [Fact]
    public void Typed_startup_hold_preserves_invalid_saved_failure_for_explicit_recovery_without_policy_fallback()
    {
        var invalid = new InvalidDataException("Malformed audit preference.");
        var unavailable = new AuditRetentionUnavailableException(invalid);
        unavailable.InnerException.Should().BeSameAs(invalid);
        unavailable.Message.Should().Contain("unconfirmed").And.Contain("explicit repair");
    }
}
