using System.Diagnostics;
using System.Text;
using AwesomeAssertions;
using Kora.Application.Diagnostics;
using Kora.Application.Diagnostics.EvidenceAnswers;
using Kora.Core.Auditing;
using Kora.Core.Context.EvidenceAnswers;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.Diagnostics.EvidenceAnswers;

[Collection("Host tracing")]
public sealed class EvidenceAnswerWorkflowTests : IDisposable
{
    private readonly ActivityListener listener = new()
    {
        ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
        Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
    };
    public EvidenceAnswerWorkflowTests() => ActivitySource.AddActivityListener(listener);
    public void Dispose() => listener.Dispose();
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("ordinary private body", "What private record?", EvidenceAnswerOutcome.Succeeded)]
    [InlineData("api_key=example", "What private record?", EvidenceAnswerOutcome.Denied)]
    [InlineData("ordinary private body", "{private", EvidenceAnswerOutcome.Unavailable)]
    public async Task MaintainedRiskPolicyAdmitsOnlyCompleteLocalUseAndDetectedRiskCreatesNothing(
        string body, string question, EvidenceAnswerOutcome expected)
    {
        var fixture = new Fixture(Kora.Application.Context.LocalSourceUsePolicy.Instance, body, question);
        using var root = fixture.Root();
        var result = await fixture.Run();
        result.Outcome.Should().Be(expected);
        fixture.Calls.Should().Be(expected == EvidenceAnswerOutcome.Succeeded ? 1 : 0);
        fixture.Joins.Should().Be(fixture.Calls);
        if (expected != EvidenceAnswerOutcome.Succeeded) { result.Receipt.Should().BeNull(); }
        fixture.Logger.Messages.Should().NotContain(text => text.Contains(body, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(ModelTurnOutcome.Succeeded, false, EvidenceAnswerOutcome.Succeeded)]
    [InlineData(ModelTurnOutcome.Succeeded, true, EvidenceAnswerOutcome.Unsupported)]
    [InlineData(ModelTurnOutcome.Unavailable, false, EvidenceAnswerOutcome.Unavailable)]
    [InlineData(ModelTurnOutcome.Cancelled, false, EvidenceAnswerOutcome.Cancelled)]
    [InlineData(ModelTurnOutcome.Unknown, false, EvidenceAnswerOutcome.InvalidResponse)]
    [InlineData(ModelTurnOutcome.Denied, false, EvidenceAnswerOutcome.Denied)]
    [InlineData(ModelTurnOutcome.DeniedEgress, false, EvidenceAnswerOutcome.Denied)]
    public async Task OriginalOneShotConfirmationReturnsOnlyRealDurableReceipts(ModelTurnOutcome model,
        bool unsupported, EvidenceAnswerOutcome expected)
    {
        var fixture = new Fixture { Outcome = model, Unsupported = unsupported };
        using var root = fixture.Root();
        fixture.Calls.Should().Be(0);
        var result = await fixture.Run();
        result.Outcome.Should().Be(expected);
        result.Receipt.Should().NotBeNull();
        fixture.Calls.Should().Be(1);
        fixture.Joins.Should().Be(1);
        fixture.Events.Select(item => item.Outcome).Should().Equal(SecurityAuditOutcome.Requested,
            expected is EvidenceAnswerOutcome.Succeeded or EvidenceAnswerOutcome.Unsupported ? SecurityAuditOutcome.Succeeded
                : expected == EvidenceAnswerOutcome.Cancelled ? SecurityAuditOutcome.Cancelled : SecurityAuditOutcome.Failed);
        string.Join(' ', fixture.Events.Select(item => item.TargetId + item.ActionId)).Should().NotContain("private");
        (await fixture.Run()).Outcome.Should().Be(EvidenceAnswerOutcome.Denied);
        fixture.Calls.Should().Be(1);
    }

    [Theory]
    [InlineData(EvidenceSourceUseDecision.Unavailable, EvidenceAnswerOutcome.Unavailable)]
    [InlineData(EvidenceSourceUseDecision.Denied, EvidenceAnswerOutcome.Denied)]
    [InlineData((EvidenceSourceUseDecision)99, EvidenceAnswerOutcome.Denied)]
    public async Task MissingDeniedOrUnknownSourceUseNeverCreatesConversation(EvidenceSourceUseDecision decision,
        EvidenceAnswerOutcome expected)
    {
        var fixture = new Fixture { Decision = decision };
        using var root = fixture.Root();
        (await fixture.Run()).Outcome.Should().Be(expected);
        fixture.Calls.Should().Be(0);
        fixture.Joins.Should().Be(0);
    }

    [Theory]
    [InlineData("no-input")]
    [InlineData("foreign")]
    [InlineData("stopped")]
    [InlineData("completed")]
    public async Task OnlyExactLiveOriginalInputMayConfirm(string defect)
    {
        var fixture = new Fixture();
        using var root = string.Equals(defect, "no-input", StringComparison.Ordinal) ? null
            : HostActivity.BeginRoot(string.Equals(defect, "foreign", StringComparison.Ordinal)
                ? HostRequest.Create(RequestOrigin.LocalUi) : fixture.Review.Original,
                HostActivityLayer.Application, HostOperation.Request);
        if (string.Equals(defect, "stopped", StringComparison.Ordinal)) { root!.Activity!.Stop(); }
        if (string.Equals(defect, "completed", StringComparison.Ordinal)) { root!.Complete(HostOperationOutcome.Completed); }
        (await fixture.Run()).Outcome.Should().Be(EvidenceAnswerOutcome.Denied);
        fixture.Calls.Should().Be(0);
        fixture.Events.Should().BeEmpty();
    }

    [Theory]
    [InlineData("control")]
    [InlineData("inspect")]
    [InlineData("native")]
    [InlineData("deadline")]
    [InlineData("revision")]
    [InlineData("policy")]
    [InlineData("cancel")]
    [InlineData("fault")]
    [InlineData("receipt-session")]
    [InlineData("receipt-body")]
    [InlineData("receipt-task")]
    [InlineData("receipt-terminal")]
    [InlineData("receipt-state")]
    public async Task RevocationAndUnconfirmedCompletionCannotPublishAnAnswer(string defect)
    {
        var fixture = new Fixture { Defect = defect };
        using var root = fixture.Root();
        using var cancellation = new CancellationTokenSource();
        fixture.BeforeReturn = () =>
        {
            switch (defect)
            {
                case "control": fixture.CanControl = false; break;
                case "inspect": fixture.CanInspect = false; break;
                case "native": fixture.Native = false; break;
                case "deadline": fixture.Time.Now = fixture.Review.ExpiresAt; break;
                case "revision": fixture.ControlRevision++; break;
                case "policy": fixture.Decision = EvidenceSourceUseDecision.Denied; break;
                case "cancel": cancellation.Cancel(); break;
                case "fault": throw new IOException("Private payload must not be logged");
            }
        };
        var result = await fixture.Run(cancellation.Token);
        result.Outcome.Should().Be(defect is "fault" or "receipt-session" or "receipt-body" or "receipt-task"
            or "receipt-terminal" or "receipt-state" ? EvidenceAnswerOutcome.Unavailable
            : defect is "cancel" ? EvidenceAnswerOutcome.Cancelled : EvidenceAnswerOutcome.Denied);
        result.Receipt.Should().BeNull();
        fixture.Joins.Should().Be(1);
        fixture.Logger.Messages.Should().NotContain(message => message.Contains("Private payload", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationWaitsForIgnoringDependencyAndPhysicalJoin(bool duringPolicy)
    {
        var fixture = new Fixture();
        using var root = fixture.Root();
        using var cancellation = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var joined = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Hold = async () => { entered.SetResult(); await release.Task; };
        fixture.HoldPolicy = duringPolicy;
        if (!duringPolicy) { fixture.Join = () => joined.Task; }
        var work = fixture.Run(cancellation.Token);
        await entered.Task.WaitAsync(Token);
        cancellation.Cancel();
        work.IsCompleted.Should().BeFalse();
        release.SetResult();
        if (!duringPolicy)
        {
            await fixture.JoinEntered.Task.WaitAsync(Token);
            work.IsCompleted.Should().BeFalse();
            joined.SetResult();
        }
        (await work).Outcome.Should().Be(EvidenceAnswerOutcome.Cancelled);
        fixture.Calls.Should().Be(duringPolicy ? 0 : 1);
    }

    [Fact]
    public async Task DefaultPolicyIsExplicitlyUnavailableAndObservesCancellation()
    {
        var fixture = new Fixture();
        var policy = new UnavailableEvidenceAnswerSourceUsePolicy();
        (await policy.AdmitAsync(fixture.Review, Token)).Should().Be(EvidenceSourceUseDecision.Unavailable);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var act = () => policy.AdmitAsync(fixture.Review, cancellation.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task PolicyRevocationAcrossItsOwnAwaitIsCheckedBeforeConversationCreation()
    {
        var fixture = new Fixture();
        fixture.DuringPolicy = () => fixture.Native = false;
        using var root = fixture.Root();
        (await fixture.Run()).Outcome.Should().Be(EvidenceAnswerOutcome.Denied);
        fixture.Calls.Should().Be(0);
    }

    [Fact]
    public async Task UnexpectedDependencyFailuresRemainExplicitAndStillJoin()
    {
        var fixture = new Fixture { BeforeReturn = () => throw new FormatException("Invalid dependency protocol") };
        using var root = fixture.Root();
        var act = () => fixture.Run();
        await act.Should().ThrowAsync<FormatException>();
        fixture.Joins.Should().Be(1);
        fixture.Events.Last().Outcome.Should().Be(SecurityAuditOutcome.Failed);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AuditFailureNeverReturnsSuccess(bool requested)
    {
        var fixture = new Fixture { FailAudit = requested ? SecurityAuditOutcome.Requested : SecurityAuditOutcome.Succeeded };
        using var root = fixture.Root();
        if (requested)
        {
            (await fixture.Run()).Outcome.Should().Be(EvidenceAnswerOutcome.Unavailable);
            fixture.Calls.Should().Be(0);
        }
        else
        {
            var act = () => fixture.Run();
            await act.Should().ThrowAsync<IOException>();
            fixture.Joins.Should().Be(1);
        }
    }

    private sealed class Clock : TimeProvider
    {
        internal DateTimeOffset Now { get; set; } = DateTimeOffset.UnixEpoch;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class Fixture : IEvidenceAnswerSourceUsePolicy, IEvidenceAnswerConversation,
        ISessionWorkspaceAccess, ISecurityAuditLog
    {
        internal Fixture(IEvidenceAnswerSourceUsePolicy? policy = null, string body = "private diagnostic body",
            string question = "What private record?")
        {
            var record = new EvidenceRecord(new(EvidenceSource.Log, new(Guid.NewGuid())),
                Time.Now, Time.Now.AddHours(1), EvidenceSegmentStatus.Present, null, null, null, null,
                "Information", "Example", 1, body, new Dictionary<string, EvidenceValue>(StringComparer.Ordinal),
                null, null, []);
            Review = new(Encoding.UTF8.GetString(DurableEvidenceQuery.Serialize(
                new(EvidencePageStatus.Available, [record], null, [], EvidencePage.StorageDisclosure))),
                question, HostRequest.Create(RequestOrigin.LocalUi), Time.Now);
            Workflow = new(policy ?? this, this, this, this, Time, Logger);
        }
        internal Clock Time { get; } = new();
        internal RecordingLogger Logger { get; } = new();
        internal EvidenceAnswerReview Review { get; }
        internal EvidenceAnswerWorkflow Workflow { get; }
        public bool CanInspect { get; set; } = true;
        public bool CanControl { get; set; } = true;
        public long ControlRevision { get; set; }
        internal bool Native { get; set; } = true;
        internal EvidenceSourceUseDecision Decision { get; set; } = EvidenceSourceUseDecision.Allowed;
        internal ModelTurnOutcome Outcome { get; set; } = ModelTurnOutcome.Succeeded;
        internal bool Unsupported { get; set; }
        internal string? Defect { get; set; }
        internal int Calls { get; private set; }
        internal int Joins { get; private set; }
        internal List<SecurityAuditEvent> Events { get; } = [];
        internal SecurityAuditOutcome? FailAudit { get; set; }
        internal Action? BeforeReturn { get; set; }
        internal Action? DuringPolicy { get; set; }
        internal Func<Task>? Hold { get; set; }
        internal bool HoldPolicy { get; set; }
        internal Func<Task>? Join { get; set; }
        internal TaskCompletionSource JoinEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal HostActivity Root() => HostActivity.BeginRoot(Review.Original, HostActivityLayer.Application, HostOperation.Request);
        internal Task<EvidenceAnswerResult> Run(CancellationToken? token = null) =>
            Workflow.AnswerAsync(Review, () => Native, token ?? Token);
        public async Task<EvidenceSourceUseDecision> AdmitAsync(EvidenceAnswerReview review, CancellationToken token)
        {
            review.Should().BeSameAs(Review);
            if (HoldPolicy && Hold is not null) { await Hold(); }
            DuringPolicy?.Invoke();
            return Decision;
        }
        public void Write(SecurityAuditEvent auditEvent)
        {
            if (auditEvent.Outcome == FailAudit) { throw new IOException("Audit unavailable"); }
            Events.Add(auditEvent);
        }
        public async Task<ConversationSubmissionReceipt> SubmitAsync(EvidenceAnswerReview review, Func<bool> current,
            Func<CancellationToken, Task> validate, CancellationToken token)
        {
            Calls++;
            current().Should().BeTrue();
            await validate(token);
            if (!HoldPolicy && Hold is not null) { await Hold(); }
            BeforeReturn?.Invoke();
            var request = new HostRequest(new(Guid.NewGuid()), Review.Destination.SessionId, new(Guid.NewGuid()), RequestOrigin.LocalUi);
            if (Defect is "receipt-session") { request = HostRequest.Create(RequestOrigin.LocalUi); }
            var provenance = new ModelTurnProvenance(new(Guid.NewGuid()), request, ModelProviderSelection.OllamaCandidate);
            var body = Unsupported ? EvidenceAnswerResponse.Unsupported : "Validated answer.";
            var executed = Outcome is ModelTurnOutcome.Succeeded or ModelTurnOutcome.Unknown;
            var message = new ConversationMessage(request, new(1), 1, executed ? 3 : 2, Time.Now,
                Defect is "receipt-body" ? "Other question" : review.Question, Outcome, ModelTurnReason.None)
            {
                Version = executed ? 2 : 1,
                Execution = executed ? provenance : null,
                ExecutionMetadataRevision = executed ? 0 : null,
                AssistantResponse = Outcome == ModelTurnOutcome.Succeeded ? new(provenance, body) : null,
            };
            var task = new HostTaskRecord(Defect is "receipt-task" ? HostRequest.Create(RequestOrigin.LocalUi) : request,
                new(3), Defect is "receipt-terminal" ? HostTaskState.IntentRecorded
                    : Defect is "receipt-state" ? HostTaskState.Denied : message.ExpectedTaskState);
            return new(message, task);
        }
        public async Task JoinAsync()
        {
            Joins++;
            JoinEntered.TrySetResult();
            if (Join is not null) { await Join(); }
        }

    }

    private sealed class RecordingLogger : ILogger<EvidenceAnswerWorkflow>
    {
        internal List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));
    }
}
