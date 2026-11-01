using AwesomeAssertions;
using Kora.Application.Memory;
using Kora.Core.Commands;
using Kora.Core.Hosting;
using Kora.Core.Memory;

namespace Kora.Application.UnitTests.Memory;

public sealed partial class MemoryAdmissionServiceTests
{
    private static async Task<MemoryRecord> InspectCopySource(Fixture f, MemoryManagementService service)
    {
        var proposed = (await Run(service, f, MemoryCommandOperation.Propose, candidate: Candidate)).Memories.Single();
        await Run(service, f, MemoryCommandOperation.Inspect, proposed);
        var reviewed = (await Run(service, f, MemoryCommandOperation.Review, proposed)).Memories.Single();
        var admitted = (await Run(service, f, MemoryCommandOperation.Admit, reviewed)).Memories.Single();
        return (await Run(service, f, MemoryCommandOperation.Inspect, admitted)).Inspected!;
    }

    [Theory]
    [InlineData(RequestOrigin.LocalUi)]
    [InlineData(RequestOrigin.ActivatedVoice)]
    public async Task SessionCopyCompletesFreshClassificationSeparateReviewAndAdmissionThenIndependentRestart(RequestOrigin origin)
    {
        using var f = new Fixture(durable: true);
        await using var service = f.Management();
        var source = await InspectCopySource(f, service);
        SessionMemoryCopyReview review;
        using (f.Root(origin))
        { review = await service.InspectSessionCopyAsync(new(f.Session, null), source, () => true, Token); }
        using (review)
        {
            review.Description.Should().Contain(Candidate.Value!).And.Contain("independent lifetime")
                .And.Contain("does NOT delete").And.Contain("lower-trust").And.Contain("not secrecy detection");
            review.CanPropose.Should().BeTrue();
            review.CanAccept.Should().BeFalse();
            review.CanAdmit.Should().BeFalse();
            MemoryRecord proposed;
            using (f.Root(origin))
            {
                proposed = (await service.ProposeSessionCopyAsync(review, MemoryContentClass.WorkflowPreference, Token)).Record!;
                await service.Invoking(s => s.ReviewSessionCopyAsync(review, true, Token)).Should().ThrowAsync<InvalidOperationException>();
            }
            proposed.Candidate!.ContentClass.Should().Be(MemoryContentClass.WorkflowPreference);
            proposed.Id.Should().NotBe(source.Id);
            proposed.Receipt.Should().BeNull();
            proposed.Lineage.Request.Should().NotBe(source.Lineage.Request);
            proposed.Lineage.CopiedFromSession.Should().Be(new MemorySessionCopySource(source.Id, source.Revision,
                f.Session.SessionId, f.Session.Generation));
            f.Stored.Values.Should().Equal(source);
            foreach (var operation in new[] { MemoryCommandOperation.Review, MemoryCommandOperation.Admit, MemoryCommandOperation.Edit })
            {
                await service.Invoking(s => s.ExecuteDeviceProfileAsync(new(operation, f.Session.SessionId.Value,
                    proposed.Id.Value, proposed.Revision.Value, Accept: true, Candidate: Candidate),
                    origin, () => true, Token)).Should().ThrowAsync<InvalidOperationException>().WithMessage("*copy review*");
            }
            using (f.Root(origin))
            { (await service.ReviewSessionCopyAsync(review, true, Token)).Record!.Receipt.Should().NotBe(source.Receipt); }
            review.CanAdmit.Should().BeTrue();
            f.Stored.Values.Should().Equal(source);
            MemoryRecord admitted;
            using (f.Root(origin))
            { admitted = (await service.AdmitSessionCopyAsync(review, Token)).Record!; }
            f.Stored[source.Id].Should().Be(source);
            review.Description.Should().BeEmpty();
            f.Audits.Should().NotContain(item => item.ActionId == "memory.use");
            f.Messages.Should().NotContain(text => text.Contains(Candidate.Value!, StringComparison.Ordinal));
            f.SwitchSession();
            using var restarted = f.CreateService(true, MemoryScopeKind.DeviceProfile);
            using (f.Root())
            {
                (await restarted.ObserveSessionAsync(Token)).Should().Equal(admitted);
                (await restarted.UseAsync(admitted.Id, admitted.Revision, MemoryDestination.Hosted, Token)).Use.Should().BeNull();
            }
        }
    }

