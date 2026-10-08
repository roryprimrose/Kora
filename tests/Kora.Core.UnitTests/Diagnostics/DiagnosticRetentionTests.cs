using AwesomeAssertions;
using Kora.Core.Configuration;
using Kora.Core.Diagnostics;

namespace Kora.Core.UnitTests.Diagnostics;

public sealed class DiagnosticRetentionTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(30)]
    [InlineData(365)]
    public void Exact_domain_and_coherent_future_policy_preserve_UTC_and_other_policies(int days)
    {
        var value = DiagnosticRetentionDays.Parse(days.ToString(System.Globalization.CultureInfo.InvariantCulture));
        value.Days.Should().Be(days);
        var policy = new DiagnosticRetentionPolicy();
        policy.Effective.Should().BeNull();
        var committed = new DateTimeOffset(2026, 10, 8, 14, 0, 0, TimeSpan.FromHours(11));
        policy.Invoking(item => item.Due(committed)).Should().Throw<InvalidOperationException>();
        policy.Activate(value);
        policy.Effective.Should().Be(value);
        policy.Due(committed).Should().Be(committed.ToUniversalTime().AddDays(days));
        policy.HoldUnavailable();
        policy.Effective.Should().BeNull();
        policy.Invoking(item => item.Activate(default)).Should().Throw<ArgumentOutOfRangeException>();
        DiagnosticRetentionDays.Default.Days.Should().Be(30);
        EvidenceRetentionPolicy.DailyFileDays.Should().Be(30);
        EvidenceRetentionPolicy.DailyFileCount.Should().Be(30);
        new EvidenceRetentionPolicy().AuditDays.Should().Be(90);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("366")]
    [InlineData("-1")]
    [InlineData("01")]
    [InlineData(" 1")]
    [InlineData("+1")]
    [InlineData("1.0")]
    [InlineData("one")]
    [InlineData("")]
    [InlineData("999999999999999")]
    public void Invalid_exact_values_fail_closed(string text)
    {
        var action = () => DiagnosticRetentionDays.Parse(text);
        action.Should().Throw<ArgumentOutOfRangeException>();
    }
}
