using System.Collections.Immutable;
using AwesomeAssertions;
using Kora.Application.Memory;
using Kora.Core.Auditing;
using Kora.Core.Commands;
using Kora.Core.Hosting;
using Kora.Core.Memory;
using Kora.Core.Memory.DeviceProfile;
using Kora.Core.Storage;

namespace Kora.Application.UnitTests.Memory;

public sealed partial class MemoryAdmissionServiceTests
{
    [Fact]
    public async Task DeviceProfileRequiresExplicitRouteAndFreshUserReviewAndSurvivesSessionRetirement()
    {
        using var f = new Fixture(durable: true);
        using var profile = f.CreateService(true, MemoryScopeKind.DeviceProfile);
        MemoryRecord admitted;
        MemoryRecord pending;
        using (f.Root())
        {
            var scope = await profile.ResolveDeviceProfileScopeAsync(Token);
            (await f.Service.ProposeAsync(Candidate, scope, MemoryProposalOrigin.User, Token)).Reason
                .Should().Be(MemoryReason.ScopeMismatch);
            pending = (await profile.ProposeAsync(Candidate, scope, MemoryProposalOrigin.User, Token)).Record!;
            (await profile.AdmitAsync(pending.Id, pending.Revision, Token)).Outcome.Should().Be(MemoryOutcome.InvalidTransition);
            var reviewed = (await profile.ReviewAsync(pending.Id, pending.Revision, true, Token)).Record!;
            admitted = (await profile.AdmitAsync(reviewed.Id, reviewed.Revision, Token)).Record!;
            pending = (await profile.ProposeAsync(Candidate, scope, MemoryProposalOrigin.User, Token)).Record!;
            profile.ExpireSession(f.Request.SessionId);
        }
        f.SwitchSession();
        using (f.Root())
        {
            (await profile.ReviewAsync(pending.Id, pending.Revision, true, Token)).Reason.Should().Be(MemoryReason.ScopeMismatch);
            using var restarted = f.CreateService(true, MemoryScopeKind.DeviceProfile);
            var use = await restarted.UseAsync(admitted.Id, admitted.Revision, MemoryDestination.Local, Token);
            (await restarted.ObserveSessionAsync(Token)).Should().Equal(admitted);
            use.Use!.Candidate.Should().Be(Candidate);
            use.Use.Lineage.Should().Be(admitted.Lineage);
            use.Use.UsedBy.SessionId.Should().Be(f.Request.SessionId);
            (await restarted.UseAsync(admitted.Id, admitted.Revision, MemoryDestination.Hosted, Token)).Reason
                .Should().Be(MemoryReason.DisclosureNotAdmitted);
            f.Boundary = f.Boundary! with { Profile = new(Guid.NewGuid()) };
            (await restarted.UseAsync(admitted.Id, admitted.Revision, MemoryDestination.Local, Token)).Reason
                .Should().Be(MemoryReason.ScopeMismatch);
        }
    }

    [Theory]
    [InlineData("model")]
    [InlineData("forbidden")]
    [InlineData("audit")]
    [InlineData("commit")]
    [InlineData("cancel")]
    [InlineData("source")]
    public async Task DeviceProfileFailsClosedWithoutForbiddenPersistenceOrSuccessfulUse(string fault)
    {
        using var f = new Fixture(durable: true);
        using var profile = f.CreateService(true, MemoryScopeKind.DeviceProfile);
        using var root = f.Root();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var scope = await profile.ResolveDeviceProfileScopeAsync(Token);
        if (fault is "model" or "forbidden")
        {
            var result = await profile.ProposeAsync(fault is "forbidden"
                ? new(MemoryContentClass.Secret, "must never persist") : Candidate, scope,
                fault is "model" ? MemoryProposalOrigin.Model : MemoryProposalOrigin.User, Token);
            result.Reason.Should().Be(fault is "model" ? MemoryReason.UserIntentRequired : MemoryReason.ContentForbidden);
            f.Stored.Should().BeEmpty();
            return;
        }
        var proposed = (await profile.ProposeAsync(Candidate, scope, MemoryProposalOrigin.User, Token)).Record!;
        var reviewed = (await profile.ReviewAsync(proposed.Id, proposed.Revision, true, Token)).Record!;
        if (fault is "audit") { f.FailTerminalAudit = true; }
        if (fault is "commit") { f.FailStoreCommit = true; }
        if (fault is "cancel") { cancellation.Cancel(); }
        if (fault is "source") { f.Boundary = f.Boundary! with { SourceRevision = new(2) }; }
        if (fault is "audit" or "commit")
        {
            await profile.Invoking(p => p.AdmitAsync(reviewed.Id, reviewed.Revision, Token)).Should().ThrowAsync<IOException>();
            if (fault is "commit") { f.Audits.Should().Contain(row => row.Outcome == SecurityAuditOutcome.Unknown); }
        }
        else
        {
            (await profile.AdmitAsync(reviewed.Id, reviewed.Revision, cancellation.Token)).Outcome
                .Should().NotBe(MemoryOutcome.Succeeded);
        }
        f.Stored.Should().BeEmpty();
    }

