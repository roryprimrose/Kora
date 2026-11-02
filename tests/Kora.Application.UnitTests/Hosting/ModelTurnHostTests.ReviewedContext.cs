using System.Text.Json;

using AwesomeAssertions;

using Kora.Application.Dependencies;
using Kora.Application.Hosting;
using Kora.Application.Hosting.ReviewedContext;
using Kora.Application.Memory;
using Kora.Core.Conversation;
using Kora.Core.Conversation.ReviewedContext;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Memory;

namespace Kora.Application.UnitTests.Dependencies;

public sealed partial class ModelTurnHostTests
{
    [Theory]
    [InlineData(RequestOrigin.LocalUi, true)]
    [InlineData(RequestOrigin.ActivatedVoice, true)]
    [InlineData(RequestOrigin.LocalUi, false)]
    public async Task MixedContextReviewsAllSourcesAndUnchangedRequestThenRetainsOneGenuineAnswer(
        RequestOrigin origin, bool qualified)
    {
        using var f = new Fixture(origin);
        var earlier = Earlier(f);
        var next = Earlier(f, 2);
        var source = new ContextMemories(f, 2);
        var (broker, catalogue, _) = InstructionBroker(f, new InstructionSource());
        var host = qualified ? QualifiedHistoryHost(f) : f.ConfiguredProfiles(production: true);
        var service = f.ConversationService(host, execution: host);
        var time = new MemoryReviewTime();
        using var workflow = service.CreateMixedContextWorkflow(new(source, f, time), broker);
        const string body = "  Explain these sources.\r\nKeep my input unchanged.  ";
        using var review = await workflow.ReviewAsync(new(f.Session, null), body,
            [new(next, ConversationHistoryRole.User), new(earlier, ConversationHistoryRole.Assistant),
                new(earlier, ConversationHistoryRole.User)], catalogue.Artifacts.Single(), source.Selection,
            static () => true, Token, origin);
        var retired = 0;
        review.Retired += () => retired++;
        review.Text.Should().Contain(earlier.Body).And.Contain(earlier.AssistantResponse!.Body)
            .And.Contain(next.Body).And.Contain("reviewed memory value").And.Contain(broker.Choices.Single().Name)
            .And.Contain(body).And.Contain("/ 32768 UTF-8 bytes").And.Contain("6 / 16 evidence items");
        review.CanConfirm.Should().BeTrue();
        f.RetainCalls.Should().Be(0);
        f.Adapter.Calls.Should().Be(0);
        var receipt = await workflow.ConfirmAsync(review, static () => true, Token);
        receipt.Message.Body.Should().Be(body);
        receipt.Message.Request.Origin.Should().Be(origin);
        receipt.Message.Outcome.Should().Be(qualified ? ModelTurnOutcome.Succeeded : ModelTurnOutcome.Unavailable);
        (receipt.Message.AssistantResponse?.Body).Should().Be(qualified ? "answer" : null);
        receipt.Message.SubmittedContext.Should().BeNull("mixed context is not a recorded history-only handoff");
        if (qualified)
        {
            using var wire = JsonDocument.Parse(f.Adapter.Wire!);
            wire.RootElement.GetProperty("UserRequest").GetString().Should().Be(body);
            wire.RootElement.GetProperty("Tools").GetArrayLength().Should().Be(0);
            wire.RootElement.GetProperty("ToolMode").GetInt32().Should().Be((int)ModelToolMode.None);
            var evidence = wire.RootElement.GetProperty("Evidence").EnumerateArray().ToArray();
            evidence.Should().HaveCount(6);
            evidence.Should().OnlyContain(item => item.GetProperty("Disclosure").GetInt32() == (int)ModelEvidenceDisclosure.LocalOnly);
            evidence.Select(item => item.TryGetProperty("HistorySource", out _)).Should().OnlyContain(value => !value);
            using var first = JsonDocument.Parse(evidence[0].GetProperty("Content").GetString()!);
            using var second = JsonDocument.Parse(evidence[1].GetProperty("Content").GetString()!);
            using var third = JsonDocument.Parse(evidence[2].GetProperty("Content").GetString()!);
            first.RootElement.GetProperty("Role").GetString().Should().Be("USER");
            second.RootElement.GetProperty("Role").GetString().Should().Be("ASSISTANT");
            third.RootElement.GetProperty("Sequence").GetInt64().Should().Be(2);
            evidence[3].GetProperty("Content").GetString().Should().Contain("Instruction");
            evidence[4].GetProperty("Id").GetProperty("Value").GetGuid().Should().Be(source.Selection[0].Id);
            evidence[5].GetProperty("Id").GetProperty("Value").GetGuid().Should().Be(source.Selection[1].Id);
            source.Reads.Should().BeGreaterThan(4);
        }
        review.Text.Should().BeEmpty();
        retired.Should().Be(1);
        review.CanConfirm.Should().BeFalse();
        await workflow.Invoking(value => value.ConfirmAsync(review, static () => true, Token))
            .Should().ThrowAsync<InvalidOperationException>();
        f.RetainCalls.Should().Be(1);
        f.Messages.Should().NotContain(message => message.Contains("reviewed memory value", StringComparison.Ordinal));
        host.IsQuiescent.Should().BeTrue();
    }