    private static readonly int[] CopyStages = [0, 1, 2];
    private static readonly string[] CopyFaults =
        ["edit", "disable", "forget", "delete", "replace", "privacy", "control", "profile", "generation", "cancel", "closed"];
    public static IEnumerable<object[]> SessionCopyDrifts =>
        from stage in CopyStages
        from fault in CopyFaults
        select new object[] { stage, fault };

    [Theory]
    [MemberData(nameof(SessionCopyDrifts))]
    public async Task SessionCopyRetiresOnSourceOrAuthorityDriftBeforeEveryConsequentialAction(int stage, string fault)
    {
        using var f = new Fixture(durable: true);
        await using var service = f.Management();
        var source = await InspectCopySource(f, service);
        var open = true;
        SessionMemoryCopyReview review;
        using (f.Root())
        { review = await service.InspectSessionCopyAsync(new(f.Session, null), source, () => open, Token); }
        using (review)
        {
            if (stage > 0)
            {
                using (f.Root()) { await service.ProposeSessionCopyAsync(review, MemoryContentClass.Decision, Token); }
            }
            if (stage > 1)
            {
                using (f.Root()) { await service.ReviewSessionCopyAsync(review, true, Token); }
            }
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
            switch (fault)
            {
                case "edit": f.Stored[source.Id] = source with { Revision = new(4), Candidate = Candidate with { Value = "new value" } }; break;
                case "disable": f.Stored[source.Id] = source with { Retention = MemoryRetentionState.Disabled }; break;
                case "forget": f.Stored[source.Id] = source with { Candidate = null, Receipt = null, Retention = MemoryRetentionState.Forgotten }; break;
                case "delete": f.Stored.Remove(source.Id); break;
                case "replace": f.Stored.Remove(source.Id); var replacement = source with { Id = new(Guid.NewGuid()) }; f.Stored[replacement.Id] = replacement; break;
                case "privacy": f.CanControl = false; break;
                case "control": f.ControlRevision++; break;
                case "profile": f.Boundary = f.Boundary! with { Profile = new(Guid.NewGuid()) }; break;
                case "generation": f.Session = f.Session with { Generation = new(2) }; break;
                case "cancel": cancellation.Cancel(); break;
                case "closed": open = false; break;
            }
            using (f.Root())
            {
                Task<MemoryResult> Act() => stage == 0 ? service.ProposeSessionCopyAsync(review, MemoryContentClass.Decision, cancellation.Token)
                    : stage == 1 ? service.ReviewSessionCopyAsync(review, true, cancellation.Token)
                    : service.AdmitSessionCopyAsync(review, cancellation.Token);
                await FluentActions.Awaiting(Act).Should().ThrowAsync<Exception>();
            }
            review.Dispose();
            review.Current.Should().BeFalse();
            f.Stored.Values.Should().NotContain(row => row.Scope.Kind == MemoryScopeKind.DeviceProfile);
        }
    }

