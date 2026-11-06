using AwesomeAssertions;
using Kora.Core.Auditing;
using Kora.Core.Authorization;
using Kora.Core.Commands;
using Kora.Core.Hosting;
using Kora.Core.Memory;

namespace Kora.Application.UnitTests.Memory;

public sealed partial class MemoryAdmissionServiceTests
{
    [Theory]
    [InlineData(MemoryCommandOperation.Edit, RequestOrigin.LocalUi)]
    [InlineData(MemoryCommandOperation.Review, RequestOrigin.LocalUi)]
    [InlineData(MemoryCommandOperation.Admit, RequestOrigin.LocalUi)]
    [InlineData(MemoryCommandOperation.Disable, RequestOrigin.LocalUi)]
    [InlineData(MemoryCommandOperation.Forget, RequestOrigin.LocalUi)]
    [InlineData(MemoryCommandOperation.Edit, RequestOrigin.ActivatedVoice)]
    [InlineData(MemoryCommandOperation.Review, RequestOrigin.ActivatedVoice)]
    [InlineData(MemoryCommandOperation.Admit, RequestOrigin.ActivatedVoice)]
    [InlineData(MemoryCommandOperation.Disable, RequestOrigin.ActivatedVoice)]
    [InlineData(MemoryCommandOperation.Forget, RequestOrigin.ActivatedVoice)]
    public async Task OriginalUserCommandReportsWithdrawalBetweenObservationAndMutationWithoutContent(
        MemoryCommandOperation operation, RequestOrigin origin)
    {
        using var f = new Fixture(durable: true);
        using (f.Root()) { await f.Admitted(); }
        await using var service = f.Management();
        var subject = (await Run(service, f, MemoryCommandOperation.List)).Memories.Single();
        if (operation is MemoryCommandOperation.Review or MemoryCommandOperation.Admit)
        {
            subject = (await Run(service, f, MemoryCommandOperation.Edit, subject, Candidate)).Memories.Single();
            await Run(service, f, MemoryCommandOperation.Inspect, subject);
            if (operation == MemoryCommandOperation.Admit)
            {
                subject = (await Run(service, f, MemoryCommandOperation.Review, subject)).Memories.Single();
            }
        }
        var audits = f.Audits.Count;
        var transactions = 0;
        f.OnStoreTransition = () =>
        {
            if (++transactions == 2) { f.Stored.Clear(); }
        };

        var result = await service.ExecuteAsync(new(operation, f.Session.SessionId.Value, subject.Id,
            subject.Revision, Accept: true, Candidate: operation == MemoryCommandOperation.Edit ? Candidate : null),
            origin, () => true, Token);

        transactions.Should().Be(2);
        result.Outcome.Should().Be("NotFound");
        result.Message.Should().Be(nameof(MemoryReason.Missing));
        result.Memories.Should().BeEmpty();
        result.Inspected.Should().BeNull();
        f.Stored.Should().BeEmpty();
        f.Audits.Skip(audits).Where(row => row.ActionId.StartsWith("memory.", StringComparison.Ordinal))
            .Select(row => row.Outcome).Should().Equal(SecurityAuditOutcome.Requested, SecurityAuditOutcome.Denied);
        f.ControlIntents.Last().Request.Origin.Should().Be(origin);
        f.ControlOutcomes.Last().State.Should().Be(HostTaskState.Denied);
        f.OnStoreTransition = null;
        (await Run(service, f, MemoryCommandOperation.List)).Memories.Should().BeEmpty();
    }

