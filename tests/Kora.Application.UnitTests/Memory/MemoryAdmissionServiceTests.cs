using System.Diagnostics;
using System.Collections.Immutable;
using AwesomeAssertions;
using Kora.Application.Memory;
using Kora.Core.Auditing;
using Kora.Core.Authorization;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Core.Memory;
using Kora.Core.Storage;
using Kora.Core.Tools;
using Microsoft.Extensions.Logging;

namespace Kora.Application.UnitTests.Memory;

[Collection("Host tracing")]
public sealed partial class MemoryAdmissionServiceTests : IDisposable
{
    private readonly ActivityListener listener = new()
    {
        ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
        Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
    };

    public MemoryAdmissionServiceTests() => ActivitySource.AddActivityListener(listener);
    public void Dispose() => listener.Dispose();
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static MemoryCandidate Candidate => new(MemoryContentClass.ResponsePreference, "user-content-not-for-logs");

    [Fact]
    public async Task Durable_admission_keeps_proposals_off_disk_reloads_exact_records_and_redacts_edits()
    {
        using var fixture = new Fixture(durable: true);
        using var root = fixture.Root();
        var proposal = await fixture.Propose();
        fixture.Stored.Should().BeEmpty();
        var reviewed = (await fixture.Service.ReviewAsync(proposal.Id, proposal.Revision, true, Token)).Record!;
        fixture.Stored.Should().BeEmpty();
        var admitted = (await fixture.Service.AdmitAsync(reviewed.Id, reviewed.Revision, Token)).Record!;
        fixture.Stored.Single().Value.Should().Be(admitted);
        await fixture.Service.UseAsync(admitted.Id, admitted.Revision, MemoryDestination.Local, Token);
        var disabled = (await fixture.Service.DisableAsync(admitted.Id, admitted.Revision, Token)).Record!;
        fixture.Stored.Single().Value.Should().Be(disabled);
        var edited = (await fixture.Service.EditAsync(disabled.Id, disabled.Revision, Candidate, Token)).Record!;
        fixture.Stored.Single().Value.Candidate.Should().BeNull();
        fixture.Stored.Single().Value.Receipt.Should().BeNull();
        using (var pendingRestart = fixture.CreateService(durable: true))
        {
            (await pendingRestart.ReviewAsync(edited.Id, edited.Revision, true, Token)).Outcome
                .Should().Be(MemoryOutcome.InvalidTransition);
        }
        var reviewedEdit = (await fixture.Service.ReviewAsync(edited.Id, edited.Revision, true, Token)).Record!;
        fixture.Stored.Single().Value.Review.Should().Be(MemoryReviewState.Proposed);
        var readmit = (await fixture.Service.AdmitAsync(reviewedEdit.Id, reviewedEdit.Revision, Token)).Record!;
        fixture.Service.Dispose();
        using var restarted = fixture.CreateService(durable: true);
        var used = await restarted.UseAsync(readmit.Id, readmit.Revision, MemoryDestination.Local, Token);
        used.Use!.Candidate.Should().Be(Candidate);
        var forgotten = (await restarted.ForgetAsync(readmit.Id, readmit.Revision, Token)).Record!;
        fixture.Stored.Single().Value.Should().Be(forgotten);
        fixture.Stored.Single().Value.Candidate.Should().BeNull();
        (await restarted.ForgetAsync(forgotten.Id, forgotten.Revision, Token)).Outcome.Should().Be(MemoryOutcome.InvalidTransition);
    }

