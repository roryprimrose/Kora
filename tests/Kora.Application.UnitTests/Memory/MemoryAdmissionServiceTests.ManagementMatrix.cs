using AwesomeAssertions;
using Kora.Core.Commands;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Memory;

namespace Kora.Application.UnitTests.Memory;

public sealed partial class MemoryAdmissionServiceTests
{
    public static TheoryData<string, MemoryCommandOperation, bool> ManagementTransitions
    {
        get
        {
            var data = new TheoryData<string, MemoryCommandOperation, bool>();
            foreach (var state in new[] { "proposed", "reviewed", "rejected", "admitted", "disabled", "forgotten", "restart-shell" })
            {
                foreach (var operation in new[] { MemoryCommandOperation.Review, MemoryCommandOperation.Admit,
                    MemoryCommandOperation.Edit, MemoryCommandOperation.Disable, MemoryCommandOperation.Forget })
                {
                    var succeeds = operation switch
                    {
                        MemoryCommandOperation.Review => state is "proposed",
                        MemoryCommandOperation.Admit => state is "reviewed",
                        MemoryCommandOperation.Edit or MemoryCommandOperation.Forget => state is not "forgotten",
                        MemoryCommandOperation.Disable => state is "admitted",
                        _ => false,
                    };
                    data.Add(state, operation, succeeds);
                }
            }
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(ManagementTransitions))]
    public async Task ManagementEnforcesEveryStateAndMutationTransition(string state, MemoryCommandOperation operation, bool succeeds)
    {
        using var fixture = new Fixture(durable: true);
        using (var root = fixture.Root()) { await fixture.Admitted(); }
        await using var service = fixture.Management();
        var row = (await Run(service, fixture, MemoryCommandOperation.List)).Memories.Single();
        if (state is "proposed" or "reviewed" or "rejected" or "restart-shell")
        {
            row = (await Run(service, fixture, MemoryCommandOperation.Edit, row, Candidate)).Memories.Single();
            if (state is "reviewed" or "rejected")
            {
                await Run(service, fixture, MemoryCommandOperation.Inspect, row);
                row = (await service.ExecuteAsync(new(MemoryCommandOperation.Review, fixture.Session.SessionId.Value,
                    row.Id, row.Revision, Accept: state is "reviewed"), RequestOrigin.LocalUi, () => true, Token)).Memories.Single();
            }
            if (state is "restart-shell")
            {
                service.Admission.ClearSessionCache(fixture.Session.SessionId);
                row = (await Run(service, fixture, MemoryCommandOperation.List)).Memories.Single();
            }
        }
        else if (state is "disabled" or "forgotten")
        {
            row = (await Run(service, fixture, state is "disabled"
                ? MemoryCommandOperation.Disable : MemoryCommandOperation.Forget, row)).Memories.Single();
        }
        if (operation == MemoryCommandOperation.Review) { await Run(service, fixture, MemoryCommandOperation.Inspect, row); }
        var saved = fixture.Stored.Single().Value;
        var result = await Run(service, fixture, operation, row, Candidate);
        result.Outcome.Should().Be(succeeds ? "Succeeded" : "InvalidTransition");
        result.Inspected.Should().BeNull();
        var after = (await Run(service, fixture, MemoryCommandOperation.List)).Memories.Single();
        after.Revision.Should().Be(row.Revision + (succeeds ? 1 : 0));
        if (!succeeds)
        {
            after.Should().Be(row);
            fixture.Stored.Single().Value.Should().Be(saved);
        }
        else
        {
            var expected = operation switch
            {
                MemoryCommandOperation.Review => (MemoryReviewState.Reviewed, MemoryRetentionState.Pending),
                MemoryCommandOperation.Admit => (MemoryReviewState.Admitted, MemoryRetentionState.Enabled),
                MemoryCommandOperation.Edit => (MemoryReviewState.Proposed, MemoryRetentionState.Pending),
                MemoryCommandOperation.Disable => (MemoryReviewState.Admitted, MemoryRetentionState.Disabled),
                _ => (row.Review, MemoryRetentionState.Forgotten),
            };
            (after.Review, after.Retention).Should().Be(expected);
            if (operation is MemoryCommandOperation.Edit or MemoryCommandOperation.Forget)
            {
                fixture.Stored.Single().Value.Candidate.Should().BeNull();
                fixture.Stored.Single().Value.Receipt.Should().BeNull();
            }
        }
        fixture.ControlOutcomes.Count.Should().Be(fixture.ControlIntents.Count);
        fixture.ControlOutcomes.Should().OnlyContain(outcome =>
            outcome.State == HostTaskState.Succeeded || outcome.State == HostTaskState.Denied);
    }

    [Fact]
    public async Task ManagementRejectsCompletedLocalCallbacksInsteadOfMintingFreshAuthority()
    {
        using var fixture = new Fixture(durable: true);
        await using var service = fixture.Management();
        using var root = fixture.Root();
        root.Complete(HostOperationOutcome.Completed);
        var action = () => Run(service, fixture, MemoryCommandOperation.List);
        await action.Should().ThrowAsync<InvalidOperationException>().WithMessage("*live original*");
        fixture.ControlIntents.Should().BeEmpty();
    }

