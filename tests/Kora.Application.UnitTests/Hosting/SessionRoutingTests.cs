using System.Collections.Immutable;
using System.Diagnostics;

using AwesomeAssertions;

using Kora.Application.Dependencies;
using Kora.Application.Hosting;
using Kora.Application.Hosting.SessionRouting;
using Kora.Core.Authorization;
using Kora.Core.Conversation;
using Kora.Core.Conversation.SessionRouting;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.Hosting;

[Collection("Host tracing")]
public sealed class SessionRoutingTests : IDisposable
{
    private readonly ActivityListener listener = new()
    {
        ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
        Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
    };
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    public SessionRoutingTests() => ActivitySource.AddActivityListener(listener);
    public void Dispose() => listener.Dispose();

    [Theory]
    [InlineData(RequestOrigin.LocalUi)]
    [InlineData(RequestOrigin.ActivatedVoice)]
    public async Task ClearProposalAcknowledgesExactNameAndCorrectionSubmitsOriginalOnlyOnce(RequestOrigin origin)
    {
        var f = new Fixture();
        const string body = "  Original e\u0301🙂\r\nunchanged  ";
        var review = await f.Service.PrepareAsync(body, origin, () => f.Source, Token);
        f.Matcher.Body.Should().Be(body);
        f.Matcher.Snapshot!.Descriptors.Should().HaveCount(2);
        f.Matcher.Snapshot.Descriptors[0].Name.Should().Be("Alpha");
        review.Candidates.Should().ContainSingle().Which.Entry.Should().Be(f.Entries[0]);
        var proposed = await f.Service.SelectAsync(review, f.Entries[0].Authority.SessionId, Token);
        proposed.Acknowledgment.Should().Be("Destination: Alpha.");
        f.Messages.Should().BeEmpty();
        var corrected = await f.Service.SelectAsync(review, f.Entries[1].Authority.SessionId, Token);
        corrected.Acknowledgment.Should().Be("Destination: Beta.");
        await f.Service.Invoking(service => service.SubmitAsync(proposed, Token)).Should().ThrowAsync<InvalidOperationException>();
        var receipt = await f.Service.SubmitAsync(corrected, Token);
        receipt.Target.Should().Be(f.Entries[1]);
        receipt.Submission.Message.Body.Should().Be(body);
        receipt.Submission.Message.Request.Origin.Should().Be(origin);
        receipt.Submission.Message.Request.SessionId.Should().Be(f.Entries[1].Authority.SessionId);
        receipt.Submission.Message.Outcome.Should().Be(ModelTurnOutcome.Unavailable);
        f.Messages.Should().ContainSingle();
        f.Admissions.Should().Be(1);
        f.Creates.Should().Be(0);
        await f.Service.Invoking(service => service.SubmitAsync(corrected, Token)).Should().ThrowAsync<InvalidOperationException>();
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ExactBoundedRegistryCursorIsPartOfProposalRevalidation(bool before, bool after)
    {
        var f = new Fixture { Next = before ? Guid.NewGuid() : null };
        var original = f.Next;
        f.Matcher.Propose = snapshot =>
        {
            f.Next = after ? before ? original : Guid.NewGuid() : null;
            return new(snapshot.Revision, SessionRouteMatch.Clear, [snapshot.Descriptors[0].Candidate]);
        };
        var operation = f.Service.PrepareAsync("request", RequestOrigin.LocalUi, () => true, Token);
        if (before && after) { (await operation).Snapshot.Descriptors.Should().HaveCount(2); }
        else { await this.Invoking(async _ => await operation).Should().ThrowAsync<InvalidOperationException>(); }
        f.Messages.Should().BeEmpty();
    }

    [Fact]
    public async Task AChangedNonemptyRegistryCursorCannotReuseThePresentedSnapshot()
    {
        var f = new Fixture { Next = Guid.NewGuid() };
        f.Matcher.Propose = snapshot =>
        {
            f.Next = Guid.NewGuid();
            return new(snapshot.Revision, SessionRouteMatch.Clear, [snapshot.Descriptors[0].Candidate]);
        };
        await f.Service.Invoking(service => service.PrepareAsync("request", RequestOrigin.LocalUi, () => true, Token))
            .Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task ReentrantRetirementAtFinalTargetReadCannotPublishAnAcknowledgment()
    {
        var f = new Fixture();
        var remaining = 0;
        var review = await f.Service.PrepareAsync("request", RequestOrigin.LocalUi, () =>
        {
            if (--remaining == 0) { f.Service.Retire(); }
            return true;
        }, Token);
        remaining = 3;
        await f.Service.Invoking(service => service.SelectAsync(review, f.Entries[0].Authority.SessionId, Token))
            .Should().ThrowAsync<InvalidOperationException>();
        f.Messages.Should().BeEmpty();
    }

    [Theory]
    [InlineData(SessionRouteMatch.Ambiguous)]
    [InlineData(SessionRouteMatch.NoMatch)]
    [InlineData(SessionRouteMatch.Unavailable)]
    public async Task AmbiguityAndFallbackOfferNewWithoutImplicitCreationOrGuessing(SessionRouteMatch match)
    {
        var f = new Fixture { Match = match };
        var review = await f.Service.PrepareAsync("new request", RequestOrigin.ActivatedVoice, () => f.Source, Token);
        review.Match.Should().Be(match);
        review.Candidates.Should().HaveCount(match == SessionRouteMatch.Ambiguous ? 2 : 0);
        var selected = await f.Service.SelectAsync(review, null, Token);
        selected.Acknowledgment.Should().Contain("New conversation");
        selected.CreationRequest.Should().BeNull();
        f.Creates.Should().Be(0);
        f.Messages.Should().BeEmpty();
        var receipt = await f.Service.SubmitAsync(selected, Token);
        f.Creates.Should().Be(1);
        selected.CreationRequest!.Origin.Should().Be(RequestOrigin.ActivatedVoice);
        receipt.Target.Authority.SessionId.Should().Be(selected.CreationRequest.SessionId);
        receipt.Submission.Message.Request.Origin.Should().Be(RequestOrigin.ActivatedVoice);
        f.Entries.Take(2).Select(entry => entry.Metadata!.Name.Value).Should().Equal("Alpha", "Beta");
    }

    [Fact]
    public async Task UnqualifiedProductionMatcherAndUnnamedMetadataRemainTruthfullyUnavailable()
    {
        var f = new Fixture();
        f.Entries[0] = f.Entries[0] with { Metadata = null };
        var service = new SessionRoutingService(f.Workspace, f.Conversation, f, f.Time);
        var review = await service.PrepareAsync("ordinary request", RequestOrigin.LocalUi, () => true, Token);
        review.Match.Should().Be(SessionRouteMatch.Unavailable);
        review.Snapshot.Descriptors[0].Name.Should().Be("Unnamed conversation");
        var correction = await service.SelectAsync(review, f.Entries[0].Authority.SessionId, Token);
        correction.Acknowledgment.Should().Be("Destination: Unnamed conversation.");
        service.Retire();
        await service.Invoking(value => value.SubmitAsync(correction, Token)).Should().ThrowAsync<InvalidOperationException>();
        f.Messages.Should().BeEmpty();
    }

    [Theory]
    [InlineData(RequestOrigin.LocalUi, SessionRouteMatch.Clear)]
    [InlineData(RequestOrigin.ActivatedVoice, SessionRouteMatch.Clear)]
    [InlineData(RequestOrigin.ActivatedVoice, SessionRouteMatch.Unavailable)]
    public async Task RoutingControlDomainIsCreatedBeforeFreezingMetadataAndNeverOfferedAsDestination(
        RequestOrigin origin, SessionRouteMatch match)
    {
        var f = new Fixture { Match = match };
        await using var admission = new SessionRoutingControlAdmission(f, f, new(f));
        var service = new SessionRoutingService(f.Workspace, f.Conversation, f, f.Time, f.Matcher, admission);
        var review = await service.PrepareAsync("original", origin, static () => true, Token);
        review.Origin.Should().Be(origin);
        review.Snapshot.Descriptors.Should().HaveCount(2);
        review.Snapshot.Descriptors.Should().NotContain(item => item.Entry.Authority.SessionId == f.ControlSession!.SessionId);
        f.Tasks.Values.Should().HaveCount(2);
        f.Tasks.Values.Should().OnlyContain(task => task.IsTerminal && task.Request.Origin == origin);
        f.Tasks.Values.Should().Contain(task => task.State == (match == SessionRouteMatch.Unavailable
            ? HostTaskState.Unavailable : HostTaskState.Succeeded));
        var second = await service.PrepareAsync("second original", origin, static () => true, Token);
        second.Snapshot.Descriptors.Should().HaveCount(2);
        f.Entries.Should().HaveCount(3, "the existing routing domain is reused, not created during each metadata snapshot");
        f.Messages.Should().BeEmpty();
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("generation")]
    [InlineData("input")]
    public async Task RoutingControlFailureCannotPublishAReviewAndRecordsJoinedTerminal(string fault)
    {
        var f = new Fixture();
        await using var admission = new SessionRoutingControlAdmission(f, f, new(f));
        var service = new SessionRoutingService(f.Workspace, f.Conversation, f, f.Time, f.Matcher, admission);
        f.Matcher.Propose = snapshot =>
        {
            if (fault is "cancel") { throw new OperationCanceledException(); }
            if (fault is "generation") { f.ControlSession = f.ControlSession! with { Generation = new(2) }; }
            if (fault is "input") { f.Source = false; }
            return new(snapshot.Revision, SessionRouteMatch.Clear, [snapshot.Descriptors[0].Candidate]);
        };
        await service.Invoking(subject => subject.PrepareAsync("original", RequestOrigin.ActivatedVoice, () => f.Source, Token))
            .Should().ThrowAsync<Exception>();
        f.Tasks.Values.Should().OnlyContain(task => task.IsTerminal);
        f.Tasks.Values.Should().Contain(task => task.State == (fault == "cancel" ? HostTaskState.Unknown : HostTaskState.Failed));
        f.Messages.Should().BeEmpty();
    }

    [Theory]
    [InlineData(Kora.Core.Dependencies.ModelTurnOutcome.Denied, HostTaskState.Denied)]
    [InlineData(Kora.Core.Dependencies.ModelTurnOutcome.DeniedEgress, HostTaskState.Denied)]
    [InlineData(Kora.Core.Dependencies.ModelTurnOutcome.Unknown, HostTaskState.Unknown)]
    [InlineData(Kora.Core.Dependencies.ModelTurnOutcome.TimedOut, HostTaskState.Unknown)]
    [InlineData(Kora.Core.Dependencies.ModelTurnOutcome.AuthenticationRequired, HostTaskState.Unavailable)]
    public async Task RoutingControlRecordsTheActualInferenceFailureNotASuccessfulEmptyMatch(
        ModelTurnOutcome outcome, HostTaskState expected)
    {
        var f = new Fixture();
        await using var admission = new SessionRoutingControlAdmission(f, f, new(f));
        var service = new SessionRoutingService(f.Workspace, f.Conversation, f, f.Time, f.Matcher, admission);
        f.Matcher.Propose = _ => throw new SessionRoutingInferenceException(outcome, ModelTurnReason.ProviderOutcome);
        await service.Invoking(subject => subject.PrepareAsync("original", RequestOrigin.LocalUi, static () => true, Token))
            .Should().ThrowAsync<SessionRoutingInferenceException>();
        f.Tasks.Values.Should().OnlyContain(task => task.IsTerminal);
        f.Tasks.Values.Should().Contain(task => task.State == expected);
        f.Messages.Should().BeEmpty();
    }

    [Fact]
    public async Task RoutingCancellationBeforeDispatchRecordsCancelledWithoutOfferingNew()
    {
        var f = new Fixture { CancelDispatch = true };
        await using var admission = new SessionRoutingControlAdmission(f, f, new(f));
        var service = new SessionRoutingService(f.Workspace, f.Conversation, f, f.Time, f.Matcher, admission);
        await service.Invoking(subject => subject.PrepareAsync("original", RequestOrigin.LocalUi, static () => true, Token))
            .Should().ThrowAsync<OperationCanceledException>();
        f.Tasks.Values.Should().OnlyContain(task => task.IsTerminal);
        f.Tasks.Values.Should().Contain(task => task.State == HostTaskState.Cancelled);
        f.Matcher.Snapshot.Should().BeNull();
        f.Messages.Should().BeEmpty();
    }

    [Fact]
    public async Task FailedRoutingCancellationReceiptIsAnExplicitFailureNeverACompletedReview()
    {
        var f = new Fixture { FailCancellationReceipt = true };
        await using var admission = new SessionRoutingControlAdmission(f, f, new(f));
        var service = new SessionRoutingService(f.Workspace, f.Conversation, f, f.Time, f.Matcher, admission);
        f.Matcher.Propose = _ => throw new OperationCanceledException();
        await service.Invoking(subject => subject.PrepareAsync("original", RequestOrigin.LocalUi, static () => true, Token))
            .Should().ThrowAsync<IOException>();
        f.Tasks.Values.Should().Contain(task => task.State == HostTaskState.DispatchRecorded,
            "an unconfirmed terminal commit cannot manufacture completion or release its durable hold");
        f.Messages.Should().BeEmpty();
    }

    [Fact]
    public async Task RoutingCapturesTheOriginalAmbientVoiceChannelAndCausalLinkBeforeAwaitingStorage()
    {
        var f = new Fixture();
        await using var admission = new SessionRoutingControlAdmission(f, f, new(f));
        using var original = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.ActivatedVoice),
            HostActivityLayer.Application, HostOperation.Request);
        f.Matcher.Propose = snapshot =>
        {
            var current = HostActivity.RequireCurrent();
            current.Request.Origin.Should().Be(RequestOrigin.ActivatedVoice);
            current.Activity!.Parent!.Links.Should().ContainSingle()
                .Which.Context.Should().Be(original.Activity!.Context);
            return new(snapshot.Revision, SessionRouteMatch.Clear, [snapshot.Descriptors[0].Candidate]);
        };
        var service = new SessionRoutingService(f.Workspace, f.Conversation, f, f.Time, f.Matcher, admission);
        var review = await service.PrepareAsync("original", RequestOrigin.ActivatedVoice, static () => true, Token);
        review.Origin.Should().Be(RequestOrigin.ActivatedVoice);
        f.Tasks.Values.Should().OnlyContain(task => task.Request.Origin == RequestOrigin.ActivatedVoice && task.IsTerminal);
    }

