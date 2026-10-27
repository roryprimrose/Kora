using AwesomeAssertions;

using Kora.Application.Dependencies;
using Kora.Application.Hosting;
using Kora.Core.Commands;
using Kora.Core.Diagnostics;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Providers;
using Kora.Core.Storage;

namespace Kora.Application.UnitTests.Dependencies;

public sealed partial class ModelTurnHostTests
{
    [Theory]
    [InlineData(RequestOrigin.LocalUi)]
    [InlineData(RequestOrigin.ActivatedVoice)]
    public async Task FailedSourceReviewBindsOriginalInputTaskProviderAndChannelThroughCanonicalNewTurnAndDeniesReplay(RequestOrigin origin)
    {
        using var f = new Fixture(origin) { TrackPendingRecords = true };
        var source = SeedFailedRetry(f);
        f.ProfileCatalogue.Status = ProviderCatalogueStatus.Current;
        f.ProfilePreferences.Value = new(ProviderService.Local, [Profile()]);
        var host = f.ConfiguredProfiles();
        await using var conversation = f.ConversationService(host, execution: host);
        var commands = conversation.CreateRequestCommands();
        var result = await commands.ExecuteAsync(FailedReview(source), origin, () => true, Token);
        var review = result.RequestReview!;
        review.FailedSource!.Message.Should().Be(source);
        review.FailedSource.Provider.Should().Be(source.Execution!.Selection);
        review.RetryProviderProfile.Should().Be(Profile());
        review.Body.Should().Be(source.Body);
        review.Origin.Should().Be(origin);
        f.RetainCalls.Should().Be(0);
        var receipt = (await commands.ExecuteAsync(FailedConfirm(review), RequestOrigin.LocalUi, () => true, Token)).RequestSubmission!;
        receipt.Message.Body.Should().Be(source.Body);
        receipt.Message.AssistantResponse!.Body.Should().NotBeNullOrWhiteSpace();
        receipt.Message.Request.Origin.Should().Be(origin);
        receipt.Message.Request.SessionId.Should().Be(source.Request.SessionId);
        receipt.Message.Request.TaskId.Should().NotBe(source.Request.TaskId);
        f.PendingRecords[source.Request.RequestId].Message.Should().Be(source);
        f.Adapter.Calls.Should().Be(1);
        f.ExecutionStarts.Should().Be(1);
        await commands.Invoking(value => value.ExecuteAsync(FailedConfirm(review), RequestOrigin.LocalUi, () => true, Token))
            .Should().ThrowAsync<InvalidOperationException>();
        f.RetainCalls.Should().Be(1);
        f.Messages.Should().NotContain(message => message.Contains(source.Body, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("body")]
    [InlineData("task")]
    [InlineData("provider")]
    [InlineData("profile")]
    [InlineData("default")]
    [InlineData("account")]
    [InlineData("session")]
    [InlineData("generation")]
    [InlineData("revision")]
    [InlineData("expiry")]
    [InlineData("private")]
    [InlineData("permission")]
    [InlineData("source")]
    [InlineData("retire")]
    [InlineData("deleted-message")]
    [InlineData("deleted-task")]
    public async Task FailedRetryConfirmationRejectsChangedSourceProviderAndAuthorityBeforeAnyNewIntentOrSend(string fault)
    {
        using var f = new Fixture { TrackPendingRecords = true };
        var source = SeedFailedRetry(f);
        f.ProfileCatalogue.Status = ProviderCatalogueStatus.Current;
        f.ProfilePreferences.Value = new(ProviderService.Local, [Profile()]);
        var host = f.ConfiguredProfiles();
        await using var conversation = f.ConversationService(host, execution: host);
        var commands = conversation.CreateRequestCommands();
        var admitted = true;
        var review = (await commands.ExecuteAsync(FailedReview(source), RequestOrigin.LocalUi, () => admitted, Token)).RequestReview!;
        var original = f.PendingRecords[source.Request.RequestId];
        if (fault is "body") { f.PendingRecords[source.Request.RequestId] = original with { Message = source with { Body = "changed" } }; }
        if (fault is "task") { f.PendingRecords[source.Request.RequestId] = original with { Task = new(source.Request, new(4), HostTaskState.Denied) }; }
        if (fault is "provider")
        { f.PendingRecords[source.Request.RequestId] = original with { Message = source with { Execution = source.Execution! with { Selection = ModelProviderSelection.CopilotCandidate } } }; }
        if (fault is "profile") { f.SessionProfileRevision++; }
        if (fault is "default") { f.ProfilePreferences.Value = new(ProviderService.Unset, []); }
        if (fault is "account") { f.ProfileCatalogue.Status = ProviderCatalogueStatus.AuthenticationRequired; }
        if (fault is "session") { f.Session = f.Session with { IsActive = false }; }
        if (fault is "generation") { f.Session = f.Session with { Generation = new(2) }; }
        if (fault is "revision") { f.ControlRevision++; }
        if (fault is "expiry") { f.Time.Now = review.ExpiresAt; }
        if (fault is "private") { f.Current = false; }
        if (fault is "permission") { f.CanControl = false; }
        if (fault is "source") { admitted = false; }
        if (fault is "retire") { commands.Retire(); }
        if (fault is "deleted-message") { f.PendingRecords.TryRemove(source.Request.RequestId, out _); }
        if (fault is "deleted-task")
        { f.BeforeTaskRead = () => { f.PendingRecords.TryRemove(source.Request.RequestId, out _); return Task.CompletedTask; }; }
        await commands.Invoking(value => value.ExecuteAsync(FailedConfirm(review), RequestOrigin.LocalUi, () => true, Token))
            .Should().ThrowAsync<InvalidOperationException>();
        f.RetainCalls.Should().Be(0);
        f.Adapter.Calls.Should().Be(0);
        f.ExecutionStarts.Should().Be(0);
    }

    [Theory]
    [InlineData("source")]
    [InlineData("profile")]
    [InlineData("account")]
    [InlineData("default")]
    public async Task FailedRetryRevalidatesSourceAndProviderAtPhysicalDispatchRatherThanOnlyAtConfirmation(string fault)
    {
        using var f = new Fixture { TrackPendingRecords = true };
        var source = SeedFailedRetry(f);
        var host = ConfigureCancellation(f);
        await using var conversation = f.ConversationService(host, execution: host);
        var commands = conversation.CreateRequestCommands();
        var review = (await commands.ExecuteAsync(FailedReview(source), RequestOrigin.LocalUi, () => true, Token)).RequestReview!;
        f.AfterExecutionStart = () =>
        {
            if (fault is "source")
            { f.PendingRecords[source.Request.RequestId] = f.PendingRecords[source.Request.RequestId] with { Message = source with { Body = "replaced at dispatch" } }; }
            if (fault is "profile") { f.SessionProfileRevision++; }
            if (fault is "account") { f.ProfileCatalogue.Status = ProviderCatalogueStatus.AuthenticationRequired; }
            if (fault is "default") { f.ProfilePreferences.Value = new(ProviderService.Unset, []); }
        };
        var failure = await commands.Invoking(value => value.ExecuteAsync(FailedConfirm(review), RequestOrigin.LocalUi, () => true, Token))
            .Should().ThrowAsync<ConversationSubmissionUnconfirmedException>();
        failure.Which.InnerException.Should().BeOfType<InvalidOperationException>();
        f.Adapter.Calls.Should().Be(0);
        f.ExecutionStarts.Should().Be(1);
        f.ExecutionCompletions.Should().Be(0);
        f.PendingRecords.Values.Should().NotContain(item => item.Message.AssistantResponse != null);
        host.IsQuiescent.Should().BeTrue();
    }

    [Fact]
    public async Task FailedRetryQueuedBehindPeerExpiresWithoutRenewingOriginalContextOrInvokingAnotherProvider()
    {
        using var f = new Fixture { TrackPendingRecords = true };
        var source = SeedFailedRetry(f);
        var host = ConfigureCancellation(f);
        await using var conversation = f.ConversationService(host, execution: host);
        var provider = new TaskCompletionSource<Kora.Application.Dependencies.ModelProviderReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Adapter.Pending = provider.Task;
        var target = new SessionWorkspaceEntry(f.Session, null);
        var first = await conversation.SubmitQueuedAsync(target, "unrelated active peer", () => true, Token);
        var commands = conversation.CreateRequestCommands();
        var review = (await commands.ExecuteAsync(FailedReview(source), RequestOrigin.LocalUi, () => true, Token)).RequestReview!;
        var terminal = new TaskCompletionSource<ConversationTurnCompletion>(TaskCreationOptions.RunContinuationsAsynchronously);
        conversation.SubmittedConversationCompleted += (_, completion) =>
        {
            if (string.Equals(completion.Receipt?.Message.Body, source.Body, StringComparison.Ordinal)) { terminal.TrySetResult(completion); }
        };
        try
        {
            var accepted = (await commands.ExecuteAsync(FailedConfirm(review), RequestOrigin.LocalUi, () => true, Token)).RequestSubmission!;
            accepted.Message.Outcome.Should().BeNull();
            accepted.Message.Execution.Should().BeNull();
            f.Time.FireDeadline();
            var expired = (await terminal.Task.WaitAsync(Token)).RequireReceipt();
            expired.Message.Outcome.Should().Be(ModelTurnOutcome.Denied);
            expired.Message.Reason.Should().Be(ModelTurnReason.ContextExpired);
            expired.Message.AssistantResponse.Should().BeNull();
            expired.Message.Execution.Should().BeNull();
            f.Adapter.Calls.Should().Be(1);
            first.Completion.IsCompleted.Should().BeFalse("expiry of the retry never cancels its peer");
            f.PendingRecords[source.Request.RequestId].Message.Should().Be(source);
            await commands.Invoking(value => value.ExecuteAsync(FailedConfirm(review), RequestOrigin.LocalUi, () => true, Token))
                .Should().ThrowAsync<InvalidOperationException>();
        }
        finally { provider.TrySetResult(LateAnswer()); await first.Completion; }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedRetryAcceptedBehindPeerRejectsChangedProviderOrDeletedSourceWithoutAnotherSend(bool deleted)
    {
        using var f = new Fixture { TrackPendingRecords = true };
        var source = SeedFailedRetry(f);
        var host = ConfigureCancellation(f);
        f.ProfilePreferences.Value = new(ProviderService.Local, [Profile(), Profile(ProviderService.Claude)]);
        await using var conversation = f.ConversationService(host, execution: host);
        var provider = new TaskCompletionSource<ModelProviderReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Adapter.Pending = provider.Task;
        var first = await conversation.SubmitQueuedAsync(new(f.Session, null), "unrelated active peer", () => true, Token);
        var commands = conversation.CreateRequestCommands();
        var review = (await commands.ExecuteAsync(FailedReview(source), RequestOrigin.LocalUi, () => true, Token)).RequestReview!;
        var terminal = new TaskCompletionSource<ConversationTurnCompletion>(TaskCreationOptions.RunContinuationsAsynchronously);
        conversation.SubmittedConversationCompleted += (_, completion) =>
        {
            if (completion.Receipt?.Message.Request != first.Receipt.Message.Request) { terminal.TrySetResult(completion); }
        };
        try
        {
            var accepted = (await commands.ExecuteAsync(FailedConfirm(review), RequestOrigin.LocalUi, () => true, Token)).RequestSubmission!;
            accepted.Message.Outcome.Should().BeNull();
            first.Completion.IsCompleted.Should().BeFalse();
            if (deleted) { f.PendingRecords.TryRemove(source.Request.RequestId, out _); }
            else
            {
                f.SessionOverrides = new(ProviderService.Claude);
                f.SessionProfileRevision++;
            }
            provider.SetResult(LateAnswer());
            (await first.Completion).RequireReceipt().Message.Outcome.Should().Be(ModelTurnOutcome.Succeeded);
            var rejected = await terminal.Task.WaitAsync(Token);
            var failure = rejected.Failure.Should().BeOfType<ConversationSubmissionUnconfirmedException>().Which;
            failure.Request.Should().Be(accepted.Message.Request);
            failure.InnerException.Should().BeOfType<InvalidOperationException>();
            f.Adapter.Calls.Should().Be(1);
            f.ExecutionStarts.Should().Be(1);
            f.PendingRecords[accepted.Message.Request.RequestId].Message.AssistantResponse.Should().BeNull();
            if (!deleted) { f.PendingRecords[source.Request.RequestId].Message.Should().Be(source); }
            host.IsQuiescent.Should().BeTrue();
            await commands.Invoking(value => value.ExecuteAsync(FailedConfirm(review), RequestOrigin.LocalUi, () => true, Token))
                .Should().ThrowAsync<InvalidOperationException>();
        }
        finally { provider.TrySetResult(LateAnswer()); await first.Completion; }
    }

    [Fact]
    public async Task FailedRetrySourceReadsExactTerminalTaskWithoutModifyingItOrCreatingAnotherIntent()
    {
        using var f = new Fixture { TrackPendingRecords = true };
        var source = SeedFailedRetry(f);
        var host = ConfigureCancellation(f);
        await using var conversation = f.ConversationService(host, execution: host);
        using var root = f.Root();
        var workspace = new SessionWorkspaceService(f, new(f), f, Microsoft.Extensions.Logging.Abstractions.NullLogger<SessionWorkspaceService>.Instance);
        var evidence = await workspace.ReadFailedRetrySourceAsync(new(f.Session, null), source, () => true, Token);
        evidence.Task.State.Should().Be(HostTaskState.Denied);
        evidence.Task.Revision.Value.Should().Be(3);
        f.PendingRecords[source.Request.RequestId].Task.Should().Be(evidence.Task);
        HostActivity.RequireCurrent().Request.Should().Be(source.Request);
        f.RetainCalls.Should().Be(0);
    }

    [Theory]
    [InlineData(ModelProviderIdentity.Copilot, false)]
    [InlineData(ModelProviderIdentity.Copilot, true)]
    [InlineData(ModelProviderIdentity.Claude, false)]
    [InlineData(ModelProviderIdentity.Claude, true)]
    public async Task FailedRetryHostedProviderIsExactAndEveryNewPhysicalSendStillNeedsSeparateConsent(ModelProviderIdentity provider, bool approve)
    {
        using var f = new Fixture { TrackPendingRecords = true };
        var source = SeedFailedRetry(f);
        source = source with { Execution = source.Execution! with { Selection = ModelProviderSelection.OllamaCandidate with { Provider = provider } } };
        f.PendingRecords[source.Request.RequestId] = f.PendingRecords[source.Request.RequestId] with { Message = source };
        var reviews = new HostedTransmissionReviewService(f, f.Time);
        var copilot = new HostedRuntime();
        var claude = new ClaudeRuntime();
        var host = provider == ModelProviderIdentity.Copilot ? f.CopilotProfiles(copilot, reviews) : f.ClaudeProfiles(claude, reviews);
        reviews.Offered += review => reviews.Confirm(review, approve, RequestOrigin.LocalUi).Should().BeTrue();
        reviews.ClaudeOffered += review =>
        {
            using var presentation = review.Presentation();
            reviews.Confirm(review, approve, RequestOrigin.LocalUi).Should().BeTrue();
        };
        await using var conversation = f.ConversationService(host, execution: host);
        var commands = conversation.CreateRequestCommands();
        var review = (await commands.ExecuteAsync(FailedReview(source), RequestOrigin.LocalUi, () => true, Token)).RequestReview!;
        copilot.Calls.Should().Be(0);
        claude.Calls.Should().Be(0);
        var receipt = (await commands.ExecuteAsync(FailedConfirm(review), RequestOrigin.LocalUi, () => true, Token)).RequestSubmission!;
        receipt.Message.Outcome.Should().Be(approve ? ModelTurnOutcome.Succeeded : ModelTurnOutcome.DeniedEgress);
        receipt.Message.AssistantResponse?.Body.Should().Be(approve
            ? provider == ModelProviderIdentity.Copilot ? "actual-boundary-answer" : "genuine Claude answer" : null);
        receipt.Message.Execution!.Selection.Should().Be(source.Execution.Selection);
        f.PendingRecords[source.Request.RequestId].Message.Should().Be(source);
        copilot.Transmissions.Should().Be(provider == ModelProviderIdentity.Copilot && approve ? 1 : 0);
        f.Adapter.Calls.Should().Be(0, "no local/default adapter may substitute for either original hosted provider");
    }

    [Theory]
    [InlineData("foreground")]
    [InlineData("provider")]
    [InlineData("tools")]
    [InlineData("body")]
    public async Task FailedRetryReviewCannotBeCombinedWithReplacementInputProviderToolsOrForegroundTarget(string fault)
    {
        using var f = new Fixture { TrackPendingRecords = true };
        var source = SeedFailedRetry(f);
        var host = ConfigureCancellation(f);
        await using var conversation = f.ConversationService(host, execution: host);
        var commands = conversation.CreateRequestCommands();
        var command = FailedReview(source);
        command = fault switch
        {
            "foreground" => command with { SessionId = null, RequestForegroundTarget = true },
            "provider" => command with { RequestProvider = SessionRequestReview.ProviderChoice.Copilot },
            "tools" => command with { RequestProvider = SessionRequestReview.ProviderChoice.Claude,
                RequestClaudeTools = [Kora.Core.Tools.ReadOnlyCapabilityCatalog.Version] },
            _ => command with { RequestBody = "replacement input" },
        };
        var foreground = fault is "foreground" ? new ForegroundConversationTarget(new(f.Session, null), () => true) : null;
        await commands.Invoking(value => value.ExecuteAsync(command, RequestOrigin.LocalUi, () => true, Token, foreground))
            .Should().ThrowAsync<InvalidOperationException>();
        f.RetainCalls.Should().Be(0);
        f.Adapter.Calls.Should().Be(0);
    }

    [Fact]
    public async Task FailedRetryNativeReviewCannotRenewRetainedVoiceChannelAuthority()
    {
        using var f = new Fixture(RequestOrigin.ActivatedVoice) { TrackPendingRecords = true };
        var source = SeedFailedRetry(f);
        var host = ConfigureCancellation(f);
        await using var conversation = f.ConversationService(host, execution: host);
        var commands = conversation.CreateRequestCommands();
        await commands.Invoking(value => value.ExecuteAsync(FailedReview(source), RequestOrigin.LocalUi, () => true, Token))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*original channel*");
        f.RetainCalls.Should().Be(0);
        f.Adapter.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData("foreign")]
    [InlineData("invocation")]
    [InlineData("origin")]
    [InlineData("policy")]
    [InlineData("sessions")]
    [InlineData("held")]
    [InlineData("session")]
    [InlineData("provider")]
    public async Task FailedRetryProviderReviewCannotBorrowMissingOrForeignHostAccountAndPolicyAuthority(string fault)
    {
        using var f = new Fixture(fault is "origin" ? RequestOrigin.HostSystem : RequestOrigin.LocalUi);
        f.ProfileCatalogue.Status = ProviderCatalogueStatus.Current;
        f.ProfilePreferences.Value = new(ProviderService.Local, [Profile()]);
        var host = fault is "policy" ? f.Host : f.ConfiguredProfiles(missingSessionStore: fault is "sessions");
        var target = new SessionWorkspaceEntry(f.Session, null);
        if (fault is "foreign") { target = target with { Authority = target.Authority with { SessionId = new(Guid.NewGuid()) } }; }
        if (fault is "invocation") { f.Request = new(f.Request.RequestId, f.Request.SessionId, f.Request.TaskId, RequestOrigin.LocalUi, new(Guid.NewGuid())); }
        if (fault is "held") { f.SessionProfileHeld = true; }
        if (fault is "session") { f.ProfileRead = () => new(f.Session with { Generation = new(2) }, 0, f.SessionOverrides, false); }
        using var root = f.Root();
        await host.Invoking(value => value.ReviewRetryProviderAsync(target,
            fault is "provider" ? ModelProviderSelection.CopilotCandidate : ModelProviderSelection.OllamaCandidate, () => true, Token))
            .Should().ThrowAsync<InvalidOperationException>();
        f.Adapter.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedRetryCanReviewExistingPolicyWithoutConfiguredProfileButNeverInferHostedAccount(bool existing)
    {
        using var f = new Fixture();
        using var root = f.Root();
        if (existing) { (await f.PolicyHost.InitializePolicyAsync(Token)).Policy.Should().NotBeNull(); }
        var target = new SessionWorkspaceEntry(f.Session, null);
        var provider = await f.PolicyHost.ReviewRetryProviderAsync(target, ModelProviderSelection.OllamaCandidate, () => true, Token);
        provider.Profile.Should().BeNull();
        await provider.ValidateAsync(Token);
        await f.PolicyHost.Invoking(value => value.ReviewRetryProviderAsync(target, ModelProviderSelection.CopilotCandidate, () => true, Token))
            .Should().ThrowAsync<InvalidOperationException>();
        await f.PolicyHost.Invoking(value => value.ReviewRetryProviderAsync(target,
            ModelProviderSelection.OllamaCandidate with { Revision = new(2) }, () => true, Token))
            .Should().ThrowAsync<InvalidOperationException>();
        f.Adapter.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData("selection")]
    [InlineData("profile")]
    [InlineData("session-revision")]
    [InlineData("current")]
    public async Task FailedRetryExpectedProviderTupleMustMatchTheActualAdmittedTurnBeforeDispatch(string fault)
    {
        using var f = new Fixture();
        using var root = f.Root();
        var host = ConfigureCancellation(f);
        var expected = await host.ReviewRetryProviderAsync(new(f.Session, null), ModelProviderSelection.OllamaCandidate, () => true, Token);
        var turn = (await host.AdmitAsync(ModelTurnChoice.Default, f.Context(), null, Token)).Turn!;
        expected.Matches(turn).Should().BeTrue();
        if (fault is "selection")
        { turn = new(turn.Owner, turn.Issuer, turn.Provenance with { Selection = ModelProviderSelection.CopilotCandidate },
            turn.Context, turn.Wire, turn.SessionGeneration, turn.TaskRevision, turn.ControlRevision, turn.Registration); }
        if (fault is "profile") { turn.Profile = Profile() with { Account = new("foreign-account") }; }
        if (fault is "session-revision") { turn.SessionPolicyRevision++; }
        if (fault is "current") { f.ControlRevision++; }
        expected.Matches(turn).Should().BeFalse();
        f.Adapter.Calls.Should().Be(0);
    }

    [Fact]
    public async Task FailedRetryRejectsHostThatCannotCarryExactProviderBinding()
    {
        using var f = new Fixture { TrackPendingRecords = true };
        var source = SeedFailedRetry(f);
        await using var conversation = f.ConversationService(new TestAdmission(ModelTurnOutcome.Unavailable));
        var commands = conversation.CreateRequestCommands();
        await commands.Invoking(value => value.ExecuteAsync(FailedReview(source), RequestOrigin.LocalUi, () => true, Token))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*owning model host*");
        f.RetainCalls.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedRetryCanonicalAdmissionRejectsMismatchedTypedProviderOrUnsupportedProviderWithoutAdapterDispatch(bool unsupported)
    {
        using var f = new Fixture { TrackPendingRecords = true };
        var source = SeedFailedRetry(f);
        var host = ConfigureCancellation(f);
        await using var conversation = f.ConversationService(host, execution: host);
        var expectation = new ModelTurnHost.RetryProviderReview(ModelProviderSelection.CopilotCandidate, Profile(), 0,
            () => true, _ => Task.CompletedTask);
        if (unsupported)
        { source = source with { Execution = source.Execution! with { Selection = ModelProviderSelection.OllamaCandidate with { Provider = ModelProviderIdentity.Codex } } }; }
        var review = new SessionRequestReview(Guid.NewGuid(), new(f.Session, null), source.Body, RequestOrigin.LocalUi,
            f.Time.Now.AddSeconds(60)) { FailedSource = new(source, f.PendingRecords[source.Request.RequestId].Task) };
        var operation = conversation.SubmitFailedRetryAsync(review, expectation, () => true, _ => Task.CompletedTask,
            [], () => { }, Token);
        if (unsupported)
        { await this.Invoking(_ => operation).Should().ThrowAsync<InvalidOperationException>().WithMessage("*no admitted retry route*"); }
        else
        {
            var receipt = await operation;
            receipt.Message.Outcome.Should().Be(ModelTurnOutcome.Denied);
            receipt.Message.Reason.Should().Be(ModelTurnReason.PolicyChanged);
            receipt.Message.Execution.Should().BeNull();
        }
        f.Adapter.Calls.Should().Be(0);
        f.ExecutionStarts.Should().Be(0);
    }

    [Fact]
    public async Task FailedRetryCallerCancellationKeepsPhysicalWorkerOwnedUntilIgnoredProviderActuallyJoins()
    {
        using var f = new Fixture { TrackPendingRecords = true };
        var source = SeedFailedRetry(f);
        var host = ConfigureCancellation(f);
        await using var conversation = f.ConversationService(host, execution: host);
        var commands = conversation.CreateRequestCommands();
        var review = (await commands.ExecuteAsync(FailedReview(source), RequestOrigin.LocalUi, () => true, Token)).RequestReview!;
        var provider = new TaskCompletionSource<ModelProviderReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Adapter.Pending = provider.Task;
        f.Adapter.OnRun = () => started.TrySetResult();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var operation = commands.ExecuteAsync(FailedConfirm(review), RequestOrigin.LocalUi, () => true, cancellation.Token);
        await started.Task.WaitAsync(Token);
        try
        {
            await cancellation.CancelAsync();
            var failure = await this.Invoking(_ => operation).Should().ThrowAsync<ConversationSubmissionUnconfirmedException>();
            failure.Which.InnerException.Should().BeAssignableTo<OperationCanceledException>();
            var disposal = conversation.DisposeAsync().AsTask();
            disposal.IsCompleted.Should().BeFalse();
            host.IsQuiescent.Should().BeFalse();
            provider.SetResult(LateAnswer());
            await disposal;
            host.IsQuiescent.Should().BeTrue();
            f.PendingRecords.Values.Should().NotContain(item => item.Message.AssistantResponse != null);
            f.PendingRecords[source.Request.RequestId].Message.Should().Be(source);
        }
        finally { provider.TrySetResult(LateAnswer()); await conversation.DisposeAsync(); }
    }

    private static ConversationMessage SeedFailedRetry(Fixture f)
    {
        var source = new ConversationMessage(f.Request, f.Session.Generation, 1, 3, f.Time.Now,
            " immutable failed original USER\r\n ", ModelTurnOutcome.QuotaExceeded, ModelTurnReason.ProviderOutcome)
        {
            Version = 2,
            Execution = new(new(Guid.NewGuid()), f.Request, ModelProviderSelection.OllamaCandidate),
            ExecutionMetadataRevision = 0,
        };
        f.PendingRecords[source.Request.RequestId] = new(source, new(source.Request, new(3), HostTaskState.Denied));
        return source;
    }

    private static SessionCommand FailedReview(ConversationMessage source) => new(SessionCommandOperation.RequestReview,
        source.Request.SessionId.Value, source.Generation.Value) { RequestFailedMessage = source };
    private static SessionCommand FailedConfirm(SessionRequestReview review) => new(SessionCommandOperation.RequestConfirm,
        review.Target.Authority.SessionId.Value) { RequestReviewId = review.Id };
}
