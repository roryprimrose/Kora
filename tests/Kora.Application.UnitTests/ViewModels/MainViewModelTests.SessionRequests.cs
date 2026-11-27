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
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BareNameRouteWithoutTrustedWakeDoesNotCreateAddressedReview(bool voice)
    {
        var fixture = await Fixture.CreateInitializedAsync();
        var store = BindRequestCommands(fixture);
        var routes = 0;
        fixture.ViewModel.BindSessionRouting((body, origin, _) =>
        {
            body.Should().Be("in Build plan, original");
            origin.Should().Be(voice ? RequestOrigin.ActivatedVoice : RequestOrigin.LocalUi);
            routes++;
            return Task.FromResult<Kora.Application.Hosting.SessionRouting.SessionRoutingReceipt?>(null);
        });
        if (voice) { await fixture.RaiseActivatedTranscriptAsync("in Build plan, original", 1); }
        else { await fixture.RunAsync("in Build plan, original"); }
        fixture.ViewModel.ResponseTitle.Should().NotBe("Review conversation request.");
        store.RequestRetentions.Should().Be(0);
        routes.Should().Be(voice ? 0 : 1);
    }

    [Theory]
    [InlineData(false, ", ")]
    [InlineData(true, ", ")]
    [InlineData(true, " ")]
    [InlineData(false, ",")]
    public async Task SpokenNameRouteReviewsAndConfirmsLongestUniqueActiveNameWithOriginalChannel(bool voice, string separator)
    {
        var fixture = await Fixture.CreateInitializedAsync();
        var store = BindRequestCommands(fixture);
        await store.RenameSessionAsync(new(new(Guid.NewGuid()), store.Session.Authority.SessionId, new(Guid.NewGuid()), RequestOrigin.LocalUi), store.Session.Authority.Generation, 0,
            new("Build plan"), () => true, TestContext.Current.CancellationToken);
        var shorter = store.Session with
        {
            Authority = store.Session.Authority with { SessionId = new(Guid.NewGuid()) },
            Metadata = new(store.Session.Authority.SessionId, new(1), new("Build"))
        };
        var cursor = Guid.NewGuid();
        store.ClaudePageRead = (after, _) => after is null
            ? new([shorter, shorter with { Authority = shorter.Authority with { SessionId = new(Guid.NewGuid()) } }], cursor)
            : new([store.Session], null);
        var body = string.Equals(separator, ",", StringComparison.Ordinal)
            ? "OrIgInAl e\u0301\r\nbody -- data  " : "  OrIgInAl e\u0301\r\nbody -- data  ";
        var input = "Kora, in Build plan" + separator + body;
        if (voice) { await fixture.RaiseActivatedTranscriptAsync(input, 1); }
        else { await fixture.RunAsync(input); }
        fixture.ViewModel.ResponseTitle.Should().Be("Review conversation request.");
        fixture.ViewModel.ResponseBody.Should().Contain("Conversation: Build plan").And.Contain(body.Trim())
            .And.Contain(voice ? "ActivatedVoice" : "LocalUi");
        store.RequestRetentions.Should().Be(0);
        await fixture.RunAsync("session request confirm");
        store.RequestMessage!.Body.Should().Be(body);
        store.RequestMessage.Request.Origin.Should().Be(voice ? RequestOrigin.ActivatedVoice : RequestOrigin.LocalUi);
        store.RequestMessage.Request.SessionId.Should().Be(store.Session.Authority.SessionId);
        store.RequestRetentions.Should().Be(1);
        await fixture.RunAsync("session request confirm");
        store.RequestRetentions.Should().Be(1);
    }

    [Theory]
    [InlineData("ambiguous")]
    [InlineData("missing")]
    [InlineData("inactive")]
    [InlineData("null-name")]
    [InlineData("prefix-only")]
    [InlineData("case")]
    [InlineData("empty-body")]
    [InlineData("name-only")]
    [InlineData("overflow")]
    [InlineData("late")]
    public async Task SpokenNameRouteRefusesWithoutFallbackOrRetention(string fault)
    {
        var fixture = await Fixture.CreateInitializedAsync();
        var admission = new RequestAdmission();
        var store = BindRequestCommands(fixture, admission);
        await store.RenameSessionAsync(new(new(Guid.NewGuid()), store.Session.Authority.SessionId, new(Guid.NewGuid()), RequestOrigin.LocalUi), store.Session.Authority.Generation, 0,
            new("Build"), () => true, TestContext.Current.CancellationToken);
        store.ClaudePageRead = (_, _) =>
        {
            if (fault is "late") { fixture.ViewModel.BindSessionCommands(new(store, new(fixture.HostStore), store, NullLogger<SessionWorkspaceService>.Instance)); }
            return fault switch
            {
                "ambiguous" => new([store.Session, store.Session with { Authority = store.Session.Authority with { SessionId = new(Guid.NewGuid()) } }], null),
                "missing" => new([], null),
                "inactive" => new([store.Session with { Authority = store.Session.Authority with { IsActive = false } }], null),
                "null-name" => new([store.Session with { Metadata = null }], null),
                "overflow" => new([], Guid.NewGuid()),
                _ => new([store.Session], null)
            };
        };
        var input = fault switch
        {
            "prefix-only" => "Kora, in Building, body",
            "case" => "Kora, in build, body",
            "empty-body" => "Kora, in Build, ",
            "name-only" => "Kora, in Build",
            _ => "Kora, in Build, body"
        };
        await fixture.RaiseActivatedTranscriptAsync(input, 1);
        fixture.ViewModel.ResponseTitle.Should().NotBe("Review conversation request.");
        store.RequestRetentions.Should().Be(0);
        admission.DefaultCalls.Should().Be(0);
        admission.CopilotCalls.Should().Be(0);
        await fixture.RunAsync("session request confirm");
        store.RequestRetentions.Should().Be(0);
    }

    [Theory]
    [InlineData("metadata")]
    [InlineData("name")]
    [InlineData("consent")]
    [InlineData("cancel")]
    public async Task NamedVoiceReviewDoesNotRenewAuthorityAtConfirmation(string change)
    {
        var fixture = await Fixture.CreateInitializedAsync();
        var store = BindRequestCommands(fixture);
        await store.RenameSessionAsync(new(new(Guid.NewGuid()), store.Session.Authority.SessionId, new(Guid.NewGuid()), RequestOrigin.LocalUi), store.Session.Authority.Generation, 0,
            new("Build"), () => true, TestContext.Current.CancellationToken);
        await fixture.RaiseActivatedTranscriptAsync("Kora, in Build, original", 1);
        fixture.ViewModel.ResponseTitle.Should().Be("Review conversation request.");
        if (change is "metadata")
        {
            await store.RenameSessionAsync(new(new(Guid.NewGuid()), store.Session.Authority.SessionId, new(Guid.NewGuid()), RequestOrigin.LocalUi), store.Session.Authority.Generation, 1,
                new("Changed"), () => true, TestContext.Current.CancellationToken);
        }
        if (change is "name")
        {
            fixture.ViewModel.AssistantNameInput = "Nova";
            await fixture.ViewModel.ApplyAssistantNameCommand.ExecuteAsync();
        }
        if (change is "consent") { await fixture.ViewModel.SetVoiceConsentAsync(false); }
        if (change is "cancel") { await fixture.RunAsync("session request cancel"); }
        await fixture.RunAsync("session request confirm");
        store.RequestRetentions.Should().Be(0);
    }

    [Fact]
    public async Task PendingQuestionBlocksExplicitRequestWithoutStealingAnswerOrAuthority()
    {
        var fixture = new Fixture();
        await using var pendingDetails = new PendingDetailsQuestion(fixture);
        fixture.Probe.Status = new("local.inference", "Local", DependencyReadiness.Ready, "Ready");
        await fixture.ViewModel.InitializeAsync();
        var store = BindRequestCommands(fixture);
        await pendingDetails.PresentAsync();
        var title = fixture.ViewModel.ResponseTitle;
        var question = fixture.ViewModel.CurrentDetailsOffer;
        await fixture.RunAsync("session request review " + store.Session.Authority.SessionId.Value.ToString("D") + " 1 0 -- One");
        fixture.ViewModel.HasDetailsQuestion.Should().BeTrue();
        fixture.ViewModel.ResponseTitle.Should().Be(title);
        fixture.ViewModel.CurrentDetailsOffer.Should().BeSameAs(question);
        store.RequestRetentions.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OriginalProviderSettingsQuestionSelectsOnlyTheExplicitLocalTools(bool voice)
    {
        var fixture = await Fixture.CreateInitializedAsync();
        var admission = new RequestAdmission();
        var store = BindRequestCommands(fixture, admission);
        const string body = "ask about AI service settings: Which Claude models are available?";
        var session = store.Session.Authority.SessionId.Value.ToString("D");
        var reviewCommand = "session request review " + session + " 1 0 -- " + body;
        if (voice) { await fixture.RaiseActivatedTranscriptAsync("Kora, " + reviewCommand, 1); }
        else { await fixture.RunAsync(reviewCommand); }
        using var review = JsonDocument.Parse(fixture.ViewModel.ResponseBody);
        var id = review.RootElement.GetProperty("requestReview").GetProperty("id").GetString();
        var confirm = "session request confirm " + session + " " + id;
        if (voice) { await fixture.RaiseActivatedTranscriptAsync("Kora, " + confirm, 1); }
        else { await fixture.RunAsync(confirm); }
        admission.Context!.ToolMode.Should().Be(ModelToolMode.ProviderSettings);
        admission.Choice.Should().Be(ModelTurnChoice.Local);
        admission.Context.Tools.Select(tool => tool.Id).Should().Equal(Kora.Core.Tools.ProviderSettingsTools.Descriptors.Select(tool => tool.Id));
        admission.Context.SystemPolicy.Should().Contain("Proposals never save settings");
        admission.CopilotCalls.Should().Be(0);
        store.RequestMessage!.Outcome.Should().Be(ModelTurnOutcome.Unavailable);

    }

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
        internal ModelTurnChoice? Choice { get; private set; }
        public Task<ModelTurnAdmission> AdmitAsync(ModelTurnChoice choice, ModelContextEnvelope context,
            ModelHandoffReview? review, CancellationToken token)
        {
            DefaultCalls++;
            Context = context;
            Choice = choice;
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
