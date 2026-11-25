using AwesomeAssertions;
using Kora.Application.Dependencies;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Providers;
using Kora.Core.Tools;
using Kora.Application.Configuration;
using Kora.Application.Hosting;
using Kora.Application.Definitions;
using Kora.Application.UnitTests.Configuration;
using Kora.Core.Artifacts;
using Kora.Core.Customizations;
using Kora.Core.Storage;
using Kora.Core.Agents;
using Microsoft.Extensions.Logging.Abstractions;

using NativeSelection = Kora.Core.Dependencies.CopilotCustomizationSelection;

namespace Kora.Application.UnitTests.Dependencies;

public sealed partial class ModelTurnHostTests
{
    [Fact]
    public async Task LegacyCopilotAdmissionRetainsTruthfulUnavailableWithoutCallingItsOrdinaryPath()
    {
        using var f = new Fixture();
        using var root = f.Root();
        f.Metadata = new(f.Session.SessionId, new(1), new("Target"));
        var legacy = new LegacyCopilotAdmission();
        await using var service = f.ConversationService(legacy);
        var selection = new NativeSelection(false, "source", "revision", "selected", "Reviewed", "Exact source", "Exact source");
        var receipt = await service.SubmitReviewedCopilotCustomizationAsync(new(f.Session, f.Metadata), "unchanged original",
            selection, () => true, _ => Task.CompletedTask, [], RequestOrigin.LocalUi, Token);
        receipt.Message.Body.Should().Be("unchanged original");
        receipt.Message.Outcome.Should().Be(ModelTurnOutcome.Unavailable);
        receipt.Message.Reason.Should().Be(ModelTurnReason.UnregisteredModel);
        receipt.Message.AssistantResponse.Should().BeNull();
        f.RetainCalls.Should().Be(1);
        legacy.Calls.Should().Be(0);
    }

    private sealed class LegacyCopilotAdmission : IModelTurnAdmission, ICopilotTurnAdmission
    {
        internal int Calls { get; private set; }
        public bool IsQuiescent => true;
        public Task<ModelTurnAdmission> AdmitCopilotAsync(ModelContextEnvelope context, CancellationToken token)
        { Calls++; return Task.FromResult(new ModelTurnAdmission(new(ModelTurnOutcome.Unknown, ModelTurnReason.ProviderOutcome))); }
        public Task<ModelTurnAdmission> AdmitAsync(ModelTurnChoice choice, ModelContextEnvelope context, ModelHandoffReview? review,
            CancellationToken token) => throw new InvalidOperationException("Selected Copilot intent must not use ordinary model admission.");
    }

