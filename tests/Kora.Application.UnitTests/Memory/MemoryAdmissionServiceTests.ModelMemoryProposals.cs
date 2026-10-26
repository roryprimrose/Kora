using System.Text.Json;

using AwesomeAssertions;

using Kora.Application.Documentation;
using Kora.Application.Memory;
using Kora.Application.Memory.ModelProposals;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Memory;
using Kora.Core.Memory.ModelProposals;
using Kora.Core.Storage;

namespace Kora.Application.UnitTests.Memory;

public sealed partial class MemoryAdmissionServiceTests
{
    private const string ModelProposalText = """{"version":1,"class":"responsePreference","value":"Prefer short answers."}""";

    [Theory]
    [InlineData("explicitFact", MemoryContentClass.ExplicitFact)]
    [InlineData("responsePreference", MemoryContentClass.ResponsePreference)]
    [InlineData("workflowPreference", MemoryContentClass.WorkflowPreference)]
    [InlineData("decision", MemoryContentClass.Decision)]
    public void ModelProposalParsesOnlyOneStrictInertBoundedCandidate(string classification, MemoryContentClass expected)
    {
        var text = ModelProposalText.Replace("responsePreference", classification, StringComparison.Ordinal);
        ModelMemoryProposal.Parse(text).Should().Be(new MemoryCandidate(expected, "Prefer short answers."));
        ModelMemoryProposal.ModelPolicy.Should().Contain("original user explicitly requests").And.Contain("session-only")
            .And.Contain("inert lower-trust").And.Contain("No other fields");
    }

