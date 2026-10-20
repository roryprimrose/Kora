using AwesomeAssertions;

using Kora.Application.Configuration;
using Kora.Core.Auditing;
using Kora.Core.Configuration;
using Kora.Core.Hosting;

namespace Kora.Application.UnitTests.Configuration;

public sealed partial class AppearanceConfigurationServiceTests
{
    [Theory]
    [InlineData(25, 125)]
    [InlineData(-25, 75)]
    [InlineData(100, 200)]
    public void ReaderAdjustmentUsesConfirmedCurrentValueAndExistingAuditedProposal(int delta, int expected)
    {
        var fixture = new Fixture();
        var proposal = fixture.Service.ProposeReaderTextScaleAdjustment(delta, SecurityAuditInitiator.TypedCommand,
            RequestOrigin.LocalUi, () => true);
        fixture.Store.Writes.Should().Be(0);
        var result = fixture.Service.Apply(proposal, TestContext.Current.CancellationToken);
        result.State.Value.GetNumber().Should().Be(expected);
        fixture.Audit.Events.Select(item => item.Outcome).Should().Equal(SecurityAuditOutcome.Requested, SecurityAuditOutcome.Succeeded);
        var reverse = fixture.Service.ProposeReaderTextScaleAdjustment(-delta, SecurityAuditInitiator.VoiceCommand,
            RequestOrigin.ActivatedVoice, () => true);
        fixture.Service.Apply(reverse, TestContext.Current.CancellationToken).State.Value.GetNumber().Should().Be(100);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(101, false)]
    [InlineData(-26, false)]
    [InlineData(int.MinValue, false)]
    [InlineData(int.MaxValue, true)]
    public void ReaderAdjustmentRejectsZeroRangeAndCheckedOverflowWithoutWriting(int delta, bool overflow)
    {
        var fixture = new Fixture();
        var propose = () => fixture.Service.ProposeReaderTextScaleAdjustment(delta, SecurityAuditInitiator.TypedCommand,
            RequestOrigin.LocalUi, () => true);
        if (overflow) { propose.Should().Throw<OverflowException>(); }
        else { propose.Should().Throw<ArgumentOutOfRangeException>(); }
        fixture.Store.Writes.Should().Be(0);
        fixture.Audit.Events.Should().BeEmpty();
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("201")]
    [InlineData("pending")]
    [InlineData("external-change")]
    public void ReaderAdjustmentRejectsInvalidUnconfirmedOrChangedPreferenceEvidence(string evidence)
    {
        var fixture = new Fixture();
        fixture.Service.Get(AppearanceOption.ReaderTextScale);
        fixture.Store.Text[string.Equals(evidence, "pending", StringComparison.Ordinal)
            ? "reader-text-scale-unconfirmed.txt" : "reader-text-scale-percent.txt"] =
            string.Equals(evidence, "external-change", StringComparison.Ordinal) ? "125" : evidence;
        var propose = () => fixture.Service.ProposeReaderTextScaleAdjustment(10, SecurityAuditInitiator.TypedCommand,
            RequestOrigin.LocalUi, () => true);
        propose.Should().Throw<InvalidDataException>();
        fixture.Store.Writes.Should().Be(0);
    }

    [Fact]
    public void ReaderAdjustmentRefusesAStaleConcurrentProposalInsteadOfRebasingIt()
    {
        var fixture = new Fixture();
        var first = fixture.Service.ProposeReaderTextScaleAdjustment(25, SecurityAuditInitiator.TypedCommand,
            RequestOrigin.LocalUi, () => true);
        var concurrent = fixture.Service.ProposeReaderTextScaleAdjustment(-25, SecurityAuditInitiator.VoiceCommand,
            RequestOrigin.ActivatedVoice, () => true);
        fixture.Service.Apply(first, TestContext.Current.CancellationToken).State.Value.GetNumber().Should().Be(125);
        fixture.Service.Apply(concurrent, TestContext.Current.CancellationToken).Outcome.Should().Be(AppearanceApplyOutcome.Stale);
        fixture.Service.Get(AppearanceOption.ReaderTextScale).Value.GetNumber().Should().Be(125);
        fixture.Store.Writes.Should().Be(2, "only the winning pending marker and preference are written");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReaderAdjustmentKeepsOriginalEligibilityThroughRequestedAudit(bool revokeBeforeProposal)
    {
        var fixture = new Fixture();
        var eligible = !revokeBeforeProposal;
        var propose = () => fixture.Service.ProposeReaderTextScaleAdjustment(25, SecurityAuditInitiator.TypedCommand,
            RequestOrigin.LocalUi, () => eligible);
        if (revokeBeforeProposal) { propose.Should().Throw<InvalidOperationException>(); }
        else
        {
            var proposal = propose();
            fixture.Audit.BeforeRequested = () => eligible = false;
            var apply = () => fixture.Service.Apply(proposal, TestContext.Current.CancellationToken);
            apply.Should().Throw<InvalidOperationException>();
            fixture.Audit.Events.Last().Outcome.Should().Be(SecurityAuditOutcome.Failed);
        }
        fixture.Store.Writes.Should().Be(0);
    }

    [Fact]
    public void ReaderAdjustmentCancellationKeepsTheExistingCancelledAuditPath()
    {
        var fixture = new Fixture();
        var proposal = fixture.Service.ProposeReaderTextScaleAdjustment(25, SecurityAuditInitiator.TypedCommand,
            RequestOrigin.LocalUi, () => true);
        using var cancellation = new CancellationTokenSource();
        fixture.Audit.BeforeRequested = cancellation.Cancel;
        var apply = () => fixture.Service.Apply(proposal, cancellation.Token);
        apply.Should().Throw<OperationCanceledException>();
        fixture.Store.Writes.Should().Be(0);
        fixture.Audit.Events.Last().Outcome.Should().Be(SecurityAuditOutcome.Cancelled);
    }
}
