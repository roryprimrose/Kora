using System.Text.Json;

using AwesomeAssertions;

using Kora.Application.Dependencies;
using Kora.Application.Hosting;
using Kora.Core.Commands;
using Kora.Core.Communication;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Storage;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Fact]
    public void HostedRequestGuideIsEmbeddedWithExplicitReviewAndNativeSendBoundary()
    {
        var guide = new Kora.Application.Documentation.EmbeddedUserDocumentationProvider().GetPages()
            .Single(page => string.Equals(page.Id, "hosted-requests", StringComparison.Ordinal)).Markdown;
        guide.Should().Contain("session request review <session-id> <generation> <metadata-revision> copilot -- <original body>")
            .And.Contain("60 seconds").And.Contain("original channel")
            .And.Contain("Transmit this exact request once").And.Contain("no automatic retry")
            .And.Contain("Local-only").And.Contain("unavailable");
    }

    [Theory]
    [InlineData("provider")]
    [InlineData("tools-provider")]
    [InlineData("tools-invalid")]
    public async Task UndefinedRequestProviderOrScopeIsRejectedBeforeRetentionOrAnyModelAdmission(string fault)
    {
        var fixture = await Fixture.CreateInitializedAsync();
        var store = new SessionCommandStore(fixture);
        var workspace = new SessionWorkspaceService(store, new(fixture.HostStore), store, NullLogger<SessionWorkspaceService>.Instance);
        var admission = new RequestAdmission();
        var conversation = new SessionConversationService(store, store, workspace, store, admission,
            TimeProvider.System, NullLogger<SessionConversationService>.Instance);
        var commands = new SessionRequestCommandService(workspace, conversation, store, TimeProvider.System);
        var command = new SessionCommand(SessionCommandOperation.RequestReview, store.Session.Authority.SessionId.Value, 1, 0)
        {
            RequestBody = "original",
            RequestProvider = fault is "provider" ? (SessionRequestReview.ProviderChoice)99
                : fault is "tools-invalid" ? SessionRequestReview.ProviderChoice.Claude : SessionRequestReview.ProviderChoice.Default,
            RequestClaudeTools = fault is "provider" ? [] : ["unknown"],
        };
        if (fault is "tools-invalid")
        {
            await commands.Invoking(value => value.ExecuteAsync(command, RequestOrigin.LocalUi, () => true,
                TestContext.Current.CancellationToken)).Should().ThrowAsync<InvalidDataException>();
        }
        else
        {
            await commands.Invoking(value => value.ExecuteAsync(command, RequestOrigin.LocalUi, () => true,
                TestContext.Current.CancellationToken)).Should().ThrowAsync<InvalidOperationException>();
        }
        store.RequestRetentions.Should().Be(0);
        admission.DefaultCalls.Should().Be(0);
        admission.CopilotCalls.Should().Be(0);
    }

    [Fact]
    public async Task ExplicitCopilotWithoutDedicatedAdmissionNeverFallsBackToDefault()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        var store = BindRequestCommands(fixture, new DefaultOnlyRequestAdmission());
        var session = store.Session.Authority.SessionId.Value.ToString("D");
        await fixture.RunAsync("session request review " + session + " 1 0 copilot -- original hosted request");
        using var result = JsonDocument.Parse(fixture.ViewModel.ResponseBody);
        var review = result.RootElement.GetProperty("requestReview").GetProperty("id").GetString();
        await fixture.RunAsync("session request confirm " + session + " " + review);
        store.RequestMessage!.Outcome.Should().Be(ModelTurnOutcome.Unavailable);
        store.RequestMessage.Reason.Should().Be(ModelTurnReason.GenerationNotEnabled);
        store.RequestRetentions.Should().Be(1);
        fixture.Reasoner.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public async Task ExplicitTypedAndCurrentNameVoiceRequestConfirmRetainsOriginalBodyAndOriginWithoutReasoning(bool voice, bool voiceConfirmation)
    {
        var fixture = await Fixture.CreateInitializedAsync();
        var store = BindRequestCommands(fixture);
        const string body = "  Original CaSe e\u0301🙂\r\n\tend  ";
        var command = "session request review " + store.Session.Authority.SessionId.Value.ToString("D") + " 1 0 -- " + body;
        if (voice) { await fixture.RaiseActivatedTranscriptAsync("Kora, " + command, 1); }
        else { await fixture.RunAsync(command); }
        fixture.ViewModel.ResponseTitle.Should().Be("Session command review.");
        using var result = JsonDocument.Parse(fixture.ViewModel.ResponseBody);
        var review = result.RootElement.GetProperty("requestReview");
        review.GetProperty("body").GetString().Should().Be(body);
        var confirm = "session request confirm " + store.Session.Authority.SessionId.Value.ToString("D")
            + " " + review.GetProperty("id").GetString();
        // Native confirmation of voice review must still retain ActivatedVoice.
        if (voiceConfirmation) { await fixture.RaiseActivatedTranscriptAsync("Kora, " + confirm, 1); }
        else { await fixture.RunAsync(confirm); }
        fixture.ViewModel.ResponseTitle.Should().Be("Session command retained.");
        store.RequestMessage!.Body.Should().Be(body);
        store.RequestMessage.Request.Origin.Should().Be(voice ? RequestOrigin.ActivatedVoice : RequestOrigin.LocalUi);
        fixture.Reasoner.Requests.Should().BeEmpty();
        await fixture.RunAsync(confirm);
        fixture.ViewModel.ResponseTitle.Should().Be("Session command not confirmed.");
        store.RequestRetentions.Should().Be(1);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ExplicitCopilotRequestPinsProviderAndOriginalChannelAcrossConfirmation(bool voice, bool voiceConfirmation)
    {
        var fixture = await Fixture.CreateInitializedAsync();
        var admission = new RequestAdmission();
        var store = BindRequestCommands(fixture, admission);
        var session = store.Session.Authority.SessionId.Value.ToString("D");
        const string body = "  Original hosted request\r\n\tend  ";
        var command = "session request review " + session + " 1 0 copilot -- " + body;
        if (voice) { await fixture.RaiseActivatedTranscriptAsync("Kora, " + command, 1); }
        else { await fixture.RunAsync(command); }
        using var result = JsonDocument.Parse(fixture.ViewModel.ResponseBody);
        var review = result.RootElement.GetProperty("requestReview");
        review.GetProperty("provider").GetString().Should().Be("Copilot");
        review.GetProperty("body").GetString().Should().Be(body);
        store.RequestRetentions.Should().Be(0);
        admission.CopilotCalls.Should().Be(0);
        var confirm = "session request confirm " + session + " " + review.GetProperty("id").GetString();
        if (voiceConfirmation) { await fixture.RaiseActivatedTranscriptAsync("Kora, " + confirm, 1); }
        else { await fixture.RunAsync(confirm); }
        fixture.ViewModel.ResponseTitle.Should().Be("Session command retained.");
        store.RequestMessage!.Body.Should().Be(body);
        store.RequestMessage.Request.Origin.Should().Be(voice ? RequestOrigin.ActivatedVoice : RequestOrigin.LocalUi);
        store.RequestMessage.Outcome.Should().Be(ModelTurnOutcome.Unavailable);
        admission.Context!.Request.Should().Be(store.RequestMessage.Request);
        admission.Context.UserRequest.Should().Be(body);
        admission.CopilotCalls.Should().Be(1);
        admission.DefaultCalls.Should().Be(0);
        fixture.Reasoner.Requests.Should().BeEmpty();
        await fixture.RunAsync(confirm);
        fixture.ViewModel.ResponseTitle.Should().Be("Session command not confirmed.");
        admission.CopilotCalls.Should().Be(1);
        store.RequestRetentions.Should().Be(1);
    }

    [Theory]
    [InlineData("name")]
    [InlineData("consent")]
    [InlineData("call")]
    [InlineData("privacy")]
    [InlineData("revoked")]
    public async Task OriginalVoiceCopilotReviewCannotGainFreshAuthorityFromTypedConfirmation(string change)
    {
        var fixture = await Fixture.CreateInitializedAsync();
        var admission = new RequestAdmission();
        var store = BindRequestCommands(fixture, admission);
        var session = store.Session.Authority.SessionId.Value.ToString("D");
        await fixture.RaiseActivatedTranscriptAsync("Kora, session request review " + session
            + " 1 0 copilot -- original hosted request", 1);
        using var result = JsonDocument.Parse(fixture.ViewModel.ResponseBody);
        var review = result.RootElement.GetProperty("requestReview").GetProperty("id").GetString();
        if (change is "name")
        {
            fixture.ViewModel.AssistantNameInput = "Nova";
            await fixture.ViewModel.ApplyAssistantNameCommand.ExecuteAsync();
        }
        if (change is "consent") { await fixture.ViewModel.SetVoiceConsentAsync(false); }
        if (change is "call")
        {
            fixture.CallState.SetState(CallState.Active);
            await fixture.Dispatcher.LastInvocation;
        }
        if (change is "privacy")
        {
            fixture.ViewModel.CloseForObservedPrivacyEvent("locked", true);
            await fixture.ViewModel.PrivacyClosureTask;
        }
        if (change is "revoked") { fixture.ViewModel.RevokeSessionPresentation(store.Session.Authority.SessionId); }
        await fixture.RunAsync("session request confirm " + session + " " + review);
        store.RequestRetentions.Should().Be(0);
        admission.CopilotCalls.Should().Be(0);
        admission.DefaultCalls.Should().Be(0);
        fixture.Reasoner.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData("unprefixed")]
    [InlineData("protected-call")]
    [InlineData("unbound")]
    [InlineData("unprefixed-unbound")]
    [InlineData("invalid")]
    public async Task VoiceRequestNamespaceNeverFallsIntoModelOrQuestionWhenUnavailable(string reason)
    {
        var fixture = await Fixture.CreateInitializedAsync();
        var store = BindRequestCommands(fixture);
        if (reason is "unbound" or "unprefixed-unbound") { BindSessions(fixture); }
        if (reason is "protected-call")
        {
            fixture.CallState.SetState(CallState.Active);
            await fixture.Dispatcher.LastInvocation;
        }
        var command = "session request review " + store.Session.Authority.SessionId.Value.ToString("D") + " 1 0 -- original";
        if (reason is "invalid") { command = "session request review selected -- original"; }
        await fixture.RaiseActivatedTranscriptAsync((reason is "unprefixed" or "unprefixed-unbound" ? "" : "Kora, ") + command, 1);
        fixture.ViewModel.ResponseTitle.Should().NotBe("Session command review.");
        fixture.Reasoner.Requests.Should().BeEmpty();
        store.RequestRetentions.Should().Be(0);
    }

    [Theory]
    [InlineData("name")]
    [InlineData("privacy-review")]
    [InlineData("privacy-command")]
    [InlineData("privacy-unrelated")]
    [InlineData("privacy-unbound")]
    [InlineData("dispose")]
    [InlineData("revoked")]
    public async Task HostTransitionsRetireRequestReviewAndPrivateCandidateWithoutRevivingOrigin(string transition)
    {
        var fixture = await Fixture.CreateInitializedAsync();
        var store = BindRequestCommands(fixture);
        var session = store.Session.Authority.SessionId.Value.ToString("D");
        await fixture.RunAsync("session request review " + session + " 1 0 -- original");
        using var result = JsonDocument.Parse(fixture.ViewModel.ResponseBody);
        var review = result.RootElement.GetProperty("requestReview").GetProperty("id").GetString();
        if (transition is "name")
        {
            fixture.ViewModel.AssistantNameInput = "Nova";
            await fixture.ViewModel.ApplyAssistantNameCommand.ExecuteAsync();
        }
        if (transition.StartsWith("privacy", StringComparison.Ordinal))
        {
            if (transition is "privacy-unrelated") { fixture.ViewModel.CommandText = "ordinary text"; }
            if (transition is "privacy-command") { fixture.ViewModel.CommandText = "session help"; }
            if (transition is "privacy-unbound") { BindSessions(fixture); }
            fixture.ViewModel.CloseForObservedPrivacyEvent("locked", true);
            await fixture.ViewModel.PrivacyClosureTask;
            if (transition is "privacy-review") { fixture.ViewModel.CommandText.Should().BeEmpty(); }
        }
        else if (transition is "dispose") { fixture.ViewModel.Dispose(); }
        else if (transition is "revoked") { fixture.ViewModel.RevokeSessionPresentation(store.Session.Authority.SessionId); }
        if (transition is "name" or "revoked")
        {
            await fixture.RunAsync("session request confirm " + session + " " + review);
            fixture.ViewModel.ResponseTitle.Should().Be("Session command not confirmed.");
        }
        store.RequestRetentions.Should().Be(0);
    }

    [Fact]
    public async Task PendingQuestionBlocksExplicitRequestWithoutStealingAnswerOrAuthority()
    {
        var fixture = new Fixture();
        fixture.Probe.Status = new("local.inference", "Local", DependencyReadiness.Ready, "Ready");
        fixture.Reasoner.Question = new("Which target?", ["One", "Two"]);
        await fixture.ViewModel.InitializeAsync();
        var store = BindRequestCommands(fixture);
        await fixture.RunAsync("choose");
        await fixture.ViewModel.ActiveReasoningTask!;
        await fixture.RunAsync("session request review " + store.Session.Authority.SessionId.Value.ToString("D") + " 1 0 -- One");
        fixture.ViewModel.IsModelQuestionPending.Should().BeTrue();
        fixture.ViewModel.ResponseTitle.Should().Be("Session command blocked.");
        store.RequestRetentions.Should().Be(0);
    }

    private static SessionCommandStore BindRequestCommands(Fixture fixture, IModelTurnAdmission? admission = null)
    {
        var store = new SessionCommandStore(fixture);
        var workspace = new SessionWorkspaceService(store, new(fixture.HostStore), store, NullLogger<SessionWorkspaceService>.Instance);
        var conversation = new SessionConversationService(store, store, workspace, store, admission ?? new RequestAdmission(),
            TimeProvider.System, NullLogger<SessionConversationService>.Instance);
        fixture.ViewModel.BindSessionCommands(workspace, new(workspace, conversation, store, TimeProvider.System));
        return store;
    }

    private sealed class RequestAdmission : IModelTurnAdmission, ICopilotTurnAdmission
    {
        public bool IsQuiescent => true;
        internal int DefaultCalls { get; private set; }
        internal int CopilotCalls { get; private set; }
        internal ModelContextEnvelope? Context { get; private set; }
        public Task<ModelTurnAdmission> AdmitAsync(ModelTurnChoice choice, ModelContextEnvelope context,
            ModelHandoffReview? review, CancellationToken token)
        {
            DefaultCalls++;
            return Task.FromResult(new ModelTurnAdmission(new(ModelTurnOutcome.Unavailable, ModelTurnReason.UnregisteredModel)));
        }
        public Task<ModelTurnAdmission> AdmitCopilotAsync(ModelContextEnvelope context, CancellationToken token)
        {
            CopilotCalls++;
            Context = context;
            return Task.FromResult(new ModelTurnAdmission(new(ModelTurnOutcome.Unavailable, ModelTurnReason.GenerationNotEnabled)));
        }
    }

    private sealed class DefaultOnlyRequestAdmission : IModelTurnAdmission
    {
        public bool IsQuiescent => true;
        public Task<ModelTurnAdmission> AdmitAsync(ModelTurnChoice choice, ModelContextEnvelope context,
            ModelHandoffReview? review, CancellationToken token) =>
            throw new InvalidOperationException("Explicit Copilot must never fall back to ordinary admission.");
    }

    private sealed partial class SessionCommandStore : ISessionConversationStore
    {
        internal ConversationMessage? RequestMessage { get; set; }
        internal int RequestRetentions { get; private set; }
        public ValueTask<ConversationSubmissionReceipt> RetainConversationMessageAsync(HostRequest request, HostRevision generation,
            long expectedMetadataRevision, string body, Func<bool> admitted, CancellationToken token)
        {
            admitted().Should().BeTrue();
            RequestRetentions++;
            RequestMessage = new(request, generation, 1, 1, DateTimeOffset.UnixEpoch, body, null, null);
            var task = new HostTaskRecord(request, new(1), HostTaskState.IntentRecorded);
            TaskResult = new(task, generation, "request-command-test", true, null);
            return ValueTask.FromResult(new ConversationSubmissionReceipt(RequestMessage, task));
        }
        public ValueTask<ConversationSubmissionReceipt> CompleteConversationSubmissionAsync(HostRequest request,
            ModelTurnOutcome outcome, ModelTurnReason reason, Func<bool> admitted, CancellationToken token)
        {
            admitted().Should().BeTrue();
            RequestMessage = RequestMessage! with { Revision = 2, Outcome = outcome, Reason = reason };
            TaskResult = TaskResult! with { Task = TaskResult.Task.Next(RequestMessage.ExpectedTaskState) };
            return ValueTask.FromResult(new ConversationSubmissionReceipt(RequestMessage, TaskResult.Task));
        }
        public ValueTask<HostTaskRecord> RecordConversationCredentialRejectionAsync(HostRequest request, HostRevision generation,
            Func<bool> admitted, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<ConversationDraftSnapshot> ReadConversationDraftAsync(HostId<SessionIdentity> session, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<ConversationDraftSnapshot> CommitConversationDraftAsync(HostRequest control, ConversationDraftSnapshot expected,
            string? body, Func<bool> admitted, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<ConversationPage> ReadConversationPageAsync(HostId<SessionIdentity> session, ConversationCursor? cursor,
            int limit, CancellationToken token) => ReadSearchPage(session, cursor, limit, token);
        public ValueTask<ConversationMessage> ReadConversationMessageAsync(HostId<SessionIdentity> session,
            HostId<RequestIdentity> request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            BeforeSpeechExactRead?.Invoke();
            var message = RequestMessage ?? throw new InvalidOperationException("The exact original request is not retained.");
            if (message.Request.SessionId != session || message.Request.RequestId != request)
            { throw new InvalidOperationException("The retained source does not match the requested session and request."); }
            return ValueTask.FromResult(message);
        }
    }
}
