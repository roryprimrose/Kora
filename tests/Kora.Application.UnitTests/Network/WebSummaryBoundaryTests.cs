using System.Diagnostics;
using AwesomeAssertions;
using Kora.Application.Network;
using Kora.Core.Auditing;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Network;
using Kora.Core.Presentation;
using Microsoft.Extensions.Logging;

namespace Kora.Application.UnitTests.Network;

[Collection("Host tracing")]
public sealed class WebSummaryBoundaryTests : IDisposable
{
    private readonly ActivityListener listener = new()
    {
        ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
        Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
    };

    public WebSummaryBoundaryTests() => ActivitySource.AddActivityListener(listener);
    public void Dispose() => listener.Dispose();

    [Theory]
    [InlineData("Nova")]
    [InlineData("NovaSummary")]
    [InlineData("Nova summarize current web result")]
    [InlineData("Nova, cancel local web summary")]
    public void ParserKeepsExactNameAndCancelBoundaries(string command)
    {
        var parsed = WebSummaryCommand.Parse(command, "Nova");
        if (command is "Nova" or "NovaSummary") { parsed.Should().BeNull(); }
        else if (command.Contains("cancel", StringComparison.Ordinal)) { parsed!.Cancel.Should().BeTrue(); }
        else { parsed.Should().NotBeNull(); }
    }

