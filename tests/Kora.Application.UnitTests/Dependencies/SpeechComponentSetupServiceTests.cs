using System.Diagnostics;
using AwesomeAssertions;
using Kora.Application.Dependencies;
using Kora.Core.Auditing;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Testing;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.Dependencies;

[Collection("Host tracing")]
public sealed class SpeechComponentSetupServiceTests : IDisposable
{
    private readonly List<Activity> stopped = [];
    private readonly ActivityListener listener;

    public SpeechComponentSetupServiceTests()
    {
        listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity => stopped.Add(activity),
        };
        ActivitySource.AddActivityListener(listener);
    }

    public void Dispose() => listener.Dispose();

    [Theory]
    [InlineData(SpeechComponentOperation.Prepare)]
    [InlineData(SpeechComponentOperation.Verify)]
    [InlineData(SpeechComponentOperation.Remove)]
    public async Task AppliesExactReviewedOperationAndReleasesExclusiveLease(SpeechComponentOperation operation)
    {
        var fixture = new SpeechComponentSetupFixture();
        if (operation == SpeechComponentOperation.Verify)
        {
            fixture.Store.State = new(true, true, SpeechComponentOwnership.Managed, "verified");
        }
        using var host = Begin();
        var plan = await fixture.Review(operation);
        plan.Disclosure.Should().Contain(fixture.Component.ModelIdentity).And.Contain(fixture.Component.Licence)
            .And.Contain("staging: 16 bytes").And.Contain("No runtime selection");
        plan.Disclosure.Should().Contain(operation == SpeechComponentOperation.Prepare ? "Download: 8 bytes" : "Download: 0 bytes");
        var result = await fixture.Service.ApplyAsync(plan, true, fixture.Progress, CancellationToken.None);
        result.Identity.Should().Be(fixture.Component.Identity);
        result.Compatible.Should().BeTrue();
        result.Qualified.Should().BeTrue();
        result.FunctionalVerified.Should().Be(operation != SpeechComponentOperation.Remove);
        fixture.Store.Prepares.Should().Be(operation == SpeechComponentOperation.Prepare ? 1 : 0);
        fixture.Store.Removals.Should().Be(operation == SpeechComponentOperation.Remove ? 1 : 0);
        fixture.RuntimeAdapter.Releases.Should().Be(fixture.RuntimeAdapter.Leases).And.Be(1);
        fixture.AuditLog.Events.Select(item => item.Outcome).Should().Equal(SecurityAuditOutcome.Requested, SecurityAuditOutcome.Succeeded);
        fixture.AuditLog.Events.Select(item => item.CorrelationId).Distinct().Should().ContainSingle();
        fixture.AuditLog.Requests.Should().OnlyContain(request => ReferenceEquals(request, host.Request));
        fixture.AuditLog.Traces.Should().OnlyContain(trace => trace.TraceId == host.Activity!.TraceId);
        stopped.Where(activity => string.Equals(activity.DisplayName, "storage.commit", StringComparison.Ordinal)).Should()
            .Contain(activity => activity.Status == ActivityStatusCode.Ok);
        stopped.SelectMany(activity => activity.TagObjects).Should().NotContain(tag =>
            tag.Key.Contains("path", StringComparison.OrdinalIgnoreCase)
            || tag.Key.Contains("model", StringComparison.OrdinalIgnoreCase)
            || tag.Key.Contains("source", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public async Task CatalogueDoesNotOfferUnqualifiedOrIncompatibleComponents(bool qualified, bool compatible)
    {
        var fixture = new SpeechComponentSetupFixture();
        fixture.RuntimeAdapter.Compatible = compatible;
        var definition = SpeechComponentFixture.Definition(qualified ? "fixture-qualified" : null);
        var service = new SpeechComponentSetupService(new([definition]), fixture.Store, fixture.RuntimeAdapter,
            fixture.AuditLog, NullLogger<SpeechComponentSetupService>.Instance, fixture.Time);
        service.Offers.Should().BeEmpty();
        using var host = Begin();
        var observed = await service.InspectAsync(definition.Identity, CancellationToken.None);
        observed.Qualified.Should().Be(qualified);
        observed.Compatible.Should().Be(compatible);
        await FluentActions.Awaiting(() => service.ReviewAsync(definition.Identity, SpeechComponentOperation.Prepare,
            () => true, CancellationToken.None)).Should().ThrowAsync<InvalidOperationException>();
        fixture.Store.Prepares.Should().Be(0);
    }

    [Fact]
    public async Task EmptyProductionAndUnavailableRuntimeCannotCreateAnOfferOrSuccessfulPreparation()
    {
        var fixture = new SpeechComponentSetupFixture();
        var runtime = new UnavailableSpeechComponentRuntime();
        runtime.IsCompatible(fixture.Component).Should().BeFalse();
        (await runtime.TryAcquireExclusiveAsync(fixture.Component, CancellationToken.None)).Should().BeNull();
        (await runtime.VerifyAsync(fixture.Component, CancellationToken.None)).Should().BeFalse();
        var service = new SpeechComponentSetupService(SpeechComponentCatalogue.Production, fixture.Store, runtime,
            fixture.AuditLog, NullLogger<SpeechComponentSetupService>.Instance, fixture.Time);
        service.Offers.Should().BeEmpty();
        using var host = Begin();
        await FluentActions.Awaiting(() => service.ReviewAsync(fixture.Component.Identity, SpeechComponentOperation.Prepare,
            () => true, CancellationToken.None)).Should().ThrowAsync<InvalidOperationException>();
        fixture.Store.Inspections.Should().Be(0);
        fixture.Store.Prepares.Should().Be(0);
    }

    [Theory]
    [InlineData("decline")]
    [InlineData("expired")]
    [InlineData("revoked")]
    [InlineData("incompatible")]
    public async Task ReviewCannotAuthorizeChangedOrUnapprovedHostState(string reason)
    {
        var fixture = new SpeechComponentSetupFixture();
        using var host = Begin();
        var plan = await fixture.Review();
        if (reason is "expired") { fixture.Time.Now = fixture.Time.Now.AddMinutes(5); }
        if (reason is "revoked") { fixture.Eligible = false; }
        if (reason is "incompatible") { fixture.RuntimeAdapter.Compatible = false; }
        await FluentActions.Awaiting(() => fixture.Service.ApplyAsync(plan, reason is not "decline", fixture.Progress, CancellationToken.None))
            .Should().ThrowAsync<UnauthorizedAccessException>();
        fixture.Store.Prepares.Should().Be(0);
        fixture.RuntimeAdapter.Leases.Should().Be(0);
        fixture.AuditLog.Events.Select(item => item.Outcome).Should().Equal(SecurityAuditOutcome.Requested, SecurityAuditOutcome.Denied);
    }

    [Fact]
    public async Task ReviewCannotCrossHostRequestOrServiceAndCannotReplay()
    {
        var fixture = new SpeechComponentSetupFixture();
        using var host = Begin();
        var plan = await fixture.Review();
        var otherService = fixture.Create(fixture.Store);
        await FluentActions.Awaiting(() => otherService.ApplyAsync(plan, true, fixture.Progress, CancellationToken.None))
            .Should().ThrowAsync<InvalidOperationException>();
        using (Begin())
        {
            await FluentActions.Awaiting(() => fixture.Service.ApplyAsync(plan, true, fixture.Progress, CancellationToken.None))
                .Should().ThrowAsync<InvalidOperationException>();
        }
        await FluentActions.Awaiting(() => fixture.Service.ApplyAsync(plan, true, fixture.Progress, CancellationToken.None))
            .Should().ThrowAsync<InvalidOperationException>();
        fixture.Store.Prepares.Should().Be(0);
        fixture.AuditLog.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task ReviewRejectsMissingExpiredAndSystemHostContext()
    {
        var fixture = new SpeechComponentSetupFixture();
        await FluentActions.Awaiting(() => fixture.Review()).Should().ThrowAsync<InvalidOperationException>();
        using (Begin(RequestOrigin.HostSystem))
        {
            await FluentActions.Awaiting(() => fixture.Review()).Should().ThrowAsync<InvalidOperationException>();
        }
        using (Begin())
        {
            fixture.Eligible = false;
            await FluentActions.Awaiting(() => fixture.Review()).Should().ThrowAsync<InvalidOperationException>();
        }
        fixture.Store.Inspections.Should().Be(0);
    }

    [Fact]
    public async Task ReviewPreservesOriginalActivatedVoiceAuditAndHostIdentity()
    {
        var fixture = new SpeechComponentSetupFixture();
        using var hostile = new Activity("untrusted-provider").SetIdFormat(ActivityIdFormat.W3C).Start();
        using var host = Begin(RequestOrigin.ActivatedVoice);
        host.Activity!.ParentId.Should().BeNull();
        var plan = await fixture.Review();
        await fixture.Service.ApplyAsync(plan, true, fixture.Progress, CancellationToken.None);
        fixture.AuditLog.Events.Should().OnlyContain(item => item.Initiator == SecurityAuditInitiator.VoiceCommand);
        fixture.AuditLog.Requests.Should().OnlyContain(item => item.SessionId == host.Request.SessionId);
    }

    [Theory]
    [InlineData("shared")]
    [InlineData("changed")]
    [InlineData("functional")]
    [InlineData("integrity")]
    [InlineData("uncertain")]
    [InlineData("after-publication")]
    [InlineData("during-check")]
    [InlineData("changed-during-check")]
    public async Task FailedOrUncertainPreparationNeverReportsUsableSuccessAndAlwaysJoinsLease(string reason)
    {
        var fixture = new SpeechComponentSetupFixture();
        using var host = Begin();
        var plan = await fixture.Review();
        switch (reason)
        {
            case "shared": fixture.RuntimeAdapter.Quiescent = false; break;
            case "changed": fixture.Store.State = new(true, false, SpeechComponentOwnership.Foreign, "foreign"); break;
            case "functional": fixture.RuntimeAdapter.Functional = false; break;
            case "integrity": fixture.Store.CorruptPrepare = true; break;
            case "uncertain": fixture.Store.WriteFailure = new IOException("Ambiguous fixture publication."); break;
            case "after-publication": fixture.Store.AfterWrite = () => fixture.Eligible = false; break;
            case "during-check": fixture.RuntimeAdapter.BeforeCheck = () => fixture.Eligible = false; break;
            case "changed-during-check":
                fixture.RuntimeAdapter.BeforeCheck = () => fixture.Store.State = new(true, false, SpeechComponentOwnership.Managed, "changed");
                break;
        }
        await FluentActions.Awaiting(() => fixture.Service.ApplyAsync(plan, true, fixture.Progress, CancellationToken.None))
            .Should().ThrowAsync<Exception>();
        fixture.AuditLog.Events.Select(item => item.Outcome).Should().Equal(SecurityAuditOutcome.Requested, SecurityAuditOutcome.Failed);
        fixture.RuntimeAdapter.Releases.Should().Be(fixture.RuntimeAdapter.Leases);
        if (reason is "shared" or "changed") { fixture.Store.Prepares.Should().Be(0); }
        if (reason is "functional") { fixture.Store.State.Installed.Should().BeTrue(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationBeforeOrAfterPublicationRequiresFreshReconciliation(bool afterPublication)
    {
        var fixture = new SpeechComponentSetupFixture();
        using var host = Begin();
        using var cancellation = new CancellationTokenSource();
        var plan = await fixture.Review();
        if (afterPublication) { fixture.Store.AfterWrite = cancellation.Cancel; }
        else { fixture.Store.WriteFailure = new OperationCanceledException(cancellation.Token); }
        await FluentActions.Awaiting(() => fixture.Service.ApplyAsync(plan, true, fixture.Progress, cancellation.Token))
            .Should().ThrowAsync<OperationCanceledException>();
        fixture.RuntimeAdapter.Releases.Should().Be(1);
        fixture.AuditLog.Events.Last().Outcome.Should().Be(SecurityAuditOutcome.Cancelled);
        fixture.Store.State.Installed.Should().Be(afterPublication);
        await FluentActions.Awaiting(() => fixture.Service.ApplyAsync(plan, true, fixture.Progress, CancellationToken.None))
            .Should().ThrowAsync<InvalidOperationException>();
        if (afterPublication)
        {
            var retry = await fixture.Review(SpeechComponentOperation.Verify);
            (await fixture.Service.ApplyAsync(retry, true, fixture.Progress, CancellationToken.None)).FunctionalVerified.Should().BeTrue();
        }
    }

    [Fact]
    public async Task VerificationOfMissingDataAndIncompleteRemovalAreExplicitFailures()
    {
        var fixture = new SpeechComponentSetupFixture();
        using var host = Begin();
        var verify = await fixture.Review(SpeechComponentOperation.Verify);
        await FluentActions.Awaiting(() => fixture.Service.ApplyAsync(verify, true, fixture.Progress, CancellationToken.None))
            .Should().ThrowAsync<InvalidDataException>();
        fixture.Store.WrongRemoval = true;
        var remove = await fixture.Review(SpeechComponentOperation.Remove);
        await FluentActions.Awaiting(() => fixture.Service.ApplyAsync(remove, true, fixture.Progress, CancellationToken.None))
            .Should().ThrowAsync<InvalidDataException>();
        fixture.RuntimeAdapter.Checks.Should().Be(0);
        fixture.RuntimeAdapter.Releases.Should().Be(2);
    }

    [Theory]
    [InlineData(SecurityAuditOutcome.Requested)]
    [InlineData(SecurityAuditOutcome.Succeeded)]
    public async Task AuditFailureCannotMasqueradeAsSuccessfulSetup(SecurityAuditOutcome outcome)
    {
        var fixture = new SpeechComponentSetupFixture();
        using var host = Begin();
        var plan = await fixture.Review();
        fixture.AuditLog.FailOn = outcome;
        await FluentActions.Awaiting(() => fixture.Service.ApplyAsync(plan, true, fixture.Progress, CancellationToken.None))
            .Should().ThrowAsync<IOException>();
        fixture.Store.Prepares.Should().Be(outcome == SecurityAuditOutcome.Requested ? 0 : 1);
        fixture.RuntimeAdapter.Releases.Should().Be(fixture.RuntimeAdapter.Leases);
        fixture.AuditLog.Events.Should().NotContain(item => item.Outcome == SecurityAuditOutcome.Succeeded);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InspectionFailureOrCancellationNeverBecomesMissingReadiness(bool cancelled)
    {
        var fixture = new SpeechComponentSetupFixture();
        using var host = Begin();
        fixture.Store.InspectionFailure = cancelled ? new OperationCanceledException() : new IOException("Fixture read denied.");
        await FluentActions.Awaiting(() => fixture.Service.InspectAsync(fixture.Component.Identity, CancellationToken.None))
            .Should().ThrowAsync<Exception>();
        fixture.Store.Prepares.Should().Be(0);
        stopped.Last().Status.Should().Be(ActivityStatusCode.Error);
    }

    [Fact]
    public async Task LeaseRetirementFailureCannotPublishSuccessfulTerminalAudit()
    {
        var fixture = new SpeechComponentSetupFixture();
        using var host = Begin();
        var plan = await fixture.Review();
        fixture.RuntimeAdapter.ReleaseFailure = new IOException("Fixture exclusive ownership release failed.");
        await FluentActions.Awaiting(() => fixture.Service.ApplyAsync(plan, true, fixture.Progress, CancellationToken.None))
            .Should().ThrowAsync<IOException>();
        fixture.AuditLog.Events.Select(item => item.Outcome).Should().Equal(SecurityAuditOutcome.Requested, SecurityAuditOutcome.Failed);
        fixture.RuntimeAdapter.Releases.Should().Be(1);
        fixture.Store.State.Verified.Should().BeTrue();
    }

    [Fact]
    public async Task InvalidOperationsAndUnavailableCancellationFailExplicitly()
    {
        var fixture = new SpeechComponentSetupFixture();
        using var host = Begin();
        await FluentActions.Awaiting(() => fixture.Service.ReviewAsync(fixture.Component.Identity, (SpeechComponentOperation)99,
            () => true, CancellationToken.None)).Should().ThrowAsync<ArgumentOutOfRangeException>();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var unavailable = new UnavailableSpeechComponentRuntime();
        await FluentActions.Awaiting(async () => await unavailable.TryAcquireExclusiveAsync(fixture.Component, cancellation.Token))
            .Should().ThrowAsync<OperationCanceledException>();
        await FluentActions.Awaiting(() => unavailable.VerifyAsync(fixture.Component, cancellation.Token))
            .Should().ThrowAsync<OperationCanceledException>();
    }

    private static HostActivity Begin(RequestOrigin origin = RequestOrigin.LocalUi) =>
        HostActivity.BeginRoot(HostRequest.Create(origin), HostActivityLayer.Application, HostOperation.Request);
}