    [Theory]
    [InlineData("revision")]
    [InlineData("foreign")]
    [InlineData("evidence")]
    [InlineData("duplicate")]
    [InlineData("clear-empty")]
    [InlineData("ambiguous-single")]
    [InlineData("unavailable-candidate")]
    [InlineData("default")]
    [InlineData("enum")]
    [InlineData("too-many")]
    [InlineData("no-match-candidate")]
    public async Task InvalidMatcherOutputFailsClosedWithoutFreshFallback(string fault)
    {
        var f = new Fixture();
        f.Matcher.Propose = snapshot =>
        {
            var candidate = snapshot.Descriptors[0].Candidate;
            return fault switch
            {
                "revision" => new(snapshot.Revision - 1, SessionRouteMatch.Clear, [candidate]),
                "foreign" => new(snapshot.Revision, SessionRouteMatch.Clear, [candidate with { SessionId = new(Guid.NewGuid()) }]),
                "evidence" => new(snapshot.Revision, SessionRouteMatch.Clear, [candidate with { EvidenceId = Guid.NewGuid() }]),
                "duplicate" => new(snapshot.Revision, SessionRouteMatch.Ambiguous, [candidate, candidate]),
                "clear-empty" => new(snapshot.Revision, SessionRouteMatch.Clear, []),
                "ambiguous-single" => new(snapshot.Revision, SessionRouteMatch.Ambiguous, [candidate]),
                "unavailable-candidate" => new(snapshot.Revision, SessionRouteMatch.Unavailable, [candidate]),
                "default" => new(snapshot.Revision, SessionRouteMatch.Clear, default),
                "too-many" => new(snapshot.Revision, SessionRouteMatch.Ambiguous, [candidate,
                    snapshot.Descriptors[1].Candidate, new(new(Guid.NewGuid()), Guid.NewGuid())]),
                "no-match-candidate" => new(snapshot.Revision, SessionRouteMatch.NoMatch, [candidate]),
                _ => new(snapshot.Revision, (SessionRouteMatch)99, []),
            };
        };
        await f.Service.Invoking(service => service.PrepareAsync("request", RequestOrigin.LocalUi, () => true, Token))
            .Should().ThrowAsync<InvalidDataException>();
        f.Messages.Should().BeEmpty();
        f.Creates.Should().Be(0);
        f.Admissions.Should().Be(0);
    }

