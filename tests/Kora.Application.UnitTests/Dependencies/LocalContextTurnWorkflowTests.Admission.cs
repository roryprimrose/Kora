using AwesomeAssertions;

using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Application.UnitTests.Dependencies;

public sealed partial class LocalContextTurnWorkflowTests
{
    [Theory]
    [InlineData(RequestOrigin.LocalUi)]
    [InlineData(RequestOrigin.ActivatedVoice)]
    public async Task CoreAdmissionEstablishesDurableAuthorityBeforeAnyEnvelopeIsBound(RequestOrigin origin)
    {
        await using var fixture = new Fixture(origin);
        using var root = fixture.Root();
        IModelContextRunner runner = fixture.Workflow;
        var issued = await runner.AdmitAsync(fixture.Target, () => fixture.Eligible, Token);
        issued.Rejection.Should().BeNull();
        await using var turn = issued.Turn ?? throw new InvalidOperationException("Missing durable admission.");
        turn.Request.Should().BeSameAs(fixture.Request);
        turn.SessionGeneration.Should().Be(fixture.Store.Authority!.Generation);
        fixture.Store.Tasks.Last().Request.Should().BeSameAs(turn.Request);
        fixture.Store.Tasks.Last().State.Should().Be(HostTaskState.DispatchRecorded);
        fixture.Model.Calls.Should().Be(0);
        turn.Completion.IsCompleted.Should().BeFalse();
        var context = new ModelContextEnvelope(turn.Request, "untrusted", "explain",
            [new(new(Guid.NewGuid()), turn.Request, new(1), "volatile-source", ModelEvidenceDisclosure.LocalOnly)],
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddSeconds(15), allowTools: false);
        var receipt = await turn.RunAsync(context, Token);
        receipt.Outcome.Should().Be(ModelTurnOutcome.Succeeded);
        receipt.DurableState.Should().Be(HostTaskState.Succeeded);
        (await turn.RunAsync(context, Token)).Reason.Should().Be(LocalContextTurnReason.TargetAlreadyUsed);
        fixture.Model.Calls.Should().Be(1);
    }

    [Theory]
    [InlineData("lease")]
    [InlineData("caller")]
    [InlineData("workflow")]
    public async Task UnsubmittedAdmissionCancellationClosesItsRealTaskWithoutBindingOrModelWork(string cancel)
    {
        await using var fixture = new Fixture();
        using var root = fixture.Root();
        using var cancellation = new CancellationTokenSource();
        IModelContextRunner runner = fixture.Workflow;
        var issued = await runner.AdmitAsync(fixture.Target, () => fixture.Eligible, cancellation.Token);
        await using var turn = issued.Turn ?? throw new InvalidOperationException("Missing durable admission.");
        if (cancel is "lease") { await turn.DisposeAsync(); }
        if (cancel is "caller") { await cancellation.CancelAsync(); }
        if (cancel is "workflow") { await fixture.Workflow.DisposeAsync(); }
        var terminal = await turn.Completion;
        terminal.Outcome.Should().Be(ModelTurnOutcome.Cancelled);
        terminal.DurableState.Should().Be(HostTaskState.Unknown);
        terminal.Answer.Should().BeNull();
        fixture.Store.Tasks.Last().IsTerminal.Should().BeTrue();
        fixture.Model.Calls.Should().Be(0);
        fixture.Audit.Events.Should().HaveCount(2);
    }

    [Theory]
    [InlineData("source")]
    [InlineData("privacy")]
    [InlineData("generation")]
    [InlineData("run-cancel")]
    public async Task PreparedTurnRevalidatesItsCapturedAuthorityBeforeModelAdmission(string change)
    {
        await using var fixture = new Fixture();
        using var root = fixture.Root();
        var issued = await fixture.Workflow.AdmitAsync(fixture.Target, () => fixture.Eligible, Token);
        await using var turn = issued.Turn ?? throw new InvalidOperationException("Missing durable admission.");
        using var cancellation = new CancellationTokenSource();
        if (change is "source") { fixture.Eligible = false; }
        if (change is "privacy") { fixture.Access.ControlRevision++; }
        if (change is "generation") { fixture.Store.Authority = fixture.Store.Authority! with { Generation = new(2) }; }
        if (change is "run-cancel") { await cancellation.CancelAsync(); }
        var receipt = await turn.RunAsync(fixture.Context(), cancellation.Token);
        receipt.Outcome.Should().Be(change is "run-cancel" ? ModelTurnOutcome.Cancelled : ModelTurnOutcome.Denied);
        receipt.Answer.Should().BeNull();
        fixture.Store.Tasks.Last().IsTerminal.Should().BeTrue();
        fixture.Model.Calls.Should().Be(0);
    }

    [Fact]
    public async Task PreparedTurnCannotBeInvokedFromAnotherSessionsAmbientAuthority()
    {
        await using var fixture = new Fixture();
        using var root = fixture.Root();
        var issued = await fixture.Workflow.AdmitAsync(fixture.Target, () => fixture.Eligible, Token);
        await using var turn = issued.Turn ?? throw new InvalidOperationException("Missing durable admission.");
        using var foreign = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi),
            HostActivityLayer.Application, HostOperation.Request);
        var receipt = await turn.RunAsync(fixture.Context(), Token);
        receipt.Reason.Should().Be(LocalContextTurnReason.OriginalInputRequired);
        receipt.DurableState.Should().Be(HostTaskState.Denied);
        fixture.Model.Calls.Should().Be(0);
    }

    [Fact]
    public async Task SubmittedRunCancellationAtTerminalCommitStillSuppressesTheAnswer()
    {
        await using var fixture = new Fixture();
        using var root = fixture.Root();
        var issued = await fixture.Workflow.AdmitAsync(fixture.Target, () => fixture.Eligible, Token);
        await using var turn = issued.Turn ?? throw new InvalidOperationException("Missing durable admission.");
        using var cancellation = new CancellationTokenSource();
        fixture.Store.BeforeCommit = record =>
        {
            if (record.Request == fixture.Request && record.State == HostTaskState.Succeeded) { cancellation.Cancel(); }
        };
        var receipt = await turn.RunAsync(fixture.Context(), cancellation.Token);
        receipt.Outcome.Should().Be(ModelTurnOutcome.Cancelled);
        receipt.DurableState.Should().Be(HostTaskState.Succeeded);
        receipt.Answer.Should().BeNull();
    }
}