    [Theory]
    [InlineData("tools")]
    [InlineData("missing-validator")]
    [InlineData("source-first")]
    [InlineData("host-first")]
    [InlineData("source-last")]
    [InlineData("host-last")]
    public async Task NativeSelectionAdmissionRequiresSourceAndHostAuthorityBeforeAndAfterProfileResolution(string fault)
    {
        using var f = new Fixture();
        using var root = f.Root();
        var reviews = new HostedTransmissionReviewService(f, f.Time);
        var runtime = new HostedRuntime();
        var host = f.CopilotProfiles(runtime, reviews);
        var context = f.Context(toolMode: fault is "tools" ? ModelToolMode.ReadOnly : ModelToolMode.None);
        var selection = new NativeSelection(false, "source", "revision", "selected", "Reviewed", "Exact source", "Exact source");
        var checks = 0;
        ValueTask<bool> Current(CancellationToken token)
        {
            checks++;
            if (fault is "host-first" && checks == 1 || fault is "host-last" && checks == 2) { f.ControlRevision++; }
            return ValueTask.FromResult(!(fault is "source-first" && checks == 1 || fault is "source-last" && checks == 2));
        }
        var admission = await host.AdmitCopilotAsync(context, selection, fault is "missing-validator" ? null! : Current, Token);
        admission.Turn.Should().BeNull();
        admission.Result.Outcome.Should().Be(ModelTurnOutcome.DeniedEgress);
        runtime.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HostedPreparationCannotGrantSelectedIntentWithToolsOrMissingSourceValidation(bool tools)
    {
        using var f = new Fixture();
        using var root = f.Root();
        var reviews = new HostedTransmissionReviewService(f, f.Time);
        var selection = new NativeSelection(false, "source", "revision", "selected", "Reviewed", "Exact source", "Exact source");
        Action prepare = () => reviews.PrepareTurn(f.Context(toolMode: tools ? ModelToolMode.ReadOnly : ModelToolMode.None),
            Profile(ProviderService.Copilot), selection, tools ? _ => ValueTask.FromResult(true) : null);
        prepare.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public async Task SelectedAuthorizationCannotFallBackToOrdinaryRuntimeOrBeReplayed()
    {
        using var f = new Fixture();
        using var root = f.Root();
        var reviews = new HostedTransmissionReviewService(f, f.Time);
        var runtime = new HostedRuntime();
        var host = f.CopilotProfiles(runtime, reviews);
        var selection = new NativeSelection(false, "source", "revision", "selected",
            "Description", "Reviewed native source", "Reviewed native source");
        var context = f.Context(toolMode: ModelToolMode.None);
        var authorization = reviews.PrepareTurn(context, Profile(ProviderService.Copilot), selection, _ => ValueTask.FromResult(true));
        var admission = await host.AdmitCopilotAsync(authorization, Token);
        admission.Turn.Should().NotBeNull();
        (await host.RunAsync(admission.Turn!, Token)).Outcome.Should().Be(ModelTurnOutcome.Unavailable);
        runtime.Calls.Should().Be(0);
        runtime.Transmissions.Should().Be(0);
        (await host.AdmitCopilotAsync(authorization, Token)).Result.Outcome.Should().Be(ModelTurnOutcome.DeniedEgress);
    }

    [Theory]
    [InlineData("valid")]
    [InlineData("initial-source")]
    [InlineData("review-source")]
    [InlineData("answer-source")]
    [InlineData("no-presenter")]
    public async Task SelectedAdapterBindsExactNativeCopyAndRevalidatesSourceAtReviewAndAnswer(string fault)
    {
        using var f = new Fixture();
        using var root = f.Root();
        var reviews = new HostedTransmissionReviewService(f, f.Time);
        var runtime = new SelectedRuntime();
        var selection = new NativeSelection(true, "source", "revision", "selected",
            "Description", "Reviewed source", "Reviewed source");
        var context = f.Context(toolMode: ModelToolMode.None);
        var profile = Profile(ProviderService.Copilot);
        var sourceCurrent = fault is not "initial-source";
        var authorization = reviews.PrepareTurn(context, profile, selection, _ => ValueTask.FromResult(sourceCurrent));
        var adapter = new CopilotModelTurnAdapter(runtime, reviews).Bind(authorization, _ => ValueTask.FromResult(true));
        var provenance = new ModelTurnProvenance(new(Guid.NewGuid()), context.Request, ModelProviderSelection.CopilotCandidate);
        if (fault is not "no-presenter")
        {
            reviews.Offered += review =>
            {
                if (fault is "review-source") { sourceCurrent = false; }
                using var presentation = review.Presentation();
                reviews.Confirm(review, true, RequestOrigin.LocalUi).Should().BeTrue();
                presentation.Complete(HostOperationOutcome.Completed);
            };
        }
        runtime.Run = async (copy, current, review) =>
        {
            copy.Should().BeSameAs(selection);
            (await current(Token)).Should().BeTrue();
            if (!await review(new(provenance, profile, new("https://api.githubcopilot.com/chat/completions"), context.Serialize()), Token))
            { return new(ModelTurnOutcome.DeniedEgress); }
            if (fault is "answer-source") { sourceCurrent = false; }
            return new(ModelTurnOutcome.Succeeded, "Native answer");
        };
        var result = await adapter.RunAsync(provenance, profile, context.Serialize(),
            (_, _, _) => throw new InvalidOperationException("Selected native runs cannot invoke tools"), Token);
        result.Outcome.Should().Be(fault is "valid" ? ModelTurnOutcome.Succeeded : ModelTurnOutcome.DeniedEgress);
        (result.Response?.Answer).Should().Be(fault is "valid" ? "Native answer" : null);
        runtime.Calls.Should().Be(fault is "initial-source" ? 0 : 1);
    }

    private sealed class SelectedRuntime : ICopilotCustomizationTurnRuntime
    {
        internal int Calls { get; private set; }
        internal Func<NativeSelection, Func<CancellationToken, ValueTask<bool>>,
            Func<CopilotTransmission, CancellationToken, ValueTask<bool>>, Task<CopilotTurnReply>> Run { get; set; } = null!;
        public bool IsQuiescent => true;
        public Task<CopilotTurnReply> RunSelectedAsync(ModelTurnProvenance provenance, ProviderProfileTuple profile,
            ReadOnlyMemory<byte> context, NativeSelection selection,
            Func<CancellationToken, ValueTask<bool>> sourceCurrent,
            Func<CopilotTransmission, CancellationToken, ValueTask<bool>> admitTransmission, CancellationToken cancellationToken)
        { Calls++; return Run(selection, sourceCurrent, admitTransmission); }
        public Task<CopilotTurnReply> RunAsync(ModelTurnProvenance provenance, ProviderProfileTuple profile,
            ReadOnlyMemory<byte> context, Func<string, string, CancellationToken, ValueTask<CapabilityReply>> invokeTool,
            Func<CopilotTransmission, CancellationToken, ValueTask<bool>> admitTransmission, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Native selection must never use ordinary runtime fallback.");
    }

    [Theory]
    [InlineData("complete")]
    [InlineData("complete-skill")]
    [InlineData("no-hosted-consent")]
    [InlineData("replaced-source")]
    [InlineData("target")]
    [InlineData("target-event")]
    [InlineData("expired")]
    [InlineData("disabled")]
    public async Task SelectedWorkflowKeepsReadHostedCopyAndCanonicalRetentionSeparate(string state)
    {
        await using var f = new SelectedWorkflowFixture();
        using var root = f.Host.Root();
        if (state is "complete-skill")
        {
            f.Sources.Source = f.Sources.Source with { Kind = CustomizationKind.Skill, PrimaryFile = "SKILL.md" };
            f.Sources.Snapshot = new(f.Sources.Source, "revision",
                [new("SKILL.md", System.Text.Encoding.UTF8.GetBytes("---\nname: reviewed\nversion: 1.0.0\ndescription: Reviewed\n---\nPlain instructions."))]);
        }
        var review = await f.Workflow.ReviewAsync(f.Sources.Source, "Target", "  unchanged task\r\n", RequestOrigin.LocalUi,
            (_, _) => Task.FromResult(true), () => true, Token);
        review.Text.Should().Contain(f.Sources.Snapshot.Files[0].Text).And.Contain("  unchanged task\r\n").And.Contain("Target");
        f.Host.RetainCalls.Should().Be(0);
        if (state is "no-hosted-consent")
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => f.Workflow.ConfirmAsync(review.Id, () => true, Token));
        }
        else
        {
            await f.Workflow.ApproveHostedSourceAsync(review.Id, () => true, Token);
            review.HostedSourceApproved.Should().BeTrue();
            f.Host.RetainCalls.Should().Be(0);
            if (state is "replaced-source") { f.Sources.Snapshot = new(f.Sources.Source, "replacement", f.Sources.Snapshot.Files); }
            if (state is "target") { f.Host.Metadata = f.Host.Metadata! with { Revision = new(2) }; }
            if (state is "target-event")
            {
                await Assert.ThrowsAsync<NotSupportedException>(() => f.Workspace.RenameAsync(f.Host.Session.SessionId,
                    f.Host.Session.Generation, f.Host.Metadata!.Revision.Value, new("Renamed"), RequestOrigin.LocalUi, Token));
            }
            if (state is "expired") { f.Host.Time.Now = review.ExpiresAt; }
            if (state is "disabled") { f.Use.SetDisabled(f.Sources.Source.Id, true); }
            if (state is "complete" or "complete-skill")
            {
                var receipt = await f.Workflow.ConfirmAsync(review.Id, () => true, Token);
                receipt.Message.Body.Should().Be("  unchanged task\r\n");
                receipt.Message.Outcome.Should().Be(ModelTurnOutcome.Unavailable, "missing native capability is not text fallback");
                receipt.Message.AssistantResponse.Should().BeNull();
                f.Host.RetainCalls.Should().Be(1);
                await Assert.ThrowsAsync<InvalidOperationException>(() => f.Workflow.ConfirmAsync(review.Id, () => true, Token));
                return;
            }
            await Assert.ThrowsAnyAsync<Exception>(() => f.Workflow.ConfirmAsync(review.Id, () => true, Token));
        }
        f.Host.RetainCalls.Should().Be(0);
    }

    [Theory]
    [InlineData("missing-presenter")]
    [InlineData("declined")]
    [InlineData("unsupported")]
    [InlineData("disabled")]
    [InlineData("foreign-source")]
    [InlineData("missing-target")]
    [InlineData("ambiguous")]
    [InlineData("paging")]
    [InlineData("retired")]
    [InlineData("unavailable")]
    public async Task SelectedWorkflowCannotTurnMissingConsentOrChangedOwnershipIntoAReview(string state)
    {
        await using var f = new SelectedWorkflowFixture();
        using var root = f.Host.Root();
        if (state is "unsupported") { f.Sources.Source = f.Sources.Source with { Kind = CustomizationKind.Prompt }; }
        if (state is "unavailable") { f.Sources.Source = f.Sources.Source with { UnavailableReason = "unavailable" }; }
        if (state is "disabled") { f.Use.SetDisabled(f.Sources.Source.Id, true); }
        if (state is "foreign-source") { f.Sources.Snapshot = new(f.Sources.Source with { Id = "foreign.source" }, "revision", f.Sources.Snapshot.Files); }
        if (state is "missing-target") { f.Host.MetadataPage = new([], null); }
        if (state is "ambiguous") { f.Host.MetadataPage = new([new(f.Host.Session, f.Host.Metadata), new(f.Host.Session, f.Host.Metadata)], null); }
        if (state is "paging") { f.Host.MetadataPage = new([], Guid.NewGuid()); }
        if (state is "retired") { f.Sources.OnRead = () => _ = f.Workflow.StopAsync(); }
        await Assert.ThrowsAnyAsync<Exception>(() => f.Workflow.ReviewAsync(f.Sources.Source, "Target", "original", RequestOrigin.LocalUi,
            state is "missing-presenter" ? null : (_, _) => Task.FromResult(state is not "declined"), () => true, Token));
        f.Host.RetainCalls.Should().Be(0);
        if (state is "missing-presenter" or "declined" or "unsupported" or "disabled" or "unavailable") { f.Sources.Reads.Should().Be(0); }
    }

    [Fact]
    public async Task SelectedReviewStopJoinsCancellationIgnoringReadAndCannotReviveConsumedOrClosedAuthority()
    {
        await using var f = new SelectedWorkflowFixture();
        using var root = f.Host.Root();
        var release = new TaskCompletionSource<CustomizationSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Sources.PendingRead = release.Task;
        var reading = f.Workflow.ReviewAsync(f.Sources.Source, "Target", "original", RequestOrigin.LocalUi,
            (_, _) => Task.FromResult(true), () => true, Token);
        f.Sources.Reads.Should().Be(1);
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Workflow.ReviewAsync(f.Sources.Source, "Target", "another",
            RequestOrigin.LocalUi, (_, _) => Task.FromResult(true), () => true, Token));
        var joined = f.Workflow.StopAsync();
        joined.IsCompleted.Should().BeFalse();
        release.SetResult(f.Sources.Snapshot);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reading);
        await joined;
        f.Sources.PendingRead = null;
        var review = await f.Workflow.ReviewAsync(f.Sources.Source, "Target", "original", RequestOrigin.LocalUi,
            (_, _) => Task.FromResult(true), () => true, Token);
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Workflow.ReviewAsync(f.Sources.Source, "Target", "another",
            RequestOrigin.LocalUi, (_, _) => Task.FromResult(true), () => true, Token));
        f.Sources.Snapshot = new(f.Sources.Source, "replacement", f.Sources.Snapshot.Files);
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Workflow.ApproveHostedSourceAsync(review.Id, () => true, Token));
        await f.Workflow.DisposeAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Workflow.ApproveHostedSourceAsync(review.Id, () => true, Token));
        f.Host.RetainCalls.Should().Be(0);
    }

    private sealed class SelectedWorkflowFixture : IAsyncDisposable
    {
        internal Fixture Host { get; } = new();
        internal Kora.Application.UnitTests.Definitions.CopilotCustomizationWorkflowTests.SourceStore Sources { get; } = new();
        internal ArtifactUsePreferences Use { get; } = new(new LocalSharedSkillPreferencesTests.Store());
        private readonly AudioControlTestStore controls = new();
        private readonly InstructionControlAdmission admission;
        private readonly ArtifactCatalogue catalogue;
        private readonly CustomizationManagementService management;
        private readonly SessionConversationService conversation;
        private readonly ModelTurnHost model;
        internal CopilotCustomizationWorkflow Workflow { get; }
        internal SessionWorkspaceService Workspace { get; }
        internal SelectedWorkflowFixture(IClaudeTurnRuntime? claude = null)
        {
            Host.Metadata = new(Host.Session.SessionId, new(1), new("Target"));
            Host.MetadataPage = new([new(Host.Session, Host.Metadata)], null);
            admission = new(controls, controls, new(controls));
            catalogue = new([OwnedAgentProfileDocument.Parse("reviewed", Sources.Snapshot.Files[0].Text).Document.Definition]);
            management = new CustomizationManagementService(Sources, admission, Use, catalogue, Host);
            Workspace = new SessionWorkspaceService(Host, new(Host), Host, NullLogger<SessionWorkspaceService>.Instance);
            var reviews = new HostedTransmissionReviewService(Host, Host.Time);
            model = claude is null ? Host.CopilotProfiles(new HostedRuntime(), reviews) : Host.ClaudeProfiles(claude, reviews);
            conversation = Host.ConversationService(model, execution: model);
            Workflow = new(management, Workspace, conversation, model, Host, Host.Time);
        }
        internal HostedCustomizationWorkflow ClaudeWorkflow() =>
            new(management, Workspace, conversation, model, Host, Host.Time, ProviderService.Claude);
        public async ValueTask DisposeAsync()
        {
            await Workflow.DisposeAsync();
            await admission.DisposeAsync();
            catalogue.Dispose();
            Host.Dispose();
        }
    }
}
