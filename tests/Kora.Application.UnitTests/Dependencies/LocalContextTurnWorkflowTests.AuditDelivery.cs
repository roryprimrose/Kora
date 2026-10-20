using AwesomeAssertions;
using Kora.Core.Auditing;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;

namespace Kora.Application.UnitTests.Dependencies;

public sealed partial class LocalContextTurnWorkflowTests
{
    [Theory]
    [InlineData("source")]
    [InlineData("cancel")]
    [InlineData("generation")]
    [InlineData("inactive")]
    [InlineData("foreign")]
    [InlineData("control")]
    public async Task TerminalAuditDeliveryCannotExtendSourceOrSessionDisclosureAuthority(string changed)
    {
        await using var fixture = new Fixture();
        using var root = fixture.Root();
        using var cancellation = new CancellationTokenSource();
        fixture.Audit.OnWrite = audit =>
        {
            if (audit.Outcome != SecurityAuditOutcome.Succeeded) { return; }
            if (changed is "source") { fixture.Eligible = false; }
            if (changed is "cancel") { cancellation.Cancel(); }
            if (changed is "generation") { fixture.Store.Authority = fixture.Store.Authority! with { Generation = new(2) }; }
            if (changed is "inactive") { fixture.Store.Authority = fixture.Store.Authority! with { IsActive = false }; }
            if (changed is "foreign") { fixture.Store.Authority = fixture.Store.Authority! with { SessionId = new(Guid.NewGuid()) }; }
            if (changed is "control") { fixture.Access.ControlRevision++; }
        };
        var receipt = await fixture.Run(cancellation.Token);
        receipt.Outcome.Should().Be(changed is "cancel" ? ModelTurnOutcome.Cancelled : ModelTurnOutcome.Denied);
        receipt.Answer.Should().BeNull();
        receipt.DurableState.Should().Be(HostTaskState.Succeeded);
        fixture.Audit.Events.Last().Outcome.Should().Be(changed is "cancel"
            ? SecurityAuditOutcome.Cancelled : SecurityAuditOutcome.Denied);
    }

    [Fact]
    public async Task SeparateRunTokenStillClosesDisclosureAfterItsExecutionCancellationScopeHasEnded()
    {
        await using var fixture = new Fixture();
        using var root = fixture.Root();
        var issued = await fixture.Workflow.AdmitAsync(fixture.Target, () => fixture.Eligible, Token);
        await using var turn = issued.Turn!;
        using var runCancellation = new CancellationTokenSource();
        fixture.Audit.OnWrite = audit =>
        {
            if (audit.Outcome == SecurityAuditOutcome.Succeeded) { runCancellation.Cancel(); }
        };
        var receipt = await turn.RunAsync(fixture.Context(), runCancellation.Token);
        receipt.Outcome.Should().Be(ModelTurnOutcome.Cancelled);
        receipt.Answer.Should().BeNull();
        receipt.DurableState.Should().Be(HostTaskState.Succeeded);
        fixture.Audit.Events.Last().Outcome.Should().Be(SecurityAuditOutcome.Cancelled);
    }
}