    [Theory]
    [InlineData("inventory")]
    [InlineData("target")]
    [InlineData("expiry")]
    [InlineData("cancel")]
    public async Task SelectionRevalidationCannotAcknowledgeAStaleOrCancelledChoice(string fault)
    {
        var f = new Fixture();
        var review = await f.Service.PrepareAsync("request", RequestOrigin.LocalUi, () => f.Source, Token);
        if (fault is "inventory") { f.Entries.RemoveAt(1); }
        if (fault is "target") { f.BeforeExactRead = () => f.Source = false; }
        if (fault is "expiry") { f.Time.Now = review.ExpiresAt; }
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        if (fault is "cancel") { await cancellation.CancelAsync(); }
        await f.Service.Invoking(service => service.SelectAsync(review, f.Entries[0].Authority.SessionId, cancellation.Token))
            .Should().ThrowAsync<Exception>();
        f.Messages.Should().BeEmpty();
        f.Creates.Should().Be(0);
    }

    [Theory]
    [InlineData("create")]
    [InlineData("cancel")]
    [InlineData("foreign")]
    [InlineData("inactive")]
    [InlineData("readback")]
    [InlineData("task")]
    [InlineData("request")]
    [InlineData("state")]
    [InlineData("revision")]
    [InlineData("source")]
    [InlineData("readback-cancel")]
    public async Task UnconfirmedFreshCreationKeepsTheOriginalAttemptAndCannotReplay(string fault)
    {
        var f = new Fixture { Match = SessionRouteMatch.Unavailable, CreationFault = fault };
        var review = await f.Service.PrepareAsync("request", RequestOrigin.ActivatedVoice, () => f.Source, Token);
        var selection = await f.Service.SelectAsync(review, null, Token);
        var failure = await f.Service.Invoking(service => service.SubmitAsync(selection, Token))
            .Should().ThrowAsync<SessionRoutingUnconfirmedException>();
        failure.Which.Creation.Should().Be(selection.CreationRequest);
        failure.Which.Creation.Origin.Should().Be(RequestOrigin.ActivatedVoice);
        f.Messages.Should().BeEmpty();
        await f.Service.Invoking(service => service.SubmitAsync(selection, Token)).Should().ThrowAsync<InvalidOperationException>();
        f.Creates.Should().Be(1);
    }

