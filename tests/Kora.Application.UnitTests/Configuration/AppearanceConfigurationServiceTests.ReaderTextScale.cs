using AwesomeAssertions;

using Kora.Application.Configuration;
using Kora.Core.Auditing;
using Kora.Core.Configuration;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Application.UnitTests.Configuration;

public sealed partial class AppearanceConfigurationServiceTests
{
    [Theory]
    [InlineData(SecurityAuditInitiator.LocalUser, RequestOrigin.LocalUi)]
    [InlineData(SecurityAuditInitiator.TypedCommand, RequestOrigin.LocalUi)]
    [InlineData(SecurityAuditInitiator.VoiceCommand, RequestOrigin.ActivatedVoice)]
    public void ReaderScaleCommitsOnlyExactOriginalInputAndPreservesOtherPreferences(
        SecurityAuditInitiator initiator, RequestOrigin origin)
    {
        var fixture = new Fixture();
        var before = fixture.Service.Get(AppearanceOption.ReaderTextScale);
        using var activity = HostActivity.BeginRoot(HostRequest.Create(origin), HostActivityLayer.Application, HostOperation.Request);
        var proposal = fixture.Service.Propose(AppearanceOption.ReaderTextScale, new AppearanceValue.Number(75),
            before.Revision, initiator, origin, () => true);
        var result = fixture.Service.Apply(proposal, TestContext.Current.CancellationToken);
        result.State.Value.Should().Be(new AppearanceValue.Number(75));
        result.State.Revision.Should().Be(before.Revision + 1);
        fixture.Service.Get(AppearanceOption.Theme).Value.Should().Be(new AppearanceValue.Theme(ApplicationThemeMode.System));
        fixture.Store.Text.Keys.Should().Equal("reader-text-scale-percent.txt");
        fixture.Audit.Events.Select(item => item.Outcome).Should().Equal(SecurityAuditOutcome.Requested, SecurityAuditOutcome.Succeeded);
        fixture.Audit.Events.Should().OnlyContain(item => item.ActionId == "configuration.reader-text-scale" && item.Initiator == initiator);
        activity.Complete(HostOperationOutcome.Completed);
    }

    [Theory]
    [InlineData(null, SecurityAuditInitiator.LocalUser, true)]
    [InlineData(RequestOrigin.HostSystem, SecurityAuditInitiator.LocalUser, true)]
    [InlineData(RequestOrigin.LocalUi, SecurityAuditInitiator.VoiceCommand, true)]
    [InlineData(RequestOrigin.ActivatedVoice, SecurityAuditInitiator.TypedCommand, true)]
    [InlineData(RequestOrigin.LocalUi, SecurityAuditInitiator.LocalUser, false)]
    public void ReaderScaleRejectsMissingOrMismatchedOriginAndClosedHost(
        RequestOrigin? origin, SecurityAuditInitiator initiator, bool eligible)
    {
        var fixture = new Fixture();
        var proposal = () => fixture.Service.Propose(AppearanceOption.ReaderTextScale, new AppearanceValue.Number(200),
            0, initiator, origin, () => eligible);
        proposal.Should().Throw<InvalidOperationException>();
        fixture.Store.Text.Should().BeEmpty();
    }

