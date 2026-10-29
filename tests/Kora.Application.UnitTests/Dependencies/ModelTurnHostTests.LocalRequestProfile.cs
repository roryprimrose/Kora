using AwesomeAssertions;

using Kora.Application.Dependencies;
using Kora.Application.Hosting;
using Kora.Core.Commands;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Providers;
using Kora.Core.Storage;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.Dependencies;

public sealed partial class ModelTurnHostTests
{
    [Theory]
    [InlineData(RequestOrigin.LocalUi, false)]
    [InlineData(RequestOrigin.ActivatedVoice, false)]
    [InlineData(RequestOrigin.LocalUi, true)]
    public async Task LocalRequestProfileUsesCompleteTupleExactlyOnceAndNextRequestInheritsWithoutPreferenceMutation(
        RequestOrigin origin, bool unqualified)
    {
        using var f = new Fixture(origin);
        f.ProfileCatalogue.Status = ProviderCatalogueStatus.Current;
        f.ProfileCatalogue.FieldCatalogue = true;
        f.ProfilePreferences.Value = new(ProviderService.Local, [Profile()]);
        var bytes = f.ProfilePreferences.Value.Serialize();
        var policy = f.SessionOverrides.Serialize();
        var host = f.ConfiguredProfiles(production: unqualified);
        await using var conversation = f.ConversationService(host, execution: host);
        var commands = LocalProfileCommands(f, conversation);
        const string body = "  Original e\u0301🙂\r\nend  ";
        var chosen = Profile() with { Reasoning = new("exact-effort"), Context = new("exact-window") };
        var review = await commands.ExecuteAsync(LocalProfileCommand(f, body, chosen), origin, static () => true, Token);
        review.RequestReview!.LocalRequestProfile.Should().Be(chosen);
        f.RetainCalls.Should().Be(0);
        f.Adapter.Calls.Should().Be(0);
        var result = await commands.ExecuteAsync(LocalProfileConfirm(review.RequestReview), RequestOrigin.LocalUi, static () => true, Token);
        var receipt = result.RequestSubmission!;
        receipt.Message.Body.Should().Be(body);
        receipt.Message.Request.Origin.Should().Be(origin);
        receipt.Message.Outcome.Should().Be(unqualified ? ModelTurnOutcome.Unavailable : ModelTurnOutcome.Succeeded);
        receipt.Message.AssistantResponse?.Body.Should().Be(unqualified ? null : "answer");
        receipt.Task.State.Should().Be(unqualified ? HostTaskState.Unavailable : HostTaskState.Succeeded);
        f.Adapter.Calls.Should().Be(unqualified ? 0 : 1);
        if (!unqualified) { f.Adapter.Profile.Should().Be(chosen); }
        await commands.Invoking(value => value.ExecuteAsync(LocalProfileConfirm(review.RequestReview), origin, static () => true, Token))
            .Should().ThrowAsync<InvalidOperationException>();
        await conversation.SubmitLocalAnswerAsync(new(f.Session, f.Metadata), "subsequent ordinary request", static () => true, Token);
        if (!unqualified) { f.Adapter.Profile.Should().Be(Profile()); }
        f.ProfilePreferences.Value.Serialize().Should().Be(bytes);
        f.ProfilePreferences.Writes.Should().Be(0);
        f.SessionOverrides.Serialize().Should().Be(policy);
        f.SessionProfileRevision.Should().Be(0);
        f.Messages.Should().NotContain(text => text.Contains(body, StringComparison.Ordinal) || text.Contains("answer", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("input")]
    [InlineData("control")]
    [InlineData("private")]
    [InlineData("owner")]
    [InlineData("generation")]
    [InlineData("metadata")]
    [InlineData("policy")]
    [InlineData("held")]
    [InlineData("catalogue")]
    [InlineData("account")]
    [InlineData("model")]
    [InlineData("reasoning")]
    [InlineData("context")]
    [InlineData("installation")]
    [InlineData("unavailable")]
    [InlineData("expiry")]
    [InlineData("preferences")]
    [InlineData("proposal")]
    public async Task StaleLocalRequestReviewRejectsBeforeRetentionOrProviderEffect(string fault)
    {
        using var f = new Fixture();
        f.ProfileCatalogue.Status = ProviderCatalogueStatus.Current;
        var host = f.ConfiguredProfiles();
        await using var conversation = f.ConversationService(host, execution: host);
        var commands = LocalProfileCommands(f, conversation);
        var source = true;
        var review = (await commands.ExecuteAsync(LocalProfileCommand(f, "original", Profile()),
            RequestOrigin.ActivatedVoice, () => source, Token)).RequestReview!;
        switch (fault)
        {
            case "input": source = false; break;
            case "control": f.ControlRevision++; break;
            case "private": f.Inspect = false; break;
            case "owner": f.Current = false; break;
            case "generation": f.Session = f.Session with { Generation = new(2) }; break;
            case "metadata": f.Metadata = new(f.Session.SessionId, new(1), new("changed")); break;
            case "policy": f.SessionProfileRevision++; break;
            case "held": f.SessionProfileHeld = true; break;
            case "catalogue": f.ProfileCatalogue.Revision++; break;
            case "account": f.ProfileCatalogue.Account = "other"; break;
            case "model": f.ProfileCatalogue.Model = "other"; break;
            case "reasoning": f.ProfileCatalogue.Reasoning = "other"; break;
            case "context": f.ProfileCatalogue.Context = "other"; break;
            case "installation": f.ProfileCatalogue.Installation = LocalProviderInstallation.Missing; break;
            case "unavailable": f.ProfileCatalogue.Status = ProviderCatalogueStatus.Unavailable; break;
            case "expiry": f.Time.Now = review.ExpiresAt; break;
            case "preferences": f.ProfilePreferences.Value = new(ProviderService.Copilot, []); break;
            case "proposal": commands.Retire(); break;
        }
        await commands.Invoking(value => value.ExecuteAsync(LocalProfileConfirm(review), RequestOrigin.LocalUi, static () => true, Token))
            .Should().ThrowAsync<InvalidOperationException>();
        f.RetainCalls.Should().Be(0);
        f.Adapter.Calls.Should().Be(0);
        f.ProfilePreferences.Writes.Should().Be(0);
    }

    [Fact]
    public async Task CancelLocalRequestReviewConsumesProposalWithoutAnySubmissionOrPreferenceWrite()
    {
        using var f = new Fixture();
        f.ProfileCatalogue.Status = ProviderCatalogueStatus.Current;
        var host = f.ConfiguredProfiles();
        await using var conversation = f.ConversationService(host, execution: host);
        var commands = LocalProfileCommands(f, conversation);
        var review = (await commands.ExecuteAsync(LocalProfileCommand(f, "original", Profile()),
            RequestOrigin.LocalUi, static () => true, Token)).RequestReview!;
        (await commands.ExecuteAsync(LocalProfileConfirm(review) with { Operation = SessionCommandOperation.RequestCancel },
            RequestOrigin.LocalUi, static () => true, Token)).Outcome.Should().Be("cancelled");
        await commands.Invoking(value => value.ExecuteAsync(LocalProfileConfirm(review), RequestOrigin.LocalUi, static () => true, Token))
            .Should().ThrowAsync<InvalidOperationException>();
        f.RetainCalls.Should().Be(0);
        f.Adapter.Calls.Should().Be(0);
        f.ProfilePreferences.Writes.Should().Be(0);
    }

    [Theory]
    [InlineData("hosted")]
    [InlineData("retry")]
    [InlineData("tools")]
    [InlineData("service")]
    [InlineData("missing-model")]
    [InlineData("held")]
    [InlineData("done")]
    [InlineData("missing-store")]
    [InlineData("missing-consumer")]
    public async Task InvalidLocalRequestChoiceNeverCreatesReviewOrEffect(string fault)
    {
        using var f = new Fixture();
        f.ProfileCatalogue.Status = ProviderCatalogueStatus.Current;
        var host = f.ConfiguredProfiles(missingSessionStore: fault is "missing-store");
        await using var conversation = f.ConversationService(fault is "missing-consumer" ? f.Host : host, execution: host);
        var commands = LocalProfileCommands(f, conversation);
        var command = LocalProfileCommand(f, "original", fault is "service" ? Profile(ProviderService.Claude)
            : fault is "missing-model" ? Profile() with { Model = new("missing") } : Profile());
        if (fault is "hosted") { command = command with { RequestProvider = SessionRequestReview.ProviderChoice.Copilot }; }
        if (fault is "retry") { command = command with { RequestFailedMessage = new(f.Request, new(1), 1, 2, f.Time.Now, "original", ModelTurnOutcome.Unavailable, ModelTurnReason.UnregisteredModel) }; }
        if (fault is "tools") { command = command with { RequestClaudeTools = ["application.version.get"] }; }
        if (fault is "held") { f.SessionProfileHeld = true; }
        if (fault is "done") { f.Session = f.Session with { IsActive = false }; }
        await commands.Invoking(value => value.ExecuteAsync(command, RequestOrigin.LocalUi, static () => true, Token))
            .Should().ThrowAsync<InvalidOperationException>();
        f.RetainCalls.Should().Be(0);
        f.Adapter.Calls.Should().Be(0);
    }

    [Fact]
    public async Task LocalRequestReviewCannotBeReboundToAnotherHostBodyChannelSessionOrReusedAdmission()
    {
        using var f = new Fixture();
        f.ProfileCatalogue.Status = ProviderCatalogueStatus.Current;
        var host = f.ConfiguredProfiles();
        using var root = f.Root();
        var context = f.Context();
        var target = new SessionWorkspaceEntry(f.Session, null);
        var review = await host.ReviewLocalRequestProfileAsync(target, context.UserRequest, f.Request.Origin, Profile(), static () => true, Token);
        (await host.AdmitLocalRequestProfileAsync(review, context, Token)).Turn.Should().BeNull("unbound previews are not request authority");
        review.Invoking(value => value.Bind(HostRequest.Create(RequestOrigin.LocalUi))).Should().Throw<InvalidOperationException>();
        review.Invoking(value => value.Bind(new(new(Guid.NewGuid()), f.Session.SessionId, new(Guid.NewGuid()), RequestOrigin.ActivatedVoice)))
            .Should().Throw<InvalidOperationException>();
        review.Bind(context.Request);
        review.Invoking(value => value.Bind(context.Request)).Should().Throw<InvalidOperationException>();
        (await f.ConfiguredProfiles().AdmitLocalRequestProfileAsync(review, context, Token)).Turn.Should().BeNull();
        var altered = await host.ReviewLocalRequestProfileAsync(target, "different body", f.Request.Origin, Profile(), static () => true, Token);
        altered.Bind(context.Request);
        (await host.AdmitLocalRequestProfileAsync(altered, context, Token)).Turn.Should().BeNull();
        (await host.AdmitLocalRequestProfileAsync(review, context, Token)).Turn.Should().NotBeNull();
        (await host.AdmitLocalRequestProfileAsync(review, context, Token)).Turn.Should().BeNull();
        f.Adapter.Calls.Should().Be(0);
    }

    [Fact]
    public async Task LocalRequestCancellationJoinsActualProviderAndRetainsOnlyTruthfulCancelledTerminal()
    {
        using var f = new Fixture();
        f.ProfileCatalogue.Status = ProviderCatalogueStatus.Current;
        var host = f.ConfiguredProfiles();
        await using var conversation = f.ConversationService(host, execution: host);
        var commands = LocalProfileCommands(f, conversation);
        var review = (await commands.ExecuteAsync(LocalProfileCommand(f, "original", Profile()),
            RequestOrigin.LocalUi, static () => true, Token)).RequestReview!;
        var pending = new TaskCompletionSource<ModelProviderReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Adapter.Pending = pending.Task;
        var submission = commands.ExecuteAsync(LocalProfileConfirm(review), RequestOrigin.LocalUi, static () => true, Token);
        var running = (await conversation.ReadRunningConversationAsync(review.Target, static () => true, Token))!;
        await conversation.CancelRunningConversationAsync(running.Target, static () => true, Token);
        submission.IsCompleted.Should().BeFalse();
        f.ExecutionCompletions.Should().Be(0);
        pending.SetResult(LateAnswer());
        var receipt = (await submission).RequestSubmission!;
        receipt.Message.Outcome.Should().Be(ModelTurnOutcome.Cancelled);
        receipt.Message.AssistantResponse.Should().BeNull();
        receipt.Task.State.Should().Be(HostTaskState.Unknown);
        host.IsQuiescent.Should().BeTrue();
        f.Adapter.Calls.Should().Be(1);
    }

    [Fact]
    public async Task LocalRequestCallerCancellationCannotReleaseOwnershipBeforeActualProviderJoin()
    {
        using var f = new Fixture();
        f.ProfileCatalogue.Status = ProviderCatalogueStatus.Current;
        var host = f.ConfiguredProfiles();
        await using var conversation = f.ConversationService(host, execution: host);
        var commands = LocalProfileCommands(f, conversation);
        var review = (await commands.ExecuteAsync(LocalProfileCommand(f, "original", Profile()),
            RequestOrigin.LocalUi, static () => true, Token)).RequestReview!;
        var pending = new TaskCompletionSource<ModelProviderReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Adapter.Pending = pending.Task;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var submission = commands.ExecuteAsync(LocalProfileConfirm(review), RequestOrigin.LocalUi, static () => true, cancellation.Token);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var observation = f.Adapter.RunToken.Register(() => cancelled.TrySetResult());
        cancellation.Cancel();
        await cancelled.Task;
        submission.IsCompleted.Should().BeFalse();
        f.ExecutionCompletions.Should().Be(0);
        host.IsQuiescent.Should().BeFalse();
        pending.SetResult(LateAnswer());
        await this.Invoking(_ => submission).Should().ThrowAsync<ConversationSubmissionUnconfirmedException>();
        host.IsQuiescent.Should().BeTrue();
        f.Retained!.AssistantResponse.Should().BeNull();
        f.Adapter.Calls.Should().Be(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LocalRequestRevalidatesAuthorityAfterAwaitedCatalogueObservationBeforeAnyRetention(bool duringConfirmation)
    {
        using var f = new Fixture();
        f.ProfileCatalogue.Status = ProviderCatalogueStatus.Current;
        var host = f.ConfiguredProfiles();
        await using var conversation = f.ConversationService(host, execution: host);
        var commands = LocalProfileCommands(f, conversation);
        if (!duringConfirmation) { f.ProfileCatalogue.AfterObservation = () => f.ControlRevision++; }
        if (duringConfirmation)
        {
            var reviewed = (await commands.ExecuteAsync(LocalProfileCommand(f, "original", Profile()),
                RequestOrigin.LocalUi, static () => true, Token)).RequestReview!;
            f.ProfileCatalogue.AfterObservation = () => f.ControlRevision++;
            await commands.Invoking(value => value.ExecuteAsync(LocalProfileConfirm(reviewed), RequestOrigin.LocalUi, static () => true, Token))
                .Should().ThrowAsync<InvalidOperationException>();
        }
        else
        {
            await commands.Invoking(value => value.ExecuteAsync(LocalProfileCommand(f, "original", Profile()),
                RequestOrigin.LocalUi, static () => true, Token)).Should().ThrowAsync<InvalidOperationException>();
        }
        f.RetainCalls.Should().Be(0);
        f.Adapter.Calls.Should().Be(0);
    }

    [Fact]
    public async Task ExplicitLocalRequestCanOverrideUnresolvedHostedDesiredServiceWithoutChangingItOrFallingBackLater()
    {
        using var f = new Fixture();
        f.ProfileCatalogue.Status = ProviderCatalogueStatus.Current;
        f.ProfilePreferences.Value = new(ProviderService.Claude, []);
        f.SessionOverrides = new(ProviderService.Copilot);
        var host = f.ConfiguredProfiles();
        await using var conversation = f.ConversationService(host, execution: host);
        var commands = LocalProfileCommands(f, conversation);
        var review = (await commands.ExecuteAsync(LocalProfileCommand(f, "original", Profile()),
            RequestOrigin.LocalUi, static () => true, Token)).RequestReview!;
        (await commands.ExecuteAsync(LocalProfileConfirm(review), RequestOrigin.LocalUi, static () => true, Token))
            .RequestSubmission!.Message.Outcome.Should().Be(ModelTurnOutcome.Succeeded);
        f.Adapter.Profile.Should().Be(Profile());
        var ordinary = await conversation.SubmitLocalAnswerAsync(new(f.Session, null), "next", static () => true, Token);
        ordinary.Message.Outcome.Should().Be(ModelTurnOutcome.Unavailable);
        f.Adapter.Calls.Should().Be(1);
        f.ProfilePreferences.Value.Service.Should().Be(ProviderService.Claude);
        f.SessionOverrides.Service.Should().Be(ProviderService.Copilot);
        f.ProfilePreferences.Writes.Should().Be(0);
    }

    private static SessionRequestCommandService LocalProfileCommands(Fixture f, SessionConversationService conversation) =>
        new(new SessionWorkspaceService(f, new(f), f, NullLogger<SessionWorkspaceService>.Instance), conversation, f, f.Time);

    private static SessionCommand LocalProfileCommand(Fixture f, string body, ProviderProfileTuple profile) =>
        new(SessionCommandOperation.RequestReview, f.Session.SessionId.Value, f.Session.Generation.Value, f.Metadata?.Revision.Value ?? 0)
        { RequestBody = body, RequestLocalProfile = profile };

    private static SessionCommand LocalProfileConfirm(SessionRequestReview review) =>
        new(SessionCommandOperation.RequestConfirm, review.Target.Authority.SessionId.Value) { RequestReviewId = review.Id };

    [Fact]
    public async Task MissingConsumerCannotReviewOrDispatchAnExplicitLocalProfileAndHostedTupleCannotResolveAsLocal()
    {
        using var f = new Fixture();
        f.ProfileCatalogue.Status = ProviderCatalogueStatus.Current;
        var host = f.ConfiguredProfiles();
        var target = new SessionWorkspaceEntry(f.Session, null);
        var review = await host.ReviewLocalRequestProfileAsync(target, "original", RequestOrigin.LocalUi,
            Profile(), static () => true, Token);
        await using var missing = f.ConversationService(new TestAdmission(ModelTurnOutcome.Unavailable));
        await missing.Invoking(value => value.ReviewLocalRequestProfileAsync(target, "original", RequestOrigin.LocalUi,
            Profile(), static () => true, Token)).Should().ThrowAsync<InvalidOperationException>();
        await f.ProfileConfiguration.Invoking(value => value.ResolveTurnAsync(static () => true,
            SessionProviderOverrides.Inherit, Token, localRequest: Profile(ProviderService.Claude)))
            .Should().ThrowAsync<InvalidOperationException>();
        var receipt = await missing.SubmitLocalRequestProfileAsync(review, static () => true,
            _ => Task.CompletedTask, [], Token);
        receipt.Message.Outcome.Should().Be(ModelTurnOutcome.Unavailable);
        receipt.Message.Reason.Should().Be(ModelTurnReason.GenerationNotEnabled);
        await missing.Invoking(value => value.SubmitLocalRequestProfileAsync(review, static () => true,
            _ => Task.CompletedTask, [], Token)).Should().ThrowAsync<InvalidOperationException>();
        f.RetainCalls.Should().Be(1, "an already bound capability cannot retain another request, even with identical text");
        f.Adapter.Calls.Should().Be(0);
    }
}
