using AwesomeAssertions;
using Kora.Core.Commands;
using Kora.Core.Hosting;
using Kora.Core.Memory;

namespace Kora.Application.UnitTests.Memory;

public sealed partial class MemoryAdmissionServiceTests
{
    [Theory]
    [InlineData("missing-boundary")]
    [InlineData("inspection")]
    [InlineData("inactive")]
    [InlineData("foreign-session")]
    [InlineData("generation")]
    [InlineData("no-transition")]
    [InlineData("denied-observation")]
    [InlineData("late-boundary")]
    [InlineData("late-cancel")]
    public async Task SessionObservationRejectsUnknownIneligibleAndUnconfirmedState(string scenario)
    {
        using var fixture = new Fixture(durable: true);
        using var root = fixture.Root();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        switch (scenario)
        {
            case "missing-boundary": fixture.Boundary = null; break;
            case "inspection": fixture.InspectEnabled = false; break;
            case "inactive": fixture.Session = fixture.Session with { IsActive = false }; break;
            case "foreign-session": fixture.Session = fixture.Session with { SessionId = new(Guid.NewGuid()) }; break;
            case "generation": fixture.Session = fixture.Session with { Generation = new(2) }; break;
            case "no-transition": fixture.SkipStoreTransition = true; break;
            case "denied-observation": fixture.StoreResult = new(MemoryOutcome.Denied, MemoryReason.AuthorityClosed); break;
            case "late-boundary": fixture.OnAfterStoreCommit = () => fixture.Boundary = fixture.Boundary! with { Revision = 2 }; break;
            case "late-cancel": fixture.OnAfterStoreCommit = cancellation.Cancel; break;
        }
        var observe = () => fixture.Service.ObserveSessionAsync(cancellation.Token);
        if (scenario is "late-cancel") { await observe.Should().ThrowAsync<OperationCanceledException>(); }
        else { await observe.Should().ThrowAsync<InvalidOperationException>(); }
    }

    [Fact]
    public async Task VolatileObservationIncludesOnlyExactEligibleSessionRecords()
    {
        using var fixture = new Fixture();
        using var root = fixture.Root();
        var sessionRecord = await fixture.Propose();
        fixture.Scope = MemoryScope.Project(fixture.Project);
        await fixture.Propose();
        (await fixture.Service.ObserveSessionAsync(Token)).Should().Equal(sessionRecord);
        fixture.Boundary = fixture.Boundary! with { Generation = new(2) };
        fixture.Session = fixture.Session with { Generation = new(2) };
        (await fixture.Service.ObserveSessionAsync(Token)).Should().BeEmpty();
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("missing-session")]
    [InlineData("admission")]
    [InlineData("inspection")]
    [InlineData("closed-input")]
    public async Task ManagementPreconditionsFailExplicitlyWithoutSuccessfulObservation(string scenario)
    {
        using var fixture = new Fixture(durable: true);
        await using var service = fixture.Management();
        var eligible = true;
        var command = new MemoryCommand(MemoryCommandOperation.List, fixture.Session.SessionId.Value);
        switch (scenario)
        {
            case "invalid": command = new(MemoryCommandOperation.Invalid, Error: "Invalid memory selector."); break;
            case "missing-session": command = new(MemoryCommandOperation.List); break;
            case "admission": eligible = false; break;
            case "inspection": fixture.InspectEnabled = false; break;
            case "closed-input": fixture.OnBoundaryRead = () => eligible = false; break;
        }
        var action = () => service.ExecuteAsync(command, RequestOrigin.LocalUi, () => eligible, Token);
        await action.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task ManagementHelpAndLiveVoiceOriginRemainOriginalUserOnly()
    {
        using var fixture = new Fixture(durable: true);
        await using var service = fixture.Management();
        var result = await service.ExecuteAsync(new(MemoryCommandOperation.Help), RequestOrigin.ActivatedVoice, () => true, Token);
        result.Message.Should().Be(MemoryCommand.Syntax);
        using var root = fixture.Root(RequestOrigin.ActivatedVoice);
        await Run(service, fixture, MemoryCommandOperation.List);
        fixture.ControlIntents.Single().Request.Origin.Should().Be(RequestOrigin.ActivatedVoice);
    }

    [Fact]
    public async Task LifecycleNotificationClearsCachedCandidateAndInspectionAndDisposalIsIdempotent()
    {
        using var fixture = new Fixture(durable: true);
        using (var root = fixture.Root()) { await fixture.Admitted(); }
        await using var service = fixture.Management();
        var row = (await Run(service, fixture, MemoryCommandOperation.List)).Memories.Single();
        row = (await Run(service, fixture, MemoryCommandOperation.Edit, row, Candidate)).Memories.Single();
        await Run(service, fixture, MemoryCommandOperation.Inspect, row);
        var ended = await fixture.ManagementSessions!.ChangeLifecycleAsync(fixture.Session.SessionId, new(1),
            false, RequestOrigin.LocalUi, Token);
        ended.IsActive.Should().BeFalse();
        await fixture.ManagementSessions.ChangeLifecycleAsync(fixture.Session.SessionId, ended.Generation,
            true, RequestOrigin.LocalUi, Token);
        fixture.Stored.Clear();
        (await Run(service, fixture, MemoryCommandOperation.List)).Memories.Should().BeEmpty();
        await service.DisposeAsync();
        await service.DisposeAsync();
    }
}