    [Theory]
    [InlineData("ordinary answer")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("""{"version":1,"class":"secret","value":"no"}""")]
    [InlineData("""{"version":1,"class":"health","value":"no"}""")]
    [InlineData("""{"version":1,"class":"inferredTrait","value":"no"}""")]
    [InlineData("""{"version":1,"class":"modelClaim","value":"no"}""")]
    [InlineData("""{"version":2,"class":"decision","value":"no"}""")]
    [InlineData("""{"version":"1","class":"decision","value":"no"}""")]
    [InlineData("""{"version":1,"class":1,"value":"no"}""")]
    [InlineData("""{"version":1,"class":"decision","value":null}""")]
    [InlineData("""{"version":1,"class":"decision","value":{}}""")]
    [InlineData("""{"version":1,"class":"decision","value":""}""")]
    [InlineData("""{"version":1,"class":"decision","value":"\n"}""")]
    [InlineData("""{"version":1,"class":"decision","value":"ok","scope":"deviceProfile"}""")]
    [InlineData("""{"version":1,"class":"decision","value":"ok","value":"replacement"}""")]
    [InlineData("""{"version":1,"class":"decision","value":"ok"} {}""")]
    [InlineData("""{"version":1,"class":"decision","value":"ok",}""")]
    public void ModelProposalRejectsMalformedHostileAndPromotedData(string text)
    {
        FluentActions.Invoking(() => ModelMemoryProposal.Parse(text)).Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void ModelProposalEnforcesActualCharacterByteAndCompletePayloadLimitsWithoutTruncation()
    {
        static string Text(string value) => JsonSerializer.Serialize(new { version = 1, @class = "decision", value });
        ModelMemoryProposal.Parse(Text(new string('x', 512))).Value!.Length.Should().Be(512);
        FluentActions.Invoking(() => ModelMemoryProposal.Parse(Text(new string('x', 513)))).Should().Throw<InvalidDataException>();
        var raw = "{\"version\":1,\"class\":\"decision\",\"value\":\"";
        ModelMemoryProposal.Parse(raw + new string('\u754c', 330) + "\"}").Value!.Length.Should().Be(330);
        FluentActions.Invoking(() => ModelMemoryProposal.Parse(raw + new string('\u754c', 341) + "\"}"))
            .Should().Throw<InvalidDataException>().WithMessage("*PayloadLimitExceeded*");
        FluentActions.Invoking(() => ModelMemoryProposal.Parse(raw + new string('\u754c', 342) + "\"}"))
            .Should().Throw<InvalidDataException>();
        FluentActions.Invoking(() => ModelMemoryProposal.Parse(new string(' ', 2049))).Should().Throw<InvalidDataException>();
        FluentActions.Invoking(() => ModelMemoryProposal.Parse("\ud800")).Should().Throw<InvalidDataException>();
        FluentActions.Invoking(() => ModelMemoryProposal.Parse(null!)).Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData(RequestOrigin.LocalUi, RequestOrigin.LocalUi)]
    [InlineData(RequestOrigin.ActivatedVoice, RequestOrigin.LocalUi)]
    [InlineData(RequestOrigin.ActivatedVoice, RequestOrigin.ActivatedVoice)]
    public async Task ModelProposalRequiresFreshInspectAcceptAndSeparateSingleUseAdmissionWithOriginalLineage(
        RequestOrigin sourceOrigin, RequestOrigin inputOrigin)
    {
        using var f = new Fixture(durable: true);
        await using var service = f.Management();
        var source = f.ModelSource = CreateModelSource(f, sourceOrigin);
        ModelMemoryProposalReview review;
        using (f.Root(inputOrigin))
        { review = await service.ReviewModelProposalAsync(new(f.Session, null), source, () => true, Token); }
        using (review)
        {
            foreach (var operation in new[] { Kora.Core.Commands.MemoryCommandOperation.Review, Kora.Core.Commands.MemoryCommandOperation.Admit })
            {
                var metadata = (await service.ExecuteAsync(new(Kora.Core.Commands.MemoryCommandOperation.List, source.Request.SessionId.Value),
                    RequestOrigin.LocalUi, () => true, Token)).Memories.Single();
                await FluentActions.Awaiting(() => service.ExecuteAsync(new(operation, source.Request.SessionId.Value,
                    metadata.Id, metadata.Revision, Accept: true), RequestOrigin.LocalUi, () => true, Token))
                    .Should().ThrowAsync<InvalidOperationException>().WithMessage("*source-bound*");
            }
            f.Stored.Should().BeEmpty();
            review.CanAccept.Should().BeTrue();
            review.CanAdmit.Should().BeFalse();
            review.Description.Should().Contain(source.Body).And.Contain("Prefer short answers.")
                .And.Contain(source.Execution!.Turn.Value.ToString("D")).And.Contain("ResponsePreference")
                .And.Contain(sourceOrigin.ToString()).And.Contain("SESSION only");
            using (f.Root(inputOrigin))
            {
                (await service.ActOnModelProposalAsync(review, false, true, Token)).Record!.Review.Should().Be(MemoryReviewState.Reviewed);
                var replay = () => service.ActOnModelProposalAsync(review, true, false, Token);
                await replay.Should().ThrowAsync<InvalidOperationException>();
            }
            f.Stored.Should().BeEmpty();
            review.CanAdmit.Should().BeTrue();
            MemoryResult result;
            using (f.Root(inputOrigin))
            { result = await service.ActOnModelProposalAsync(review, true, false, Token); }
            result.Outcome.Should().Be(MemoryOutcome.Succeeded);
            result.Use.Should().BeNull();
            f.Stored.Values.Single().Should().Be(result.Record);
            result.Record!.Lineage.Request.Should().Be(source.Request);
            result.Record.Lineage.Origin.Should().Be(MemoryProposalOrigin.Model);
            result.Record.Scope.Should().Be(MemoryScope.Session(source.Request.SessionId));
            result.Record.Candidate.Should().Be(new MemoryCandidate(MemoryContentClass.ResponsePreference, "Prefer short answers."));
            review.Description.Should().BeEmpty();
            using (f.Root())
            {
                await FluentActions.Awaiting(() => service.ActOnModelProposalAsync(review, true, false, Token))
                    .Should().ThrowAsync<InvalidOperationException>();
                await FluentActions.Awaiting(() => service.ReviewModelProposalAsync(new(f.Session, null), source, () => true, Token))
                    .Should().ThrowAsync<InvalidOperationException>().WithMessage("*already has*");
            }
            f.Audits.Select(item => item.ActionId).Should().NotContain("memory.use");
            f.Audits.Should().Contain(item => item.ActionId == "memory.admit" && item.Outcome == Kora.Core.Auditing.SecurityAuditOutcome.Succeeded);
        }
    }

    [Theory]
    [InlineData("reject")]
    [InlineData("cancel")]
    [InlineData("privacy")]
    [InlineData("control")]
    [InlineData("owner")]
    [InlineData("lifecycle")]
    [InlineData("withdraw")]
    [InlineData("source")]
    [InlineData("source-generation")]
    [InlineData("token")]
    [InlineData("clear-cache")]
    public async Task ModelProposalRevocationNeverAdmitsOrRetainsBody(string change)
    {
        using var f = new Fixture(durable: true);
        await using var service = f.Management();
        var source = f.ModelSource = CreateModelSource(f);
        var live = true;
        ModelMemoryProposalReview review;
        using (f.Root())
        { review = await service.ReviewModelProposalAsync(new(f.Session, null), source, () => live, Token); }
        using (review)
        {
            if (change is "reject")
            {
                using (f.Root())
                { (await service.ActOnModelProposalAsync(review, false, false, Token)).Record!.Review.Should().Be(MemoryReviewState.Rejected); }
            }
            else
            {
                using (f.Root()) { await service.ActOnModelProposalAsync(review, false, true, Token); }
                using var cancel = new CancellationTokenSource();
                switch (change)
                {
                    case "cancel": review.Dispose(); break;
                    case "privacy": f.InspectEnabled = false; break;
                    case "control": f.ControlRevision++; break;
                    case "owner": f.Current = false; break;
                    case "lifecycle": f.Session = f.Session with { IsActive = false }; break;
                    case "withdraw": live = false; break;
                    case "source": f.ModelSource = source with { Body = "different retained body" }; break;
                    case "source-generation": f.ModelSource = source with { Generation = new(2) }; break;
                    case "token": cancel.Cancel(); break;
                    case "clear-cache": service.ClearVolatile(); break;
                }
                using (f.Root())
                {
                    await FluentActions.Awaiting(() => service.ActOnModelProposalAsync(review, true, false, cancel.Token))
                        .Should().ThrowAsync<Exception>();
                }
            }
            f.Stored.Should().BeEmpty();
            review.CanAdmit.Should().BeFalse();
        }
    }

    [Theory]
    [InlineData("system")]
    [InlineData("completed")]
    [InlineData("stopped")]
    [InlineData("invocation")]
    [InlineData("source-input")]
    [InlineData("foreign-session")]
    [InlineData("forged-source")]
    [InlineData("unsuccessful")]
    [InlineData("malformed")]
    [InlineData("not-retained")]
    [InlineData("unavailable")]
    public async Task ModelProposalRejectsUntrustedSourceAndInputBeforeMemoryMutation(string change)
    {
        using var f = new Fixture(durable: true);
        await using var service = f.Management();
        var source = f.ModelSource = CreateModelSource(f);
        if (change is "forged-source") { source = source with { Body = "invented user" }; }
        if (change is "unsuccessful") { source = source with { AssistantResponse = null, Outcome = ModelTurnOutcome.Denied }; }
        if (change is "malformed") { source = source with { AssistantResponse = source.AssistantResponse! with { Body = "ordinary answer" } }; }
        if (change is "not-retained") { f.ModelSource = null; }
        if (change is "unavailable") { f.Current = false; }
        var request = change is "source-input" ? source.Request
            : change is "foreign-session" ? HostRequest.Create(RequestOrigin.LocalUi)
            : new HostRequest(new(Guid.NewGuid()), f.Session.SessionId, new(Guid.NewGuid()),
                change is "system" ? RequestOrigin.HostSystem : RequestOrigin.LocalUi);
        if (change is "invocation")
        { request = new(request.RequestId, request.SessionId, request.TaskId, request.Origin, new(Guid.NewGuid())); }
        using var root = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request);
        if (change is "completed") { root.Complete(HostOperationOutcome.Completed); }
        if (change is "stopped") { root.Activity!.Stop(); }
        await FluentActions.Awaiting(() => service.ReviewModelProposalAsync(new(f.Session, null), source, () => true, Token))
            .Should().ThrowAsync<Exception>();
        f.Stored.Should().BeEmpty();
        f.Audits.Should().NotContain(item => item.ActionId == "memory.propose");
    }

