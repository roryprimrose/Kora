using AwesomeAssertions;
using Kora.Core.Commands;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Memory;

namespace Kora.Application.UnitTests.Memory;

public sealed partial class MemoryAdmissionServiceTests
{
    [Fact]
    public async Task UserProposalCreatesOnlyVolatileMetadataThenRequiresInspectReviewAndSeparateAdmission()
    {
        using var fixture = new Fixture(durable: true);
        await using var service = fixture.Management();
        var created = await Run(service, fixture, MemoryCommandOperation.Propose, candidate: Candidate);
        var row = created.Memories.Single();
        row.Revision.Should().Be(1);
        row.Review.Should().Be(MemoryReviewState.Proposed);
        row.Retention.Should().Be(MemoryRetentionState.Pending);
        created.Inspected.Should().BeNull();
        fixture.Stored.Should().BeEmpty();
        var listed = await Run(service, fixture, MemoryCommandOperation.List);
        listed.Memories.Should().Equal(row);
        var review = () => Run(service, fixture, MemoryCommandOperation.Review, row);
        await review.Should().ThrowAsync<InvalidOperationException>();
        (await Run(service, fixture, MemoryCommandOperation.Admit, row)).Outcome.Should().Be("InvalidTransition");
        var inspected = (await Run(service, fixture, MemoryCommandOperation.Inspect, row)).Inspected!;
        inspected.Candidate.Should().Be(Candidate);
        inspected.Lineage.Origin.Should().Be(MemoryProposalOrigin.User);
        inspected.Lineage.Request.Should().Be(fixture.ControlIntents.First().Request);
        row = (await review()).Memories.Single();
        fixture.Stored.Should().BeEmpty();
        row = (await Run(service, fixture, MemoryCommandOperation.Admit, row)).Memories.Single();
        fixture.Stored.Single().Value.Candidate.Should().Be(Candidate);
        row.Review.Should().Be(MemoryReviewState.Admitted);
        fixture.Messages.Should().NotContain(message => message.Contains(Candidate.Value!, StringComparison.Ordinal));
        fixture.Audits.Should().NotContain(item => item.TargetId == Candidate.Value);
    }

    [Theory]
    [InlineData("session")]
    [InlineData("privacy")]
    [InlineData("control")]
    [InlineData("restart")]
    public async Task RevocationDiscardsUnadmittedBodyAndIdentityWithoutWritingADurableShell(string change)
    {
        using var fixture = new Fixture(durable: true);
        await using var service = fixture.Management();
        var row = (await Run(service, fixture, MemoryCommandOperation.Propose, candidate: Candidate)).Memories.Single();
        if (change is "session") { service.ClearSessionDrafts(fixture.Session.SessionId); }
        if (change is "privacy") { service.ClearVolatile(); }
        if (change is "control")
        {
            fixture.ControlRevision++;
            fixture.Boundary = fixture.Boundary! with { Revision = fixture.ControlRevision };
        }
        await using var restart = fixture.Management();
        var target = change is "restart" ? restart : service;
        (await Run(target, fixture, MemoryCommandOperation.List)).Memories.Should().BeEmpty();
        (await Run(target, fixture, MemoryCommandOperation.Inspect, row)).Outcome.Should().Be("NotFound");
        fixture.Stored.Should().BeEmpty();
    }