    [Fact]
    public async Task Durable_restart_refuses_stale_revisions_and_unavailable_scopes_without_publishing()
    {
        using var fixture = new Fixture(durable: true);
        using var root = fixture.Root();
        var admitted = await fixture.Admitted();
        fixture.Stored[admitted.Id] = admitted with { Revision = new(admitted.Revision.Value + 1), Retention = MemoryRetentionState.Disabled };
        (await fixture.Service.UseAsync(admitted.Id, admitted.Revision, MemoryDestination.Local, Token)).Outcome
            .Should().Be(MemoryOutcome.RevisionConflict);
        fixture.Scope = MemoryScope.DeviceProfile(fixture.Profile);
        (await fixture.Service.ProposeAsync(Candidate, fixture.Scope, MemoryProposalOrigin.User, Token)).Reason
            .Should().Be(MemoryReason.ScopeMismatch);
        fixture.Scope = MemoryScope.Session(fixture.Request.SessionId);
        (await fixture.Service.ProposeAsync(Candidate, null, MemoryProposalOrigin.User, Token)).Reason
            .Should().Be(MemoryReason.ScopeMismatch);
        fixture.OnStoreTransition = () => fixture.Session = fixture.Session with { IsActive = false };
        var use = () => fixture.Service.UseAsync(admitted.Id, new(admitted.Revision.Value + 1), MemoryDestination.Local, Token);
        await use.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Expired_durable_session_cannot_reload_content_or_publish_after_store_capacity_denial()
    {
        using var fixture = new Fixture(durable: true);
        using var root = fixture.Root();
        var proposal = await fixture.Propose();
        var reviewed = (await fixture.Service.ReviewAsync(proposal.Id, proposal.Revision, true, Token)).Record!;
        fixture.StoreResult = new(MemoryOutcome.CapacityExceeded, MemoryReason.Capacity);
        (await fixture.Service.AdmitAsync(reviewed.Id, reviewed.Revision, Token)).Outcome
            .Should().Be(MemoryOutcome.CapacityExceeded);
        fixture.Audits.Last().Outcome.Should().Be(SecurityAuditOutcome.Denied);
        fixture.StoreResult = null;
        (await fixture.Service.UseAsync(reviewed.Id, reviewed.Revision, MemoryDestination.Local, Token)).Reason
            .Should().Be(MemoryReason.NotReviewed);
        var admitted = (await fixture.Service.AdmitAsync(reviewed.Id, reviewed.Revision, Token)).Record!;
        fixture.Service.ExpireSession(fixture.Request.SessionId);
        (await fixture.Service.UseAsync(admitted.Id, admitted.Revision, MemoryDestination.Local, Token)).Reason
            .Should().Be(MemoryReason.AuthorityClosed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Durable_audit_or_commit_failure_never_publishes_a_successful_mutation(bool auditFailure)
    {
        using var fixture = new Fixture(durable: true);
        using var root = fixture.Root();
        var proposal = await fixture.Propose();
        var reviewed = (await fixture.Service.ReviewAsync(proposal.Id, proposal.Revision, true, Token)).Record!;
        fixture.FailTerminalAudit = auditFailure;
        fixture.FailStoreCommit = !auditFailure;
        var action = () => fixture.Service.AdmitAsync(reviewed.Id, reviewed.Revision, Token);
        await action.Should().ThrowAsync<IOException>();
        fixture.Stored.Should().BeEmpty();
        fixture.FailTerminalAudit = false;
        fixture.FailStoreCommit = false;
        (await fixture.Service.UseAsync(reviewed.Id, reviewed.Revision, MemoryDestination.Local, Token)).Reason
            .Should().Be(MemoryReason.NotReviewed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Durable_callback_and_commit_fences_refuse_lifecycle_changes(bool atCommit)
    {
        using var fixture = new Fixture(durable: true);
        using var root = fixture.Root();
        var proposal = await fixture.Propose();
        var reviewed = (await fixture.Service.ReviewAsync(proposal.Id, proposal.Revision, true, Token)).Record!;
        if (atCommit) { fixture.OnStoreCommit = () => fixture.Service.Dispose(); }
        else { fixture.OnTerminalAudit = () => fixture.Service.Dispose(); }
        var action = () => fixture.Service.AdmitAsync(reviewed.Id, reviewed.Revision, Token);
        await action.Should().ThrowAsync<InvalidOperationException>();
        fixture.Stored.Should().BeEmpty();
    }

    [Fact]
    public async Task Durable_transition_rechecks_authority_after_delayed_store_read()
    {
        using var fixture = new Fixture(durable: true);
        using var root = fixture.Root();
        fixture.OnStoreTransition = () => fixture.ControlRevision++;
        var action = () => fixture.Service.ProposeAsync(Candidate, fixture.Scope, MemoryProposalOrigin.User, Token);
        await action.Should().ThrowAsync<InvalidOperationException>();
        fixture.Stored.Should().BeEmpty();
    }

    [Fact]
    public async Task Late_committed_admission_cannot_overwrite_a_newer_durable_forget_in_the_cache()
    {
        using var fixture = new Fixture(durable: true);
        using var root = fixture.Root();
        var proposal = await fixture.Propose();
        var reviewed = (await fixture.Service.ReviewAsync(proposal.Id, proposal.Revision, true, Token)).Record!;
        MemoryRecord? forgotten = null;
        fixture.OnAfterStoreCommit = () =>
        {
            fixture.OnAfterStoreCommit = null;
            forgotten = fixture.Service.ForgetAsync(reviewed.Id, new(reviewed.Revision.Value + 1), Token)
                .GetAwaiter().GetResult().Record!;
        };
        var admitted = (await fixture.Service.AdmitAsync(reviewed.Id, reviewed.Revision, Token)).Record!;
        forgotten.Should().NotBeNull();
        fixture.Stored.Single().Value.Should().Be(forgotten);
        (await fixture.Service.UseAsync(forgotten!.Id, forgotten.Revision, MemoryDestination.Local, Token)).Reason
            .Should().Be(MemoryReason.NotEnabled);
        (await fixture.Service.UseAsync(admitted.Id, admitted.Revision, MemoryDestination.Local, Token)).Outcome
            .Should().Be(MemoryOutcome.RevisionConflict);
    }

    [Fact]
    public async Task Production_session_scope_resolver_fails_closed_on_host_control_session_and_revision_changes()
    {
        using var fixture = new Fixture();
        using var root = fixture.Root();
        var scope = new SessionMemoryScopeAccess(fixture, fixture, fixture);
        scope.Observe(fixture.Request).Should().BeNull();
        (await scope.ResolveAsync(fixture.Request, Token)).Should().Be(fixture.Boundary);
        scope.Observe(HostRequest.Create(RequestOrigin.LocalUi)).Should().BeNull();
        fixture.ControlRevision++;
        scope.Observe(fixture.Request).Should().BeNull();
        fixture.Boundary = fixture.Boundary! with { Revision = fixture.ControlRevision };
        (await scope.ResolveAsync(fixture.Request, Token)).Should().NotBeNull();
        fixture.Current = false;
        scope.Observe(fixture.Request).Should().BeNull();
        (await scope.ResolveAsync(fixture.Request, Token)).Should().BeNull();
        fixture.Current = true;
        fixture.CanControl = false;
        scope.Observe(fixture.Request).Should().BeNull();
        (await scope.ResolveAsync(fixture.Request, Token)).Should().BeNull();
        fixture.CanControl = true;
        fixture.OnBoundaryRead = () => fixture.ControlRevision++;
        (await scope.ResolveAsync(fixture.Request, Token)).Should().BeNull();
    }

    [Fact]
    public async Task Model_proposals_have_host_ids_lineage_and_no_implicit_admission_use_or_persistence()
    {
        using var fixture = new Fixture();
        using var root = fixture.Root(RequestOrigin.HostSystem);
        var proposal = await fixture.Propose(origin: MemoryProposalOrigin.Model);
        proposal.Review.Should().Be(MemoryReviewState.Proposed);
        proposal.Retention.Should().Be(MemoryRetentionState.Pending);
        proposal.Receipt.Should().BeNull();
        proposal.Id.Validate();
        proposal.Revision.Value.Should().Be(1);
        proposal.Lineage.Request.Should().BeSameAs(root.Request);
        proposal.Lineage.Origin.Should().Be(MemoryProposalOrigin.Model);
        proposal.CreatedAt.Should().Be(DateTimeOffset.UnixEpoch);
        (await fixture.Service.UseAsync(proposal.Id, proposal.Revision, MemoryDestination.Local, Token)).Reason.Should().Be(MemoryReason.NotReviewed);
        (await fixture.Service.ReviewAsync(proposal.Id, proposal.Revision, true, Token)).Reason.Should().Be(MemoryReason.UserIntentRequired);
        (await fixture.Service.AdmitAsync(proposal.Id, proposal.Revision, Token)).Reason.Should().Be(MemoryReason.UserIntentRequired);
        (await fixture.Service.EditAsync(proposal.Id, proposal.Revision, Candidate, Token)).Reason.Should().Be(MemoryReason.UserIntentRequired);
        (await fixture.Service.DisableAsync(proposal.Id, proposal.Revision, Token)).Reason.Should().Be(MemoryReason.UserIntentRequired);
        (await fixture.Service.ForgetAsync(proposal.Id, proposal.Revision, Token)).Reason.Should().Be(MemoryReason.UserIntentRequired);
        fixture.Writes.Should().Be(0);
    }

    [Theory]
    [InlineData(RequestOrigin.LocalUi)]
    [InlineData(RequestOrigin.ActivatedVoice)]
    public async Task Exact_explicit_review_and_admission_enable_only_local_use_with_ids_and_provenance(RequestOrigin origin)
    {
        using var fixture = new Fixture();
        using var root = fixture.Root(origin);
        var proposal = await fixture.Propose(origin: MemoryProposalOrigin.Model);
        var reviewed = (await fixture.Service.ReviewAsync(proposal.Id, proposal.Revision, true, Token)).Record!;
        reviewed.Review.Should().Be(MemoryReviewState.Reviewed);
        reviewed.Retention.Should().Be(MemoryRetentionState.Pending);
        reviewed.Receipt!.Request.Should().Be(root.Request.RequestId);
        reviewed.Receipt.Revision.Should().Be(reviewed.Revision);
        (await fixture.Service.UseAsync(reviewed.Id, reviewed.Revision, MemoryDestination.Local, Token)).Reason.Should().Be(MemoryReason.NotReviewed);
        var admitted = (await fixture.Service.AdmitAsync(reviewed.Id, reviewed.Revision, Token)).Record!;
        admitted.Review.Should().Be(MemoryReviewState.Admitted);
        var use = (await fixture.Service.UseAsync(admitted.Id, admitted.Revision, MemoryDestination.Local, Token)).Use!;
        use.Id.Should().Be(admitted.Id);
        use.Revision.Should().Be(admitted.Revision);
        use.Candidate.Should().Be(Candidate);
        use.Lineage.Should().Be(admitted.Lineage);
        use.Receipt.Should().Be(reviewed.Receipt);
        use.Scope.Should().Be(admitted.Scope);
        use.UsedBy.Should().BeSameAs(root.Request);
        (await fixture.Service.UseAsync(admitted.Id, admitted.Revision, MemoryDestination.Hosted, Token)).Reason.Should().Be(MemoryReason.DisclosureNotAdmitted);
        (await fixture.Service.UseAsync(admitted.Id, admitted.Revision, MemoryDestination.Unknown, Token)).Use.Should().BeNull();
        fixture.Writes.Should().Be(0);
    }

    [Fact]
    public async Task Rejection_and_invalid_transitions_are_explicit_and_edits_require_fresh_review()
    {
        using var fixture = new Fixture();
        using var root = fixture.Root();
        var proposal = await fixture.Propose();
        (await fixture.Service.AdmitAsync(proposal.Id, proposal.Revision, Token)).Outcome.Should().Be(MemoryOutcome.InvalidTransition);
        (await fixture.Service.DisableAsync(proposal.Id, proposal.Revision, Token)).Outcome.Should().Be(MemoryOutcome.InvalidTransition);
        var rejected = (await fixture.Service.ReviewAsync(proposal.Id, proposal.Revision, false, Token)).Record!;
        rejected.Review.Should().Be(MemoryReviewState.Rejected);
        rejected.Receipt.Should().BeNull();
        (await fixture.Service.ReviewAsync(rejected.Id, rejected.Revision, true, Token)).Outcome.Should().Be(MemoryOutcome.InvalidTransition);
        (await fixture.Service.AdmitAsync(rejected.Id, rejected.Revision, Token)).Outcome.Should().Be(MemoryOutcome.InvalidTransition);
        var edited = (await fixture.Service.EditAsync(rejected.Id, rejected.Revision,
            new(MemoryContentClass.Decision, "new exact value"), Token)).Record!;
        edited.Review.Should().Be(MemoryReviewState.Proposed);
        edited.Retention.Should().Be(MemoryRetentionState.Pending);
        edited.Lineage.Origin.Should().Be(MemoryProposalOrigin.User);
        var reviewed = (await fixture.Service.ReviewAsync(edited.Id, edited.Revision, true, Token)).Record!;
        var admitted = (await fixture.Service.AdmitAsync(reviewed.Id, reviewed.Revision, Token)).Record!;
        var replacement = (await fixture.Service.EditAsync(admitted.Id, admitted.Revision, Candidate, Token)).Record!;
        replacement.Receipt.Should().BeNull();
        (await fixture.Service.UseAsync(replacement.Id, replacement.Revision, MemoryDestination.Local, Token)).Use.Should().BeNull();
        (await fixture.Service.AdmitAsync(replacement.Id, replacement.Revision, Token)).Outcome.Should().Be(MemoryOutcome.InvalidTransition);
    }

    [Fact]
    public async Task Disable_and_forget_do_not_reenable_or_retain_content()
    {
        using var fixture = new Fixture();
        using var root = fixture.Root();
        var admitted = await fixture.Admitted();
        var disabled = (await fixture.Service.DisableAsync(admitted.Id, admitted.Revision, Token)).Record!;
        disabled.Retention.Should().Be(MemoryRetentionState.Disabled);
        (await fixture.Service.UseAsync(disabled.Id, disabled.Revision, MemoryDestination.Local, Token)).Reason.Should().Be(MemoryReason.NotEnabled);
        (await fixture.Service.AdmitAsync(disabled.Id, disabled.Revision, Token)).Outcome.Should().Be(MemoryOutcome.InvalidTransition);
        (await fixture.Service.DisableAsync(disabled.Id, disabled.Revision, Token)).Outcome.Should().Be(MemoryOutcome.InvalidTransition);
        var forgotten = (await fixture.Service.ForgetAsync(disabled.Id, disabled.Revision, Token)).Record!;
        forgotten.Candidate.Should().BeNull();
        forgotten.Receipt.Should().BeNull();
        forgotten.Retention.Should().Be(MemoryRetentionState.Forgotten);
        (await fixture.Service.UseAsync(forgotten.Id, forgotten.Revision, MemoryDestination.Local, Token)).Reason.Should().Be(MemoryReason.NotEnabled);
        (await fixture.Service.EditAsync(forgotten.Id, forgotten.Revision, Candidate, Token)).Outcome.Should().Be(MemoryOutcome.InvalidTransition);
        (await fixture.Service.ForgetAsync(forgotten.Id, forgotten.Revision, Token)).Outcome.Should().Be(MemoryOutcome.InvalidTransition);
        // Previously returned immutable snapshots are not capabilities; every use re-resolves current state.
        (await fixture.Service.UseAsync(admitted.Id, admitted.Revision, MemoryDestination.Local, Token)).Outcome.Should().Be(MemoryOutcome.RevisionConflict);
    }

    [Fact]
    public async Task Forbidden_classes_unknown_origin_scope_and_limits_never_create_state()
    {
        using var fixture = new Fixture();
        using var root = fixture.Root();
        foreach (var contentClass in new[] { MemoryContentClass.Unknown, MemoryContentClass.Credential, MemoryContentClass.Secret,
            MemoryContentClass.Health, MemoryContentClass.InferredTrait, MemoryContentClass.TransientTask, MemoryContentClass.ModelClaim,
            (MemoryContentClass)999 })
        {
            (await fixture.Service.ProposeAsync(new(contentClass, "claim"), fixture.Scope, MemoryProposalOrigin.Model, Token))
                .Reason.Should().Be(MemoryReason.ContentForbidden);
        }
        (await fixture.Service.ProposeAsync(Candidate, null, MemoryProposalOrigin.User, Token)).Reason.Should().Be(MemoryReason.ScopeMismatch);
        (await fixture.Service.ProposeAsync(Candidate, fixture.Scope, MemoryProposalOrigin.Unknown, Token)).Reason.Should().Be(MemoryReason.LineageUnknown);
        (await fixture.Service.ProposeAsync(null, fixture.Scope, MemoryProposalOrigin.User, Token)).Reason.Should().Be(MemoryReason.ContentForbidden);
        (await fixture.Service.ProposeAsync(new(MemoryContentClass.ExplicitFact, new string('x', 513)), fixture.Scope, MemoryProposalOrigin.User, Token))
            .Reason.Should().Be(MemoryReason.ValueLimitExceeded);
        var admitted = await fixture.Admitted();
        (await fixture.Service.EditAsync(admitted.Id, admitted.Revision, new(MemoryContentClass.Health, "health"), Token))
            .Reason.Should().Be(MemoryReason.ContentForbidden);
        (await fixture.Service.UseAsync(admitted.Id, admitted.Revision, MemoryDestination.Local, Token)).Outcome.Should().Be(MemoryOutcome.Succeeded);
    }

    [Fact]
    public async Task Unknown_ids_and_stale_or_default_revisions_have_no_authority()
    {
        using var fixture = new Fixture();
        using var root = fixture.Root();
        (await fixture.Service.ReviewAsync(new(Guid.NewGuid()), new(1), true, Token)).Outcome.Should().Be(MemoryOutcome.NotFound);
        (await fixture.Service.ForgetAsync(default, default, Token)).Outcome.Should().Be(MemoryOutcome.NotFound);
        var proposal = await fixture.Propose();
        (await fixture.Service.ReviewAsync(proposal.Id, default, true, Token)).Outcome.Should().Be(MemoryOutcome.RevisionConflict);
        var reviewed = (await fixture.Service.ReviewAsync(proposal.Id, proposal.Revision, true, Token)).Record!;
        (await fixture.Service.AdmitAsync(reviewed.Id, proposal.Revision, Token)).Outcome.Should().Be(MemoryOutcome.RevisionConflict);
        fixture.Boundary = fixture.Boundary! with { Revision = 2 };
        (await fixture.Service.AdmitAsync(reviewed.Id, reviewed.Revision, Token)).Outcome.Should().Be(MemoryOutcome.InvalidTransition);
    }

    [Theory]
    [InlineData("missing-context")]
    [InlineData("stopped-context")]
    [InlineData("completed-context")]
    [InlineData("incoming-context")]
    public async Task Only_live_host_resolved_context_can_propose(string failure)
    {
        using var fixture = new Fixture();
        using var root = failure is "stopped-context" or "completed-context" ? fixture.Root() : null;
        using var incoming = failure is "incoming-context" ? new Activity("untrusted").Start() : null;
        if (failure is "stopped-context") { root!.Activity!.Stop(); }
        if (failure is "completed-context") { root!.Complete(HostOperationOutcome.Completed); }
        (await fixture.Service.ProposeAsync(Candidate, fixture.Scope, MemoryProposalOrigin.User, Token)).Reason.Should().Be(MemoryReason.HostContextRequired);
        fixture.Audits.Should().BeEmpty();
    }

    [Theory]
    [InlineData("host")]
    [InlineData("privacy")]
    [InlineData("boundary")]
    [InlineData("owner")]
    [InlineData("lineage")]
    [InlineData("wrong-session")]
    [InlineData("inactive")]
    [InlineData("generation")]
    [InlineData("boundary-session")]
    [InlineData("control-revision")]
    [InlineData("scope-revision")]
    [InlineData("late-host")]
    [InlineData("late-privacy")]
    [InlineData("late-stop")]
    [InlineData("late-complete")]
    public async Task Admission_revalidates_ownership_privacy_scope_and_session_after_authoritative_read(string failure)
    {
        using var fixture = new Fixture();
        using var root = fixture.Root();
        switch (failure)
        {
            case "host": fixture.Current = false; break;
            case "privacy": fixture.CanControl = false; break;
            case "boundary": fixture.Boundary = null; break;
            case "owner": fixture.Boundary = fixture.Boundary! with { IsOwner = false }; break;
            case "lineage": fixture.Boundary = fixture.Boundary! with { LineageKnown = false }; break;
            case "wrong-session": fixture.Session = fixture.Session with { SessionId = new(Guid.NewGuid()) }; break;
            case "inactive": fixture.Session = fixture.Session with { IsActive = false }; break;
            case "generation": fixture.Session = fixture.Session with { Generation = new(2) }; break;
            case "boundary-session": fixture.Boundary = fixture.Boundary! with { Session = new(Guid.NewGuid()) }; fixture.Scope = MemoryScope.DeviceProfile(fixture.Profile); break;
            case "control-revision": fixture.OnRead = () => fixture.ControlRevision++; break;
            case "scope-revision": fixture.OnRead = () => fixture.Boundary = fixture.Boundary! with { Revision = 2 }; break;
            case "late-host": fixture.OnRead = () => fixture.Current = false; break;
            case "late-privacy": fixture.OnRead = () => fixture.CanControl = false; break;
            case "late-stop": fixture.OnRead = () => root.Activity!.Stop(); break;
            case "late-complete": fixture.OnRead = () => root.Complete(HostOperationOutcome.Completed); break;
        }
        var result = await fixture.Service.ProposeAsync(Candidate, fixture.Scope, MemoryProposalOrigin.User, Token);
        result.Outcome.Should().Be(MemoryOutcome.Denied);
        result.Record.Should().BeNull();
    }

    [Theory]
    [InlineData(MemoryScopeKind.Session)]
    [InlineData(MemoryScopeKind.DeviceProfile)]
    [InlineData(MemoryScopeKind.Project)]
    [InlineData(MemoryScopeKind.Source)]
    public async Task Scope_and_source_boundaries_are_exact_before_relevance(MemoryScopeKind kind)
    {
        using var fixture = new Fixture();
        using var root = fixture.Root();
        fixture.Scope = fixture.MakeScope(kind);
        var admitted = await fixture.Admitted();
        fixture.Boundary = fixture.Boundary! with { Profile = new(Guid.NewGuid()) };
        (await fixture.Service.UseAsync(admitted.Id, admitted.Revision, MemoryDestination.Local, Token)).Use.Should().BeNull();
        fixture.Boundary = fixture.Boundary with { Profile = fixture.Profile };
        fixture.Boundary = kind switch
        {
            MemoryScopeKind.Session => fixture.Boundary with { Generation = new(2) },
            MemoryScopeKind.Project => fixture.Boundary with { Project = new(Guid.NewGuid()) },
            MemoryScopeKind.Source => fixture.Boundary with { Source = new(Guid.NewGuid()) },
            _ => fixture.Boundary with { SourceRevision = new(2) },
        };
        fixture.Session = fixture.Session with { Generation = fixture.Boundary.Generation };
        (await fixture.Service.ForgetAsync(admitted.Id, admitted.Revision, Token)).Outcome.Should().Be(MemoryOutcome.Denied);
    }

    [Fact]
    public async Task Only_admitted_profile_memory_can_cross_sessions_and_pending_reviews_cannot()
    {
        using var fixture = new Fixture();
        MemoryRecord admitted;
        MemoryRecord pending;
        using (fixture.Root())
        {
            fixture.Scope = MemoryScope.DeviceProfile(fixture.Profile);
            admitted = await fixture.Admitted();
            pending = await fixture.Propose();
        }
        fixture.SwitchSession();
        using var later = fixture.Root();
        (await fixture.Service.UseAsync(admitted.Id, admitted.Revision, MemoryDestination.Local, Token)).Outcome.Should().Be(MemoryOutcome.Succeeded);
        (await fixture.Service.ReviewAsync(pending.Id, pending.Revision, true, Token)).Reason.Should().Be(MemoryReason.ScopeMismatch);
        (await fixture.Service.EditAsync(pending.Id, pending.Revision, Candidate, Token)).Reason.Should().Be(MemoryReason.ScopeMismatch);
        var edited = (await fixture.Service.EditAsync(admitted.Id, admitted.Revision, Candidate, Token)).Record!;
        edited.Lineage.Request.SessionId.Should().Be(fixture.Request.SessionId);
        edited.Review.Should().Be(MemoryReviewState.Proposed);
    }

    [Fact]
    public async Task Session_retirement_clears_only_session_scoped_content_and_revokes_late_callbacks()
    {
        using var fixture = new Fixture();
        using var root = fixture.Root();
        var session = await fixture.Admitted();
        fixture.Scope = MemoryScope.DeviceProfile(fixture.Profile);
        var profile = await fixture.Admitted();
        fixture.Service.ExpireSession(fixture.Request.SessionId);
        (await fixture.Service.UseAsync(session.Id, session.Revision, MemoryDestination.Local, Token)).Outcome.Should().Be(MemoryOutcome.RevisionConflict);
        (await fixture.Service.UseAsync(session.Id, new(session.Revision.Value + 1), MemoryDestination.Local, Token)).Reason.Should().Be(MemoryReason.NotEnabled);
        (await fixture.Service.UseAsync(profile.Id, profile.Revision, MemoryDestination.Local, Token)).Outcome.Should().Be(MemoryOutcome.Succeeded);
        fixture.OnRead = () => fixture.Service.ExpireSession(fixture.Request.SessionId);
        (await fixture.Service.ProposeAsync(Candidate, fixture.Scope, MemoryProposalOrigin.User, Token)).Reason.Should().Be(MemoryReason.AuthorityClosed);
        var act = () => fixture.Service.ExpireSession(default);
        act.Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData("early")]
    [InlineData("late")]
    [InlineData("exception")]
    public async Task Cancellation_never_publishes_state(string timing)
    {
        using var fixture = new Fixture();
        using var root = fixture.Root();
        using var cancel = new CancellationTokenSource();
        if (timing is "early") { cancel.Cancel(); }
        else
        {
            fixture.OnRead = () =>
            {
                cancel.Cancel();
                if (timing is "exception") { cancel.Token.ThrowIfCancellationRequested(); }
            };
        }
        var result = await fixture.Service.ProposeAsync(Candidate, fixture.Scope, MemoryProposalOrigin.User, cancel.Token);
        result.Outcome.Should().Be(MemoryOutcome.Cancelled);
        result.Record.Should().BeNull();
        fixture.Audits.Last().Outcome.Should().Be(SecurityAuditOutcome.Cancelled);
    }

    [Fact]
    public async Task Concurrent_review_callbacks_resolve_exact_current_revision_after_the_read()
    {
        using var fixture = new Fixture();
        using var root = fixture.Root();
        var proposal = await fixture.Propose();
        var late = new TaskCompletionSource<SessionWorkspaceEntry>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Pending = late.Task;
        var pending = fixture.Service.ReviewAsync(proposal.Id, proposal.Revision, true, Token);
        fixture.Pending = null;
        var reviewed = await fixture.Service.ReviewAsync(proposal.Id, proposal.Revision, true, Token);
        reviewed.Outcome.Should().Be(MemoryOutcome.Succeeded);
        late.SetResult(new(fixture.Session, null));
        (await pending).Outcome.Should().Be(MemoryOutcome.RevisionConflict);
    }

    [Fact]
    public async Task Dispose_revokes_and_clears_workspace_even_when_authoritative_read_returns_late()
    {
        using var fixture = new Fixture();
        using var root = fixture.Root();
        var late = new TaskCompletionSource<SessionWorkspaceEntry>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Pending = late.Task;
        var pending = fixture.Service.ProposeAsync(Candidate, fixture.Scope, MemoryProposalOrigin.Model, Token);
        fixture.Service.Dispose();
        late.SetResult(new(fixture.Session, null));
        (await pending).Reason.Should().Be(MemoryReason.AuthorityClosed);
        fixture.Pending = null;
        (await fixture.Service.ProposeAsync(Candidate, fixture.Scope, MemoryProposalOrigin.User, Token)).Reason.Should().Be(MemoryReason.AuthorityClosed);
    }

    [Fact]
    public async Task Bounded_workspace_never_evicts_or_reuses_forgotten_identity()
    {
        using var fixture = new Fixture();
        using var root = fixture.Root();
        for (var index = 0; index < MemoryPolicy.MaximumEntries; index++)
        {
            var proposal = await fixture.Propose();
            if (index == 0) { await fixture.Service.ForgetAsync(proposal.Id, proposal.Revision, Token); }
        }
        var result = await fixture.Service.ProposeAsync(Candidate, fixture.Scope, MemoryProposalOrigin.User, Token);
        result.Outcome.Should().Be(MemoryOutcome.CapacityExceeded);
        result.Record.Should().BeNull();
    }

    [Fact]
    public async Task Memory_without_a_source_has_explicit_request_lineage_and_no_implicit_source()
    {
        using var fixture = new Fixture();
        using var root = fixture.Root();
        fixture.Boundary = fixture.Boundary! with { Source = null, SourceRevision = null };
        var admitted = await fixture.Admitted();
        admitted.Lineage.Source.Should().BeNull();
        admitted.Lineage.SourceRevision.Should().BeNull();
        (await fixture.Service.UseAsync(admitted.Id, admitted.Revision, MemoryDestination.Local, Token)).Outcome.Should().Be(MemoryOutcome.Succeeded);
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("expire")]
    [InlineData("dispose")]
    [InlineData("host")]
    [InlineData("scope")]
    public async Task Final_audit_callbacks_cannot_resurrect_or_publish_revoked_memory(string callback)
    {
        using var fixture = new Fixture();
        using var root = fixture.Root();
        using var cancel = new CancellationTokenSource();
        fixture.OnTerminalAudit = () =>
        {
            fixture.OnTerminalAudit = null;
            switch (callback)
            {
                case "cancel": cancel.Cancel(); break;
                case "expire": fixture.Service.ExpireSession(fixture.Request.SessionId); break;
                case "dispose": fixture.Service.Dispose(); break;
                case "host": fixture.Current = false; break;
                case "scope": fixture.Boundary = fixture.Boundary! with { Revision = 2 }; break;
            }
        };
        var result = await fixture.Service.ProposeAsync(Candidate, fixture.Scope, MemoryProposalOrigin.User, cancel.Token);
        result.Record.Should().BeNull();
        result.Outcome.Should().NotBe(MemoryOutcome.Succeeded);
        fixture.Audits.Last().Outcome.Should().Be(callback is "cancel" ? SecurityAuditOutcome.Cancelled : SecurityAuditOutcome.Denied);
    }

    [Theory]
    [InlineData("read")]
    [InlineData("retired-read")]
    [InlineData("requested-audit")]
    [InlineData("terminal-audit")]
    public async Task Failed_required_authority_or_audit_is_explicit_and_publishes_nothing(string failure)
    {
        using var fixture = new Fixture();
        using var root = fixture.Root();
        if (failure is "read" or "retired-read")
        {
            fixture.OnRead = () =>
            {
                if (failure is "retired-read") { root.Activity!.Stop(); }
                throw new IOException("private content");
            };
        }
        else if (failure is "requested-audit") { fixture.FailRequestedAudit = true; }
        else { fixture.FailTerminalAudit = true; }
        var act = () => fixture.Service.ProposeAsync(Candidate, fixture.Scope, MemoryProposalOrigin.User, Token);
        await act.Should().ThrowAsync<IOException>();
        fixture.Messages.Should().Contain(message => message.Contains("IOException", StringComparison.Ordinal));
        fixture.Messages.Should().NotContain(message => message.Contains("private content", StringComparison.Ordinal));
        fixture.FailTerminalAudit = false;
        fixture.FailRequestedAudit = false;
        fixture.OnRead = null;
        using var recovery = failure is "retired-read" ? fixture.Root() : null;
        (await fixture.Service.ReviewAsync(new(Guid.NewGuid()), new(1), true, Token)).Outcome.Should().Be(MemoryOutcome.NotFound);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Reentrant_forgetting_during_audit_cannot_publish_admission_or_use(bool use)
    {
        using var fixture = new Fixture();
        using var root = fixture.Root();
        var proposal = await fixture.Propose();
        var reviewed = (await fixture.Service.ReviewAsync(proposal.Id, proposal.Revision, true, Token)).Record!;
        var subject = use ? (await fixture.Service.AdmitAsync(reviewed.Id, reviewed.Revision, Token)).Record! : reviewed;
        Task<MemoryResult>? forgotten = null;
        fixture.OnTerminalAudit = () =>
        {
            fixture.OnTerminalAudit = null;
            forgotten = fixture.Service.ForgetAsync(subject.Id, subject.Revision, Token);
        };
        var result = use
            ? await fixture.Service.UseAsync(subject.Id, subject.Revision, MemoryDestination.Local, Token)
            : await fixture.Service.AdmitAsync(subject.Id, subject.Revision, Token);
        result.Outcome.Should().Be(MemoryOutcome.RevisionConflict);
        result.Record.Should().BeNull();
        result.Use.Should().BeNull();
        var tombstone = (await forgotten!).Record!;
        tombstone.Candidate.Should().BeNull();
        (await fixture.Service.UseAsync(tombstone.Id, tombstone.Revision, MemoryDestination.Local, Token)).Use.Should().BeNull();
    }

    [Fact]
    public async Task Diagnostics_and_activities_retain_host_correlation_without_content_or_incoming_authority()
    {
        using var fixture = new Fixture();
        using var incoming = new Activity("incoming").SetIdFormat(ActivityIdFormat.W3C).Start();
        incoming.AddBaggage("session", "hostile");
        using var root = fixture.Root();
        root.Activity!.ParentId.Should().BeNull();
        var admitted = await fixture.Admitted();
        await fixture.Service.UseAsync(admitted.Id, admitted.Revision, MemoryDestination.Local, Token);
        fixture.Audits.Should().HaveCount(8);
        fixture.Audits.Where((_, index) => index % 2 == 0).Should().OnlyContain(item => item.Outcome == SecurityAuditOutcome.Requested);
        fixture.LogRequests.Should().OnlyContain(request => ReferenceEquals(request, root.Request));
        fixture.Messages.Should().NotContain(message => message.Contains(Candidate.Value!, StringComparison.Ordinal));
        root.Activity.TagObjects.Should().NotContain(tag => Equals(tag.Value, Candidate.Value));
        HostActivity.Current.Should().BeSameAs(root);
    }

    private sealed partial class Fixture : ISessionWorkspaceStore, ISessionWorkspaceAccess, ICapabilityHostAccess,
        IMemoryScopeAccess, IMemoryStore, ISecurityAuditLog, ILogger<MemoryAdmissionService>, IDisposable
    {
        internal HostRequest Request { get; private set; } = HostRequest.Create(RequestOrigin.LocalUi);
        internal HostId<DeviceProfileIdentity> Profile { get; } = new(Guid.NewGuid());
        internal HostId<MemoryProjectIdentity> Project { get; } = new(Guid.NewGuid());
        internal HostId<MemorySourceIdentity> Source { get; } = new(Guid.NewGuid());
        internal MemoryBoundary? Boundary { get; set; }
        internal MemoryScope Scope { get; set; }
        internal WorkSessionAuthorization Session { get; set; }
        internal MemoryAdmissionService Service { get; }
        internal Action? OnRead { get; set; }
        internal Task<SessionWorkspaceEntry>? Pending { get; set; }
        internal bool Current { get; set; } = true;
        internal bool FailTerminalAudit { get; set; }
        internal bool FailRequestedAudit { get; set; }
        internal Action? OnTerminalAudit { get; set; }
        internal int Writes { get; private set; }
        internal List<SecurityAuditEvent> Audits { get; } = [];
        internal List<string> Messages { get; } = [];
        internal List<HostRequest> LogRequests { get; } = [];
        internal Dictionary<HostId<MemoryIdentity>, MemoryRecord> Stored { get; } = [];
        internal bool FailStoreCommit { get; set; }
        internal MemoryResult? StoreResult { get; set; }
        internal Action? OnStoreTransition { get; set; }
        internal Action? OnStoreCommit { get; set; }
        internal Action? OnAfterStoreCommit { get; set; }
        internal Action? OnBoundaryRead { get; set; }
        internal bool InspectEnabled { get; set; } = true;
        internal bool SkipStoreTransition { get; set; }
        public bool IsCurrentHost => Current;
        public bool CanInspect => InspectEnabled && CanControl;
        public bool CanControl { get; set; } = true;
        public long ControlRevision { get; set; } = 1;
        internal Fixture(bool durable = false)
        {
            Session = new(Request.SessionId, new(1), true);
            Boundary = new(Profile, Request.SessionId, new(1), Project, Source, new(1), 1, true, true, true);
            Scope = MemoryScope.Session(Request.SessionId);
            Service = CreateService(durable);
        }
        internal MemoryAdmissionService CreateService(bool durable) =>
            new(this, this, this, this, this, this, new FixedTime(), durable ? this : null);
        public ValueTask<MemoryBoundary> ReadMemoryBoundaryAsync(HostRequest request, long controlRevision, CancellationToken token)
        {
            OnBoundaryRead?.Invoke();
            return ValueTask.FromResult(Boundary!);
        }
        public ValueTask<MemoryResult> TransactMemoryAsync(HostRequest request, MemoryBoundary boundary,
            Func<ImmutableArray<MemoryRecord>, MemoryStorageCommit> transition, Func<bool> admitted, CancellationToken token)
        {
            if (SkipStoreTransition) { return ValueTask.FromResult(new MemoryResult(MemoryOutcome.Denied, MemoryReason.AuthorityClosed)); }
            OnStoreTransition?.Invoke();
            var commit = transition([.. Stored.Values]);
            OnStoreCommit?.Invoke();
            token.ThrowIfCancellationRequested();
            if (!admitted()) { throw new InvalidOperationException("Memory admission closed at commit."); }
            if (Session.SessionId != request.SessionId || !Session.IsActive || Session.Generation != boundary.Generation
                || Boundary != boundary)
            {
                throw new InvalidOperationException("The authoritative session memory boundary changed.");
            }
            if (FailStoreCommit) { throw new IOException("Injected atomic commit failure."); }
            if (StoreResult is { } overrideResult) { return ValueTask.FromResult(overrideResult); }
            if (commit.Replacement is { } replacement)
            {
                Stored[replacement.Id] = replacement;
                if (MemoryControlEnabled)
                {
                    ControlOutcomes.Add(ControlIntents.Single(row => row.Request == request).Next(HostTaskState.Succeeded));
                }
            }
            OnAfterStoreCommit?.Invoke();
            return ValueTask.FromResult(commit.Result);
        }
        internal HostActivity Root(RequestOrigin origin = RequestOrigin.LocalUi)
        {
            Request = new(new(Guid.NewGuid()), Request.SessionId, Request.TaskId, origin);
            return HostActivity.BeginRoot(Request, HostActivityLayer.Application, HostOperation.Request);
        }
        internal void SwitchSession()
        {
            Request = HostRequest.Create(RequestOrigin.LocalUi);
            Session = new(Request.SessionId, new(1), true);
            Boundary = Boundary! with { Session = Request.SessionId };
        }
        internal MemoryScope MakeScope(MemoryScopeKind kind) => kind switch
        {
            MemoryScopeKind.Session => MemoryScope.Session(Request.SessionId),
            MemoryScopeKind.DeviceProfile => MemoryScope.DeviceProfile(Profile),
            MemoryScopeKind.Project => MemoryScope.Project(Project),
            _ => MemoryScope.Source(Source),
        };
        internal async Task<MemoryRecord> Propose(MemoryProposalOrigin origin = MemoryProposalOrigin.User)
        {
            var result = await Service.ProposeAsync(Candidate, Scope, origin, Token);
            result.Outcome.Should().Be(MemoryOutcome.Succeeded);
            return result.Record!;
        }
        internal async Task<MemoryRecord> Admitted()
        {
            var proposal = await Propose();
            var reviewed = (await Service.ReviewAsync(proposal.Id, proposal.Revision, true, Token)).Record!;
            return (await Service.AdmitAsync(reviewed.Id, reviewed.Revision, Token)).Record!;
        }
        public MemoryBoundary? Observe(HostRequest request) => Boundary;
        public ValueTask<MemoryBoundary?> ResolveAsync(HostRequest request, CancellationToken token) =>
            ValueTask.FromResult(Observe(request));
        public ValueTask<SessionWorkspaceEntry> ReadMetadataAsync(HostId<SessionIdentity> session, CancellationToken cancellationToken)
        {
            OnRead?.Invoke();
            return Pending is { } pending ? new(pending) : ValueTask.FromResult(new SessionWorkspaceEntry(Session, null));
        }
        public void Write(SecurityAuditEvent auditEvent)
        {
            if (FailRequestedAudit && auditEvent.Outcome == SecurityAuditOutcome.Requested) { throw new IOException("requested audit unavailable"); }
            if (FailTerminalAudit && auditEvent.Outcome != SecurityAuditOutcome.Requested) { throw new IOException("audit unavailable"); }
            HostActivity.RequireCurrent().Request.Should().BeSameAs(Request);
            Audits.Add(auditEvent);
            if (auditEvent.Outcome != SecurityAuditOutcome.Requested) { OnTerminalAudit?.Invoke(); }
        }
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
            if (HostActivity.Current is { } current) { LogRequests.Add(current.Request); }
        }
        public void Dispose() => Service.Dispose();
        private ValueTask<T> Write<T>() { Writes++; throw new NotSupportedException(); }
        public ValueTask<SessionDispositionPreview> PreviewDispositionAsync(HostId<SessionIdentity> session, HostRevision generation, long revision, CancellationToken token) => Write<SessionDispositionPreview>();
        public ValueTask<SessionDispositionReceipt> DisposeSessionAsync(HostRequest request, SessionDispositionPreview preview, Func<bool> eligible, CancellationToken token) => Write<SessionDispositionReceipt>();
        public ValueTask<HostTaskRecord> RecordControlIntentAsync(HostRequest request, CancellationToken token)
        {
            if (!MemoryControlEnabled) { return Write<HostTaskRecord>(); }
            Request = request;
            var intent = new HostTaskRecord(request, new(1), HostTaskState.IntentRecorded);
            ControlIntents.Add(intent);
            return ValueTask.FromResult(intent);
        }
        public ValueTask<SessionPage<WorkSessionAuthorization>> ReadSessionsAsync(Guid? after, int limit, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<SessionPage<SessionWorkspaceEntry>> ReadMetadataPageAsync(Guid? after, int limit, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<HostTaskObservation?> ReadTaskAsync(HostId<SessionIdentity> session, HostId<TaskIdentity> task, CancellationToken token)
        {
            if (!MemoryControlEnabled) { throw new NotSupportedException(); }
            var record = ControlOutcomes.LastOrDefault(row => row.Request.TaskId == task)
                ?? ControlIntents.LastOrDefault(row => row.Request.TaskId == task);
            return ValueTask.FromResult(record is null ? null
                : new HostTaskObservation(record, Session.Generation, "memory-management-test", true, null));
        }
        public ValueTask<HostTaskObservation> CancelWaitingTaskAsync(HostRequest control, HostTaskCancellationTarget target, Func<bool> eligible, CancellationToken token) => Write<HostTaskObservation>();
        public ValueTask<SessionWorkspaceEntry> CreateNamedSessionAsync(HostRequest request, SessionName name, Func<bool> eligible, CancellationToken token) => Write<SessionWorkspaceEntry>();
        public ValueTask<SessionWorkspaceEntry> RenameSessionAsync(HostRequest request, HostRevision generation, long revision, SessionName name, Func<bool> eligible, CancellationToken token) => Write<SessionWorkspaceEntry>();
        public ValueTask<SessionPage<HostQuestionRecord>> ReadQuestionPageAsync(HostId<SessionIdentity> session, Guid? after, int limit, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<SessionPage<HostTaskRecord>> ReadTaskPageAsync(HostId<SessionIdentity> session, Guid? after, int limit, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<WorkSessionAuthorization> ChangeIdleLifecycleAsync(HostRequest request, HostRevision generation, bool active, Func<bool> eligible, CancellationToken token)
        {
            if (!MemoryControlEnabled) { return Write<WorkSessionAuthorization>(); }
            eligible().Should().BeTrue();
            Session = Session with { IsActive = active, Generation = new(generation.Value + 1) };
            Boundary = Boundary! with { Generation = Session.Generation };
            return ValueTask.FromResult(Session);
        }
    }

    private sealed class FixedTime : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch;
    }
}
