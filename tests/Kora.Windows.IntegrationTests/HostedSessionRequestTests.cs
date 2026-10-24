using System.Text;

using AwesomeAssertions;

using Kora.Application.Configuration;
using Kora.Application.Dependencies;
using Kora.Application.Hosting;
using Kora.Application.Tools;
using Kora.Application.Voice;
using Kora.Core.Auditing;
using Kora.Core.Commands;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Providers;
using Kora.Core.Storage;
using Kora.Core.Tools;
using Kora.Windows.IntegrationTests.Storage;
using Kora.Windows.Storage;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class HostedSessionRequestTests
{
    [Theory]
    [InlineData(RequestOrigin.LocalUi, RequestOrigin.LocalUi, true)]
    [InlineData(RequestOrigin.LocalUi, RequestOrigin.ActivatedVoice, true)]
    [InlineData(RequestOrigin.ActivatedVoice, RequestOrigin.LocalUi, true)]
    [InlineData(RequestOrigin.ActivatedVoice, RequestOrigin.ActivatedVoice, true)]
    [InlineData(RequestOrigin.LocalUi, RequestOrigin.LocalUi, false)]
    [InlineData(RequestOrigin.ActivatedVoice, RequestOrigin.LocalUi, false)]
    public async Task Reviewed_copilot_request_requires_separate_final_send_and_retains_only_genuine_original_answer(
        RequestOrigin origin, RequestOrigin confirmation, bool approve)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        var runtime = new CopilotConversationRuntime();
        await using var composed = new Composition(f, copilot: runtime);
        var commands = new SessionRequestCommandService(composed.Workspace, composed.Conversation, composed.Access, f.Time);
        var target = await f.Store.ReadMetadataAsync(f.Request.SessionId, f.Token);
        const string body = "  original hosted request\r\n\tend  ";
        var review = (await commands.ExecuteAsync(HostedReview(target, body), origin, () => true, f.Token)).RequestReview!;
        review.Provider.Should().Be(SessionRequestReview.ProviderChoice.Copilot);
        review.Origin.Should().Be(origin);
        review.Body.Should().Be(body);
        runtime.Request.Should().BeNull();
        f.Count("conversation_messages").Should().Be(0);
        var offers = 0;
        composed.Reviews.Offered += disclosure =>
        {
            offers++;
            disclosure.Transmission.Provenance.Request.Origin.Should().Be(origin);
            disclosure.Transmission.Provenance.Request.SessionId.Should().Be(target.Authority.SessionId);
            disclosure.Transmission.Profile.Should().Be(CopilotCatalogue.Profile);
            Encoding.UTF8.GetString(disclosure.Transmission.Body.AsSpan()).Should().Contain("original hosted request");
            runtime.Sends.Should().Be(0, "request confirmation is not final physical-send approval");
            using var presentation = disclosure.Presentation();
            composed.Reviews.Confirm(disclosure, approve, RequestOrigin.LocalUi).Should().BeTrue();
            presentation.Complete(HostOperationOutcome.Completed);
        };
        var confirm = HostedConfirm(review);
        var receipt = (await commands.ExecuteAsync(confirm, confirmation, () => true, f.Token)).RequestSubmission!;
        receipt.Message.Request.Origin.Should().Be(origin);
        receipt.Message.Request.Should().Be(runtime.Request);
        receipt.Message.Body.Should().Be(body);
        receipt.Message.Outcome.Should().Be(approve ? ModelTurnOutcome.Succeeded : ModelTurnOutcome.DeniedEgress);
        if (approve)
        {
            receipt.Message.AssistantResponse!.Body.Should().Be("genuine hosted boundary answer");
            receipt.Task.State.Should().Be(HostTaskState.Succeeded);
        }
        else { receipt.Message.AssistantResponse.Should().BeNull(); }
        var saved = await f.Store.ReadConversationMessageAsync(target.Authority.SessionId, receipt.Message.Request.RequestId, f.Token);
        saved.Should().Be(receipt.Message);
        offers.Should().Be(1);
        runtime.Sends.Should().Be(approve ? 1 : 0);
        composed.Turns.IsQuiescent.Should().BeTrue();
        await commands.Invoking(value => value.ExecuteAsync(confirm, confirmation, () => true, f.Token))
            .Should().ThrowAsync<InvalidOperationException>();
        runtime.Sends.Should().Be(approve ? 1 : 0);
        WindowsSqliteConversationTests.AuditText(f).Should().NotContain(body).And.NotContain("genuine hosted boundary answer");
    }

    [Fact]
    public async Task Reviewed_copilot_without_native_disclosure_presenter_denies_without_send_or_answer()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        var runtime = new CopilotConversationRuntime();
        await using var composed = new Composition(f, copilot: runtime);
        var commands = new SessionRequestCommandService(composed.Workspace, composed.Conversation, composed.Access, f.Time);
        var target = await f.Store.ReadMetadataAsync(f.Request.SessionId, f.Token);
        var review = (await commands.ExecuteAsync(HostedReview(target, "original hosted request"),
            RequestOrigin.ActivatedVoice, () => true, f.Token)).RequestReview!;
        var receipt = (await commands.ExecuteAsync(HostedConfirm(review), RequestOrigin.LocalUi, () => true, f.Token)).RequestSubmission!;
        receipt.Message.Outcome.Should().Be(ModelTurnOutcome.DeniedEgress);
        receipt.Message.AssistantResponse.Should().BeNull();
        runtime.Sends.Should().Be(0);
        composed.Turns.IsQuiescent.Should().BeTrue();
    }

    [Theory]
    [InlineData("account")]
    [InlineData("model")]
    public async Task Hosted_final_send_rechecks_current_account_and_model_after_disclosure(string change)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        var runtime = new CopilotConversationRuntime();
        await using var composed = new Composition(f, copilot: runtime);
        var commands = new SessionRequestCommandService(composed.Workspace, composed.Conversation, composed.Access, f.Time);
        var target = await f.Store.ReadMetadataAsync(f.Request.SessionId, f.Token);
        var review = (await commands.ExecuteAsync(HostedReview(target, "original hosted request"),
            RequestOrigin.ActivatedVoice, () => true, f.Token)).RequestReview!;
        composed.Reviews.Offered += disclosure =>
        {
            composed.Catalogue.ChangedAccount = change is "account";
            composed.Catalogue.ChangedModel = change is "model";
            using var presentation = disclosure.Presentation();
            composed.Reviews.Confirm(disclosure, true, RequestOrigin.LocalUi).Should().BeTrue();
            presentation.Complete(HostOperationOutcome.Completed);
        };
        var receipt = (await commands.ExecuteAsync(HostedConfirm(review), RequestOrigin.LocalUi, () => true, f.Token)).RequestSubmission!;
        receipt.Message.Outcome.Should().Be(ModelTurnOutcome.Denied);
        receipt.Message.Reason.Should().Be(ModelTurnReason.UnregisteredModel);
        receipt.Message.AssistantResponse.Should().BeNull();
        runtime.Sends.Should().Be(0);
        composed.Turns.IsQuiescent.Should().BeTrue();
    }

    [Fact]
    public async Task Hosted_each_physical_send_requires_fresh_disclosure_and_second_denial_cannot_become_answer()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        var runtime = new CopilotConversationRuntime { RequestedSends = 2 };
        await using var composed = new Composition(f, copilot: runtime);
        var commands = new SessionRequestCommandService(composed.Workspace, composed.Conversation, composed.Access, f.Time);
        var target = await f.Store.ReadMetadataAsync(f.Request.SessionId, f.Token);
        var review = (await commands.ExecuteAsync(HostedReview(target, "original hosted request"),
            RequestOrigin.ActivatedVoice, () => true, f.Token)).RequestReview!;
        var offers = 0;
        composed.Reviews.Offered += disclosure =>
        {
            offers++;
            runtime.Sends.Should().Be(offers - 1);
            using var presentation = disclosure.Presentation();
            composed.Reviews.Confirm(disclosure, offers == 1, RequestOrigin.LocalUi).Should().BeTrue();
            presentation.Complete(HostOperationOutcome.Completed);
        };
        var receipt = (await commands.ExecuteAsync(HostedConfirm(review), RequestOrigin.LocalUi, () => true, f.Token)).RequestSubmission!;
        offers.Should().Be(2);
        runtime.Sends.Should().Be(1);
        receipt.Message.Outcome.Should().Be(ModelTurnOutcome.DeniedEgress);
        receipt.Message.AssistantResponse.Should().BeNull();
        composed.Turns.IsQuiescent.Should().BeTrue();
    }

    [Fact]
    public async Task Hosted_running_cancellation_owns_runtime_join_and_suppresses_late_success()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        var runtime = new CopilotConversationRuntime { HoldCompletion = true };
        await using var composed = new Composition(f, copilot: runtime);
        var commands = new SessionRequestCommandService(composed.Workspace, composed.Conversation, composed.Access, f.Time);
        var target = await f.Store.ReadMetadataAsync(f.Request.SessionId, f.Token);
        var review = (await commands.ExecuteAsync(HostedReview(target, "original hosted request"),
            RequestOrigin.ActivatedVoice, () => true, f.Token)).RequestReview!;
        composed.Reviews.Offered += disclosure =>
        {
            using var presentation = disclosure.Presentation();
            composed.Reviews.Confirm(disclosure, true, RequestOrigin.LocalUi).Should().BeTrue();
            presentation.Complete(HostOperationOutcome.Completed);
        };
        var submission = commands.ExecuteAsync(HostedConfirm(review), RequestOrigin.LocalUi, () => true, f.Token);
        try
        {
            await runtime.Sent.Task.WaitAsync(f.Token);
            runtime.IsQuiescent.Should().BeFalse();
            var status = await composed.Conversation.ReadRunningConversationAsync(target, () => true, f.Token);
            await composed.Conversation.CancelRunningConversationAsync(status!.Target, () => true, f.Token);
            submission.IsCompleted.Should().BeFalse("cancellation is not a runtime resource join");
            var dispatched = await f.Store.ReadConversationMessageAsync(target.Authority.SessionId, status.Target.Request.RequestId, f.Token);
            dispatched.AssistantResponse.Should().BeNull();
            dispatched.Outcome.Should().BeNull();
            runtime.Release.TrySetResult();
            var receipt = (await submission).RequestSubmission!;
            receipt.Message.Outcome.Should().Be(ModelTurnOutcome.Cancelled);
            receipt.Message.AssistantResponse.Should().BeNull();
            receipt.Message.Request.Origin.Should().Be(RequestOrigin.ActivatedVoice);
            runtime.IsQuiescent.Should().BeTrue();
            composed.Turns.IsQuiescent.Should().BeTrue();
            runtime.Sends.Should().Be(1);
        }
        finally
        {
            runtime.Release.TrySetResult();
            await submission;
        }
    }

    [Theory]
    [InlineData("expiry")]
    [InlineData("source")]
    [InlineData("control")]
    [InlineData("retire")]
    [InlineData("target")]
    [InlineData("provider")]
    public async Task Hosted_review_revalidates_original_authority_before_retention_without_fallback(string fault)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        var runtime = new CopilotConversationRuntime();
        await using var composed = new Composition(f, copilot: runtime);
        var commands = new SessionRequestCommandService(composed.Workspace, composed.Conversation, composed.Access, f.Time);
        var target = await f.Store.ReadMetadataAsync(f.Request.SessionId, f.Token);
        var source = true;
        var command = HostedReview(target, "original hosted request");
        if (fault is "provider")
        {
            await commands.Invoking(value => value.ExecuteAsync(command with { RequestProvider = (SessionRequestReview.ProviderChoice)99 },
                RequestOrigin.ActivatedVoice, () => source, f.Token)).Should().ThrowAsync<InvalidOperationException>();
        }
        else
        {
            var review = (await commands.ExecuteAsync(command, RequestOrigin.ActivatedVoice, () => source, f.Token)).RequestReview!;
            if (fault is "expiry") { f.Time.Now = review.ExpiresAt; }
            if (fault is "source") { source = false; }
            if (fault is "control") { composed.Access.ControlRevision++; }
            if (fault is "retire") { commands.Retire(); }
            var confirm = HostedConfirm(review);
            if (fault is "target") { confirm = confirm with { SessionId = Guid.NewGuid() }; }
            await commands.Invoking(value => value.ExecuteAsync(confirm, RequestOrigin.LocalUi, () => true, f.Token))
                .Should().ThrowAsync<InvalidOperationException>();
            source = true;
            await commands.Invoking(value => value.ExecuteAsync(HostedConfirm(review), RequestOrigin.LocalUi, () => true, f.Token))
                .Should().ThrowAsync<InvalidOperationException>();
        }
        f.Count("conversation_messages").Should().Be(0);
        runtime.Request.Should().BeNull();
        runtime.Sends.Should().Be(0);
        composed.Turns.IsQuiescent.Should().BeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Hosted_request_cancel_discards_review_or_joins_pending_disclosure_without_send(bool running)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        var runtime = new CopilotConversationRuntime();
        await using var composed = new Composition(f, copilot: runtime);
        var commands = new SessionRequestCommandService(composed.Workspace, composed.Conversation, composed.Access, f.Time);
        var target = await f.Store.ReadMetadataAsync(f.Request.SessionId, f.Token);
        var review = (await commands.ExecuteAsync(HostedReview(target, "original hosted request"),
            RequestOrigin.ActivatedVoice, () => true, f.Token)).RequestReview!;
        if (!running)
        {
            (await commands.ExecuteAsync(HostedConfirm(review) with { Operation = SessionCommandOperation.RequestCancel },
                RequestOrigin.LocalUi, () => true, f.Token)).Outcome.Should().Be("cancelled");
            f.Count("conversation_messages").Should().Be(0);
        }
        else
        {
            var offered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var retired = false;
            composed.Reviews.Offered += _ => offered.SetResult();
            composed.Reviews.Retired += _ => retired = true;
            var submission = commands.ExecuteAsync(HostedConfirm(review), RequestOrigin.LocalUi, () => true, f.Token);
            await offered.Task.WaitAsync(f.Token);
            submission.IsCompleted.Should().BeFalse();
            var status = await composed.Conversation.ReadRunningConversationAsync(target, () => true, f.Token);
            await composed.Conversation.CancelRunningConversationAsync(status!.Target, () => true, f.Token);
            var receipt = (await submission).RequestSubmission!;
            receipt.Message.Outcome.Should().Be(ModelTurnOutcome.Cancelled);
            receipt.Message.AssistantResponse.Should().BeNull();
            receipt.Message.Request.Origin.Should().Be(RequestOrigin.ActivatedVoice);
            retired.Should().BeTrue();
        }
        await commands.Invoking(value => value.ExecuteAsync(HostedConfirm(review), RequestOrigin.LocalUi, () => true, f.Token))
            .Should().ThrowAsync<InvalidOperationException>();
        runtime.Sends.Should().Be(0);
        composed.Turns.IsQuiescent.Should().BeTrue();
    }

    private static SessionCommand HostedReview(Kora.Core.Storage.SessionWorkspaceEntry target, string body) =>
        SessionCommand.Parse("session request review " + target.Authority.SessionId.Value.ToString("D")
            + " " + target.Authority.Generation.Value + " " + (target.Metadata?.Revision.Value ?? 0)
            + " copilot -- " + body, "Kora")!;

    private static SessionCommand HostedConfirm(SessionRequestReview review) =>
        new(SessionCommandOperation.RequestConfirm, review.Target.Authority.SessionId.Value) { RequestReviewId = review.Id };

    private sealed class Composition : IAsyncDisposable
    {
        private readonly AudioControlAdmission admission;
        internal WindowsSqliteSessionWorkspaceTests.Access Access { get; } = new();
        internal SessionWorkspaceService Workspace { get; }
        internal SessionConversationService Conversation { get; }
        internal ModelTurnHost Turns { get; }
        internal HostedTransmissionReviewService Reviews { get; }
        internal CopilotCatalogue Catalogue { get; }

        internal Composition(InteractionStorageFixture f, CopilotConversationRuntime copilot)
        {
            Workspace = WindowsSqliteSessionWorkspaceTests.Service(f, Access);
            admission = new(f.Store, f.Store, new(f.Tasks));
            var audit = new AuditLog();
            new LocalModelProviderModePreferences(f.Paths).Save(ModelProviderMode.HostedPreferred);
            new LocalProviderProfilePreferences(f.Paths).Save(new(ProviderService.Copilot, [CopilotCatalogue.Profile]));
            var mode = new ProviderModeConfigurationService(new LocalModelProviderModePreferences(f.Paths), admission, audit);
            mode.Observe();
            Catalogue = new(f);
            var profiles = new ProviderProfileConfigurationService(new LocalProviderProfilePreferences(f.Paths),
                Catalogue, admission, audit, f.Time);
            var dependencies = new DependencyBootstrapper([], NullLogger<DependencyBootstrapper>.Instance);
            var boundary = new HostBoundary();
            var runtimes = new Kora.Tools.Runtime.RecordedRuntimeObservation(dependencies);
            var registry = new ReadOnlyCapabilityRegistry(boundary, new(), new(), new(boundary), new(dependencies),
                new(runtimes), new(runtimes), NullLogger<ReadOnlyCapabilityRegistry>.Instance);
            Reviews = new(audit, f.Time);
            Turns = new(f.Store, Access, boundary, registry, audit, NullLogger<ModelTurnHost>.Instance, f.Time,
                [new(ModelProviderSelection.CopilotCandidate,
                    ModelQualificationGate.DotNetFinalRequest | ModelQualificationGate.AllPathLifecycle
                        | ModelQualificationGate.ExecutionAccount | ModelQualificationGate.IntegratedHost,
                    f.Time.Now.AddMinutes(5), new CopilotModelTurnAdapter(copilot, Reviews))],
                mode, profiles, f.Store, hostedReviews: Reviews);
            Conversation = new(f.Store, f.Store, Workspace, Access, Turns, f.Time,
                NullLogger<SessionConversationService>.Instance, execution: Turns, cancellationAudit: audit, interactions: f.Store);
        }

        public async ValueTask DisposeAsync()
        {
            await Turns.DisposeAsync();
            await admission.DisposeAsync();
        }
    }

    private sealed class CopilotConversationRuntime : ICopilotTurnRuntime
    {
        public bool IsQuiescent { get; private set; } = true;
        internal HostRequest? Request { get; private set; }
        internal int Sends { get; private set; }
        internal int RequestedSends { get; init; } = 1;
        internal bool HoldCompletion { get; init; }
        internal TaskCompletionSource Sent { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<CopilotTurnReply> RunAsync(ModelTurnProvenance provenance, ProviderProfileTuple profile,
            ReadOnlyMemory<byte> context, Func<string, string, CancellationToken, ValueTask<CapabilityReply>> invokeTool,
            Func<CopilotTransmission, CancellationToken, ValueTask<bool>> admitTransmission, CancellationToken token)
        {
            IsQuiescent = false;
            try
            {
                Request = provenance.Request;
                for (var i = 0; i < RequestedSends; i++)
                {
                    if (!await admitTransmission(new(provenance, profile, new Uri("https://api.githubcopilot.com/chat/completions"),
                        context), token)) { return new(ModelTurnOutcome.DeniedEgress); }
                    Sends++;
                }
                Sent.TrySetResult();
                if (HoldCompletion) { await Release.Task; }
                return new(ModelTurnOutcome.Succeeded, "genuine hosted boundary answer");
            }
            finally { IsQuiescent = true; }
        }
    }

    private sealed class CopilotCatalogue(InteractionStorageFixture fixture) : IProviderCapabilityCatalogue
    {
        internal static ProviderProfileTuple Profile { get; } = new(ProviderService.Copilot, new("explicit-account"), new(1),
            new("model-1"), new("default"), new("default"));
        internal bool ChangedAccount { get; set; }
        internal bool ChangedModel { get; set; }
        public Task<ProviderCatalogueObservation> ObserveAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new ProviderCatalogueObservation(LocalProviderInstallation.Missing,
                Enum.GetValues<ProviderService>().Where(service => service != ProviderService.Unset).Select(service =>
                    new ProviderCapabilityCatalogue(service, service == ProviderService.Copilot
                        ? ProviderCatalogueStatus.Current : ProviderCatalogueStatus.Unavailable,
                        service == ProviderService.Copilot ? ChangedAccount ? new("changed-account") : Profile.Account : null, new(1), fixture.Time.Now,
                        fixture.Time.Now.AddMinutes(5), service == ProviderService.Copilot
                            ? [new(ChangedModel ? new("changed-model") : Profile.Model, "Test model", ModelProviderSelection.CopilotCandidate,
                                [new(Profile.Reasoning, "Default", true)], [new(Profile.Context, "Default", true)])] : [])).ToArray()));
    }

    private sealed class HostBoundary : ICapabilityHostAccess, IApplicationInfo
    {
        public bool IsCurrentHost => true;
        public string Version => "1.0.0";
    }

    private sealed class AuditLog : ISecurityAuditLog
    {
        public void Write(SecurityAuditEvent auditEvent) =>
            HostActivity.RequireCurrent().Request.Origin.Should().BeOneOf(RequestOrigin.LocalUi, RequestOrigin.ActivatedVoice);
    }
}