    [Theory]
    [InlineData("source")]
    [InlineData("cancel")]
    [InlineData("storage")]
    public async Task FinalCanonicalBoundaryCannotCommitAfterOriginalSourceLoss(string fault)
    {
        var f = new Fixture();
        var review = await f.Service.PrepareAsync("request", RequestOrigin.LocalUi, () => f.Source, Token);
        var selection = await f.Service.SelectAsync(review, f.Entries[0].Authority.SessionId, Token);
        f.BeforeExactRead = () =>
        {
            if (fault is "source") { f.Source = false; }
            if (fault is "cancel") { throw new OperationCanceledException(); }
            if (fault is "storage") { throw new IOException("private storage unavailable"); }
        };
        await f.Service.Invoking(service => service.SubmitAsync(selection, Token)).Should().ThrowAsync<Exception>();
        f.Messages.Should().BeEmpty();
        f.Admissions.Should().Be(0);
    }

    [Theory]
    [InlineData("source")]
    [InlineData("control")]
    [InlineData("private")]
    [InlineData("permission")]
    [InlineData("metadata")]
    [InlineData("done")]
    [InlineData("retire")]
    [InlineData("cancel")]
    [InlineData("failure")]
    public async Task MatcherAwaitRevalidatesOriginalAuthorityAndInventoryWithoutFallback(string fault)
    {
        var f = new Fixture();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Matcher.Wait = async () => { entered.SetResult(); await release.Task; };
        var pending = f.Service.PrepareAsync("request", RequestOrigin.LocalUi, () => f.Source, cancellation.Token);
        await entered.Task;
        if (fault is "source") { f.Source = false; }
        if (fault is "control") { f.ControlRevision++; }
        if (fault is "private") { f.CanInspect = false; }
        if (fault is "permission") { f.CanControl = false; }
        if (fault is "metadata") { f.Entries[0] = f.Entries[0] with { Metadata = f.Entries[0].Metadata! with { Revision = new(2) } }; }
        if (fault is "done") { f.Entries[0] = f.Entries[0] with { Authority = f.Entries[0].Authority with { IsActive = false } }; }
        if (fault is "retire") { f.Service.Retire(); }
        if (fault is "cancel") { await cancellation.CancelAsync(); }
        if (fault is "failure") { f.Matcher.Failure = true; }
        release.SetResult();
        await this.Invoking(async _ => await pending).Should().ThrowAsync<Exception>();
        f.Messages.Should().BeEmpty();
        f.Creates.Should().Be(0);
    }