    [Fact]
    public void ModelMemoryGuideIsEmbeddedAndSeparatesReviewAdmissionUseAndProviderEnablement()
    {
        var guide = new EmbeddedUserDocumentationProvider().GetPages()
            .Single(page => string.Equals(page.Id, "model-memory-proposals", StringComparison.Ordinal)).Markdown;
        guide.Should().Contain("session-only").And.Contain("Accept").And.Contain("Admit").And.Contain("Reject")
            .And.Contain("no automatic learning").And.Contain("unavailable").And.Contain("original channel");
    }

    [Theory]
    [InlineData("expire")]
    [InlineData("admission")]
    [InlineData("terminal")]
    [InlineData("audit")]
    [InlineData("denied")]
    [InlineData("owner")]
    [InlineData("closed-service")]
    public async Task ModelProposalFailuresAndExpiredReviewsRetireTheExactVolatileCandidate(string failure)
    {
        using var f = new Fixture(durable: true);
        var clock = new ModelProposalClock();
        await using var service = f.Management(time: clock);
        var source = f.ModelSource = CreateModelSource(f);
        var live = true;
        ModelMemoryProposalReview review;
        using (f.Root())
        { review = await service.ReviewModelProposalAsync(new(f.Session, null), source, () => live, Token); }
        using (review)
        using (f.Root())
        {
            if (failure is "expire") { clock.Now = DateTimeOffset.UnixEpoch.AddSeconds(60); }
            if (failure is "admission") { live = false; }
            if (failure is "terminal") { f.AfterControlOutcome = () => live = false; }
            if (failure is "audit") { f.FailTerminalAudit = true; }
            if (failure is "denied")
            {
                var calls = 0;
                f.OnStoreTransition = () =>
                {
                    if (++calls == 2) { f.StoreResult = new(MemoryOutcome.Denied, MemoryReason.AuthorityClosed); }
                };
            }
            if (failure is "closed-service") { await service.DisposeAsync(); }
            if (failure is "owner")
            {
                await using var other = f.Management();
                await FluentActions.Awaiting(() => other.ActOnModelProposalAsync(review, false, true, Token))
                    .Should().ThrowAsync<InvalidOperationException>().WithMessage("*another owner*");
            }
            else if (failure is "denied")
            {
                (await service.ActOnModelProposalAsync(review, false, true, Token)).Outcome.Should().Be(MemoryOutcome.Denied);
                review.CanAccept.Should().BeFalse();
            }
            else
            {
                await FluentActions.Awaiting(() => service.ActOnModelProposalAsync(review, false, true, Token))
                    .Should().ThrowAsync<Exception>();
            }
            f.Stored.Should().BeEmpty();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ModelProposalSourceAndTerminalLateCompletionNeverResurrectsReview(bool afterProposal)
    {
        using var f = new Fixture(durable: true);
        await using var service = f.Management();
        var source = f.ModelSource = CreateModelSource(f);
        var live = true;
        using var root = f.Root();
        if (afterProposal)
        {
            f.AfterControlOutcome = () => live = false;
            await FluentActions.Awaiting(() => service.ReviewModelProposalAsync(new(f.Session, null), source, () => live, Token))
                .Should().ThrowAsync<InvalidOperationException>();
        }
        else
        {
            var read = new TaskCompletionSource<ConversationMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            f.ModelSourceRead = read.Task;
            var pending = service.ReviewModelProposalAsync(new(f.Session, null), source, () => live, Token);
            pending.IsCompleted.Should().BeFalse();
            live = false;
            read.SetResult(source);
            await FluentActions.Awaiting(() => pending).Should().ThrowAsync<InvalidOperationException>();
        }
        f.Stored.Should().BeEmpty();
        f.ModelSourceRead = null;
        f.AfterControlOutcome = null;
        live = true;
        (await service.ExecuteAsync(new(Kora.Core.Commands.MemoryCommandOperation.List, source.Request.SessionId.Value),
            RequestOrigin.LocalUi, () => true, Token)).Memories.Should().BeEmpty();
    }

    [Theory]
    [InlineData("user-origin")]
    [InlineData("foreign")]
    [InlineData("invocation")]
    [InlineData("system")]
    public async Task ModelProposalAdmissionRejectsInvalidOriginalSourceLineage(string change)
    {
        using var f = new Fixture();
        using var root = f.Root();
        var source = CreateModelSource(f).Request;
        source = change switch
        {
            "foreign" => HostRequest.Create(RequestOrigin.LocalUi),
            "invocation" => new(source.RequestId, source.SessionId, source.TaskId, source.Origin, new(Guid.NewGuid())),
            "system" => new(source.RequestId, source.SessionId, source.TaskId, RequestOrigin.HostSystem),
            _ => source,
        };
        var result = await f.Service.ProposeAsync(Candidate, f.Scope,
            change is "user-origin" ? MemoryProposalOrigin.User : MemoryProposalOrigin.Model, Token, source);
        result.Reason.Should().Be(MemoryReason.LineageUnknown);
        result.Record.Should().BeNull();
    }

    private sealed class ModelProposalClock : TimeProvider
    {
        internal DateTimeOffset Now { get; set; } = DateTimeOffset.UnixEpoch;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact]
    public async Task ModelProposalRejectsAStoppedActivityEvenIfAnExternalAmbientWriterRestoresIt()
    {
        using var f = new Fixture(durable: true);
        await using var service = f.Management();
        var source = f.ModelSource = CreateModelSource(f);
        var previous = System.Diagnostics.Activity.Current;
        using var input = f.Root();
        input.Activity!.Stop();
        System.Diagnostics.Activity.Current = input.Activity;
        try
        {
            await FluentActions.Awaiting(() => service.ReviewModelProposalAsync(new(f.Session, null), source, () => true, Token))
                .Should().ThrowAsync<InvalidOperationException>().WithMessage("*live host-resolved activity*");
            f.ControlIntents.Should().BeEmpty();
            f.Stored.Should().BeEmpty();
        }
        finally { System.Diagnostics.Activity.Current = previous; }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ModelProposalCancellationAndDisposalJoinAnOutstandingSourceReadBeforeReleasingOwnership(bool dispose)
    {
        using var f = new Fixture(durable: true);
        await using var service = f.Management();
        var source = f.ModelSource = CreateModelSource(f);
        using var cancel = new CancellationTokenSource();
        using var input = f.Root();
        var read = new TaskCompletionSource<ConversationMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.ModelSourceRead = read.Task;
        var pending = service.ReviewModelProposalAsync(new(f.Session, null), source, () => true, cancel.Token);
        pending.IsCompleted.Should().BeFalse();
        Task? closing = null;
        if (dispose)
        {
            closing = service.DisposeAsync().AsTask();
            closing.IsCompleted.Should().BeFalse();
        }
        else { cancel.Cancel(); }
        pending.IsCompleted.Should().BeFalse("requesting cancellation does not join the actual source read");
        read.SetResult(source);
        await FluentActions.Awaiting(() => pending).Should().ThrowAsync<Exception>();
        if (closing is not null) { await closing; }
        f.Stored.Should().BeEmpty();
        f.Audits.Should().NotContain(item => item.ActionId == "memory.propose");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ModelProposalCannotContinueAfterOriginalInputClosesDuringAnAwaitedSourceRead(bool stop)
    {
        using var f = new Fixture(durable: true);
        await using var service = f.Management();
        var source = f.ModelSource = CreateModelSource(f);
        using var input = f.Root();
        var read = new TaskCompletionSource<ConversationMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.ModelSourceRead = read.Task;
        var pending = service.ReviewModelProposalAsync(new(f.Session, null), source, () => true, Token);
        pending.IsCompleted.Should().BeFalse();
        if (stop) { input.Activity!.Stop(); }
        else { input.Complete(HostOperationOutcome.Completed); }
        read.SetResult(source);
        await FluentActions.Awaiting(() => pending).Should().ThrowAsync<InvalidOperationException>();
        f.Stored.Should().BeEmpty();
        f.Audits.Should().NotContain(item => item.ActionId == "memory.propose");
    }

    private static ConversationMessage CreateModelSource(Fixture f, RequestOrigin origin = RequestOrigin.LocalUi)
    {
        var request = new HostRequest(new(Guid.NewGuid()), f.Session.SessionId, new(Guid.NewGuid()), origin);
        var provenance = new ModelTurnProvenance(new(Guid.NewGuid()), request, ModelProviderSelection.OllamaCandidate);
        return new(request, f.Session.Generation, 1, 3, DateTimeOffset.UnixEpoch,
            "Explicitly propose one short-answer preference for this session.", ModelTurnOutcome.Succeeded, ModelTurnReason.None)
        { Version = 2, Execution = provenance, ExecutionMetadataRevision = 0, AssistantResponse = new(provenance, ModelProposalText) };
    }

    private sealed partial class Fixture : ISessionConversationStore
    {
        internal ConversationMessage? ModelSource { get; set; }
        internal Task<ConversationMessage>? ModelSourceRead { get; set; }
        public ValueTask<ConversationMessage> ReadConversationMessageAsync(HostId<SessionIdentity> session,
            HostId<RequestIdentity> request, CancellationToken token) => ModelSourceRead is { } read ? new(read) : ModelSource is { } source
                ? ValueTask.FromResult(source) : throw new InvalidOperationException("Retained source was removed.");
        public ValueTask<HostTaskRecord> RecordConversationCredentialRejectionAsync(HostRequest request, HostRevision generation, Func<bool> admitted, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<ConversationDraftSnapshot> ReadConversationDraftAsync(HostId<SessionIdentity> session, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<ConversationDraftSnapshot> CommitConversationDraftAsync(HostRequest control, ConversationDraftSnapshot expected, string? body, Func<bool> admitted, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<ConversationSubmissionReceipt> RetainConversationMessageAsync(HostRequest request, HostRevision generation, long expectedMetadataRevision, string body, Func<bool> admitted, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<ConversationSubmissionReceipt> CompleteConversationSubmissionAsync(HostRequest request, ModelTurnOutcome outcome, ModelTurnReason reason, Func<bool> admitted, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<ConversationPage> ReadConversationPageAsync(HostId<SessionIdentity> session, ConversationCursor? cursor, int limit, CancellationToken token) => throw new NotSupportedException();
    }
}