    [Theory]
    [InlineData("identity")]
    [InlineData("revision")]
    [InlineData("accept")]
    [InlineData("model")]
    [InlineData("completed")]
    [InlineData("stopped")]
    [InlineData("missing-session")]
    [InlineData("cancel")]
    public async Task ProposalCannotSupplyIdentityReviewOrCallbackAuthority(string failure)
    {
        using var fixture = new Fixture(durable: true);
        await using var service = fixture.Management();
        using var root = failure is "model" or "completed" or "stopped" ? fixture.Root(
            failure is "model" ? RequestOrigin.HostSystem : RequestOrigin.LocalUi) : null;
        if (failure is "completed") { root!.Complete(HostOperationOutcome.Completed); }
        if (failure is "stopped") { root!.Activity!.Stop(); }
        using var cancel = new CancellationTokenSource();
        if (failure is "cancel") { cancel.Cancel(); }
        var command = new MemoryCommand(MemoryCommandOperation.Propose,
            failure is "missing-session" ? null : fixture.Session.SessionId.Value,
            failure is "identity" ? Guid.NewGuid() : null, failure is "revision" ? 1 : 0,
            failure is "accept", Candidate);
        var act = () => service.ExecuteAsync(command, RequestOrigin.LocalUi, () => true, cancel.Token);
        await act.Should().ThrowAsync<Exception>();
        fixture.Stored.Should().BeEmpty();
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("privacy")]
    [InlineData("control")]
    [InlineData("generation")]
    [InlineData("session")]
    [InlineData("cache")]
    [InlineData("cancel")]
    public async Task LateProposalCallbacksCannotPublishVolatileCandidates(string change)
    {
        using var fixture = new Fixture(durable: true);
        await using var service = fixture.Management();
        using var cancel = new CancellationTokenSource();
        var reads = 0;
        fixture.OnAfterStoreCommit = () =>
        {
            if (++reads != 2) { return; }
            switch (change)
            {
                case "owner": fixture.Current = false; break;
                case "privacy": fixture.CanControl = false; break;
                case "control": fixture.ControlRevision++; break;
                case "generation": fixture.Boundary = fixture.Boundary! with { Generation = new(2) }; break;
                case "session": fixture.Boundary = fixture.Boundary! with { Session = new(Guid.NewGuid()) }; break;
                case "cache": service.ClearVolatile(); break;
                case "cancel": cancel.Cancel(); break;
            }
        };
        MemoryCommandResult? result = null;
        try
        {
            result = await service.ExecuteAsync(new(MemoryCommandOperation.Propose, fixture.Session.SessionId.Value,
                Candidate: Candidate), RequestOrigin.LocalUi, () => true, cancel.Token);
        }
        catch (Exception exception) when (exception is InvalidOperationException or OperationCanceledException) { }
        result?.Memories.Should().BeEmpty();
        fixture.Stored.Should().BeEmpty();
    }

    [Theory]
    [InlineData("null")]
    [InlineData("class")]
    [InlineData("blank")]
    [InlineData("control")]
    [InlineData("utf16")]
    [InlineData("utf8")]
    [InlineData("surrogate")]
    [InlineData("payload")]
    public async Task ProposalUsesAuthoritativeDomainValidationWithoutFallback(string invalid)
    {
        using var fixture = new Fixture(durable: true);
        await using var service = fixture.Management();
        var candidate = invalid switch
        {
            "null" => null,
            "class" => Candidate with { ContentClass = MemoryContentClass.Secret },
            "blank" => Candidate with { Value = " " },
            "control" => Candidate with { Value = "a\nb" },
            "utf16" => Candidate with { Value = new string('a', 513) },
            "utf8" => Candidate with { Value = new string('界', 342) },
            "surrogate" => Candidate with { Value = "\ud800" },
            _ => Candidate with { Value = new string('é', 512) },
        };
        var result = await Run(service, fixture, MemoryCommandOperation.Propose, candidate: candidate);
        result.Outcome.Should().Be("Denied");
        result.Message.Should().Be(MemoryPolicy.ValidateCandidate(candidate).ToString());
        result.Memories.Should().BeEmpty();
        fixture.Stored.Should().BeEmpty();
    }

    [Theory]
    [InlineData("audit")]
    [InlineData("storage")]
    public async Task ProposalFailureIsExplicitAndPublishesNoCandidate(string failure)
    {
        using var fixture = new Fixture(durable: true);
        await using var service = fixture.Management();
        fixture.FailTerminalAudit = failure is "audit";
        fixture.FailStoreCommit = failure is "storage";
        var act = () => Run(service, fixture, MemoryCommandOperation.Propose, candidate: Candidate);
        await act.Should().ThrowAsync<IOException>();
        fixture.FailTerminalAudit = false;
        fixture.FailStoreCommit = false;
        (await Run(service, fixture, MemoryCommandOperation.List)).Memories.Should().BeEmpty();
    }

    [Fact]
    public async Task VolatileProposalsAndTombstonesShareTheBoundedCapacityWithoutEviction()
    {
        using var fixture = new Fixture(durable: true);
        await using var service = fixture.Management();
        for (var index = 0; index < MemoryPolicy.MaximumEntries; index++)
        {
            var row = (await Run(service, fixture, MemoryCommandOperation.Propose, candidate: Candidate)).Memories.Single();
            if (index == 0) { await Run(service, fixture, MemoryCommandOperation.Forget, row); }
        }
        (await Run(service, fixture, MemoryCommandOperation.Propose, candidate: Candidate)).Outcome.Should().Be("CapacityExceeded");
        fixture.Stored.Should().BeEmpty();
        (await Run(service, fixture, MemoryCommandOperation.List)).Memories.Should().HaveCount(MemoryPolicy.MaximumEntries);
    }
}