    [Theory]
    [InlineData("expiry")]
    [InlineData("generation")]
    [InlineData("metadata")]
    [InlineData("done")]
    [InlineData("control")]
    [InlineData("source")]
    [InlineData("retire")]
    public async Task DestinationChangesAfterAcknowledgmentCannotSubmitOrCreateFallback(string fault)
    {
        var f = new Fixture();
        var review = await f.Service.PrepareAsync("request", RequestOrigin.LocalUi, () => f.Source, Token);
        var selected = await f.Service.SelectAsync(review, f.Entries[0].Authority.SessionId, Token);
        if (fault is "expiry") { f.Time.Now = review.ExpiresAt; }
        if (fault is "generation") { f.Entries[0] = f.Entries[0] with { Authority = f.Entries[0].Authority with { Generation = new(2) } }; }
        if (fault is "metadata") { f.Entries[0] = f.Entries[0] with { Metadata = f.Entries[0].Metadata! with { Revision = new(2) } }; }
        if (fault is "done") { f.Entries[0] = f.Entries[0] with { Authority = f.Entries[0].Authority with { IsActive = false } }; }
        if (fault is "control") { f.ControlRevision++; }
        if (fault is "source") { f.Source = false; }
        if (fault is "retire") { f.Service.Retire(); }
        await f.Service.Invoking(service => service.SubmitAsync(selected, Token)).Should().ThrowAsync<InvalidOperationException>();
        f.Messages.Should().BeEmpty();
        f.Creates.Should().Be(0);
    }

    [Fact]
    public async Task ArchivedEntriesAreNeverProposedAndUnpresentedCorrectionIsDenied()
    {
        var f = new Fixture();
        f.Entries[1] = f.Entries[1] with { Authority = f.Entries[1].Authority with { IsActive = false } };
        var review = await f.Service.PrepareAsync("request", RequestOrigin.LocalUi, () => true, Token);
        review.Snapshot.Descriptors.Should().ContainSingle();
        await f.Service.Invoking(service => service.SelectAsync(review, f.Entries[1].Authority.SessionId, Token))
            .Should().ThrowAsync<InvalidOperationException>();
        f.Messages.Should().BeEmpty();
    }