    [Fact]
    public void ReaderScaleRejectsMissingEligibilityAndHostileAmbientOrigin()
    {
        var fixture = new Fixture();
        var missing = () => fixture.Service.Propose(AppearanceOption.ReaderTextScale, new AppearanceValue.Number(200),
            0, SecurityAuditInitiator.LocalUser, RequestOrigin.LocalUi);
        missing.Should().Throw<InvalidOperationException>();
        using var listener = new System.Diagnostics.ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref System.Diagnostics.ActivityCreationOptions<System.Diagnostics.ActivityContext> _) => System.Diagnostics.ActivitySamplingResult.AllData,
        };
        System.Diagnostics.ActivitySource.AddActivityListener(listener);
        using var activity = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.HostSystem),
            HostActivityLayer.Application, HostOperation.Request);
        var hostile = () => fixture.Service.Propose(AppearanceOption.ReaderTextScale, new AppearanceValue.Number(200),
            0, SecurityAuditInitiator.LocalUser, RequestOrigin.LocalUi, () => true);
        hostile.Should().Throw<InvalidOperationException>();
        activity.Complete(HostOperationOutcome.Completed);
    }

    [Theory]
    [InlineData("eligibility")]
    [InlineData("disk")]
    [InlineData("saved-source")]
    public void ReaderScaleRechecksAtRequestedAuditBeforeAnyWrite(string change)
    {
        var fixture = new Fixture();
        var before = fixture.Service.Get(AppearanceOption.ReaderTextScale);
        var eligible = true;
        var proposal = fixture.Service.Propose(AppearanceOption.ReaderTextScale, new AppearanceValue.Number(200),
            before.Revision, SecurityAuditInitiator.LocalUser, RequestOrigin.LocalUi, () => eligible);
        fixture.Audit.BeforeRequested = () =>
        {
            switch (change)
            {
                case "eligibility": eligible = false; break;
                case "disk": fixture.Store.Failure = new IOException("reader disk full"); break;
                default: fixture.Store.Text["reader-text-scale-percent.txt"] = "75"; break;
            }
        };
        var apply = () => fixture.Service.Apply(proposal, TestContext.Current.CancellationToken);
        apply.Should().Throw<Exception>();
        fixture.Store.Writes.Should().Be(0);
        fixture.Audit.Events.Select(item => item.Outcome).Should().Equal(SecurityAuditOutcome.Requested, SecurityAuditOutcome.Failed);
    }

    [Theory]
    [InlineData("readback")]
    [InlineData("success-audit")]
    [InlineData("confirmation")]
    public void ReaderScaleUncertaintyNeverPublishesSuccessOrLosesPendingEvidence(string failure)
    {
        var fixture = new Fixture();
        var before = fixture.Service.Get(AppearanceOption.ReaderTextScale);
        var notifications = 0;
        fixture.Service.Changed += (_, _) => notifications++;
        fixture.Store.BeforeWrite = () =>
        {
            if (fixture.Store.Writes == 1 && string.Equals(failure, "readback", StringComparison.Ordinal))
            {
                fixture.Store.SuppressWrite = true;
            }
        };
        fixture.Store.DeleteFailure = string.Equals(failure, "confirmation", StringComparison.Ordinal) ? new IOException("confirmation failed") : null;
        fixture.Audit.BeforeSuccess = () =>
        {
            if (string.Equals(failure, "success-audit", StringComparison.Ordinal)) { throw new IOException("audit unavailable"); }
        };
        var proposal = fixture.Service.Propose(AppearanceOption.ReaderTextScale, new AppearanceValue.Number(200),
            before.Revision, SecurityAuditInitiator.LocalUser, RequestOrigin.LocalUi, () => true);
        var apply = () => fixture.Service.Apply(proposal, TestContext.Current.CancellationToken);
        if (string.Equals(failure, "readback", StringComparison.Ordinal)) { apply.Should().Throw<InvalidDataException>(); }
        else { apply.Should().Throw<IOException>(); }
        fixture.Store.Text.Should().ContainKey("reader-text-scale-unconfirmed.txt");
        notifications.Should().Be(0);
        var current = () => fixture.Service.Get(AppearanceOption.ReaderTextScale);
        current.Should().Throw<InvalidDataException>();
        var restarted = () => fixture.CreateService().Get(AppearanceOption.ReaderTextScale);
        restarted.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void ReaderScaleCancellationAndStaleRevisionDoNotWriteWhileLateCancellationKeepsCommit()
    {
        var fixture = new Fixture();
        var before = fixture.Service.Get(AppearanceOption.ReaderTextScale);
        var proposal = fixture.Service.Propose(AppearanceOption.ReaderTextScale, new AppearanceValue.Number(200),
            before.Revision, SecurityAuditInitiator.LocalUser, RequestOrigin.LocalUi, () => true);
        using var cancelled = new CancellationTokenSource();
        fixture.Audit.BeforeRequested = cancelled.Cancel;
        var apply = () => fixture.Service.Apply(proposal, cancelled.Token);
        apply.Should().Throw<OperationCanceledException>();
        fixture.Store.Writes.Should().Be(0);
        fixture.Audit.Events.Last().Outcome.Should().Be(SecurityAuditOutcome.Cancelled);
        fixture.Audit.BeforeRequested = null;
        using var late = new CancellationTokenSource();
        fixture.Store.BeforeWrite = late.Cancel;
        fixture.Service.Apply(proposal, late.Token).Succeeded.Should().BeTrue();
        fixture.Service.Apply(proposal, TestContext.Current.CancellationToken).Outcome.Should().Be(AppearanceApplyOutcome.Stale);
    }

    [Fact]
    public void ReaderScaleRejectsSavedPresenceChangesEvenWhenNumericDefaultMatches()
    {
        var fixture = new Fixture();
        var before = fixture.Service.Get(AppearanceOption.ReaderTextScale);
        var proposal = fixture.Service.Propose(AppearanceOption.ReaderTextScale, new AppearanceValue.Number(200),
            before.Revision, SecurityAuditInitiator.LocalUser, RequestOrigin.LocalUi, () => true);
        fixture.Store.Text["reader-text-scale-percent.txt"] = "100";
        var apply = () => fixture.Service.Apply(proposal, TestContext.Current.CancellationToken);
        apply.Should().Throw<InvalidDataException>();
        fixture.Store.Writes.Should().Be(0);
    }
}