    [Theory]
    [InlineData("history")]
    [InlineData("instruction")]
    [InlineData("disabled")]
    [InlineData("memory")]
    [InlineData("original")]
    [InlineData("private")]
    [InlineData("control")]
    [InlineData("session")]
    [InlineData("done")]
    [InlineData("expiry")]
    [InlineData("cancel")]
    [InlineData("retire")]
    public async Task MixedContextRejectsSourceOrPrivateDriftBeforeRetentionOrTransport(string fault)
    {
        using var f = new Fixture();
        var earlier = Earlier(f);
        var memories = new ContextMemories(f);
        var source = new InstructionSource();
        var (broker, catalogue, revisions) = InstructionBroker(f, source);
        var host = QualifiedHistoryHost(f);
        var service = f.ConversationService(host, execution: host);
        var time = new MemoryReviewTime();
        using var workflow = new MixedContextWorkflow(service, new(memories, f, time), broker, time);
        var original = true;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var review = await workflow.ReviewAsync(new(f.Session, null), "question",
            [new(earlier, ConversationHistoryRole.User)], catalogue.Artifacts.Single(), memories.Selection,
            () => original, cancellation.Token);
        switch (fault)
        {
            case "history": f.HistoryMessages.Clear(); break;
            case "instruction": source.Snapshot = null; break;
            case "disabled": revisions.DisableExact(source.Snapshot!, catalogue); break;
            case "memory": memories.Invalidate(); break;
            case "original": original = false; break;
            case "private": f.CanControl = false; break;
            case "control": f.ControlRevision++; break;
            case "session": f.Session = f.Session with { Generation = new(2) }; break;
            case "done": f.Session = f.Session with { IsActive = false }; break;
            case "expiry": time.Now = time.Now.AddMinutes(2); break;
            case "cancel": cancellation.Cancel(); break;
            case "retire": workflow.Retire(); break;
        }
        await workflow.Invoking(value => value.ConfirmAsync(review, static () => true, Token))
            .Should().ThrowAsync<Exception>();
        review.Text.Should().BeEmpty();
        f.RetainCalls.Should().Be(0);
        f.Adapter.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("memory")]
    [InlineData("instruction")]
    [InlineData("history")]
    public async Task MixedContextSuppressesNonCooperativeLateAnswerAndHoldsResourcesUntilRealJoin(string fault)
    {
        using var f = new Fixture();
        var earlier = Earlier(f);
        var memories = new ContextMemories(f);
        var instruction = new InstructionSource();
        var (broker, catalogue, revisions) = InstructionBroker(f, instruction);
        var host = QualifiedHistoryHost(f);
        var service = f.ConversationService(host, execution: host);
        var time = new MemoryReviewTime();
        using var workflow = new MixedContextWorkflow(service, new(memories, f, time), broker, time);
        var review = await workflow.ReviewAsync(new(f.Session, null), "question",
            [new(earlier, ConversationHistoryRole.Assistant)], catalogue.Artifacts.Single(), memories.Selection,
            static () => true, Token);
        var pending = new TaskCompletionSource<ModelProviderReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Adapter.Pending = pending.Task;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var submission = workflow.ConfirmAsync(review, static () => true, cancellation.Token);
        f.Adapter.Calls.Should().Be(1);
        host.IsQuiescent.Should().BeFalse();
        if (fault is "cancel") { cancellation.Cancel(); }
        if (fault is "memory") { memories.Invalidate(); }
        if (fault is "instruction") { revisions.DisableExact(instruction.Snapshot!, catalogue); }
        if (fault is "history") { f.HistoryMessages.Clear(); }
        if (fault is "cancel" or "memory")
        {
            await ((Func<Task>)(async () => { await submission; })).Should().ThrowAsync<Exception>();
            host.IsQuiescent.Should().BeFalse("logical cancellation does not prove physical transport release");
            f.Retained!.AssistantResponse.Should().BeNull();
        }
        pending.SetResult(new(ModelTurnOutcome.Succeeded, ModelProviderSelection.OllamaCandidate, new("late answer", null)));
        if (fault is "instruction" or "history")
        { await ((Func<Task>)(async () => { await submission; })).Should().ThrowAsync<Exception>(); }
        await host.DisposeAsync();
        host.IsQuiescent.Should().BeTrue();
        f.ExecutionCompletions.Should().Be(0);
        f.Retained!.AssistantResponse.Should().BeNull();
        review.Text.Should().BeEmpty();
    }