    [Theory]
    [InlineData("stopped")]
    [InlineData("completed")]
    [InlineData("system")]
    public void ReviewRequiresLiveOriginalUserActivity(string closure)
    {
        using var root = Root(closure is "system" ? RequestOrigin.HostSystem : RequestOrigin.LocalUi);
        if (closure is "stopped") { root.Activity!.Stop(); Activity.Current = root.Activity; }
        if (closure is "completed") { root.Complete(HostOperationOutcome.Completed); }
        var create = () => Workflow(new Execution()).Prepare(Snapshot(), static () => true);
        create.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ReviewRejectsClosedExactSource()
    {
        using var root = Root();
        var prepare = () => Workflow(new Execution()).Prepare(Snapshot(), static () => false);
        prepare.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public async Task StoppedActivityInheritedFromAnotherAsyncContextCannotAuthorizeReview()
    {
        using var root = Root();
        await Task.Run(() => root.Activity!.Stop(), Token);
        var prepare = () => Workflow(new Execution()).Prepare(Snapshot(), static () => true);
        prepare.Should().Throw<InvalidOperationException>().WithMessage("Local web summarization requires live original-user input.");
    }

    [Fact]
    public async Task CancelledConfirmationProducesNoExecutionAndTypedAudit()
    {
        using var root = Root();
        var execution = new Execution();
        var workflow = Workflow(execution);
        var review = workflow.Prepare(Snapshot(), static () => true);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var result = await workflow.Confirm(review.Id, static () => true, SecurityAuditInitiator.LocalUser, cancellation.Token);
        result.Outcome.Should().Be(ModelTurnOutcome.Cancelled);
        execution.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FaultsAreLoggedWithoutSourceContentAndRetainTerminalAudit(bool cancelled)
    {
        using var root = Root();
        var logger = new EnabledLogger<WebSummaryWorkflow>();
        var workflow = new WebSummaryWorkflow(new(), new Execution { Fault = cancelled
            ? new OperationCanceledException() : new IOException("synthetic failure") }, new Audit(), TimeProvider.System, logger);
        var review = workflow.Prepare(Snapshot(), static () => true);
        var confirm = () => workflow.Confirm(review.Id, static () => true, SecurityAuditInitiator.LocalUser, Token);
        if (cancelled) { await confirm.Should().ThrowAsync<OperationCanceledException>(); }
        else { await confirm.Should().ThrowAsync<IOException>(); }
        logger.Messages.Should().ContainSingle().Which.Should().NotContain("private source");
    }

    [Theory]
    [InlineData(LocalContextTurnReason.OriginalInputRequired, ModelTurnReason.OriginalUserRequired)]
    [InlineData(LocalContextTurnReason.TargetAlreadyUsed, ModelTurnReason.TurnAlreadyUsed)]
    [InlineData(LocalContextTurnReason.ContextMismatch, ModelTurnReason.ContextMismatch)]
    [InlineData(LocalContextTurnReason.SessionNotCurrent, ModelTurnReason.SessionNotAdmitted)]
    [InlineData(LocalContextTurnReason.Cancelled, ModelTurnReason.CallerCancelled)]
    [InlineData(LocalContextTurnReason.EvidenceUnavailable, ModelTurnReason.HostInterrupted)]
    [InlineData(LocalContextTurnReason.ModelDenied, ModelTurnReason.InvalidProviderResponse)]
    public async Task SharedRejectionsRetainTheirExplicitReason(LocalContextTurnReason reason, ModelTurnReason expected)
    {
        using var root = Root();
        var workflow = Workflow(new Execution());
        var review = workflow.Prepare(Snapshot(), static () => true);
        var execution = new WebSummaryModelExecution(new RejectionRunner(reason), new(), TimeProvider.System, new WebSourcePolicy());
        var result = await execution.Run(review, static () => true, Token);
        result.Reason.Should().Be(expected);
        result.Response.Should().BeNull();
    }

    [Fact]
    public async Task ExecutionRejectsDifferentOriginEvenWhenInvokedOutsideWorkflow()
    {
        WebSummaryReview review;
        using (var root = Root()) { review = Workflow(new Execution()).Prepare(Snapshot(), static () => true); }
        using var voice = Root(RequestOrigin.ActivatedVoice);
        var execution = new WebSummaryModelExecution(new RejectionRunner(LocalContextTurnReason.ModelDenied), new(),
            TimeProvider.System, new WebSourcePolicy());
        (await execution.Run(review, static () => true, Token)).Reason.Should().Be(ModelTurnReason.OriginalUserRequired);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static HostActivity Root(RequestOrigin origin = RequestOrigin.LocalUi) =>
        HostActivity.BeginRoot(HostRequest.Create(origin), HostActivityLayer.Application, HostOperation.Request);
    private static WebResultSnapshot Snapshot() => WebResultSnapshot.Capture(new(new(Guid.NewGuid()), 1),
        HostRequest.Create(RequestOrigin.LocalUi), 1, new("https://example.com/"),
        new(WebPageGetOutcome.Succeeded, "retrieved", new("https://example.com/"), "text/plain", "private source"),
        DateTimeOffset.UtcNow).WebResult!;
    private static WebSummaryWorkflow Workflow(IWebSummaryModelExecution execution) =>
        new(new(), execution, new Audit(), TimeProvider.System, new EnabledLogger<WebSummaryWorkflow>());

    private sealed class Execution : IWebSummaryModelExecution
    {
        internal Exception? Fault { get; init; }
        internal int Calls { get; private set; }
        public Task<ModelTurnResult> Run(WebSummaryReview review, Func<bool> current, CancellationToken token)
        {
            Calls++;
            if (Fault is { } fault) { throw fault; }
            return Task.FromResult(new ModelTurnResult(ModelTurnOutcome.Unavailable, ModelTurnReason.QualificationPending));
        }
    }

    private sealed class RejectionRunner(LocalContextTurnReason reason) : IModelContextRunner
    {
        public Task<ModelContextAdmission> AdmitAsync(LocalContextSessionTarget target, Func<bool> sourceCurrent,
            CancellationToken token, Func<CancellationToken, Task>? validateSource = null) =>
            Task.FromResult(new ModelContextAdmission(new LocalContextTurnReceipt(target.Request, ModelTurnOutcome.Denied, reason)));
    }

    private sealed class Audit : ISecurityAuditLog
    {
        public void Write(SecurityAuditEvent auditEvent) => HostActivity.RequireCurrent();
    }
}