    [Theory]
    [InlineData(MemoryCommandOperation.Review)]
    [InlineData(MemoryCommandOperation.Admit)]
    [InlineData(MemoryCommandOperation.Edit)]
    [InlineData(MemoryCommandOperation.Disable)]
    [InlineData(MemoryCommandOperation.Forget)]
    public async Task ManagementRejectsExplicitModelOriginsBeforeRecordingIntent(MemoryCommandOperation operation)
    {
        using var fixture = new Fixture(durable: true);
        await using var service = fixture.Management();
        var action = () => service.ExecuteAsync(new(operation, fixture.Session.SessionId.Value,
            Guid.NewGuid(), 1, Candidate: Candidate), RequestOrigin.HostSystem, () => true, Token);
        await action.Should().ThrowAsync<InvalidOperationException>();
        fixture.ControlIntents.Should().BeEmpty();
        fixture.Stored.Should().BeEmpty();
    }

    public static TheoryData<MemoryCommandOperation, string> MutationBoundaryChanges
    {
        get
        {
            var data = new TheoryData<MemoryCommandOperation, string>();
            foreach (var operation in new[] { MemoryCommandOperation.Review, MemoryCommandOperation.Admit,
                MemoryCommandOperation.Edit, MemoryCommandOperation.Disable, MemoryCommandOperation.Forget })
            {
                foreach (var change in new[] { "owner", "privacy", "revision", "generation", "session", "lifecycle", "input", "cancel", "corrupt" })
                {
                    data.Add(operation, change);
                }
            }
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(MutationBoundaryChanges))]
    public async Task ManagementRevalidatesEachMutationAtTheAuthoritativeBoundary(MemoryCommandOperation operation, string change)
    {
        using var fixture = new Fixture(durable: true);
        using (var root = fixture.Root()) { await fixture.Admitted(); }
        await using var service = fixture.Management();
        var row = (await Run(service, fixture, MemoryCommandOperation.List)).Memories.Single();
        if (operation is MemoryCommandOperation.Review or MemoryCommandOperation.Admit)
        {
            row = (await Run(service, fixture, MemoryCommandOperation.Edit, row, Candidate)).Memories.Single();
            await Run(service, fixture, MemoryCommandOperation.Inspect, row);
            if (operation == MemoryCommandOperation.Admit)
            {
                row = (await Run(service, fixture, MemoryCommandOperation.Review, row)).Memories.Single();
            }
        }
        var saved = fixture.Stored.Single().Value;
        var eligible = true;
        var transitions = 0;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        fixture.OnStoreTransition = () =>
        {
            if (++transitions != 2) { return; }
            switch (change)
            {
                case "owner": fixture.Current = false; break;
                case "privacy": fixture.CanControl = false; break;
                case "revision": fixture.ControlRevision++; break;
                case "generation": fixture.Boundary = fixture.Boundary! with { Generation = new(2) }; break;
                case "session": fixture.Boundary = fixture.Boundary! with { Session = new(Guid.NewGuid()) }; break;
                case "lifecycle": service.ExpireSession(fixture.Session.SessionId); break;
                case "input": eligible = false; break;
                case "cancel": cancellation.Cancel(); break;
                case "corrupt": throw new InvalidDataException("Invalid saved memory.");
            }
        };
        var action = () => service.ExecuteAsync(new(operation, fixture.Session.SessionId.Value, row.Id, row.Revision,
            Accept: true, Candidate: Candidate), RequestOrigin.LocalUi, () => eligible, cancellation.Token);
        if (change is "corrupt") { await action.Should().ThrowAsync<InvalidDataException>(); }
        else { await action.Should().ThrowAsync<Exception>(); }
        transitions.Should().Be(2);
        fixture.Stored.Single().Value.Should().Be(saved);
    }

    [Fact]
    public async Task ManagementNeverPublishesContentOrClaimsRollbackAfterCommittedPrivacyClosure()
    {
        using var fixture = new Fixture(durable: true);
        using (var root = fixture.Root()) { await fixture.Admitted(); }
        await using var service = fixture.Management();
        var row = (await Run(service, fixture, MemoryCommandOperation.List)).Memories.Single();
        var commits = 0;
        fixture.OnAfterStoreCommit = () =>
        {
            if (++commits == 2) { fixture.CanControl = false; }
        };
        var action = () => Run(service, fixture, MemoryCommandOperation.Disable, row);
        await action.Should().ThrowAsync<InvalidOperationException>().WithMessage("*no rollback*");
        fixture.Stored.Single().Value.Retention.Should().Be(MemoryRetentionState.Disabled);
        fixture.ControlOutcomes.Last().State.Should().Be(HostTaskState.Succeeded);
        fixture.ControlOutcomes.Count.Should().Be(fixture.ControlIntents.Count);
        fixture.CanControl = true;
        (await Run(service, fixture, MemoryCommandOperation.List)).Memories.Single().Retention.Should().Be(MemoryRetentionState.Disabled);
    }
}