    [Fact]
    public void MixedContextMeasuresWholeSerializedEnvelopeAndRejectsOneByteOrOneItemOverWithoutOmission()
    {
        using var f = new Fixture();
        var earlier = Earlier(f);
        var memories = new ContextMemories(f);
        ModelContextEnvelope Create(string instruction, IEnumerable<MemoryRecord>? selection = null) =>
            MixedConversationContext.CreateEnvelope(f.Request, "q", [new(earlier, ConversationHistoryRole.User)],
                "instruction", instruction, memories.Snapshot.Boundary, selection ?? memories.Snapshot.Records,
                f.Time.Now, f.Time.Now.AddSeconds(15));
        var baseEnvelope = Create("a");
        var exactInstruction = "a" + new string('x', 32768 - baseEnvelope.Serialize().Length);
        var exact = Create(exactInstruction);
        exact.Serialize().Length.Should().Be(32768);
        exact.Evidence.Should().HaveCount(3);
        ((Action)(() => Create(exactInstruction + "x"))).Should().Throw<InvalidDataException>();
        var many = new ContextMemories(f, 15);
        ((Action)(() => Create("a", many.Snapshot.Records))).Should().Throw<InvalidDataException>();
        ((Action)(() => Create(""))).Should().Throw<InvalidDataException>();
        ((Action)(() => Create("a", []))).Should().Throw<InvalidDataException>();
        ((Action)(() => Create("a", [memories.Snapshot.Records[0], memories.Snapshot.Records[0]])))
            .Should().Throw<InvalidDataException>();
        ((Action)(() => Create("a", [memories.Snapshot.Records[0] with { Scope = MemoryScope.DeviceProfile(new(Guid.NewGuid())) }])))
            .Should().Throw<InvalidDataException>();
        ((Action)(() => MixedConversationContext.CreateEnvelope(f.Request, "q",
            [new(earlier, ConversationHistoryRole.User)], "", "a", memories.Snapshot.Boundary,
            memories.Snapshot.Records, f.Time.Now, f.Time.Now.AddSeconds(15)))).Should().Throw<InvalidDataException>();
        ((Action)(() => Create("a", [memories.Snapshot.Records[0] with { Retention = MemoryRetentionState.Disabled }])))
            .Should().Throw<InvalidDataException>();
        ((Action)(() => Create("a", [null!]))).Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData("history-error")]
    [InlineData("history-cancel")]
    [InlineData("memory-error")]
    [InlineData("memory-cancel")]
    [InlineData("combined-count")]
    [InlineData("combined-cancel")]
    public async Task MixedContextFailedReviewNeverPublishesSourcesOrRetainsInput(string fault)
    {
        using var f = new Fixture();
        var earlier = Earlier(f);
        var source = new ContextMemories(f, fault is "combined-count" ? 15 : 1);
        var instruction = new InstructionSource
        {
            Snapshot = InstructionSnapshot(InstructionBody),
        };
        var (broker, catalogue, _) = InstructionBroker(f, instruction);
        var time = new MemoryReviewTime();
        var service = f.ConversationService(new TestAdmission(ModelTurnOutcome.Unavailable));
        using var workflow = new MixedContextWorkflow(service, new(source, f, time), broker, time);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        if (fault is "history-error") { f.HistoryMessages.Clear(); }
        if (fault is "history-cancel") { cancellation.Cancel(); }
        if (fault is "memory-error") { source.OnRead = () => throw new IOException("Memory source unavailable."); }
        if (fault is "memory-cancel") { source.OnRead = cancellation.Cancel; }
        if (fault is "combined-cancel")
        {
            // A cancellation raised after the source review is constructed must still retire all sources.
            time.OnCreateTimer = () => f.BeforeHistoryRead = cancellation.Cancel;
        }
        await workflow.Invoking(value => value.ReviewAsync(new(f.Session, null), "new question",
            [new(earlier, ConversationHistoryRole.User)], catalogue.Artifacts.Single(), source.Selection,
            static () => true, cancellation.Token)).Should().ThrowAsync<Exception>();
        f.RetainCalls.Should().Be(0);
        f.Adapter.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData(ModelTurnOutcome.Cancelled)]
    [InlineData(ModelTurnOutcome.Unknown)]
    public async Task MixedContextPreservesTruthfulTerminalHostFailuresWithoutAnAnswer(ModelTurnOutcome outcome)
    {
        using var f = new Fixture();
        using var root = f.Root();
        var earlier = Earlier(f);
        var source = new ContextMemories(f);
        var (broker, catalogue, _) = InstructionBroker(f, new InstructionSource());
        var time = new MemoryReviewTime();
        var service = f.ConversationService(new TestAdmission(outcome));
        using var workflow = new MixedContextWorkflow(service, new(source, f, time), broker, time);
        using var review = await workflow.ReviewAsync(new(f.Session, null), "question",
            [new(earlier, ConversationHistoryRole.User)], catalogue.Artifacts.Single(), source.Selection,
            static () => true, Token);
        var receipt = await workflow.ConfirmAsync(review, static () => true, Token);
        receipt.Message.Outcome.Should().Be(outcome);
        receipt.Message.AssistantResponse.Should().BeNull();
        review.Text.Should().BeEmpty();
        f.Adapter.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData("foreign-session")]
    [InlineData("foreign-channel")]
    [InlineData("disposed")]
    [InlineData("consumed-memory")]
    [InlineData("cancel-validation")]
    public async Task MixedContextDoesNotAllowEnvelopeRetargetingOrRenewConsumedSourceAuthority(string fault)
    {
        using var f = new Fixture();
        var earlier = Earlier(f);
        var source = new ContextMemories(f);
        var (broker, catalogue, _) = InstructionBroker(f, new InstructionSource());
        var time = new MemoryReviewTime();
        var service = f.ConversationService(new TestAdmission(ModelTurnOutcome.Unavailable));
        using var workflow = new MixedContextWorkflow(service, new(source, f, time), broker, time);
        var review = await workflow.ReviewAsync(new(f.Session, null), "question",
            [new(earlier, ConversationHistoryRole.User)], catalogue.Artifacts.Single(), source.Selection,
            static () => true, Token);
        if (fault is "consumed-memory")
        {
            review.Memory.TryConsume().Should().BeTrue();
            await workflow.Invoking(value => value.ConfirmAsync(review, static () => true, Token))
                .Should().ThrowAsync<InvalidOperationException>();
        }
        else if (fault is "cancel-validation")
        {
            f.BeforeHistoryRead = () => throw new OperationCanceledException("Original source read cancelled.");
            await workflow.Invoking(value => value.ConfirmAsync(review, static () => true, Token))
                .Should().ThrowAsync<OperationCanceledException>();
        }
        else
        {
            if (fault is "disposed") { review.Dispose(); }
            var request = fault is "foreign-session" ? HostRequest.Create(RequestOrigin.LocalUi)
                : fault is "foreign-channel" ? new(f.Request.RequestId, f.Request.SessionId, f.Request.TaskId, RequestOrigin.ActivatedVoice) : f.Request;
            review.Invoking(value => value.CreateEnvelope(request, f.Time.Now)).Should().Throw<InvalidOperationException>();
        }
        review.Dispose();
        review.Invoking(value => _ = value.Instruction).Should().Throw<InvalidOperationException>();
        f.RetainCalls.Should().Be(0);
        f.Adapter.Calls.Should().Be(0);
    }