    [Fact]
    public async Task ProfileManagementSeparatesSessionInventoryAndClearsReviewOnEditDisableForgetAndRestart()
    {
        using var f = new Fixture(durable: true);
        await using var management = f.Management();
        Task<MemoryCommandResult> Execute(MemoryCommandOperation operation, MemorySummary? row = null) =>
            management.ExecuteDeviceProfileAsync(new(operation, f.Session.SessionId.Value, row?.Id, row?.Revision ?? 0,
                Accept: operation == MemoryCommandOperation.Review,
                Candidate: operation is MemoryCommandOperation.Propose or MemoryCommandOperation.Edit ? Candidate : null),
                RequestOrigin.LocalUi, static () => true, Token);
        var proposed = (await Execute(MemoryCommandOperation.Propose)).Memories.Single();
        proposed.Scope.Kind.Should().Be(MemoryScopeKind.DeviceProfile);
        await Execute(MemoryCommandOperation.Inspect, proposed);
        var reviewed = (await Execute(MemoryCommandOperation.Review, proposed)).Memories.Single();
        var admitted = (await Execute(MemoryCommandOperation.Admit, reviewed)).Memories.Single();
        var disabled = (await Execute(MemoryCommandOperation.Disable, admitted)).Memories.Single();
        var edited = (await Execute(MemoryCommandOperation.Edit, disabled)).Memories.Single();
        f.Stored.Single().Value.Candidate.Should().BeNull();
        await Execute(MemoryCommandOperation.Inspect, edited);
        reviewed = (await Execute(MemoryCommandOperation.Review, edited)).Memories.Single();
        admitted = (await Execute(MemoryCommandOperation.Admit, reviewed)).Memories.Single();
        var forgotten = (await Execute(MemoryCommandOperation.Forget, admitted)).Memories.Single();
        forgotten.Retention.Should().Be(MemoryRetentionState.Forgotten);
        await using var restarted = f.Management();
        (await restarted.ExecuteDeviceProfileAsync(new(MemoryCommandOperation.List, f.Session.SessionId.Value),
            RequestOrigin.LocalUi, static () => true, Token)).Memories.Should().Equal(forgotten);
        using var workflow = management.CreateDeviceProfileContextWorkflow();
        await management.DisposeAsync();
        management.Invoking(m => m.CreateDeviceProfileContextWorkflow()).Should().Throw<ObjectDisposedException>();
    }

    [Theory]
    [InlineData("none")]
    [InlineData("foreign")]
    [InlineData("disabled")]
    [InlineData("source")]
    [InlineData("cancel")]
    public async Task ProfileContextRequiresExplicitSelectionFreshReviewAndExactlyOneConfirmation(string drift)
    {
        using var f = new Fixture(durable: true);
        using var service = f.CreateService(true, MemoryScopeKind.DeviceProfile);
        MemoryRecord admitted;
        using (f.Root())
        {
            var proposal = (await service.ProposeAsync(Candidate, MemoryScope.DeviceProfile(f.Profile),
                MemoryProposalOrigin.User, Token)).Record!;
            var reviewed = (await service.ReviewAsync(proposal.Id, proposal.Revision, true, Token)).Record!;
            admitted = (await service.AdmitAsync(reviewed.Id, reviewed.Revision, Token)).Record!;
        }
        f.SwitchSession();
        var source = f.ContextSource(MemoryScopeKind.DeviceProfile);
        using var workflow = new SessionMemoryContext(source, f, new FixedTime(), MemoryScopeKind.DeviceProfile);
        var target = new SessionWorkspaceEntry(f.Session, null);
        using var review = await workflow.ReviewAsync(target, [Summary(admitted)], "Use exactly this preference.", static () => true, Token);
        review.Records.Should().Equal(admitted);
        var selectSession = () => SessionMemoryContext.ValidateSelection(target, [Summary(admitted)]);
        selectSession.Should().Throw<InvalidDataException>();
        var selectProfile = () => SessionMemoryContext.ValidateSelection(target,
            [Summary(admitted) with { Scope = MemoryScope.Session(target.Authority.SessionId) }], MemoryScopeKind.DeviceProfile);
        selectProfile.Should().Throw<InvalidDataException>();
        using var use = workflow.Confirm(review, static () => true, Token);
        workflow.Invoking(w => w.Confirm(review, static () => true, Token)).Should().Throw<InvalidOperationException>();
        if (drift is "foreign") { f.Boundary = f.Boundary! with { Profile = new(Guid.NewGuid()) }; }
        if (drift is "disabled") { f.Stored[admitted.Id] = admitted with { Retention = MemoryRetentionState.Disabled }; }
        if (drift is "source") { f.Boundary = f.Boundary! with { SourceRevision = new(2) }; }
        if (drift is "cancel") { workflow.Retire(); }
        if (drift is "none")
        {
            using var root = f.Root();
            await use.ValidateAsync(Token);
            use.CreateEnvelope(root.Request, DateTimeOffset.UnixEpoch).Evidence.Single().Content.Should().Contain(Candidate.Value!);
            f.Stored.Single().Value.Should().Be(admitted);
        }
        else
        {
            using var root = f.Root();
            await use.Invoking(u => u.ValidateAsync(Token)).Should().ThrowAsync<Exception>();
            review.Current.Should().BeFalse();
        }
    }