    [Fact]
    public async Task FailedCorrectionCannotLeaveThePreviousDestinationSubmissionArmed()
    {
        var f = new Fixture();
        var review = await f.Service.PrepareAsync("request", RequestOrigin.LocalUi, () => true, Token);
        var previous = await f.Service.SelectAsync(review, f.Entries[0].Authority.SessionId, Token);
        await f.Service.Invoking(service => service.SelectAsync(review, new(Guid.NewGuid()), Token))
            .Should().ThrowAsync<InvalidOperationException>();
        await f.Service.Invoking(service => service.SubmitAsync(previous, Token)).Should().ThrowAsync<InvalidOperationException>();
        f.Messages.Should().BeEmpty();
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("default")]
    [InlineData("oversized")]
    [InlineData("metadata-owner")]
    [InlineData("metadata-revision")]
    [InlineData("empty-id")]
    [InlineData("generation")]
    public async Task MalformedHostInventoryDoesNotBecomeAnEmptyFallback(string fault)
    {
        var f = new Fixture();
        if (fault is "duplicate") { f.Entries.Add(f.Entries[0]); }
        if (fault is "default") { f.DefaultInventory = true; }
        if (fault is "oversized") { for (var i = 0; i < 50; i++) { f.Entries.Add(Fixture.Entry("Extra")); } }
        if (fault is "metadata-owner") { f.Entries[0] = f.Entries[0] with { Metadata = f.Entries[0].Metadata! with { SessionId = new(Guid.NewGuid()) } }; }
        if (fault is "metadata-revision") { f.Entries[0] = f.Entries[0] with { Metadata = f.Entries[0].Metadata! with { Revision = default } }; }
        if (fault is "empty-id") { f.Entries[0] = f.Entries[0] with { Authority = f.Entries[0].Authority with { SessionId = default } }; }
        if (fault is "generation") { f.Entries[0] = f.Entries[0] with { Authority = f.Entries[0].Authority with { Generation = default } }; }
        await f.Service.Invoking(service => service.PrepareAsync("request", RequestOrigin.LocalUi, () => true, Token))
            .Should().ThrowAsync<InvalidDataException>();
        f.Creates.Should().Be(0);
        f.Matcher.Snapshot.Should().BeNull();
    }

    private sealed class Clock : TimeProvider
    {
        internal DateTimeOffset Now { get; set; } = DateTimeOffset.UnixEpoch;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class Matcher : ISessionRelatednessMatcher
    {
        internal string? Body { get; private set; }
        internal SessionRouteSnapshot? Snapshot { get; private set; }
        internal Func<SessionRouteSnapshot, SessionRouteProposal> Propose { get; set; } =
            snapshot => new(snapshot.Revision, SessionRouteMatch.Clear, [snapshot.Descriptors[0].Candidate]);
        internal Func<Task>? Wait { get; set; }
        internal bool Failure { get; set; }
        public async Task<SessionRouteProposal> MatchAsync(string request, SessionRouteSnapshot snapshot, Func<bool> admitted, CancellationToken token)
        {
            Body = request; Snapshot = snapshot;
            if (Wait is { } wait) { await wait(); }
            if (Failure) { throw new IOException("Inference failed, not unavailable."); }
            return Propose(snapshot);
        }
    }