    [Theory]
    [InlineData(MemoryOperation.Use, MemoryScopeKind.Session)]
    [InlineData(MemoryOperation.Edit, MemoryScopeKind.Session)]
    [InlineData(MemoryOperation.Review, MemoryScopeKind.Session)]
    [InlineData(MemoryOperation.Admit, MemoryScopeKind.Session)]
    [InlineData(MemoryOperation.Disable, MemoryScopeKind.Session)]
    [InlineData(MemoryOperation.Forget, MemoryScopeKind.Session)]
    [InlineData(MemoryOperation.Use, MemoryScopeKind.DeviceProfile)]
    [InlineData(MemoryOperation.Edit, MemoryScopeKind.DeviceProfile)]
    [InlineData(MemoryOperation.Review, MemoryScopeKind.DeviceProfile)]
    [InlineData(MemoryOperation.Admit, MemoryScopeKind.DeviceProfile)]
    [InlineData(MemoryOperation.Disable, MemoryScopeKind.DeviceProfile)]
    [InlineData(MemoryOperation.Forget, MemoryScopeKind.DeviceProfile)]
    public async Task MissingDurableMemoryCannotSupplyAnActionFromCachedContent(
        MemoryOperation operation, MemoryScopeKind scopeKind)
    {
        using var f = new Fixture(durable: true);
        using var service = f.CreateService(true, scopeKind);
        using var root = f.Root();
        var scope = f.MakeScope(scopeKind);
        var proposed = (await service.ProposeAsync(Candidate, scope, MemoryProposalOrigin.User, Token)).Record!;
        var reviewed = (await service.ReviewAsync(proposed.Id, proposed.Revision, true, Token)).Record!;
        var subject = (await service.AdmitAsync(reviewed.Id, reviewed.Revision, Token)).Record!;
        if (operation is MemoryOperation.Review or MemoryOperation.Admit)
        {
            subject = (await service.EditAsync(subject.Id, subject.Revision, Candidate, Token)).Record!;
            if (operation == MemoryOperation.Admit)
            {
                subject = (await service.ReviewAsync(subject.Id, subject.Revision, true, Token)).Record!;
            }
        }
        var survivor = (await service.ProposeAsync(Candidate, scope, MemoryProposalOrigin.User, Token)).Record!;
        survivor = (await service.ReviewAsync(survivor.Id, survivor.Revision, true, Token)).Record!;
        survivor = (await service.AdmitAsync(survivor.Id, survivor.Revision, Token)).Record!;
        (await service.ObserveSessionAsync(Token)).Should().Contain(subject);
        var audits = f.Audits.Count;
        f.OnStoreTransition = () => f.Stored.Remove(subject.Id);

        var result = operation switch
        {
            MemoryOperation.Use => await service.UseAsync(subject.Id, subject.Revision, MemoryDestination.Local, Token),
            MemoryOperation.Edit => await service.EditAsync(subject.Id, subject.Revision, Candidate, Token),
            MemoryOperation.Review => await service.ReviewAsync(subject.Id, subject.Revision, true, Token),
            MemoryOperation.Admit => await service.AdmitAsync(subject.Id, subject.Revision, Token),
            MemoryOperation.Disable => await service.DisableAsync(subject.Id, subject.Revision, Token),
            _ => await service.ForgetAsync(subject.Id, subject.Revision, Token),
        };

        result.Outcome.Should().Be(MemoryOutcome.NotFound);
        result.Reason.Should().Be(MemoryReason.Missing);
        result.Record.Should().BeNull();
        result.Use.Should().BeNull();
        f.Stored.Values.Should().Equal(survivor);
        f.Audits.Skip(audits).Select(row => row.Outcome).Should()
            .Equal(SecurityAuditOutcome.Requested, SecurityAuditOutcome.Denied);
        f.OnStoreTransition = null;
        (await service.ObserveSessionAsync(Token)).Should().Equal(survivor);
        (await service.UseAsync(survivor.Id, survivor.Revision, MemoryDestination.Local, Token))
            .Use!.Candidate.Should().Be(Candidate);
    }

    [Theory]
    [InlineData(MemoryScopeKind.Session)]
    [InlineData(MemoryScopeKind.DeviceProfile)]
    public async Task AuthoritativeReconciliationPreservesGenuinelyVolatileProposalReviewAndAdmission(
        MemoryScopeKind scopeKind)
    {
        using var f = new Fixture(durable: true);
        using var service = f.CreateService(true, scopeKind);
        using var root = f.Root();
        var proposed = (await service.ProposeAsync(Candidate, f.MakeScope(scopeKind),
            MemoryProposalOrigin.User, Token)).Record!;
        f.Stored.Should().BeEmpty();
        (await service.ObserveSessionAsync(Token)).Should().Equal(proposed);

        var reviewed = await service.ReviewAsync(proposed.Id, proposed.Revision, true, Token);
        reviewed.Outcome.Should().Be(MemoryOutcome.Succeeded);
        f.Stored.Should().BeEmpty();
        var admitted = await service.AdmitAsync(proposed.Id, reviewed.Record!.Revision, Token);

        admitted.Outcome.Should().Be(MemoryOutcome.Succeeded);
        f.Stored.Values.Should().Equal(admitted.Record!);
        (await service.UseAsync(proposed.Id, admitted.Record!.Revision, MemoryDestination.Local, Token))
            .Use!.Candidate.Should().Be(Candidate);
    }

    [Fact]
    public async Task ReconciliationOfAnotherSessionPreservesTheOriginalSessionsPendingEdit()
    {
        using var f = new Fixture(durable: true);
        MemoryRecord edited;
        using (f.Root())
        {
            var admitted = await f.Admitted();
            edited = (await f.Service.EditAsync(admitted.Id, admitted.Revision, Candidate, Token)).Record!;
        }
        var originalRequest = f.Request;
        var originalSession = f.Session;
        var originalBoundary = f.Boundary!;
        var redacted = f.Stored[edited.Id];
        f.Stored.Clear();
        f.SwitchSession();
        using (f.Root())
        {
            var proposed = await f.Service.ProposeAsync(Candidate, MemoryScope.Session(f.Session.SessionId),
                MemoryProposalOrigin.User, Token);
            proposed.Outcome.Should().Be(MemoryOutcome.Succeeded);
            (await f.Service.ObserveSessionAsync(Token)).Should().Equal(proposed.Record!);
        }
        f.RestoreMemorySession(originalRequest, originalSession, originalBoundary);
        f.Stored[edited.Id] = redacted;
        using (f.Root())
        {
            var reviewed = await f.Service.ReviewAsync(edited.Id, edited.Revision, true, Token);
            reviewed.Outcome.Should().Be(MemoryOutcome.Succeeded);
            reviewed.Record!.Candidate.Should().Be(Candidate);
            f.Stored[edited.Id].Candidate.Should().BeNull();
        }
    }

    private sealed partial class Fixture
    {
        internal void RestoreMemorySession(HostRequest request, WorkSessionAuthorization session, MemoryBoundary boundary)
        {
            Request = request;
            Session = session;
            Boundary = boundary;
        }
    }
}
