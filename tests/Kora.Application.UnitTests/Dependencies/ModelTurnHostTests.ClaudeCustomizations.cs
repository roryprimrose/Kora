using AwesomeAssertions;
using Kora.Application.Dependencies;
using Kora.Core.Customizations;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Providers;

namespace Kora.Application.UnitTests.Dependencies;

public sealed partial class ModelTurnHostTests
{
    [Theory]
    [InlineData("complete")]
    [InlineData("profile-at-commit")]
    [InlineData("source-at-commit")]
    public async Task SelectedClaudeUsesOriginalRetentionAndRechecksSourceAndProfileBeforeAnswerCommit(string fault)
    {
        using var f = new Fixture();
        var runtime = new HostClaudeCopy();
        var host = f.ClaudeProfiles(runtime, new(f, f.Time));
        await using var service = f.ConversationService(host, execution: host);
        var source = true;
        service.RunningConversationChanged += status =>
        {
            if (status.Phase != ConversationExecutionPhase.Completing) { return; }
            if (fault is "profile-at-commit") { f.ProfileCatalogue.Status = ProviderCatalogueStatus.Unavailable; }
            if (fault is "source-at-commit") { source = false; }
        };
        var copy = new SelectedHostedDefinition(CustomizationKind.Agent, "source", "revision", "reviewed", "Description", "Exact source");
        Task Validate(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (!source) { throw new InvalidOperationException("Selected source withdrawn."); }
            return Task.CompletedTask;
        }
        var pending = service.SubmitReviewedClaudeCustomizationAsync(new(f.Session, null), "  original task\r\n",
            copy, () => true, Validate, [], RequestOrigin.LocalUi, Token);
        if (fault is "source-at-commit")
        {
            await Assert.ThrowsAsync<ConversationSubmissionUnconfirmedException>(() => pending);
            f.Retained!.AssistantResponse.Should().BeNull();
            f.ExecutionCompletions.Should().Be(0);
        }
        else
        {
            var receipt = await pending;
            receipt.Message.Body.Should().Be("  original task\r\n");
            receipt.Message.Outcome.Should().Be(fault is "complete" ? ModelTurnOutcome.Succeeded : ModelTurnOutcome.DeniedEgress);
            if (fault is "complete")
            {
                receipt.Message.AssistantResponse.Should().NotBeNull();
                receipt.Message.AssistantResponse!.Body.Should().Be("Genuine selected answer");
            }
            else { receipt.Message.AssistantResponse.Should().BeNull(); }
            f.ExecutionCompletions.Should().Be(1);
        }
        f.RetainCalls.Should().Be(1);
        runtime.Selection.Should().BeSameAs(copy);
        runtime.Context!.UserRequest.Should().Be("  original task\r\n");
        runtime.Context.ToolMode.Should().Be(ModelToolMode.None);
        runtime.Context.Evidence.Should().BeEmpty();
        host.IsQuiescent.Should().BeTrue();
    }

    private sealed class HostClaudeCopy : IClaudeCustomizationTurnRuntime
    {
        public bool IsQuiescent => true;
        internal SelectedHostedDefinition? Selection { get; private set; }
        internal ModelContextEnvelope? Context { get; private set; }
        public async Task<ClaudeTurnReply> RunSelectedAsync(ModelTurnProvenance provenance, ProviderProfileTuple profile,
            ModelContextEnvelope context, SelectedHostedDefinition selection,
            Func<ClaudeTransmission, CancellationToken, ValueTask<bool>> admit,
            Func<CancellationToken, ValueTask<bool>> current, CancellationToken token)
        {
            Selection = selection; Context = context;
            return await current(token) ? new(ModelTurnOutcome.Succeeded, "Genuine selected answer") : new(ModelTurnOutcome.DeniedEgress);
        }
        public Task<ClaudeTurnReply> RunAsync(ModelTurnProvenance provenance, ProviderProfileTuple profile,
            ModelContextEnvelope context, Func<ClaudeTransmission, CancellationToken, ValueTask<bool>> admit,
            Func<CancellationToken, ValueTask<bool>> current, CancellationToken token) =>
            throw new InvalidOperationException("Selected turn cannot use an ordinary runtime fallback.");
    }