    [Fact]
    public async Task ProfileRouteRefusesMissingStorageCapabilityAndUnknownBoundary()
    {
        using var f = new Fixture(durable: true);
        using var root = f.Root();
        using var service = new MemoryAdmissionService(f, f, f, f, f, f, new FixedTime(),
            new SessionOnlyMemoryStore(f), MemoryScopeKind.DeviceProfile);
        await service.Invoking(s => s.ProposeAsync(Candidate, MemoryScope.DeviceProfile(f.Profile),
            MemoryProposalOrigin.User, Token)).Should().ThrowAsync<InvalidOperationException>();
        f.Boundary = null;
        await service.Invoking(s => s.ResolveDeviceProfileScopeAsync(Token)).Should().ThrowAsync<InvalidOperationException>();
        (await service.UseAsync(new(Guid.NewGuid()), new(1), MemoryDestination.Local, Token)).Use.Should().BeNull();
        using var unknown = f.CreateService(true, MemoryScopeKind.Source);
        (await unknown.ProposeAsync(Candidate, MemoryScope.Source(f.Source), MemoryProposalOrigin.User, Token)).Outcome
            .Should().Be(MemoryOutcome.Denied);
    }

    [Fact]
    public async Task ProfileContextRefusesAStoreWithoutExplicitProfileCapability()
    {
        using var f = new Fixture(durable: true);
        using var root = f.Root();
        var sessionRecord = await f.Admitted();
        var record = sessionRecord with { Scope = MemoryScope.DeviceProfile(f.Profile) };
        f.Stored[record.Id] = record;
        await using var management = f.Management();
        var source = new SessionMemoryContextSource(f.ManagementSessions!, f, f, f, new SessionOnlyMemoryStore(f), f,
            MemoryScopeKind.DeviceProfile);
        await source.Invoking(s => s.ReadAsync(new(f.Session, null), [Summary(record)], static () => true, Token))
            .Should().ThrowAsync<InvalidOperationException>();
    }

    private sealed class SessionOnlyMemoryStore(Fixture f) : IMemoryStore
    {
        public ValueTask<MemoryBoundary> ReadMemoryBoundaryAsync(HostRequest request, long controlRevision, CancellationToken token) =>
            f.ReadMemoryBoundaryAsync(request, controlRevision, token);
        public ValueTask<MemoryResult> TransactMemoryAsync(HostRequest request, MemoryBoundary boundary,
            Func<ImmutableArray<MemoryRecord>, MemoryStorageCommit> transition, Func<bool> admitted, CancellationToken token) =>
            f.TransactMemoryAsync(request, boundary, transition, admitted, token);
    }

    private sealed partial class Fixture : IDeviceProfileMemoryStore
    {
        public ValueTask<MemoryResult> TransactDeviceProfileMemoryAsync(HostRequest request, MemoryBoundary boundary,
            Func<ImmutableArray<MemoryRecord>, MemoryStorageCommit> transition, Func<bool> admitted, CancellationToken token) =>
            TransactMemoryAsync(request, boundary, rows => transition([.. rows.Where(row => row.Scope.Kind == MemoryScopeKind.DeviceProfile)]),
                admitted, token);
    }
}