    [Fact]
    public async Task MixedContextRetirementWhileMaterializingPreviewCannotPublishAnOlderReview()
    {
        using var f = new Fixture();
        var earlier = Earlier(f);
        var source = new ContextMemories(f);
        var (broker, catalogue, _) = InstructionBroker(f, new InstructionSource());
        var time = new MixedPublicationClock();
        var service = f.ConversationService(new TestAdmission(ModelTurnOutcome.Unavailable));
        using var workflow = new MixedContextWorkflow(service, new(source, f, time), broker, time);
        time.BeforePublication = workflow.Retire;
        await workflow.Invoking(value => value.ReviewAsync(new(f.Session, null), "question",
            [new(earlier, ConversationHistoryRole.User)], catalogue.Artifacts.Single(), source.Selection,
            static () => true, Token)).Should().ThrowAsync<OperationCanceledException>();
        time.Revoked.Should().BeTrue();
        f.RetainCalls.Should().Be(0);
        f.Adapter.Calls.Should().Be(0);
    }

    private sealed class MixedPublicationClock : TimeProvider
    {
        private readonly MemoryReviewTime source = new();
        private int reads;
        private bool armed;
        internal Action? BeforePublication { get; set; }
        internal bool Revoked { get; private set; }
        public override DateTimeOffset GetUtcNow()
        {
            // After timer registration: envelope timestamp, current check, snapshot, records, then request preview.
            if (armed && ++reads == 5) { Revoked = true; BeforePublication!(); }
            return source.GetUtcNow();
        }
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = source.CreateTimer(callback, state, dueTime, period);
            armed = true;
            return timer;
        }
    }
}