    private sealed class Fixture : ISessionWorkspaceStore, ISessionConversationStore, IHostTaskStore, IModelTurnAdmission,
        ISessionWorkspaceAccess, ISessionRoutingControlStore
    {
        internal List<SessionWorkspaceEntry> Entries { get; } = [Entry("Alpha"), Entry("Beta")];
        internal Dictionary<HostId<TaskIdentity>, HostTaskRecord> Tasks { get; } = [];
        internal List<ConversationMessage> Messages { get; } = [];
        internal Matcher Matcher { get; } = new();
        internal Clock Time { get; } = new();
        internal SessionWorkspaceService Workspace { get; }
        internal SessionConversationService Conversation { get; }
        internal SessionRoutingService Service { get; }
        internal bool Source { get; set; } = true;
        internal bool DefaultInventory { get; set; }
        internal Action? BeforeExactRead { get; set; }
        internal string? CreationFault { get; set; }
        internal Guid? Next { get; set; }
        internal int Creates { get; private set; }
        internal int Admissions { get; private set; }
        internal WorkSessionAuthorization? ControlSession { get; set; }
        internal bool CancelDispatch { get; set; }
        internal bool FailCancellationReceipt { get; set; }
        public bool CanInspect { get; set; } = true;
        public bool CanControl { get; set; } = true;
        public long ControlRevision { get; set; } = 1;
        public bool IsQuiescent => true;
        internal SessionRouteMatch Match
        {
            set => Matcher.Propose = snapshot => new(snapshot.Revision, value,
                value == SessionRouteMatch.Clear ? [snapshot.Descriptors[0].Candidate]
                    : value == SessionRouteMatch.Ambiguous ? [.. snapshot.Descriptors.Select(item => item.Candidate)] : []);
        }

        internal Fixture()
        {
            Workspace = new(this, new(this), this, NullLogger<SessionWorkspaceService>.Instance);
            Conversation = new(this, this, Workspace, this, this, Time, NullLogger<SessionConversationService>.Instance);
            Service = new(Workspace, Conversation, this, Time, Matcher);
        }
        internal static SessionWorkspaceEntry Entry(string name)
        {
            var id = new HostId<SessionIdentity>(Guid.NewGuid());
            return new(new(id, new(1), true), new(id, new(1), new(name)));
        }
        public ValueTask<WorkSessionAuthorization> CreateRoutingControlSessionAsync(HostRequest request, Func<bool> admitted, CancellationToken token)
        {
            admitted().Should().BeTrue();
            ControlSession = new(request.SessionId, new(1), true);
            Entries.Add(new(ControlSession, null));
            return ValueTask.FromResult(ControlSession);
        }
        public ValueTask<T> WithRoutingControlSessionAsync<T>(HostRequest request, HostRevision generation, Func<T> operation, CancellationToken token)
        {
            if (ControlSession?.SessionId != request.SessionId || ControlSession.Generation != generation)
            { throw new InvalidOperationException("Stale routing control session."); }
            return ValueTask.FromResult(operation());
        }
        public ValueTask<HostTaskRecord> AdmitLocalContextDispatchAsync(HostRequest request, HostRevision generation,
            Func<bool> eligible, CancellationToken token)
        {
            eligible().Should().BeTrue();
            if (CancelDispatch) { throw new OperationCanceledException(); }
            var dispatched = Tasks[request.TaskId].Next(HostTaskState.DispatchRecorded);
            Tasks[request.TaskId] = dispatched;
            return ValueTask.FromResult(dispatched);
        }
        public ValueTask<SessionPage<SessionWorkspaceEntry>> ReadMetadataPageAsync(Guid? after, int limit, CancellationToken token)
        {
            after.Should().BeNull(); limit.Should().Be(50);
            return ValueTask.FromResult(new SessionPage<SessionWorkspaceEntry>(DefaultInventory ? default : [.. Entries], Next));
        }
        public ValueTask<SessionWorkspaceEntry> ReadMetadataAsync(HostId<SessionIdentity> session, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            BeforeExactRead?.Invoke();
            if (Creates > 0 && CreationFault is "readback-cancel") { throw new OperationCanceledException(); }
            var entry = Entries.Single(item => item.Authority.SessionId == session);
            if (Creates > 0 && CreationFault is "readback") { entry = entry with { Metadata = entry.Metadata! with { Revision = new(2) } }; }
            return ValueTask.FromResult(entry);
        }
        public ValueTask<HostTaskRecord> RecordControlIntentAsync(HostRequest request, CancellationToken token)
        {
            var task = new HostTaskRecord(request, new(1), HostTaskState.IntentRecorded);
            Tasks.Add(request.TaskId, task);
            return ValueTask.FromResult(task);
        }
        public ValueTask CommitAsync(HostTaskRecord record, long revision, CancellationToken token)
        {
            if (FailCancellationReceipt && record.State is HostTaskState.Unknown or HostTaskState.Cancelled)
            { throw new IOException("Cancellation receipt is unavailable."); }
            Tasks[record.Request.TaskId] = record;
            return ValueTask.CompletedTask;
        }
        public ValueTask<SessionWorkspaceEntry> CreateNamedSessionAsync(HostRequest request, SessionName name, Func<bool> admitted, CancellationToken token)
        {
            admitted().Should().BeTrue();
            Creates++;
            if (CreationFault is "create") { throw new IOException("creation outcome unavailable"); }
            if (CreationFault is "cancel") { throw new OperationCanceledException(); }
            var entry = new SessionWorkspaceEntry(new(request.SessionId, new(1), true), new(request.SessionId, new(1), name));
            Entries.Add(entry);
            if (CreationFault is "source") { Source = false; }
            return ValueTask.FromResult(CreationFault switch
            {
                "foreign" => entry with { Authority = entry.Authority with { SessionId = new(Guid.NewGuid()) } },
                "inactive" => entry with { Authority = entry.Authority with { IsActive = false } },
                _ => entry,
            });
        }
        public ValueTask<ConversationSubmissionReceipt> RetainConversationMessageAsync(HostRequest request, HostRevision generation,
            long revision, string body, Func<bool> admitted, CancellationToken token)
        {
            admitted().Should().BeTrue();
            var message = new ConversationMessage(request, generation, Messages.Count + 1, 1, Time.Now, body, null, null);
            Messages.Add(message);
            var task = new HostTaskRecord(request, new(1), HostTaskState.IntentRecorded);
            Tasks.Add(request.TaskId, task);
            return ValueTask.FromResult(new ConversationSubmissionReceipt(message, task));
        }
        public Task<ModelTurnAdmission> AdmitAsync(ModelTurnChoice choice, ModelContextEnvelope context, ModelHandoffReview? review, CancellationToken token)
        { Admissions++; return Task.FromResult(new ModelTurnAdmission(new(ModelTurnOutcome.Unavailable, ModelTurnReason.UnregisteredModel))); }
        public ValueTask<ConversationSubmissionReceipt> CompleteConversationSubmissionAsync(HostRequest request, ModelTurnOutcome outcome,
            ModelTurnReason reason, Func<bool> admitted, CancellationToken token)
        {
            admitted().Should().BeTrue();
            var index = Messages.FindIndex(message => message.Request == request);
            var message = Messages[index] with { Revision = 2, Outcome = outcome, Reason = reason };
            Messages[index] = message;
            var task = Tasks[request.TaskId].Next(message.ExpectedTaskState);
            Tasks[request.TaskId] = task;
            return ValueTask.FromResult(new ConversationSubmissionReceipt(message, task));
        }
        public ValueTask<HostTaskRecord?> ReadTaskAsync(HostId<TaskIdentity> task, CancellationToken token) =>
            ValueTask.FromResult(Tasks.GetValueOrDefault(task));
        public ValueTask<HostTaskObservation?> ReadTaskAsync(HostId<SessionIdentity> session, HostId<TaskIdentity> task, CancellationToken token)
        {
            if (!Tasks.TryGetValue(task, out var record) || Creates > 0 && CreationFault is "task") { return ValueTask.FromResult<HostTaskObservation?>(null); }
            if (Creates > 0)
            {
                record = CreationFault switch
                {
                    "request" => new(HostRequest.Create(RequestOrigin.ActivatedVoice), record.Revision, record.State),
                    "state" => new(record.Request, record.Revision, HostTaskState.Denied),
                    "revision" => new(record.Request, new(3), record.State),
                    _ => record,
                };
            }
            return ValueTask.FromResult<HostTaskObservation?>(new(record, new(1), "host", true, null));
        }
        public ValueTask<ConversationMessage> ReadConversationMessageAsync(HostId<SessionIdentity> session, HostId<RequestIdentity> request, CancellationToken token) =>
            ValueTask.FromResult(Messages.Single(message => message.Request.SessionId == session && message.Request.RequestId == request));
        public ValueTask<IReadOnlyList<HostTaskRecord>> ReadIncompleteAsync(int limit, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<SessionDispositionPreview> PreviewDispositionAsync(HostId<SessionIdentity> session, HostRevision generation, long revision, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<SessionDispositionReceipt> DisposeSessionAsync(HostRequest request, SessionDispositionPreview preview, Func<bool> admitted, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<SessionPage<WorkSessionAuthorization>> ReadSessionsAsync(Guid? after, int limit, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<HostTaskObservation> CancelWaitingTaskAsync(HostRequest request, HostTaskCancellationTarget target, Func<bool> admitted, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<SessionWorkspaceEntry> RenameSessionAsync(HostRequest request, HostRevision generation, long revision, SessionName name, Func<bool> admitted, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<SessionPage<Kora.Core.Interaction.HostQuestionRecord>> ReadQuestionPageAsync(HostId<SessionIdentity> session, Guid? after, int limit, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<SessionPage<HostTaskRecord>> ReadTaskPageAsync(HostId<SessionIdentity> session, Guid? after, int limit, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<WorkSessionAuthorization> ChangeIdleLifecycleAsync(HostRequest request, HostRevision generation, bool active, Func<bool> admitted, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<HostTaskRecord> RecordConversationCredentialRejectionAsync(HostRequest request, HostRevision generation, Func<bool> admitted, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<ConversationDraftSnapshot> ReadConversationDraftAsync(HostId<SessionIdentity> session, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<ConversationDraftSnapshot> CommitConversationDraftAsync(HostRequest request, ConversationDraftSnapshot expected, string? body, Func<bool> admitted, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<ConversationPage> ReadConversationPageAsync(HostId<SessionIdentity> session, ConversationCursor? cursor, int limit, CancellationToken token) => throw new NotSupportedException();
    }
}
