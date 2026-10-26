using System.Text.Json;
using System.Diagnostics;

using AwesomeAssertions;

using Kora.Application.Hosting.ReviewedContext;
using Kora.Core.Conversation;
using Kora.Core.Conversation.ReviewedContext;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Application.UnitTests.Dependencies;

public sealed partial class ModelTurnHostTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ExactAssistantReplyUsesCanonicalSingleEvidenceAndRetainsOnlyNewInputAndGenuineAnswer(bool qualified)
    {
        using var f = new Fixture();
        var source = Earlier(f);
        _ = Earlier(f, 2);
        var host = qualified ? QualifiedHistoryHost(f) : f.ConfiguredProfiles(production: true);
        var service = f.ConversationService(host, execution: host);
        using var workflow = service.CreateReplyContextWorkflow();
        const string body = "  expand the second point\r\n e\u0301  ";
        using var review = await workflow.ReviewReplyAsync(new(f.Session, null), body, source, static () => true, Token);
        review.Text.Should().Contain(source.AssistantResponse!.Body).And.Contain(body).And.NotContain(source.Body)
            .And.Contain("1 / 16 evidence items");
        f.Adapter.Calls.Should().Be(0);
        f.RetainCalls.Should().Be(0);
        var receipt = await workflow.ConfirmReplyAsync(review, static () => true, Token);
        receipt.Message.Body.Should().Be(body);
        receipt.Message.Request.SessionId.Should().Be(f.Session.SessionId);
        receipt.Message.Request.Origin.Should().Be(RequestOrigin.LocalUi);
        receipt.Message.Outcome.Should().Be(qualified ? ModelTurnOutcome.Succeeded : ModelTurnOutcome.Unavailable);
        receipt.Message.AssistantResponse?.Body.Should().Be(qualified ? "answer" : null);
        if (qualified)
        {
            using var wire = JsonDocument.Parse(f.Adapter.Wire!);
            wire.RootElement.GetProperty("UserRequest").GetString().Should().Be(body);
            wire.RootElement.GetProperty("Tools").GetArrayLength().Should().Be(0);
            wire.RootElement.GetProperty("ToolMode").GetInt32().Should().Be((int)ModelToolMode.None);
            var evidence = wire.RootElement.GetProperty("Evidence").EnumerateArray().Single();
            evidence.GetProperty("Disclosure").GetInt32().Should().Be((int)ModelEvidenceDisclosure.LocalOnly);
            using var data = JsonDocument.Parse(evidence.GetProperty("Content").GetString()!);
            data.RootElement.GetProperty("Sequence").GetInt64().Should().Be(source.Sequence);
            data.RootElement.GetProperty("Role").GetString().Should().Be("ASSISTANT");
            data.RootElement.GetProperty("Body").GetString().Should().Be(source.AssistantResponse.Body);
        }
        review.Text.Should().BeEmpty();
        await workflow.Invoking(value => value.ConfirmReplyAsync(review, static () => true, Token))
            .Should().ThrowAsync<InvalidOperationException>();
        f.RetainCalls.Should().Be(1);
        host.IsQuiescent.Should().BeTrue();
    }

    [Theory]
    [InlineData("source")]
    [InlineData("sequence")]
    [InlineData("revision")]
    [InlineData("answer")]
    [InlineData("original")]
    [InlineData("generation")]
    [InlineData("done")]
    [InlineData("private")]
    [InlineData("control")]
    [InlineData("expiry")]
    [InlineData("clock")]
    [InlineData("retire")]
    [InlineData("dispose")]
    [InlineData("cancel")]
    [InlineData("foreign-workflow")]
    public async Task ExactReplyRefusesChangedSourceTargetAuthorityOrExpiredSingleUseConsentBeforeRetention(string fault)
    {
        using var f = new Fixture();
        var source = Earlier(f);
        var host = QualifiedHistoryHost(f);
        var service = f.ConversationService(host, execution: host);
        using var workflow = service.CreateReplyContextWorkflow();
        var live = true;
        using var review = await workflow.ReviewReplyAsync(new(f.Session, null), "follow-up", source, () => live, Token);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        if (fault is "source") { f.HistoryMessages.Clear(); }
        if (fault is "sequence") { f.HistoryMessages[source.Request.RequestId] = source with { Sequence = 3 }; }
        if (fault is "revision") { f.HistoryMessages[source.Request.RequestId] = source with { Revision = 4 }; }
        if (fault is "answer")
        { f.HistoryMessages[source.Request.RequestId] = source with { AssistantResponse = new(source.Execution!, "changed exact answer") }; }
        if (fault is "original") { live = false; }
        if (fault is "generation") { f.Session = f.Session with { Generation = new(2) }; }
        if (fault is "done") { f.Session = f.Session with { IsActive = false }; }
        if (fault is "private") { f.CanControl = false; }
        if (fault is "control") { f.ControlRevision++; }
        if (fault is "expiry") { f.Time.Now = f.Time.Now.AddSeconds(60); }
        if (fault is "clock") { f.Time.Now = f.Time.Now.AddTicks(-1); }
        if (fault is "retire") { workflow.Retire(); }
        if (fault is "dispose") { workflow.Dispose(); }
        if (fault is "cancel") { cancellation.Cancel(); }
        using var foreign = service.CreateReplyContextWorkflow();
        var confirm = fault is "foreign-workflow" ? foreign : workflow;
        await confirm.Invoking(value => value.ConfirmReplyAsync(review, static () => true, cancellation.Token))
            .Should().ThrowAsync<Exception>();
        f.RetainCalls.Should().Be(0);
        f.Adapter.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData("foreign-session")]
    [InlineData("missing-answer")]
    [InlineData("invalid-body")]
    [InlineData("hosted-origin")]
    [InlineData("disposed")]
    public async Task ExactReplyRejectsInvalidOrUnadmittedSelectionWithoutSourceReadOrDispatch(string fault)
    {
        using var f = new Fixture();
        var source = Earlier(f);
        using var workflow = f.ConversationService(new TestAdmission(ModelTurnOutcome.Unavailable)).CreateReplyContextWorkflow();
        var target = new Kora.Core.Storage.SessionWorkspaceEntry(f.Session, null);
        var body = "follow-up";
        if (fault is "foreign-session") { target = target with { Authority = f.Session with { SessionId = new(Guid.NewGuid()) } }; }
        if (fault is "missing-answer") { source = source with { AssistantResponse = null }; }
        if (fault is "invalid-body") { body = new string('x', ConversationText.MaximumUtf8Bytes + 1); }
        if (fault is "disposed") { workflow.Dispose(); }
        using var root = fault is "hosted-origin"
            ? HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.HostSystem), HostActivityLayer.Application, HostOperation.Request) : null;
        await workflow.Invoking(value => value.ReviewReplyAsync(target, body, source, static () => true, Token))
            .Should().ThrowAsync<Exception>();
        f.HistoryReads.Should().Be(0);
        f.Adapter.Calls.Should().Be(0);
        f.RetainCalls.Should().Be(0);
    }

    [Fact]
    public async Task ExactReplyCancellationHoldsOwnershipUntilNonCooperativeProviderActuallyReturns()
    {
        using var f = new Fixture();
        var source = Earlier(f);
        var host = QualifiedHistoryHost(f);
        var service = f.ConversationService(host, execution: host);
        using var workflow = service.CreateReplyContextWorkflow();
        using var review = await workflow.ReviewReplyAsync(new(f.Session, null), "follow-up", source, static () => true, Token);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Adapter.OnTool = async _ => { entered.SetResult(); await release.Task; };
        var operation = workflow.ConfirmReplyAsync(review, static () => true, Token);
        await entered.Task.WaitAsync(Token);
        workflow.Retire();
        await operation.Invoking(async task => await task).Should().ThrowAsync<Exception>();
        host.IsQuiescent.Should().BeFalse("logical cancellation cannot release the physical provider");
        var joined = host.DisposeAsync().AsTask();
        joined.IsCompleted.Should().BeFalse();
        release.SetResult();
        await joined;
        host.IsQuiescent.Should().BeTrue();
        f.ExecutionCompletions.Should().Be(0);
        review.Text.Should().BeEmpty();
    }

    [Fact]
    public async Task ExactReplyReplacesUnconfirmedReviewWithoutRenewingOldSourceConsent()
    {
        using var f = new Fixture();
        var source = Earlier(f);
        var service = f.ConversationService(new TestAdmission(ModelTurnOutcome.Unavailable));
        using var workflow = service.CreateReplyContextWorkflow();
        using var first = await workflow.ReviewReplyAsync(new(f.Session, null), "first", source, static () => true, Token);
        using var second = await workflow.ReviewReplyAsync(new(f.Session, null), "second", source, static () => true, Token);
        first.Text.Should().BeEmpty();
        await workflow.Invoking(value => value.ConfirmReplyAsync(first, static () => true, Token))
            .Should().ThrowAsync<InvalidOperationException>();
        using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.HostSystem), HostActivityLayer.Application, HostOperation.Request);
        await workflow.Invoking(value => value.ConfirmReplyAsync(second, static () => true, Token))
            .Should().ThrowAsync<InvalidOperationException>();
        f.RetainCalls.Should().Be(0);
    }

    [Fact]
    public async Task ExactReplyRetiredAtCompletedSourceReviewCannotPublishStaleReview()
    {
        using var f = new Fixture();
        var source = Earlier(f);
        using var workflow = f.ConversationService(new TestAdmission(ModelTurnOutcome.Unavailable)).CreateReplyContextWorkflow();
        var retired = false;
        using var observer = new ActivityListener
        {
            ShouldListenTo = value => string.Equals(value.Name, "Kora.Application", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity =>
            {
                if (string.Equals(activity.OperationName, "session.request", StringComparison.Ordinal))
                { retired = true; workflow.Retire(); }
            },
        };
        ActivitySource.AddActivityListener(observer);
        await workflow.Invoking(value => value.ReviewReplyAsync(new(f.Session, null), "follow-up", source, static () => true, Token))
            .Should().ThrowAsync<OperationCanceledException>();
        retired.Should().BeTrue();
        f.RetainCalls.Should().Be(0);
    }

    [Fact]
    public async Task ExactReplyCannotStartAnotherReviewWhileOriginalConfirmationStillExecutes()
    {
        using var f = new Fixture();
        var source = Earlier(f);
        using var workflow = f.ConversationService(new TestAdmission(ModelTurnOutcome.Unavailable)).CreateReplyContextWorkflow();
        using var review = await workflow.ReviewReplyAsync(new(f.Session, null), "follow-up", source, static () => true, Token);
        Task<Kora.Application.Hosting.ConversationHistoryReview>? replacement = null;
        await workflow.Invoking(value => value.ConfirmReplyAsync(review, () =>
        {
            replacement ??= workflow.ReviewReplyAsync(new(f.Session, null), "replacement", source, static () => true, Token);
            return true;
        }, Token)).Should().ThrowAsync<Exception>();
        replacement.Should().NotBeNull();
        await replacement!.Invoking(async task => await task).Should().ThrowAsync<InvalidOperationException>();
        f.RetainCalls.Should().Be(0);
    }

    [Fact]
    public async Task ReplySubsetCannotAcquireUnselectedMemoryOrInstructionAuthority()
    {
        using var f = new Fixture();
        var source = Earlier(f);
        var memories = new ContextMemories(f);
        var (broker, catalogue, _) = InstructionBroker(f, new InstructionSource());
        var service = f.ConversationService(new TestAdmission(ModelTurnOutcome.Unavailable));
        var time = new MemoryReviewTime();
        using var replyWorkflow = service.CreateReplyContextWorkflow();
        using var mixed = new MixedContextWorkflow(service, new(memories, f, time), broker, time);
        using var noInstruction = new MixedContextWorkflow(service, new(memories, f, time), null, time);
        using var review = await mixed.ReviewAsync(new(f.Session, null), "question",
            [new(source, ConversationHistoryRole.Assistant)], catalogue.Artifacts.Single(), memories.Selection, static () => true, Token);
        await replyWorkflow.Invoking(value => value.ReviewAsync(new(f.Session, null), "question",
            [new(source, ConversationHistoryRole.Assistant)], catalogue.Artifacts.Single(), memories.Selection, static () => true, Token))
            .Should().ThrowAsync<InvalidOperationException>();
        await noInstruction.Invoking(value => value.ReviewAsync(new(f.Session, null), "question",
            [new(source, ConversationHistoryRole.Assistant)], catalogue.Artifacts.Single(), memories.Selection, static () => true, Token))
            .Should().ThrowAsync<InvalidOperationException>();
        await replyWorkflow.Invoking(value => value.ConfirmAsync(review, static () => true, Token))
            .Should().ThrowAsync<InvalidOperationException>();
        f.RetainCalls.Should().Be(0);
        f.Adapter.Calls.Should().Be(0);
    }
}
