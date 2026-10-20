using AwesomeAssertions;
using Kora.Application.Hosting;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.Dependencies;

public sealed partial class LocalContextTurnWorkflowTests
{
    [Theory]
    [InlineData("stopped")]
    [InlineData("completed")]
    public async Task AdmissionRejectsRetiredOriginalScopeBeforeDurableWork(string change)
    {
        await using var f = new Fixture();
        using var root = f.Root();
        if (change is "stopped") { root.Activity!.Stop(); }
        else { root.Complete(HostOperationOutcome.Completed); }
        (await f.Workflow.AdmitAsync(f.Target, () => true, Token)).Rejection!.Reason
            .Should().Be(LocalContextTurnReason.OriginalInputRequired);
        f.Store.Tasks.Should().BeEmpty();
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("task")]
    [InlineData("source")]
    [InlineData("generation")]
    [InlineData("state")]
    [InlineData("request")]
    public async Task AdmissionRequiresExactDispatchReadbackWithoutIssuingALease(string defect)
    {
        await using var f = new Fixture(fresh: false);
        using var root = f.Root();
        f.Log.Enabled = true;
        f.Store.TaskObservation = observation => defect switch
        {
            "missing" => null,
            "task" => observation! with { Task = observation.Task.Next(HostTaskState.Unknown) },
            "source" => observation! with { CurrentSource = false },
            "generation" => observation! with { Generation = new(2) },
            _ => observation,
        };
        f.Store.DispatchResult = record => defect switch
        {
            "state" => new(record.Request, record.Revision, HostTaskState.IntentRecorded),
            "request" => new(HostRequest.Create(RequestOrigin.LocalUi), record.Revision, record.State),
            _ => record,
        };
        // Keep readback equal to the deliberately malformed returned receipt to reach its state/request checks.
        if (defect is "state" or "request")
        {
            f.Store.TaskObservation = observation => observation! with
            { Task = f.Store.DispatchResult(observation.Task) };
        }
        var result = await f.Workflow.AdmitAsync(f.Target, () => true, Token);
        result.Turn.Should().BeNull();
        result.Rejection!.Outcome.Should().Be(ModelTurnOutcome.Unknown);
        f.Model.Calls.Should().Be(0);
        f.Log.Messages.Should().NotBeEmpty().And.NotContain(message => message.Contains("never-persist-source-secret", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("unknown-outcome")]
    [InlineData("unknown-reason")]
    [InlineData("terminal")]
    [InlineData("provenance")]
    [InlineData("response")]
    [InlineData("action")]
    [InlineData("grant")]
    [InlineData("question")]
    public async Task SuccessfulModelPayloadRequiresExactTerminalIdentityAndAnswerOnlyShape(string defect)
    {
        await using var f = new Fixture();
        using var root = f.Root();
        f.Log.Enabled = true;
        f.Model.OmitTerminal = defect is "terminal";
        f.Model.TransformResult = result => defect switch
        {
            "unknown-outcome" => result with { Outcome = (ModelTurnOutcome)999 },
            "unknown-reason" => result with { Reason = (ModelTurnReason)999 },
            "provenance" => result with { Provenance = result.Provenance! with { Turn = new(Guid.NewGuid()) } },
            "response" => result with { Response = null },
            "action" => result with { Response = new("x", Kora.Core.Commands.BuiltInAction.LockMachine) },
            "grant" => result with { Response = new("x", null, new(Kora.Core.Commands.GrantChangeOperation.Add,
                Kora.Core.Commands.BuiltInAction.LockMachine, Kora.Core.Commands.ModelApprovalScope.Once)) },
            "question" => result with { Response = new("x", null, Question: new("choose", ["one", "two"])) },
            _ => result,
        };
        var receipt = await f.Run();
        receipt.Outcome.Should().Be(ModelTurnOutcome.Unknown);
        receipt.Answer.Should().BeNull();
        receipt.DurableState.Should().Be(HostTaskState.Unknown);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("foreign")]
    public async Task TerminalReadbackUncertaintySuppressesAnswerAndHoldsNewAdmission(string defect)
    {
        await using var f = new Fixture();
        using var root = f.Root();
        f.Store.BeforeCommit = record =>
        {
            if (record.State == HostTaskState.Succeeded && record.Request == f.Request)
            {
                f.Store.TaskObservation = observation => defect is "missing" ? null
                    : observation! with { Task = observation.Task.Next(HostTaskState.Unknown) };
            }
        };
        var receipt = await f.Run();
        receipt.Outcome.Should().Be(ModelTurnOutcome.Unknown);
        receipt.Reason.Should().Be(LocalContextTurnReason.EvidenceUnavailable);
        receipt.Answer.Should().BeNull();
    }

    [Fact]
    public async Task SourceValidationFailureAfterTerminalCommitSuppressesAnswer()
    {
        await using var f = new Fixture();
        using var root = f.Root();
        var terminal = false;
        f.Store.BeforeCommit = record => terminal |= record.Request == f.Request && record.State == HostTaskState.Succeeded;
        var result = await f.Run(validateSource: _ => terminal ? Task.FromException(new IOException("private-source-failure"))
            : Task.CompletedTask);
        result.Outcome.Should().Be(ModelTurnOutcome.Unknown);
        result.Answer.Should().BeNull();
    }

    [Fact]
    public async Task SourceGateIsCheckedAgainAfterItsAsyncValidator()
    {
        await using var f = new Fixture();
        using var root = f.Root();
        var result = await f.Run(validateSource: _ =>
        {
            f.Eligible = false;
            return Task.CompletedTask;
        });
        result.Reason.Should().Be(LocalContextTurnReason.SourceNotCurrent);
        f.Model.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData("cancel-create")]
    [InlineData("cancel-intent")]
    [InlineData("io-intent")]
    [InlineData("denied-create")]
    public async Task FreshSessionFailureHasTruthfulNonReplayableCreationOutcome(string failure)
    {
        await using var f = new Fixture();
        using var root = f.Root();
        using var cancellation = new CancellationTokenSource();
        f.Store.AsyncTerminalCommit = true;
        if (failure is "cancel-create")
        {
            f.Store.BeforeCreation = cancellation.Cancel;
            f.Store.CreationFailure = new OperationCanceledException(cancellation.Token);
        }
        if (failure is "cancel-intent") { f.Store.IntentFailure = new OperationCanceledException(); }
        if (failure is "io-intent") { f.Store.IntentFailure = new IOException("intent-unavailable"); }
        if (failure is "denied-create") { f.Store.CreationFailure = new InvalidOperationException("denied"); }
        var result = await f.Run(cancellation.Token);
        result.Answer.Should().BeNull();
        f.Model.Calls.Should().Be(0);
    }

    [Fact]
    public async Task DisposedWorkflowRejectsWithoutPublishingAnUnownedLease()
    {
        await using var f = new Fixture();
        using var root = f.Root();
        f.Log.Enabled = true;
        await f.Workflow.DisposeAsync();
        var result = await f.Workflow.AdmitAsync(f.Target, () => true, Token);
        result.Rejection!.Reason.Should().Be(LocalContextTurnReason.EvidenceUnavailable);
        result.Turn.Should().BeNull();
        f.Model.Calls.Should().Be(0);
    }

    [Fact]
    public async Task QueuedAdmissionCancellationReturnsWithoutWaitingForUnrelatedModelWork()
    {
        await using var f = new Fixture();
        using var root = f.Root();
        f.Model.Pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = f.Run();
        using var cancellation = new CancellationTokenSource();
        var secondRequest = new HostRequest(new(Guid.NewGuid()), f.Request.SessionId, new(Guid.NewGuid()), RequestOrigin.LocalUi);
        async Task<ModelContextAdmission> Second()
        {
            using var secondRoot = HostActivity.BeginRoot(secondRequest, HostActivityLayer.Application, HostOperation.Request);
            return await f.Workflow.AdmitAsync(LocalContextSessionTarget.Existing(secondRequest, new(1)), () => true, cancellation.Token);
        }
        var second = Second();
        try
        {
            second.IsCompleted.Should().BeFalse();
            await cancellation.CancelAsync();
            var result = await second;
            result.Rejection!.Outcome.Should().Be(ModelTurnOutcome.Cancelled);
            result.Turn.Should().BeNull();
            first.IsCompleted.Should().BeFalse();
        }
        finally { f.Model.Pending.TrySetResult(); }
        (await first).Outcome.Should().Be(ModelTurnOutcome.Succeeded);
    }

    [Theory]
    [InlineData("completed")]
    [InlineData("stopped")]
    [InlineData("foreign")]
    public async Task LocalSessionCreationRequiresItsExactLiveOriginalRequest(string defect)
    {
        var store = new Store();
        var sessions = new SessionWorkspaceService(store, new(store), new Access(), NullLogger<SessionWorkspaceService>.Instance);
        var request = HostRequest.Create(RequestOrigin.LocalUi);
        using var root = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request);
        if (defect is "completed") { root.Complete(HostOperationOutcome.Completed); }
        if (defect is "stopped") { root.Activity!.Stop(); }
        var call = () => sessions.CreateLocalContextSessionAsync(defect is "foreign" ? HostRequest.Create(RequestOrigin.LocalUi) : request,
            () => true, Token);
        await call.Should().ThrowAsync<InvalidOperationException>();
    }

    [Theory]
    [InlineData("stopped")]
    [InlineData("completed")]
    [InlineData("foreign")]
    [InlineData("system")]
    [InlineData("cancel")]
    [InlineData("failure")]
    public async Task ExactControlAdmissionRejectsInvalidAmbientAuthorityAndPropagatesOperationFailure(string defect)
    {
        var store = new Store();
        await using var admission = new ExactControlProbe(store);
        var request = HostRequest.Create(defect is "system" ? RequestOrigin.HostSystem : RequestOrigin.LocalUi);
        using var root = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request);
        if (defect is "stopped") { root.Activity!.Stop(); }
        if (defect is "completed") { root.Complete(HostOperationOutcome.Completed); }
        var run = () => admission.Run(defect is "foreign" ? HostRequest.Create(RequestOrigin.LocalUi) : request, defect);
        if (defect is "cancel") { await run.Should().ThrowAsync<OperationCanceledException>(); }
        else if (defect is "failure") { await run.Should().ThrowAsync<IOException>(); }
        else { await run.Should().ThrowAsync<InvalidOperationException>(); }
    }

    private sealed class ExactControlProbe(Store store) : HostControlAdmission(store, new(store))
    {
        protected override ValueTask<Kora.Core.Authorization.WorkSessionAuthorization> CreateSessionAsync(
            HostRequest request, Func<bool> eligible, CancellationToken token) => throw new NotSupportedException();
        internal Task<int> Run(HostRequest request, string defect) => RunExactAsync(request, () => true,
            (_, _) => defect is "cancel" ? Task.FromException<int>(new OperationCanceledException())
                : Task.FromException<int>(new IOException()), _ => HostOperationOutcome.Completed, Token);
    }
}
