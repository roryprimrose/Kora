using AwesomeAssertions;

using Kora.Application.Dependencies;
using Kora.Core.Commands;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

using Microsoft.Extensions.Logging;

namespace Kora.Application.UnitTests.Dependencies;

public sealed partial class LocalContextTurnWorkflowTests
{
    [Theory]
    [InlineData("missing")]
    [InlineData("different-task")]
    [InlineData("not-current")]
    [InlineData("generation")]
    [InlineData("state")]
    [InlineData("request")]
    public async Task AdmissionRejectsMalformedDurableDispatchBeforeIssuingATurn(string defect)
    {
        await using var fixture = new Fixture(fresh: false);
        using var root = fixture.Root();
        HostTaskRecord? dispatchReceipt = null;
        fixture.Store.DispatchReceipt = dispatch =>
        {
            dispatchReceipt = defect switch
            {
                "state" => new(dispatch.Request, dispatch.Revision, HostTaskState.IntentRecorded),
                "request" => new(HostRequest.Create(dispatch.Request.Origin), dispatch.Revision, dispatch.State),
                _ => dispatch,
            };
            return dispatchReceipt;
        };
        fixture.Store.TaskReadback = observed =>
        {
            if (observed.Task.IsTerminal) { return observed; }
            return defect switch
            {
                "missing" => null,
                "different-task" => observed with
                {
                    Task = new(HostRequest.Create(fixture.Request.Origin), observed.Task.Revision, observed.Task.State),
                },
                "not-current" => observed with { CurrentSource = false },
                "generation" => observed with { Generation = new(2) },
                _ => observed with { Task = dispatchReceipt! },
            };
        };

        var admission = await fixture.Workflow.AdmitAsync(fixture.Target, () => fixture.Eligible, Token);

        admission.Turn.Should().BeNull();
        admission.Rejection!.Outcome.Should().Be(ModelTurnOutcome.Unknown);
        admission.Rejection.Reason.Should().Be(LocalContextTurnReason.EvidenceUnavailable);
        admission.Rejection.Answer.Should().BeNull();
        fixture.Model.Calls.Should().Be(0);
        fixture.Store.Tasks.Should().NotContain(task => task.State == HostTaskState.Succeeded);
        fixture.Audit.Events.Select(item => item.Outcome).Should()
            .Equal(Kora.Core.Auditing.SecurityAuditOutcome.Requested, Kora.Core.Auditing.SecurityAuditOutcome.Unknown);
    }

    [Theory]
    [InlineData("outcome")]
    [InlineData("reason")]
    [InlineData("missing-turn")]
    [InlineData("missing-terminal")]
    [InlineData("copied-terminal")]
    [InlineData("missing-provenance")]
    [InlineData("foreign-provenance")]
    [InlineData("missing-response")]
    [InlineData("missing-answer")]
    [InlineData("action")]
    [InlineData("grant")]
    [InlineData("question")]
    public async Task RunSuppressesInvalidOrUnownedModelResultsAndCommitsUnknown(string defect)
    {
        await using var fixture = new Fixture(fresh: false);
        using var root = fixture.Root();
        if (defect is "missing-turn")
        {
            fixture.Model.AdmissionResult = new(ModelTurnOutcome.Succeeded, ModelTurnReason.None,
                Response: new("unowned answer", null));
        }
        fixture.Model.TransformResult = (turn, result) => defect switch
        {
            "outcome" => result with { Outcome = (ModelTurnOutcome)int.MaxValue },
            "reason" => result with { Reason = (ModelTurnReason)int.MaxValue },
            "missing-provenance" => result with { Provenance = null },
            "foreign-provenance" => result with
            {
                Provenance = new(new(Guid.NewGuid()), turn.Provenance.Request, ModelProviderSelection.OllamaCandidate),
            },
            "missing-response" => result with { Response = null },
            "missing-answer" => result with { Response = new(null, null) },
            "action" => result with { Response = new("not answer-only", BuiltInAction.ShowVersion) },
            "grant" => result with
            {
                Response = new("not answer-only", null,
                    new(GrantChangeOperation.Add, BuiltInAction.ShowVersion, ModelApprovalScope.Session)),
            },
            "question" => result with { Response = new("not answer-only", null, Question: new("Choose", ["A", "B"])) },
            _ => result,
        };
        fixture.Model.AfterResult = (turn, result) =>
        {
            if (defect is "missing-terminal") { turn.TerminalResult = null; }
            if (defect is "copied-terminal") { turn.TerminalResult = result with { }; }
        };

        var receipt = await fixture.Run();

        receipt.Outcome.Should().Be(ModelTurnOutcome.Unknown);
        receipt.Reason.Should().Be(LocalContextTurnReason.ModelOutcomeUncertain);
        receipt.ModelResult!.Reason.Should().Be(ModelTurnReason.InvalidProviderResponse);
        receipt.ModelResult.Response.Should().BeNull();
        receipt.Answer.Should().BeNull();
        receipt.DurableState.Should().Be(HostTaskState.Unknown);
        fixture.Store.Tasks.Last().State.Should().Be(HostTaskState.Unknown);
        fixture.Model.Calls.Should().Be(defect is "missing-turn" ? 0 : 1);
        (await fixture.Run()).Reason.Should().Be(LocalContextTurnReason.TargetAlreadyUsed);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("stale")]
    [InlineData("read-error")]
    [InlineData("commit-error")]
    [InlineData("final-source-error")]
    public async Task RunNeverDisclosesAnAnswerWhenItsTerminalEvidenceCannotBeConfirmed(string defect)
    {
        await using var fixture = new Fixture(fresh: false);
        using var root = fixture.Root();
        HostTaskRecord? dispatch = null;
        var terminalCommitted = false;
        fixture.Store.BeforeCommit = record =>
        {
            if (record.State == HostTaskState.Succeeded) { terminalCommitted = true; }
        };
        fixture.Store.FailTerminal = defect is "commit-error";
        fixture.Store.TaskReadback = observed =>
        {
            if (!observed.Task.IsTerminal) { dispatch = observed.Task; return observed; }
            return defect switch
            {
                "missing" => null,
                "stale" => observed with { Task = dispatch! },
                "read-error" => throw new IOException("terminal read unavailable"),
                _ => observed,
            };
        };
        Task Validate(CancellationToken _)
        {
            if (defect is "final-source-error" && terminalCommitted)
            {
                throw new IOException("final source fence unavailable");
            }
            return Task.CompletedTask;
        }

        var receipt = await fixture.Run(validateSource: Validate);

        receipt.Outcome.Should().Be(ModelTurnOutcome.Unknown);
        receipt.Reason.Should().Be(LocalContextTurnReason.EvidenceUnavailable);
        receipt.Answer.Should().BeNull();
        receipt.DurableState.Should().BeNull();
        fixture.Model.Calls.Should().Be(1);
        (await fixture.Run()).Reason.Should().Be(LocalContextTurnReason.TargetAlreadyUsed);
        fixture.Model.Calls.Should().Be(1);
    }

