using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Providers;

namespace Kora.Application.UnitTests.Configuration;

public sealed partial class SessionProviderProfileConfigurationTests
{
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    [InlineData(4)] [InlineData(5)] [InlineData(6)] [InlineData(7)]
    [InlineData(8)] [InlineData(9)] [InlineData(10)] [InlineData(11)]
    [InlineData(12)] [InlineData(13)] [InlineData(14)] [InlineData(15)]
    public async Task CopyPreservesEverySparseCombinationRatherThanFlatteningTheEffectiveTuple(int mask)
    {
        await using var f = new Fixture(current: true);
        var tuple = f.Profiles.Preferences.Value!.For(ProviderService.Local)!;
        var sparse = new SessionProviderOverrides((mask & 1) != 0 ? ProviderService.Local : null,
            (mask & 2) != 0 ? tuple.Model : null, (mask & 4) != 0 ? tuple.Reasoning : null,
            (mask & 8) != 0 ? tuple.Context : null, (mask & 14) != 0 ? tuple : null);
        f.SourceSnapshot = f.SourceSnapshot with { Revision = 9, Desired = sparse };
        f.Snapshot = f.Snapshot with { Revision = 3, Desired = new(ProviderService.Codex) };
        var source = f.SourceSnapshot;
        var proposal = await f.Copy();
        proposal.Source!.Saved.Should().BeSameAs(source);
        proposal.Draft.Saved.Session.Should().Be(f.Session);
        proposal.Draft.Saved.Desired.Service.Should().Be(ProviderService.Codex);
        proposal.PreviousEffective.Should().BeNull();
        proposal.ResultingEffective.Should().Be(tuple);
        proposal.Desired.Serialize().Should().Be(sparse.Serialize());
        f.Snapshot.Revision.Should().Be(3);
        f.Store.Tasks.Should().BeEmpty();
        var saved = await f.Service.Confirm(proposal, RequestOrigin.LocalUi, () => f.Eligible, f.Token);
        saved.Desired.Serialize().Should().Be(sparse.Serialize());
        saved.Revision.Should().Be(4);
        f.SourceSnapshot.Should().BeSameAs(source);
        f.Store.Tasks.Should().OnlyContain(task => task.Request.SessionId == f.Session.SessionId
            && task.Request.InvocationId == null);
        f.Profiles.Preferences.Writes.Should().Be(0);
        await f.Service.Invoking(service => service.Confirm(proposal, RequestOrigin.LocalUi, () => true, f.Token))
            .Should().ThrowAsync<InvalidOperationException>();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnavailableServiceAndAllInheritCanBeCopiedWithoutInventingModelsOrExecution(bool inherit)
    {
        await using var f = new Fixture();
        f.SourceSnapshot = f.SourceSnapshot with { Desired = inherit ? SessionProviderOverrides.Inherit : new(ProviderService.Claude) };
        var proposal = await f.Copy(RequestOrigin.ActivatedVoice);
        proposal.Source!.Status.Should().Contain("Unavailable");
        proposal.ResultingEffective.Should().BeNull();
        await f.Service.Confirm(proposal, RequestOrigin.ActivatedVoice, () => true, f.Token);
        f.Snapshot.Desired.Serialize().Should().Be(f.SourceSnapshot.Desired.Serialize());
        // Even identical values use the existing destination-only audited revision/terminal control path.
        f.Snapshot.Revision.Should().Be(1);
        f.SourceSnapshot.Revision.Should().Be(0);
        f.Profiles.Preferences.Writes.Should().Be(0);
    }

    [Theory]
    [InlineData("service")] [InlineData("account")] [InlineData("catalogue")]
    [InlineData("model")] [InlineData("reasoning")] [InlineData("context")]
    [InlineData("unavailable")] [InlineData("expired")]
    public async Task CopyRejectsUnavailableOrForeignBindingsWithoutRebindingMatchingIdsOrLabels(string failure)
    {
        await using var f = new Fixture(current: true);
        var tuple = f.Profiles.Preferences.Value!.For(ProviderService.Local)!;
        var bound = failure switch
        {
            "service" => tuple with { Service = ProviderService.Claude },
            "account" => tuple with { Account = new("other-account") },
            "catalogue" => tuple with { CatalogueRevision = new(2) },
            "model" => tuple with { Model = new("missing-model") },
            "reasoning" => tuple with { Reasoning = new("missing-effort") },
            "context" => tuple with { Context = new("missing-window") },
            _ => tuple,
        };
        f.SourceSnapshot = f.SourceSnapshot with { Desired = new(model: bound.Model, reasoning: bound.Reasoning, context: bound.Context, binding: bound) };
        if (failure is "unavailable") { f.Profiles.Catalogue.Status = ProviderCatalogueStatus.Unavailable; }
        if (failure is "expired") { f.Profiles.Time.Now = f.Profiles.Catalogue.Expiry; }
        await f.Invoking(value => value.Copy()).Should().ThrowAsync<InvalidOperationException>();
        f.Store.Tasks.Should().BeEmpty();
        f.Snapshot.Revision.Should().Be(0);
    }

    [Theory]
    [InlineData("source-revision")] [InlineData("destination-revision")]
    [InlineData("source-generation")] [InlineData("destination-generation")]
    [InlineData("source-done")] [InlineData("destination-done")]
    [InlineData("source-held")] [InlineData("destination-held")]
    [InlineData("account")] [InlineData("catalogue")] [InlineData("shape")]
    [InlineData("app")] [InlineData("app-aba")] [InlineData("private")]
    [InlineData("control")] [InlineData("input")] [InlineData("expiry")]
    [InlineData("catalogue-expiry")] [InlineData("origin")]
    public async Task SeparateConfirmationRevalidatesBothSnapshotsAndRetiresFailedOneShotTokens(string drift)
    {
        await using var f = new Fixture(current: true);
        var proposal = await f.Copy();
        switch (drift)
        {
            case "source-revision": f.SourceSnapshot = f.SourceSnapshot with { Revision = 2 }; break;
            case "destination-revision": f.Snapshot = f.Snapshot with { Revision = 2 }; break;
            case "source-generation": f.SourceSnapshot = f.SourceSnapshot with { Session = f.SourceSnapshot.Session with { Generation = new(2) } }; break;
            case "destination-generation": f.Snapshot = f.Snapshot with { Session = f.Snapshot.Session with { Generation = new(2) } }; break;
            case "source-done": f.SourceSnapshot = f.SourceSnapshot with { Session = f.SourceSnapshot.Session with { IsActive = false } }; break;
            case "destination-done": f.Snapshot = f.Snapshot with { Session = f.Snapshot.Session with { IsActive = false } }; break;
            case "source-held": f.SourceSnapshot = f.SourceSnapshot with { Held = true }; break;
            case "destination-held": f.Snapshot = f.Snapshot with { Held = true }; break;
            case "account": f.Profiles.Catalogue.Account = "changed"; break;
            case "catalogue": f.Profiles.Catalogue.Revision = 2; break;
            case "shape": f.Profiles.Catalogue.Model = "different-model-same-label"; break;
            case "app": f.Profiles.Preferences.Value = new(ProviderService.Claude, []); break;
            case "app-aba":
                var appBefore = f.Profiles.Preferences.Value!.Serialize();
                await f.Profiles.Refresh();
                (await f.Profiles.Select(ProviderService.Claude)).Should().BeTrue();
                await f.Profiles.Refresh();
                (await f.Profiles.Select(ProviderService.Local)).Should().BeTrue();
                f.Profiles.Preferences.Value!.Serialize().Should().Be(appBefore);
                f.Profiles.Service.Get().Revision.Should().BeGreaterThan(proposal.AppRevision);
                break;
            case "private": f.Access.ControlRevision++; break;
            case "control": f.Access.CanControl = false; break;
            case "input": f.Eligible = false; break;
            case "expiry": f.Profiles.Time.Now = proposal.ExpiresAt; break;
            case "catalogue-expiry": f.Profiles.Catalogue.Expiry = f.Profiles.Time.Now.AddSeconds(1); f.Profiles.Time.Now = f.Profiles.Catalogue.Expiry; break;
        }
        await f.Service.Invoking(service => service.Confirm(proposal,
            drift is "origin" ? RequestOrigin.ActivatedVoice : RequestOrigin.LocalUi, () => f.Eligible, f.Token))
            .Should().ThrowAsync<InvalidOperationException>();
        var revision = f.Snapshot.Revision;
        await f.Service.Invoking(service => service.Confirm(proposal, RequestOrigin.LocalUi, () => true, f.Token))
            .Should().ThrowAsync<InvalidOperationException>();
        f.Snapshot.Revision.Should().Be(revision);
    }

    [Theory]
    [InlineData("same")] [InlineData("foreign")] [InlineData("held")]
    [InlineData("inactive")] [InlineData("control")] [InlineData("callback")]
    public async Task CopyRequiresDistinctHostResolvedSessionsAndOriginalCurrentControl(string failure)
    {
        await using var f = new Fixture();
        if (failure is "held") { f.SourceSnapshot = f.SourceSnapshot with { Held = true }; }
        if (failure is "inactive") { f.Snapshot = f.Snapshot with { Session = f.Snapshot.Session with { IsActive = false } }; }
        if (failure is "control") { f.Access.CanControl = false; }
        var request = HostRequest.Create(RequestOrigin.LocalUi);
        using var root = failure is "callback"
            ? HostActivity.BeginRoot(new(request.RequestId, request.SessionId, request.TaskId, request.Origin, new(Guid.NewGuid())), HostActivityLayer.Application, HostOperation.Request)
            : null;
        var source = failure is "same" ? f.Session.SessionId
            : failure is "foreign" ? new HostId<SessionIdentity>(Guid.NewGuid()) : f.SourceSnapshot.Session.SessionId;
        await f.Service.Invoking(service => service.ReviewCopy(source, f.Session.SessionId, RequestOrigin.LocalUi, () => true, f.Token))
            .Should().ThrowAsync<Exception>();
        f.Store.Tasks.Should().BeEmpty();
    }

    [Theory]
    [InlineData("retire")] [InlineData("discard-source")] [InlineData("discard-destination")]
    [InlineData("source-at-effect")] [InlineData("destination-at-effect")]
    [InlineData("source-after-read")] [InlineData("retire-in-live")]
    [InlineData("cancel")] [InlineData("audit")] [InlineData("readback")]
    public async Task ReentrantRetirementCancellationAndUncertainEffectsNeverClaimCopyOrRetry(string failure)
    {
        await using var f = new Fixture();
        f.SourceSnapshot = f.SourceSnapshot with { Desired = new(ProviderService.Codex) };
        var proposal = await f.Copy();
        using var cancellation = new CancellationTokenSource();
        if (failure is "retire") { f.Profiles.Preferences.AfterRead = () => f.Service.Retire(proposal); }
        if (failure is "retire-in-live")
        {
            var reads = 0;
            f.Profiles.Preferences.AfterRead = () => { if (++reads == 2) { f.Service.Retire(proposal); } };
        }
        if (failure is "discard-source") { f.Service.Discard(proposal.Source!); }
        if (failure is "discard-destination") { f.Service.Discard(proposal.Draft); }
        if (failure is "source-at-effect") { f.BeforeCommit = () => f.SourceSnapshot = f.SourceSnapshot with { Revision = 2 }; }
        if (failure is "destination-at-effect") { f.BeforeCommit = () => f.Snapshot = f.Snapshot with { Revision = 2 }; }
        if (failure is "source-after-read")
        {
            f.AfterProfileRead = session =>
            {
                if (session == f.SourceSnapshot.Session.SessionId) { f.SourceSnapshot = f.SourceSnapshot with { Revision = 2 }; }
            };
        }
        if (failure is "cancel") { cancellation.Cancel(); }
        if (failure is "audit") { f.CommitFailure = new IOException("audit unavailable"); }
        if (failure is "readback") { f.FailReadback = true; }
        await f.Service.Invoking(service => service.Confirm(proposal, RequestOrigin.LocalUi, () => true, cancellation.Token))
            .Should().ThrowAsync<Exception>();
        f.Snapshot.Desired.Service.Should().Be(failure is "readback" ? ProviderService.Codex : null);
        f.SourceSnapshot.Desired.Service.Should().Be(ProviderService.Codex);
        f.Profiles.Preferences.AfterRead = null;
        await f.Service.Invoking(service => service.Confirm(proposal, RequestOrigin.LocalUi, () => true, f.Token))
            .Should().ThrowAsync<InvalidOperationException>();
        if (failure is "audit" or "readback")
        { f.Logger.Requests.Should().ContainSingle().Which.SessionId.Should().Be(f.Session.SessionId); }
    }

    [Fact]
    public async Task AnAdmittedDoneSourceSuppliesOnlyDataAndNeverResumesOrMutatesItsLifecycle()
    {
        await using var f = new Fixture();
        f.SourceSnapshot = f.SourceSnapshot with { Session = f.SourceSnapshot.Session with { IsActive = false }, Desired = new(ProviderService.Claude) };
        var before = f.SourceSnapshot;
        var proposal = await f.Copy();
        await f.Service.Confirm(proposal, RequestOrigin.LocalUi, () => true, f.Token);
        f.SourceSnapshot.Should().BeSameAs(before);
        f.Snapshot.Desired.Service.Should().Be(ProviderService.Claude);
        f.Store.Tasks.Should().OnlyContain(task => task.Request.SessionId == f.Session.SessionId);
    }

    [Fact]
    public async Task UnavailableSnapshotsAlsoBoundCopyLifetimeAndExpiredDraftsCannotIssueNewTokens()
    {
        await using var f = new Fixture();
        f.Profiles.Catalogue.Expiry = f.Profiles.Time.Now.AddSeconds(1);
        var proposal = await f.Copy();
        var draft = await f.Inspect();
        proposal.ExpiresAt.Should().Be(f.Profiles.Catalogue.Expiry);
        f.Profiles.Time.Now = proposal.ExpiresAt;
        f.Service.Invoking(service => service.SelectService(draft, ProviderService.Claude)).Should().Throw<InvalidOperationException>();
        await f.Service.Invoking(service => service.Confirm(proposal, RequestOrigin.LocalUi, () => true, f.Token))
            .Should().ThrowAsync<InvalidOperationException>();
        f.Snapshot.Revision.Should().Be(0);
    }

    [Fact]
    public async Task ForgedAndForeignProposalsCannotReuseHostIssuedConfirmationOrDestinationAuthority()
    {
        await using var f = new Fixture();
        await using var other = new Fixture();
        var proposal = await f.Copy();
        var forged = new SessionProviderProposal(proposal.Draft, proposal.Desired, null, proposal.ResultingEffective,
            proposal.ExpiresAt, proposal.Source);
        await f.Service.Invoking(service => service.Confirm(forged, RequestOrigin.LocalUi, () => true, f.Token))
            .Should().ThrowAsync<InvalidOperationException>();
        await other.Service.Invoking(service => service.Confirm(proposal, RequestOrigin.LocalUi, () => true, f.Token))
            .Should().ThrowAsync<InvalidOperationException>();
        await f.Service.Confirm(proposal, RequestOrigin.LocalUi, () => true, f.Token);
        other.Snapshot.Revision.Should().Be(0);
    }

    [Fact]
    public async Task ReviewCannotJoinDifferentAppOrCatalogueSnapshotsAndCleansBothPreviewLeases()
    {
        await using var f = new Fixture(current: true);
        var calls = 0;
        f.Profiles.Catalogue.AfterObservation = () => { if (++calls == 1) { f.Profiles.Catalogue.Revision++; } };
        await f.Invoking(value => value.Copy()).Should().ThrowAsync<InvalidOperationException>();
        f.Profiles.Catalogue.AfterObservation = null;
        for (var index = 0; index < 70; index++)
        {
            var proposal = await f.Copy();
            f.Service.Discard(proposal.Draft);
        }
        f.Store.Tasks.Should().BeEmpty();
    }

    [Fact]
    public async Task SourceLeaseRetiredByAdmissionCallbackCannotEnterConfirmationOrWrite()
    {
        await using var f = new Fixture();
        SessionProviderProposal? proposal = null;
        var withdrawSource = false;
        proposal = await f.Service.ReviewCopy(f.SourceSnapshot.Session.SessionId, f.Session.SessionId,
            RequestOrigin.LocalUi, () =>
            {
                if (withdrawSource)
                {
                    withdrawSource = false;
                    f.Service.Discard(proposal!.Source!);
                }
                return true;
            }, f.Token);
        withdrawSource = true;
        await f.Service.Invoking(service => service.Confirm(proposal, RequestOrigin.LocalUi, () => true, f.Token))
            .Should().ThrowAsync<InvalidOperationException>();
        f.Snapshot.Revision.Should().Be(0);
        f.SourceSnapshot.Revision.Should().Be(0);
        f.Store.Tasks.Should().BeEmpty();
    }
}