    [Theory]
    [InlineData("complete")]
    [InlineData("skill")]
    [InlineData("no-read")]
    [InlineData("no-copy")]
    [InlineData("replaced")]
    [InlineData("target")]
    [InlineData("expired")]
    [InlineData("disabled")]
    public async Task ClaudeWorkflowKeepsReadCopyAndOriginalRetentionIndependent(string fault)
    {
        await using var f = new SelectedWorkflowFixture(new ClaudeRuntime());
        await using var workflow = f.ClaudeWorkflow();
        using var root = f.Host.Root();
        if (fault is "skill")
        {
            f.Sources.Source = f.Sources.Source with { Kind = CustomizationKind.Skill, PrimaryFile = "SKILL.md" };
            f.Sources.Snapshot = new(f.Sources.Source, "revision", [new("SKILL.md", System.Text.Encoding.UTF8.GetBytes(
                "---\nname: reviewed\nversion: 1.0.0\ndescription: Reviewed\n---\nPlain instructions."))]);
        }
        if (fault is "no-read")
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => workflow.ReviewAsync(f.Sources.Source,
                "Target", "original", RequestOrigin.LocalUi, (_, _) => Task.FromResult(false), () => true, Token));
            f.Sources.Reads.Should().Be(0);
            f.Host.RetainCalls.Should().Be(0);
            return;
        }
        var review = await workflow.ReviewAsync(f.Sources.Source, "Target", "  exact task\r\n", RequestOrigin.LocalUi,
            (_, _) => Task.FromResult(true), () => true, Token);
        review.Provider.Should().Be(ProviderService.Claude);
        review.SelectedCopy!.SourceText.Should().Be(f.Sources.Snapshot.Files[0].Text);
        review.Text.Should().Contain("lower-trust USER data").And.Contain("  exact task\r\n").And.Contain("Target");
        f.Host.RetainCalls.Should().Be(0);
        if (fault is "no-copy")
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => workflow.ConfirmAsync(review.Id, () => true, Token));
            f.Host.RetainCalls.Should().Be(0);
            return;
        }
        await workflow.ApproveHostedSourceAsync(review.Id, () => true, Token);
        f.Host.RetainCalls.Should().Be(0);
        if (fault is "replaced") { f.Sources.Snapshot = new(f.Sources.Source, "replacement", f.Sources.Snapshot.Files); }
        if (fault is "target") { f.Host.Metadata = f.Host.Metadata! with { Revision = new(2) }; }
        if (fault is "expired") { f.Host.Time.Now = review.ExpiresAt; }
        if (fault is "disabled") { f.Use.SetDisabled(f.Sources.Source.Id, true); }
        if (fault is not ("complete" or "skill"))
        {
            await Assert.ThrowsAnyAsync<Exception>(() => workflow.ConfirmAsync(review.Id, () => true, Token));
            f.Host.RetainCalls.Should().Be(0);
            return;
        }
        var receipt = await workflow.ConfirmAsync(review.Id, () => true, Token);
        receipt.Message.Body.Should().Be("  exact task\r\n");
        receipt.Message.Outcome.Should().Be(ModelTurnOutcome.Unavailable);
        receipt.Message.AssistantResponse.Should().BeNull("non-capable runtime must not receive an ordinary text fallback");
        f.Host.RetainCalls.Should().Be(1);
        await Assert.ThrowsAsync<InvalidOperationException>(() => workflow.ConfirmAsync(review.Id, () => true, Token));
    }

    [Fact]
    public async Task ClaudeWorkflowStopRetainsCancellationIgnoringSourceReadUntilJoin()
    {
        await using var f = new SelectedWorkflowFixture(new ClaudeRuntime());
        await using var workflow = f.ClaudeWorkflow();
        using var root = f.Host.Root();
        var release = new TaskCompletionSource<CustomizationSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Sources.PendingRead = release.Task;
        var reading = workflow.ReviewAsync(f.Sources.Source, "Target", "original", RequestOrigin.LocalUi,
            (_, _) => Task.FromResult(true), () => true, Token);
        f.Sources.Reads.Should().Be(1);
        var joined = workflow.StopAsync();
        joined.IsCompleted.Should().BeFalse();
        await Assert.ThrowsAsync<InvalidOperationException>(() => workflow.ReviewAsync(f.Sources.Source, "Target", "successor",
            RequestOrigin.LocalUi, (_, _) => Task.FromResult(true), () => true, Token));
        release.SetResult(f.Sources.Snapshot);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reading);
        await joined;
        f.Host.RetainCalls.Should().Be(0);
    }

    [Theory]
    [InlineData("tools")]
    [InlineData("evidence")]
    [InlineData("mcp")]
    [InlineData("missing-validator")]
    [InlineData("source-first")]
    [InlineData("host-first")]
    [InlineData("source-last")]
    [InlineData("host-last")]
    public async Task SelectedClaudeAdmissionRevalidatesSourceAndHostAroundProfileResolution(string fault)
    {
        using var f = new Fixture();
        using var root = f.Root();
        var reviews = new HostedTransmissionReviewService(f, f.Time);
        var runtime = new ClaudeRuntime();
        var host = f.ClaudeProfiles(runtime, reviews);
        var copy = new SelectedHostedDefinition(CustomizationKind.Skill, "source", "revision", "reviewed", "Description", "Exact source");
        var checks = 0;
        ValueTask<bool> Current(CancellationToken token)
        {
            checks++;
            if (fault is "host-first" && checks == 1 || fault is "host-last" && checks == 2) { f.ControlRevision++; }
            return ValueTask.FromResult(!(fault is "source-first" && checks == 1 || fault is "source-last" && checks == 2));
        }
        ModelContextEnvelope context = fault switch
        {
            "tools" => new(f.Request, "policy", "body", [], f.Time.Now, f.Time.Now.AddMinutes(1), ModelToolMode.ReadOnly,
                ClaudeObservationTools.Constrain([Kora.Core.Tools.ReadOnlyCapabilityCatalog.Version])),
            "evidence" => new(f.Request, "policy", "body",
                [new(new(Guid.NewGuid()), f.Request, new(1), "not implicit context", ModelEvidenceDisclosure.HostedEligible)],
                f.Time.Now, f.Time.Now.AddMinutes(1), ModelToolMode.None),
            "mcp" => new(f.Request, "policy", "body", [], f.Time.Now, f.Time.Now.AddMinutes(1), ModelToolMode.McpReadOnly,
                mcpTool: new("mcp.reviewed", McpSchema)),
            _ => f.Context(toolMode: ModelToolMode.None),
        };
        var admission = await host.AdmitClaudeAsync(context, copy, fault is "missing-validator" ? null! : Current, Token);
        admission.Turn.Should().BeNull();
        admission.Result.Outcome.Should().Be(ModelTurnOutcome.DeniedEgress);
        admission.Result.Reason.Should().Be(ModelTurnReason.EgressNotAdmitted);
        runtime.Calls.Should().Be(0);
        checks.Should().Be(fault switch
        {
            "source-first" or "host-first" => 1,
            "source-last" or "host-last" => 2,
            _ => 0,
        });
    }

    [Fact]
    public async Task UnsupportedHostedProviderCannotReadRetainOrPoisonTheSelectedWorkflowOwner()
    {
        var runtime = new ClaudeRuntime();
        await using var f = new SelectedWorkflowFixture(runtime);
        using var root = f.Host.Root();
        Action construct = () => f.ClaudeWorkflow(ProviderService.Local);
        construct.Should().Throw<ArgumentOutOfRangeException>();
        f.Sources.Reads.Should().Be(0);
        f.Host.RetainCalls.Should().Be(0);
        runtime.Calls.Should().Be(0);

        await using var workflow = f.ClaudeWorkflow();
        var review = await workflow.ReviewAsync(f.Sources.Source, "Target", "original after rejection", RequestOrigin.LocalUi,
            (_, _) => Task.FromResult(true), () => true, Token);
        await workflow.ApproveHostedSourceAsync(review.Id, () => true, Token);
        var receipt = await workflow.ConfirmAsync(review.Id, () => true, Token);
        receipt.Message.Body.Should().Be("original after rejection");
        receipt.Message.Outcome.Should().Be(ModelTurnOutcome.Unavailable);
        receipt.Message.AssistantResponse.Should().BeNull("missing selected capability cannot use the ordinary runtime");
        f.Host.RetainCalls.Should().Be(1);
        runtime.Calls.Should().Be(0);
    }
}