    [Theory]
    [InlineData("reject")]
    [InlineData("forbidden")]
    [InlineData("audit")]
    [InlineData("commit")]
    [InlineData("capacity")]
    [InlineData("uncertain")]
    public async Task SessionCopyReportsFailureWithoutInheritingSourceAuthorityOrClaimingRollback(string fault)
    {
        using var f = new Fixture(durable: true);
        await using var service = f.Management();
        var source = await InspectCopySource(f, service);
        SessionMemoryCopyReview review;
        using (f.Root())
        { review = await service.InspectSessionCopyAsync(new(f.Session, null), source, () => true, Token); }
        using (review)
        {
            using (f.Root())
            {
                var result = await service.ProposeSessionCopyAsync(review, fault is "forbidden"
                    ? MemoryContentClass.Secret : MemoryContentClass.ResponsePreference, Token);
                if (fault is "forbidden")
                {
                    result.Reason.Should().Be(MemoryReason.ContentForbidden);
                    f.Stored.Values.Should().Equal(source);
                    return;
                }
            }
            using (f.Root()) { await service.ReviewSessionCopyAsync(review, fault is not "reject", Token); }
            if (fault is "reject") { review.Current.Should().BeFalse(); return; }
            f.FailTerminalAudit = fault is "audit";
            f.FailStoreCommit = fault is "commit";
            if (fault is "capacity")
            {
                f.OnTerminalAudit = () =>
                {
                    if (string.Equals(f.Audits.Last().ActionId, "memory.admit", StringComparison.Ordinal))
                    { f.StoreResult = new(MemoryOutcome.CapacityExceeded, MemoryReason.Capacity); }
                };
            }
            var open = true;
            if (fault is "uncertain") { f.OnAfterStoreCommit = () => { if (f.Stored.Count == 2) { open = false; f.CanControl = false; } }; }
            using (f.Root())
            {
                if (fault is "audit" or "commit" or "uncertain")
                {
                    await service.Invoking(s => s.AdmitSessionCopyAsync(review, Token)).Should().ThrowAsync<Exception>();
                }
                else { (await service.AdmitSessionCopyAsync(review, Token)).Outcome.Should().Be(MemoryOutcome.CapacityExceeded); }
            }
            f.Stored[source.Id].Should().Be(source);
            f.Stored.Count.Should().Be(fault is "uncertain" ? 2 : 1);
            open.Should().Be(fault is not "uncertain");
            review.Current.Should().BeFalse();
        }
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("expire")]
    [InlineData("cache")]
    [InlineData("uninspected")]
    [InlineData("foreign")]
    [InlineData("completed")]
    [InlineData("host")]
    [InlineData("invocation")]
    public async Task SessionCopyRequiresExactInspectionLiveOriginalInputAndCurrentUnexpiredOwner(string fault)
    {
        using var f = new Fixture(durable: true);
        var clock = new ModelProposalClock();
        await using var service = f.Management(time: clock);
        var source = await InspectCopySource(f, service);
        if (fault is "uninspected")
        {
            service.ClearInspection();
            using (f.Root())
            {
                await service.Invoking(s => s.InspectSessionCopyAsync(new(f.Session, null), source, () => true, Token))
                    .Should().ThrowAsync<InvalidOperationException>();
            }
            return;
        }
        if (fault is "foreign")
        {
            using (f.Root())
            {
                await service.Invoking(s => s.InspectSessionCopyAsync(
                    new(f.Session with { SessionId = new(Guid.NewGuid()) }, null), source, () => true, Token))
                    .Should().ThrowAsync<InvalidOperationException>();
            }
            return;
        }
        SessionMemoryCopyReview review;
        using (f.Root())
        { review = await service.InspectSessionCopyAsync(new(f.Session, null), source, () => true, Token); }
        using (review)
        {
            if (fault is "cache")
            {
                using (f.Root()) { await service.ProposeSessionCopyAsync(review, MemoryContentClass.Decision, Token); }
                service.ClearVolatile();
            }
            if (fault is "expire") { clock.Now = DateTimeOffset.UnixEpoch.AddSeconds(60); }
            if (fault is "owner")
            {
                await using var other = f.Management();
                using (f.Root())
                {
                    await other.Invoking(s => s.ProposeSessionCopyAsync(review, MemoryContentClass.Decision, Token))
                        .Should().ThrowAsync<InvalidOperationException>().WithMessage("*another memory owner*");
                }
            }
            else
            {
                using var input = f.Root(fault is "host" ? RequestOrigin.HostSystem : RequestOrigin.LocalUi);
                if (fault is "completed") { input.Complete(Kora.Core.Diagnostics.HostOperationOutcome.Completed); }
                var request = new HostRequest(f.Request.RequestId, f.Request.SessionId, f.Request.TaskId,
                    f.Request.Origin, new(Guid.NewGuid()));
                using var invocation = fault is "invocation"
                    ? Kora.Core.Diagnostics.HostActivity.BeginRoot(request,
                        Kora.Core.Diagnostics.HostActivityLayer.Application, Kora.Core.Diagnostics.HostOperation.Request)
                    : null;
                await FluentActions.Awaiting(() => fault is "cache"
                    ? service.ReviewSessionCopyAsync(review, true, Token)
                    : service.ProposeSessionCopyAsync(review, MemoryContentClass.Decision, Token))
                    .Should().ThrowAsync<InvalidOperationException>();
            }
            f.Stored.Values.Should().Equal(source);
        }
    }