    [Fact]
    public async Task QueuedCancellationClosesOnlyTheWaitingAdmissionWithoutDispatchOrReplay()
    {
        await using var fixture = new Fixture(fresh: false);
        using var root = fixture.Root();
        using var cancellation = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Model.Pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Model.BeforeRun = () => entered.TrySetResult();
        var first = fixture.Run();
        await entered.Task.WaitAsync(Token);
        var queuedRequest = new HostRequest(new(Guid.NewGuid()), fixture.Request.SessionId, new(Guid.NewGuid()), fixture.Request.Origin);
        Task<ModelContextAdmission> queued;
        using (var queuedRoot = HostActivity.BeginRoot(queuedRequest, HostActivityLayer.Application, HostOperation.Request))
        {
            queued = fixture.Workflow.AdmitAsync(LocalContextSessionTarget.Existing(queuedRequest, new(1)),
                static () => true, cancellation.Token);
        }
        ModelContextAdmission rejected;
        try
        {
            queued.IsCompleted.Should().BeFalse();
            await cancellation.CancelAsync();
            rejected = await queued.WaitAsync(Token);
            rejected.Turn.Should().BeNull();
            rejected.Rejection!.Outcome.Should().Be(ModelTurnOutcome.Cancelled);
            rejected.Rejection.DurableState.Should().BeNull();
            fixture.Store.Tasks.Should().NotContain(task => task.Request == queuedRequest);
            first.IsCompleted.Should().BeFalse();
        }
        finally { fixture.Model.Pending.TrySetResult(); }
        (await first).Outcome.Should().Be(ModelTurnOutcome.Succeeded);
        fixture.Model.Calls.Should().Be(1);
        fixture.Audit.Events.Where(item => string.Equals(item.TargetId, queuedRequest.TaskId.Value.ToString("D"), StringComparison.Ordinal))
            .Select(item => item.Outcome).Should()
            .Equal(Kora.Core.Auditing.SecurityAuditOutcome.Requested, Kora.Core.Auditing.SecurityAuditOutcome.Cancelled);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EnabledDiagnosticLoggerPreservesTruthfulFailureAndResultOutcomes(bool failTerminal)
    {
        var logger = new EnabledContextLogger();
        await using var fixture = new Fixture(fresh: false, logger: logger);
        using var root = fixture.Root();
        fixture.Store.FailTerminal = failTerminal;

        var receipt = await fixture.Run();

        receipt.Outcome.Should().Be(failTerminal ? ModelTurnOutcome.Unknown : ModelTurnOutcome.Succeeded);
        receipt.Answer.Should().Be(failTerminal ? null : "volatile answer");
        fixture.Audit.Events.Last().Outcome.Should().Be(failTerminal
            ? Kora.Core.Auditing.SecurityAuditOutcome.Unknown : Kora.Core.Auditing.SecurityAuditOutcome.Succeeded);
    }

    private sealed class EnabledContextLogger : ILogger<LocalContextTurnWorkflow>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => _ = formatter(state, exception);
    }
}
