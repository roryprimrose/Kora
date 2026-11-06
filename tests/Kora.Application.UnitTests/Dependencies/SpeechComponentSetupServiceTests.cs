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
        if (operation is SpeechComponentOperation.Verify or SpeechComponentOperation.Remove)
        {
            fixture.Store.State = ManagedInventory();
        }
        using var host = Begin();
        var plan = await fixture.Review(operation);
        plan.Disclosure.Should().Contain(fixture.Component.ModelIdentity).And.Contain(fixture.Component.Licence)
            .And.Contain("staging: 16 bytes").And.Contain("No runtime selection");
        plan.Disclosure.Should().Contain(operation == SpeechComponentOperation.Prepare ? "Download: 8 bytes" : "Download: 0 bytes");
        if (operation == SpeechComponentOperation.Remove)
        {
            plan.RemovalDisclosure.Should().Contain("Exact remaining inventoried bytes: 72")
                .And.Contain("Independent privacy and consent").And.Contain("Unavailable").And.Contain("No other profile or process");
        }
        var result = await fixture.Service.ApplyAsync(plan, true, fixture.Progress, CancellationToken.None);
        result.Identity.Should().Be(fixture.Component.Identity);
        result.Runtime.Compatibility.Should().Be(SpeechComponentCompatibility.Compatible);
        result.Qualified.Should().BeTrue();
        result.Runtime.Functional.Should().Be(operation == SpeechComponentOperation.Remove
            ? SpeechComponentFunctionalEvidence.NotChecked : SpeechComponentFunctionalEvidence.Verified);
        fixture.Store.Prepares.Should().Be(operation == SpeechComponentOperation.Prepare ? 1 : 0);
        fixture.Store.Removals.Should().Be(operation == SpeechComponentOperation.Remove ? 1 : 0);
        fixture.RuntimeAdapter.Holds.Should().Be(operation == SpeechComponentOperation.Remove ? 1 : 0);
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
        fixture.RuntimeAdapter.Compatibility = compatible ? SpeechComponentCompatibility.Compatible : SpeechComponentCompatibility.Incompatible;
        var definition = SpeechComponentFixture.Definition(qualified ? "fixture-qualified" : null);
        var service = new SpeechComponentSetupService(new([definition]), fixture.Store, fixture.RuntimeAdapter,
            fixture.AuditLog, NullLogger<SpeechComponentSetupService>.Instance, fixture.Time);
        service.Offers.Should().BeEmpty();
        using var host = Begin();
        var observed = await service.InspectAsync(definition.Identity, CancellationToken.None);
        observed.Qualified.Should().Be(qualified);
        observed.Runtime.Compatibility.Should().Be(fixture.RuntimeAdapter.Compatibility);
        await FluentActions.Awaiting(() => service.ReviewAsync(definition.Identity, SpeechComponentOperation.Prepare,
            () => true, CancellationToken.None)).Should().ThrowAsync<InvalidOperationException>();
        fixture.Store.Prepares.Should().Be(0);
    }

    [Fact]
    public async Task EmptyProductionAndUnavailableRuntimeCannotCreateAnOfferOrSuccessfulPreparation()
    {
        var fixture = new SpeechComponentSetupFixture();
        var runtime = new UnavailableSpeechComponentRuntime();
        runtime.Observe(fixture.Component).Compatibility.Should().Be(SpeechComponentCompatibility.Unavailable);
        (await runtime.TryAcquireExclusiveAsync(fixture.Component, CancellationToken.None)).State.Should().Be(SpeechComponentMutationState.Unavailable);
        (await runtime.VerifyAsync(fixture.Component, CancellationToken.None)).Functional.Should().Be(SpeechComponentFunctionalEvidence.Unavailable);
        FluentActions.Invoking(() => runtime.HoldUnavailable(fixture.Component)).Should().Throw<InvalidOperationException>();
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
        if (reason is "incompatible") { fixture.RuntimeAdapter.Compatibility = SpeechComponentCompatibility.Incompatible; }
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
            case "shared": fixture.RuntimeAdapter.MutationState = SpeechComponentMutationState.SharedOrUserOwned; break;
            case "changed": fixture.Store.State = new(true, false, SpeechComponentOwnership.Foreign, "foreign"); break;
            case "functional": fixture.RuntimeAdapter.Functional = SpeechComponentFunctionalEvidence.Failed; break;
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
        fixture.AuditLog.Events.Select(item => item.Outcome).Should().Equal(SecurityAuditOutcome.Requested,
            reason is "shared" ? SecurityAuditOutcome.Denied : SecurityAuditOutcome.Failed);
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
            (await fixture.Service.ApplyAsync(retry, true, fixture.Progress, CancellationToken.None)).Runtime.Functional.Should()
                .Be(SpeechComponentFunctionalEvidence.Verified);
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
        fixture.Store.State = ManagedInventory();
        var remove = await fixture.Review(SpeechComponentOperation.Remove);
        await FluentActions.Awaiting(() => fixture.Service.ApplyAsync(remove, true, fixture.Progress, CancellationToken.None))
            .Should().ThrowAsync<InvalidDataException>();
        fixture.RuntimeAdapter.Checks.Should().Be(0);
        fixture.RuntimeAdapter.Releases.Should().Be(2);
    }

    private static SpeechComponentSnapshot ManagedInventory() =>
        new(true, true, SpeechComponentOwnership.Managed, "verified")
        { ExactBytes = 72, Inventory = "model.data: 4 bytes; tokens.data: 4 bytes; ownership receipt: 64 bytes" };

    [Theory]
    [InlineData("missing")]
    [InlineData("foreign")]
    [InlineData("unknown-bytes")]
    [InlineData("unknown-inventory")]
    [InlineData("unknown-native")]
    [InlineData("unknown-consumers")]
    [InlineData("unknown-dependents")]
    [InlineData("voice")]
    public async Task RemovalCannotOfferConfirmationForUnknownInventoryOrNonNativeOrigin(string scenario)
    {
        var fixture = new SpeechComponentSetupFixture();
        fixture.Store.State = scenario switch
        {
            "missing" => new(false, false, SpeechComponentOwnership.Missing, "missing"),
            "foreign" => ManagedInventory() with { Ownership = SpeechComponentOwnership.Foreign },
            "unknown-bytes" => ManagedInventory() with { ExactBytes = null },
            "unknown-inventory" => ManagedInventory() with { Inventory = null },
            _ => ManagedInventory(),
        };
        if (scenario is "unknown-native") { fixture.RuntimeAdapter.NativeIdentity = null; }
        if (scenario is "unknown-consumers") { fixture.RuntimeAdapter.ConsumersKnown = false; }
        if (scenario is "unknown-dependents") { fixture.RuntimeAdapter.Dependents = null; }
        using var host = Begin(scenario is "voice" ? RequestOrigin.ActivatedVoice : RequestOrigin.LocalUi);
        await FluentActions.Awaiting(() => fixture.Review(SpeechComponentOperation.Remove))
            .Should().ThrowAsync<InvalidOperationException>();
        fixture.Store.Removals.Should().Be(0);
        fixture.RuntimeAdapter.Leases.Should().Be(0);
        fixture.AuditLog.Events.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ChangedConsumersOrNativeObjectInvalidatesReviewedRemoval(bool native)
    {
        var fixture = new SpeechComponentSetupFixture();
        fixture.Store.State = ManagedInventory() with { Ownership = SpeechComponentOwnership.RemovalPending };
        using var host = Begin();
        var plan = await fixture.Review(SpeechComponentOperation.Remove);
        if (native) { fixture.RuntimeAdapter.NativeIdentity = "replaced-native"; }
        else { fixture.RuntimeAdapter.ConsumerRevision++; }
        await FluentActions.Awaiting(() => fixture.Service.ApplyAsync(plan, true, fixture.Progress, TestContext.Current.CancellationToken))
            .Should().ThrowAsync<UnauthorizedAccessException>();
        fixture.Store.Removals.Should().Be(0);
        fixture.RuntimeAdapter.Leases.Should().Be(0);
        fixture.AuditLog.Events.Last().Outcome.Should().Be(SecurityAuditOutcome.Denied);
    }

    [Fact]
    public async Task ConsumerOwnershipBecomingUnknownAfterReviewCannotRetireOrRemoveAssets()
    {
        var fixture = new SpeechComponentSetupFixture();
        fixture.Store.State = ManagedInventory();
        using var host = Begin();
        var plan = await fixture.Review(SpeechComponentOperation.Remove);
        fixture.RuntimeAdapter.ConsumersKnown = false;
        await FluentActions.Awaiting(() => fixture.Service.ApplyAsync(plan, true, fixture.Progress, TestContext.Current.CancellationToken))
            .Should().ThrowAsync<UnauthorizedAccessException>();
        fixture.Store.Removals.Should().Be(0);
        fixture.RuntimeAdapter.Leases.Should().Be(0);
    }

    [Theory]
    [InlineData("inspection")]
    [InlineData("runtime")]
    [InlineData("storage")]
    public async Task StorageExceptionPathsNeverEnterDiagnosticsOrTrustedAudit(string failure)
    {
        var fixture = new SpeechComponentSetupFixture();
        const string privatePath = @"Q:\fixture-only-private-owner\speech-data.model";
        using var host = Begin();
        if (failure is "storage")
        {
            var plan = await fixture.Review();
            fixture.Store.WriteFailure = new IOException(privatePath);
            await FluentActions.Awaiting(() => fixture.Service.ApplyAsync(plan, true, fixture.Progress, TestContext.Current.CancellationToken))
                .Should().ThrowAsync<IOException>();
        }
        else
        {
            if (failure is "inspection") { fixture.Store.InspectionFailure = new IOException(privatePath); }
            else { fixture.RuntimeAdapter.ObservationFailure = new IOException(privatePath); }
            (await fixture.Service.InspectAsync(fixture.Component.Identity, TestContext.Current.CancellationToken))
                .Runtime.Compatibility.Should().Be(SpeechComponentCompatibility.Unknown);
        }
        fixture.Logger.Messages.Should().NotBeEmpty().And.NotContain(message => message.Contains(privatePath, StringComparison.Ordinal));
        fixture.Logger.Exceptions.Should().OnlyContain(exception => exception == null);
        fixture.AuditLog.Events.Should().NotContain(item => item.ReasonCode != null && item.ReasonCode.Contains(privatePath, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("io")]
    [InlineData("cancel")]
    [InlineData("release")]
    [InlineData("revoked")]
    [InlineData("recovery-revoked")]
    public async Task IncompleteRemovalReportsExactObservedRemainderOnlyWhileOriginalHostStillAdmitsDisclosure(string failure)
    {
        var fixture = new SpeechComponentSetupFixture();
        fixture.Store.State = ManagedInventory();
        using var host = Begin();
        var plan = await fixture.Review(SpeechComponentOperation.Remove);
        if (failure is "release") { fixture.RuntimeAdapter.ReleaseFailure = new IOException("Fixture join uncertain."); }
        else
        {
            fixture.Store.WriteFailure = failure is "cancel" ? new OperationCanceledException() : new IOException("Fixture partial I/O.");
            if (failure is "revoked") { fixture.Store.BeforeWrite = () => fixture.Eligible = false; }
            if (failure is "recovery-revoked")
            {
                fixture.Store.BeforeWrite = () => fixture.Store.AfterInspect = () => fixture.Eligible = false;
            }
        }
        await FluentActions.Awaiting(() => fixture.Service.ApplyAsync(plan, true, fixture.Progress, TestContext.Current.CancellationToken))
            .Should().ThrowAsync<Exception>();
        if (failure is "revoked" or "recovery-revoked") { plan.RemovalRecovery.Should().BeNull(); }
        else
        {
            plan.RemovalRecovery.Should().NotBeNull();
            plan.RemovalRecovery!.Storage.Should().Be(fixture.Store.State);
        }
        fixture.AuditLog.Events.Last().Outcome.Should().NotBe(SecurityAuditOutcome.Succeeded);
        fixture.Store.Prepares.Should().Be(0);
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
        if (cancelled)
        {
            await FluentActions.Awaiting(() => fixture.Service.InspectAsync(fixture.Component.Identity, CancellationToken.None))
                .Should().ThrowAsync<OperationCanceledException>();
        }
        else
        {
            var unknown = await fixture.Service.InspectAsync(fixture.Component.Identity, CancellationToken.None);
            unknown.Storage.Should().BeNull();
            unknown.Runtime.Compatibility.Should().Be(SpeechComponentCompatibility.Unknown);
            unknown.Runtime.Functional.Should().Be(SpeechComponentFunctionalEvidence.Unknown);
            unknown.Description.Should().Contain("Unknown").And.Contain("Fixture read denied.");
            unknown.CanOffer.Should().BeFalse();
        }
        fixture.Store.Prepares.Should().Be(0);
        stopped.Last().Status.Should().Be(ActivityStatusCode.Error);
    }

    [Fact]
    public async Task InspectionReportsOnlyExistingEvidenceAndNeverAcquiresOrRunsFunctionalChecks()
    {
        var fixture = new SpeechComponentSetupFixture();
        using var host = Begin();
        var untested = await fixture.Service.InspectAsync(fixture.Component.Identity, TestContext.Current.CancellationToken);
        untested.Runtime.Functional.Should().Be(SpeechComponentFunctionalEvidence.NotChecked);
        fixture.RuntimeAdapter.ExistingFunctional = SpeechComponentFunctionalEvidence.Verified;
        var existing = await fixture.Service.InspectAsync(fixture.Component.Identity, TestContext.Current.CancellationToken);
        existing.Runtime.Functional.Should().Be(SpeechComponentFunctionalEvidence.Verified);
        fixture.RuntimeAdapter.Checks.Should().Be(0);
        fixture.RuntimeAdapter.Leases.Should().Be(0);
        fixture.Store.Prepares.Should().Be(0);
        fixture.Store.Removals.Should().Be(0);
        fixture.AuditLog.Events.Should().BeEmpty();
    }

    [Theory]
    [InlineData(SpeechComponentMutationState.Unavailable, "runtime-unavailable")]
    [InlineData(SpeechComponentMutationState.Busy, "component-in-use")]
    [InlineData(SpeechComponentMutationState.UnknownOwnership, "ownership-unknown")]
    [InlineData(SpeechComponentMutationState.SharedOrUserOwned, "shared-or-user-owned")]
    [InlineData(SpeechComponentMutationState.Denied, "mutation-denied")]
    public async Task ExclusiveOwnershipDenialIsTypedAuditedAndNeverBecomesANoopLease(
        SpeechComponentMutationState state, string reason)
    {
        var fixture = new SpeechComponentSetupFixture();
        using var host = Begin();
        var plan = await fixture.Review();
        fixture.RuntimeAdapter.MutationState = state;
        var failure = await FluentActions.Awaiting(() => fixture.Service.ApplyAsync(plan, true, fixture.Progress,
            TestContext.Current.CancellationToken)).Should().ThrowAsync<SpeechComponentMutationDeniedException>();
        failure.Which.State.Should().Be(state);
        failure.Which.ReasonCode.Should().Be(reason);
        failure.Which.Message.Should().NotBeNullOrWhiteSpace();
        fixture.AuditLog.Events.Select(item => item.Outcome).Should().Equal(SecurityAuditOutcome.Requested, SecurityAuditOutcome.Denied);
        fixture.AuditLog.Events.Last().ReasonCode.Should().Be(reason);
        fixture.RuntimeAdapter.Leases.Should().Be(0);
        fixture.RuntimeAdapter.Checks.Should().Be(0);
        fixture.Store.Prepares.Should().Be(0);
        fixture.Store.Removals.Should().Be(0);
    }

    [Theory]
    [InlineData(SpeechComponentCompatibility.Unknown)]
    [InlineData(SpeechComponentCompatibility.Unavailable)]
    [InlineData(SpeechComponentCompatibility.Incompatible)]
    public async Task UnknownOrUnavailableCompatibilityIsNotConflatedWithVerifiedCompatibility(SpeechComponentCompatibility state)
    {
        var fixture = new SpeechComponentSetupFixture();
        fixture.RuntimeAdapter.Compatibility = state;
        using var host = Begin();
        fixture.Service.Components.Should().ContainSingle();
        fixture.Service.Offers.Should().BeEmpty();
        var observed = await fixture.Service.InspectAsync(fixture.Component.Identity, TestContext.Current.CancellationToken);
        observed.Runtime.Compatibility.Should().Be(state);
        observed.Description.Should().Contain(state.ToString());
        observed.CanOffer.Should().BeFalse();
        await FluentActions.Awaiting(() => fixture.Review()).Should().ThrowAsync<InvalidOperationException>();
        fixture.RuntimeAdapter.Checks.Should().Be(0);
        fixture.RuntimeAdapter.Leases.Should().Be(0);
    }

    [Theory]
    [InlineData(SpeechComponentFunctionalEvidence.NotChecked)]
    [InlineData(SpeechComponentFunctionalEvidence.Failed)]
    [InlineData(SpeechComponentFunctionalEvidence.Unknown)]
    [InlineData(SpeechComponentFunctionalEvidence.Unavailable)]
    public async Task ExplicitVerificationRequiresAuthoritativeVerifiedEvidence(SpeechComponentFunctionalEvidence evidence)
    {
        var fixture = new SpeechComponentSetupFixture();
        fixture.RuntimeAdapter.Functional = evidence;
        using var host = Begin();
        var plan = await fixture.Review();
        var failure = await FluentActions.Awaiting(() => fixture.Service.ApplyAsync(plan, true, fixture.Progress,
            TestContext.Current.CancellationToken)).Should().ThrowAsync<InvalidDataException>();
        failure.Which.Message.Should().Contain(evidence.ToString());
        fixture.RuntimeAdapter.Releases.Should().Be(1);
        fixture.AuditLog.Events.Last().Outcome.Should().Be(SecurityAuditOutcome.Failed);
    }

    [Fact]
    public async Task UnknownStorageOrRuntimeEvidenceCannotProduceAReviewOrMissingReadiness()
    {
        var fixture = new SpeechComponentSetupFixture();
        using var host = Begin();
        fixture.Store.InspectionFailure = new UnauthorizedAccessException("Fixture storage ownership unknown.");
        var unknown = await fixture.Service.InspectAsync(fixture.Component.Identity, TestContext.Current.CancellationToken);
        unknown.Storage.Should().BeNull();
        unknown.Description.Should().Contain("Unknown").And.Contain("ownership");
        await FluentActions.Awaiting(() => fixture.Review()).Should().ThrowAsync<InvalidOperationException>();
        fixture.Store.InspectionFailure = null;
        fixture.RuntimeAdapter.ObservationFailure = new IOException("Fixture adapter record unreadable.");
        fixture.Service.Offers.Should().BeEmpty();
        var missingButUnknownRuntime = await fixture.Service.InspectAsync(fixture.Component.Identity, TestContext.Current.CancellationToken);
        missingButUnknownRuntime.Storage?.Installed.Should().BeFalse();
        missingButUnknownRuntime.Runtime.Compatibility.Should().Be(SpeechComponentCompatibility.Unknown);
        missingButUnknownRuntime.Runtime.Functional.Should().Be(SpeechComponentFunctionalEvidence.Unknown);
        fixture.RuntimeAdapter.Checks.Should().Be(0);
        fixture.RuntimeAdapter.Leases.Should().Be(0);
    }

    [Fact]
    public async Task UnknownCatalogueIdentityIsDeniedBeforeAnyStorageOrRuntimeWork()
    {
        var fixture = new SpeechComponentSetupFixture();
        using var host = Begin();
        await FluentActions.Awaiting(() => fixture.Service.InspectAsync(new("not-catalogued", "v1"),
            TestContext.Current.CancellationToken)).Should().ThrowAsync<InvalidOperationException>();
        fixture.Store.Inspections.Should().Be(0);
        fixture.RuntimeAdapter.Checks.Should().Be(0);
        fixture.RuntimeAdapter.Leases.Should().Be(0);
    }

    [Fact]
    public async Task UnknownPostPublicationReadbackCannotProduceSuccessAndRetiresTheExclusiveLease()
    {
        var fixture = new SpeechComponentSetupFixture();
        using var host = Begin();
        var plan = await fixture.Review();
        fixture.Store.AfterWrite = () => fixture.Store.InspectionFailure = new IOException("Fixture post-publication readback unavailable.");
        await FluentActions.Awaiting(() => fixture.Service.ApplyAsync(plan, true, fixture.Progress, TestContext.Current.CancellationToken))
            .Should().ThrowAsync<InvalidDataException>();
        fixture.RuntimeAdapter.Releases.Should().Be(1);
        fixture.Store.State.Installed.Should().BeTrue();
        fixture.AuditLog.Events.Select(item => item.Outcome).Should().Equal(SecurityAuditOutcome.Requested, SecurityAuditOutcome.Failed);
    }

    [Fact]
    public async Task MissingMutationObservationIsAnAuditedUnknownOwnershipDenial()
    {
        var fixture = new SpeechComponentSetupFixture();
        using var host = Begin();
        var plan = await fixture.Review();
        fixture.RuntimeAdapter.ReturnNoMutation = true;
        var failure = await FluentActions.Awaiting(() => fixture.Service.ApplyAsync(plan, true, fixture.Progress,
            TestContext.Current.CancellationToken)).Should().ThrowAsync<SpeechComponentMutationDeniedException>();
        failure.Which.State.Should().Be(SpeechComponentMutationState.UnknownOwnership);
        fixture.AuditLog.Events.Last().ReasonCode.Should().Be("ownership-unknown");
        fixture.Store.Prepares.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingOrIncompatibleFunctionalObservationCannotEnableAComponent(bool missing)
    {
        var fixture = new SpeechComponentSetupFixture();
        using var host = Begin();
        var plan = await fixture.Review();
        fixture.RuntimeAdapter.ReturnNoVerification = missing;
        if (!missing)
        {
            fixture.RuntimeAdapter.BeforeCheck = () => fixture.RuntimeAdapter.Compatibility = SpeechComponentCompatibility.Incompatible;
        }
        await FluentActions.Awaiting(() => fixture.Service.ApplyAsync(plan, true, fixture.Progress,
            TestContext.Current.CancellationToken)).Should().ThrowAsync<InvalidDataException>();
        fixture.RuntimeAdapter.Releases.Should().Be(1);
        fixture.AuditLog.Events.Last().Outcome.Should().Be(SecurityAuditOutcome.Failed);
    }

    [Fact]
    public async Task MissingReadOnlyRuntimeObservationRemainsUnknownWithoutProbing()
    {
        var fixture = new SpeechComponentSetupFixture();
        fixture.RuntimeAdapter.ReturnNoObservation = true;
        using var host = Begin();
        var state = await fixture.Service.InspectAsync(fixture.Component.Identity, TestContext.Current.CancellationToken);
        state.Runtime.Compatibility.Should().Be(SpeechComponentCompatibility.Unknown);
        fixture.RuntimeAdapter.Checks.Should().Be(0);
        fixture.Service.Offers.Should().BeEmpty();
    }

    [Fact]
    public async Task HostRevocationWithUnchangedStorageBeforeMutationStopsTheOperation()
    {
        var fixture = new SpeechComponentSetupFixture();
        using var host = Begin();
        var plan = await fixture.Review();
        fixture.Store.AfterInspect = () => fixture.Eligible = false;
        await FluentActions.Awaiting(() => fixture.Service.ApplyAsync(plan, true, fixture.Progress,
            TestContext.Current.CancellationToken)).Should().ThrowAsync<InvalidOperationException>();
        fixture.Store.Prepares.Should().Be(0);
        fixture.RuntimeAdapter.Releases.Should().Be(1);
    }

    [Fact]
    public async Task FinalAdmissionRevocationAfterSuccessfulReadbackStillCannotCommitSuccess()
    {
        var fixture = new SpeechComponentSetupFixture();
        using var host = Begin();
        var plan = await fixture.Review();
        var checksAfterReadback = 0;
        fixture.Store.AfterInspect = () =>
        {
            if (fixture.Store.Inspections == 4)
            {
                fixture.AdmissionOverride = () => ++checksAfterReadback == 1;
            }
        };
        await FluentActions.Awaiting(() => fixture.Service.ApplyAsync(plan, true, fixture.Progress,
            TestContext.Current.CancellationToken)).Should().ThrowAsync<UnauthorizedAccessException>();
        checksAfterReadback.Should().Be(2);
        fixture.RuntimeAdapter.Releases.Should().Be(1);
        fixture.AuditLog.Events.Last().Outcome.Should().Be(SecurityAuditOutcome.Failed);
    }

    [Fact]
    public async Task CompatibilityLossDuringReadbackCannotPublishAFunctionalSuccess()
    {
        var fixture = new SpeechComponentSetupFixture();
        using var host = Begin();
        var plan = await fixture.Review();
        fixture.Store.AfterInspect = () =>
        {
            if (fixture.Store.Inspections == 4) { fixture.RuntimeAdapter.Compatibility = SpeechComponentCompatibility.Unknown; }
        };
        await FluentActions.Awaiting(() => fixture.Service.ApplyAsync(plan, true, fixture.Progress,
            TestContext.Current.CancellationToken)).Should().ThrowAsync<InvalidDataException>();
        fixture.RuntimeAdapter.Releases.Should().Be(1);
        fixture.AuditLog.Events.Last().Outcome.Should().Be(SecurityAuditOutcome.Failed);
    }

    [Fact]
    public async Task VerifiedExistingDataReviewDisclosesZeroTransferAndRejectsUnknownStoragePlans()
    {
        var fixture = new SpeechComponentSetupFixture();
        fixture.Store.State = new(true, true, SpeechComponentOwnership.Managed, "verified");
        using var host = Begin();
        var plan = await fixture.Review();
        plan.Disclosure.Should().Contain("Download: 0 bytes");
        var unknown = new SpeechComponentReadiness(fixture.Component.Identity, null,
            fixture.RuntimeAdapter.Observe(fixture.Component), true, "Unknown fixture storage.");
        FluentActions.Invoking(() => new SpeechComponentSetupPlan(Guid.NewGuid(), fixture.Component,
            SpeechComponentOperation.Prepare, unknown, host.Request, () => true, fixture.Time.Now))
            .Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DiagnosticEventsPreserveAllLifecycleOutcomesWithLoggingEnabledOrDisabled(bool enabled)
    {
        var fixture = new SpeechComponentSetupFixture();
        fixture.Logger.Enabled = enabled;
        using var host = Begin();
        var plan = await fixture.Review();
        await fixture.Service.ApplyAsync(plan, true, fixture.Progress, TestContext.Current.CancellationToken);
        plan = await fixture.Review();
        fixture.RuntimeAdapter.MutationState = SpeechComponentMutationState.Busy;
        await FluentActions.Awaiting(() => fixture.Service.ApplyAsync(plan, true, fixture.Progress,
            TestContext.Current.CancellationToken)).Should().ThrowAsync<SpeechComponentMutationDeniedException>();
        fixture.RuntimeAdapter.MutationState = SpeechComponentMutationState.Acquired;
        plan = await fixture.Review();
        fixture.Store.WriteFailure = new OperationCanceledException();
        await FluentActions.Awaiting(() => fixture.Service.ApplyAsync(plan, true, fixture.Progress,
            TestContext.Current.CancellationToken)).Should().ThrowAsync<OperationCanceledException>();
        fixture.Store.WriteFailure = new TimeoutException("Fixture transfer timed out.");
        plan = await fixture.Review();
        await FluentActions.Awaiting(() => fixture.Service.ApplyAsync(plan, true, fixture.Progress,
            TestContext.Current.CancellationToken)).Should().ThrowAsync<TimeoutException>();
        fixture.Store.InspectionFailure = new IOException("Fixture read unavailable.");
        (await fixture.Service.InspectAsync(fixture.Component.Identity, TestContext.Current.CancellationToken)).Storage.Should().BeNull();
        fixture.RuntimeAdapter.Releases.Should().Be(fixture.RuntimeAdapter.Leases);
        fixture.AuditLog.Events.Count(item => item.Outcome == SecurityAuditOutcome.Succeeded).Should().Be(1);
    }

    [Fact]
    public async Task AuditedCancellationPreservesTheOriginalExceptionTokenAndCancelledTask()
    {
        var fixture = new SpeechComponentSetupFixture();
        using var host = Begin();
        var plan = await fixture.Review();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var original = new OperationCanceledException("Fixture transfer cancelled.", cancellation.Token);
        fixture.Store.WriteFailure = original;
        var operation = fixture.Service.ApplyAsync(plan, true, fixture.Progress, TestContext.Current.CancellationToken);
        var failure = await FluentActions.Awaiting(() => operation).Should().ThrowAsync<OperationCanceledException>();
        failure.Which.Should().BeSameAs(original);
        failure.Which.CancellationToken.Should().Be(cancellation.Token);
        operation.IsCanceled.Should().BeTrue();
        fixture.RuntimeAdapter.Releases.Should().Be(1);
        fixture.AuditLog.Events.Last().Outcome.Should().Be(SecurityAuditOutcome.Cancelled);
    }

    [Fact]
    public async Task UnexpectedProgrammingFailurePropagatesWithoutAFalseTerminalSuccessAndStillRetiresLease()
    {
        var fixture = new SpeechComponentSetupFixture();
        using var host = Begin();
        var plan = await fixture.Review();
        fixture.Store.WriteFailure = new ArgumentException("Fixture invalid platform contract.");
        await FluentActions.Awaiting(() => fixture.Service.ApplyAsync(plan, true, fixture.Progress,
            TestContext.Current.CancellationToken)).Should().ThrowAsync<ArgumentException>();
        fixture.RuntimeAdapter.Releases.Should().Be(1);
        fixture.AuditLog.Events.Select(item => item.Outcome).Should().Equal(SecurityAuditOutcome.Requested);
        stopped.Where(activity => string.Equals(activity.DisplayName, "storage.commit", StringComparison.Ordinal))
            .Should().OnlyContain(activity => activity.Status == ActivityStatusCode.Error);
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