    [Theory]
    [InlineData("proposed")]
    [InlineData("disabled")]
    [InlineData("body")]
    public async Task SessionCopyInspectionRefusesUnreviewedDisabledAndRedactedSources(string state)
    {
        using var f = new Fixture(durable: true);
        await using var service = f.Management();
        var source = await InspectCopySource(f, service);
        source = state switch
        {
            "proposed" => source with { Review = MemoryReviewState.Proposed },
            "disabled" => source with { Retention = MemoryRetentionState.Disabled },
            _ => source with { Candidate = null },
        };
        f.Stored[source.Id] = source;
        await Run(service, f, MemoryCommandOperation.Inspect, Summary(source));
        using (f.Root())
        {
            await service.Invoking(s => s.InspectSessionCopyAsync(new(f.Session, null), source, () => true, Token))
                .Should().ThrowAsync<InvalidOperationException>();
        }
        f.Stored.Values.Should().NotContain(row => row.Scope.Kind == MemoryScopeKind.DeviceProfile);
    }

    [Fact]
    public async Task SessionCopyAdmissionCannotUseAnOrdinaryCachedReceiptOrChangedCandidateValue()
    {
        using var f = new Fixture(durable: true);
        using var profile = f.CreateService(true, MemoryScopeKind.DeviceProfile);
        using var root = f.Root();
        var source = await f.Admitted();
        var scope = MemoryScope.DeviceProfile(f.Profile);
        (await profile.ProposeSessionCopyAsync(Candidate with { Value = "replacement" }, scope, source, Token)).Reason
            .Should().Be(MemoryReason.LineageUnknown);
        var proposed = (await profile.ProposeSessionCopyAsync(Candidate, scope, source, Token)).Record!;
        (await profile.ReviewAsync(proposed.Id, proposed.Revision, true, Token)).Reason.Should().Be(MemoryReason.LineageUnknown);
        (await profile.EditAsync(proposed.Id, proposed.Revision, Candidate, Token)).Reason.Should().Be(MemoryReason.LineageUnknown);
        (await profile.UseAsync(proposed.Id, proposed.Revision, MemoryDestination.Local, Token)).Reason.Should().Be(MemoryReason.NotReviewed);
        var reviewed = (await profile.ReviewSessionCopyAsync(proposed, source, true, Token)).Record!;
        (await profile.AdmitAsync(reviewed.Id, reviewed.Revision, Token)).Reason.Should().Be(MemoryReason.LineageUnknown);
        var admitted = (await profile.AdmitSessionCopyAsync(reviewed, source, Token)).Record!;
        (await profile.UseAsync(admitted.Id, admitted.Revision, MemoryDestination.Local, Token)).Use.Should().NotBeNull();
        var next = (await profile.ProposeSessionCopyAsync(Candidate, scope, source, Token)).Record!;
        var rejected = (await profile.ReviewSessionCopyAsync(next, source, false, Token)).Record!;
        (await profile.AdmitAsync(rejected.Id, rejected.Revision, Token)).Outcome.Should().Be(MemoryOutcome.InvalidTransition);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SessionCopyFinalTransactionRefusesSourceForgetAfterFreshReadWithoutSnapshotAuthority(bool forget)
    {
        using var f = new Fixture(durable: true);
        await using var service = f.Management();
        var source = await InspectCopySource(f, service);
        SessionMemoryCopyReview review;
        using (f.Root())
        { review = await service.InspectSessionCopyAsync(new(f.Session, null), source, () => true, Token); }
        using (review)
        {
            using (f.Root()) { await service.ProposeSessionCopyAsync(review, MemoryContentClass.Decision, Token); }
            using (f.Root()) { await service.ReviewSessionCopyAsync(review, true, Token); }
            f.OnTerminalAudit = () =>
            {
                if (string.Equals(f.Audits.Last().ActionId, "memory.admit", StringComparison.Ordinal))
                {
                    f.Stored[source.Id] = forget
                        ? source with { Revision = new(4), Candidate = null, Receipt = null, Retention = MemoryRetentionState.Forgotten }
                        : source with { Revision = new(4), Retention = MemoryRetentionState.Disabled };
                }
            };
            using (f.Root())
            {
                var result = await service.AdmitSessionCopyAsync(review, Token);
                result.Outcome.Should().Be(MemoryOutcome.RevisionConflict);
                result.Record.Should().BeNull();
                result.Use.Should().BeNull();
            }
            review.Current.Should().BeFalse();
            f.Stored.Values.Should().NotContain(row => row.Scope.Kind == MemoryScopeKind.DeviceProfile);
        }
    }
}
