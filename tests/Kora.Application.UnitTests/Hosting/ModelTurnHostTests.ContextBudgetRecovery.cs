using System.Text;
using System.Text.Json;

using AwesomeAssertions;

using Kora.Application.Hosting.ReviewedContext;
using Kora.Application.Memory;
using Kora.Core.Conversation;
using Kora.Core.Conversation.ReviewedContext;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;

namespace Kora.Application.UnitTests.Dependencies;

public sealed partial class ModelTurnHostTests
{
    private static ConversationHistorySelection[] BudgetHistory(Fixture f)
    {
        return [.. Enumerable.Range(1, 8).Select(index =>
        {
            var message = Earlier(f, index) with { Body = new string('h', 3800) };
            f.HistoryMessages[message.Request.RequestId] = message;
            return new ConversationHistorySelection(message, ConversationHistoryRole.User);
        })];
    }

    [Theory]
    [InlineData(RequestOrigin.LocalUi, false)]
    [InlineData(RequestOrigin.ActivatedVoice, false)]
    [InlineData(RequestOrigin.LocalUi, true)]
    public async Task BudgetRecoveryPreservesExactInputAndChoicesUntilExplicitWholeOmissionThenFreshReviewAndSeparateRealAnswer(
        RequestOrigin origin, bool omitMemoryFirst)
    {
        using var f = new Fixture(origin);
        var history = BudgetHistory(f);
        var originals = f.HistoryMessages.Values.ToArray();
        var memories = new ContextMemories(f, 2);
        var (broker, catalogue, _) = InstructionBroker(f, new InstructionSource());
        var host = QualifiedHistoryHost(f);
        var time = new MemoryReviewTime();
        using var workflow = new MixedContextWorkflow(f.ConversationService(host, execution: host), new(memories, f, time), broker, time);
        const string body = "  Original e\u0301 \U0001f600 \"<question>\"\r\n  ";
        using var over = await workflow.ReviewAsync(new(f.Session, null), body, history.Reverse(),
            catalogue.Artifacts.Single(), memories.Selection, static () => true, Token, origin);
        var retired = 0;
        over.Retired += () => retired++;
        over.InputUtf8Bytes.Should().BeGreaterThan(ModelContextEnvelope.MaximumInputUtf8Bytes);
        over.CanConfirm.Should().BeFalse();
        over.Body.Should().Be(body);
        over.History.Should().Equal(history);
        over.Memory.Selection.Should().Equal(memories.Selection);
        over.Text.Should().Contain("OVER BUDGET").And.Contain(body).And.Contain("Nothing was submitted or omitted").And.Contain("required");
        f.RetainCalls.Should().Be(0);
        f.Adapter.Calls.Should().Be(0);
        await workflow.Invoking(value => value.ConfirmAsync(over, static () => true, Token))
            .Should().ThrowAsync<InvalidOperationException>();
        var unit = over.Contributions.First(item => omitMemoryFirst ? item.MemoryId is not null : item.History is not null);
        time.Now = time.Now.AddSeconds(90);
        var replacement = await workflow.RemoveWholeContributionAsync(over, unit, static () => true, Token);
        over.Text.Should().BeEmpty();
        retired.Should().Be(1);
        over.Contributions.Should().BeEmpty();
        over.CanConfirm.Should().BeFalse();
        await workflow.Invoking(value => value.ConfirmAsync(over, static () => true, Token))
            .Should().ThrowAsync<InvalidOperationException>();
        if (omitMemoryFirst)
        {
            replacement.Memory.Records.Should().HaveCount(1);
            replacement.Memory.Selection.Should().HaveCount(1);
            var prior = replacement;
            replacement = await workflow.RemoveWholeContributionAsync(prior,
                prior.Contributions.First(item => item.History is not null), static () => true, Token);
            prior.Text.Should().BeEmpty();
        }
        using var valid = replacement;
        valid.CanConfirm.Should().BeTrue();
        valid.Body.Should().Be(body);
        valid.History.Should().Equal(history.Skip(1));
        valid.Text.Should().Contain(body).And.Contain("local-only tool-free").And.NotContain("OVER BUDGET");
        valid.InputUtf8Bytes.Should().BeLessThan(ModelContextEnvelope.MaximumInputUtf8Bytes);
        f.Adapter.Calls.Should().Be(0);
        f.RetainCalls.Should().Be(0);
        var receipt = await workflow.ConfirmAsync(valid, static () => true, Token);
        receipt.Message.Body.Should().Be(body);
        receipt.Message.Request.Origin.Should().Be(origin);
        receipt.Message.AssistantResponse!.Body.Should().Be("answer");
        f.RetainCalls.Should().Be(1);
        f.Adapter.Calls.Should().Be(1);
        f.Adapter.Wire!.Length.Should().Be(valid.InputUtf8Bytes);
        using var wire = JsonDocument.Parse(f.Adapter.Wire);
        wire.RootElement.GetProperty("UserRequest").GetString().Should().Be(body);
        wire.RootElement.GetProperty("Tools").GetArrayLength().Should().Be(0);
        wire.RootElement.GetProperty("Evidence").GetArrayLength().Should().Be(omitMemoryFirst ? 9 : 10);
        f.HistoryMessages.Values.Should().Equal(originals);
        host.IsQuiescent.Should().BeTrue();
        f.Messages.Should().NotContain(value => value.Contains(body, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("expiry")]
    [InlineData("timer")]
    [InlineData("cancel")]
    [InlineData("retire")]
    [InlineData("private")]
    [InlineData("control")]
    [InlineData("original")]
    [InlineData("instruction")]
    [InlineData("disabled")]
    [InlineData("memory")]
    [InlineData("history")]
    [InlineData("session")]
    [InlineData("done")]
    [InlineData("late")]
    public async Task BudgetRecoveryCannotRenewExpiredOrRevokedReadsOrPublishStaleReduction(string fault)
    {
        using var f = new Fixture();
        var history = BudgetHistory(f);
        var memories = new ContextMemories(f);
        var instruction = new InstructionSource();
        var (broker, catalogue, revisions) = InstructionBroker(f, instruction);
        var host = QualifiedHistoryHost(f);
        var time = new MemoryReviewTime();
        using var workflow = new MixedContextWorkflow(f.ConversationService(host, execution: host), new(memories, f, time), broker, time);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var live = true;
        using var over = await workflow.ReviewAsync(new(f.Session, null), "original", history,
            catalogue.Artifacts.Single(), memories.Selection, () => live, cancellation.Token);
        var unit = over.Contributions.First(item => item.CanOmit);
        switch (fault)
        {
            case "expiry": time.Now = time.Now.AddMinutes(2); break;
            case "timer": time.Expire(); break;
            case "cancel": cancellation.Cancel(); break;
            case "retire": workflow.Retire(); break;
            case "private": f.CanControl = false; break;
            case "control": f.ControlRevision++; break;
            case "original": live = false; break;
            case "instruction": instruction.Snapshot = null; break;
            case "disabled": revisions.DisableExact(instruction.Snapshot!, catalogue); break;
            case "memory": memories.Invalidate(); break;
            case "history": f.HistoryMessages.Clear(); break;
            case "session": f.Session = f.Session with { Generation = new(2) }; break;
            case "done": f.Session = f.Session with { IsActive = false }; break;
            case "late": f.BeforeHistoryRead = workflow.Retire; break;
        }
        await workflow.Invoking(value => value.RemoveWholeContributionAsync(over, unit, static () => true, Token))
            .Should().ThrowAsync<Exception>();
        over.CanConfirm.Should().BeFalse();
        over.Text.Should().BeEmpty();
        f.RetainCalls.Should().Be(0);
        f.Adapter.Calls.Should().Be(0);
    }

    [Fact]
    public async Task BudgetRecoveryDeadlineRemainsOriginalAfterNewReviewAndRequiredUnitsCannotBeRemoved()
    {
        using var f = new Fixture();
        var history = BudgetHistory(f);
        var memories = new ContextMemories(f);
        var (broker, catalogue, _) = InstructionBroker(f, new InstructionSource());
        var time = new MemoryReviewTime();
        using var workflow = new MixedContextWorkflow(f.ConversationService(new TestAdmission(ModelTurnOutcome.Unavailable)),
            new(memories, f, time), broker, time);
        using var over = await workflow.ReviewAsync(new(f.Session, null), "original", history,
            catalogue.Artifacts.Single(), memories.Selection, static () => true, Token);
        foreach (var required in over.Contributions.Where(item => !item.CanOmit))
        {
            await workflow.Invoking(value => value.RemoveWholeContributionAsync(over, required, static () => true, Token))
                .Should().ThrowAsync<InvalidOperationException>();
        }
        time.Now = time.Now.AddSeconds(119);
        using var reduced = await workflow.RemoveWholeContributionAsync(over,
            over.Contributions.First(item => item.CanOmit), static () => true, Token);
        reduced.CanConfirm.Should().BeTrue();
        time.Now = time.Now.AddSeconds(1);
        reduced.CanConfirm.Should().BeFalse();
        reduced.Text.Should().BeEmpty();
        await workflow.Invoking(value => value.RemoveWholeContributionAsync(reduced,
            new("forged", history[0], null), static () => true, Token)).Should().ThrowAsync<InvalidOperationException>();
        f.RetainCalls.Should().Be(0);
        f.Adapter.Calls.Should().Be(0);
    }

    [Fact]
    public async Task BudgetRecoveryTruthfullyRefusesWhenOnlyRequiredWholeSourcesRemainOverBudget()
    {
        using var f = new Fixture();
        var earlier = Earlier(f);
        var memories = new ContextMemories(f);
        var instruction = new InstructionSource { Snapshot = InstructionSnapshot(new string('x', 32000)) };
        var (broker, catalogue, _) = InstructionBroker(f, instruction);
        var time = new MemoryReviewTime();
        using var workflow = new MixedContextWorkflow(f.ConversationService(new TestAdmission(ModelTurnOutcome.Unavailable)),
            new(memories, f, time), broker, time);
        using var over = await workflow.ReviewAsync(new(f.Session, null), "original",
            [new(earlier, ConversationHistoryRole.User)], catalogue.Artifacts.Single(), memories.Selection,
            static () => true, Token);
        over.InputUtf8Bytes.Should().BeGreaterThan(ModelContextEnvelope.MaximumInputUtf8Bytes);
        over.CanConfirm.Should().BeFalse();
        over.Contributions.Should().HaveCount(3).And.OnlyContain(item => !item.CanOmit);
        over.Text.Should().Contain("cannot complete without fresh source input");
        foreach (var required in over.Contributions)
        {
            await workflow.Invoking(value => value.RemoveWholeContributionAsync(over, required, static () => true, Token))
                .Should().ThrowAsync<InvalidOperationException>();
        }
        await workflow.Invoking(value => value.ConfirmAsync(over, static () => true, Token))
            .Should().ThrowAsync<InvalidOperationException>();
        f.RetainCalls.Should().Be(0);
        f.Adapter.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData("held")]
    [InlineData("profile")]
    [InlineData("damaged")]
    public async Task BudgetRecoveryStillUsesFreshIndependentCanonicalModelAdmission(string fault)
    {
        using var f = new Fixture();
        var history = BudgetHistory(f);
        var memories = new ContextMemories(f);
        var (broker, catalogue, _) = InstructionBroker(f, new InstructionSource());
        var host = QualifiedHistoryHost(f);
        var time = new MemoryReviewTime();
        using var workflow = new MixedContextWorkflow(f.ConversationService(host, execution: host), new(memories, f, time), broker, time);
        using var over = await workflow.ReviewAsync(new(f.Session, null), "original", history,
            catalogue.Artifacts.Single(), memories.Selection, static () => true, Token);
        using var valid = await workflow.RemoveWholeContributionAsync(over,
            over.Contributions.First(item => item.CanOmit), static () => true, Token);
        valid.CanConfirm.Should().BeTrue();
        if (fault is "held") { f.SessionProfileHeld = true; }
        if (fault is "profile") { f.ProfilePreferences.Value = new(Kora.Core.Providers.ProviderService.Unset, []); }
        if (fault is "damaged") { f.ProfileCatalogue.Status = Kora.Core.Providers.ProviderCatalogueStatus.Unavailable; }
        var receipt = await workflow.ConfirmAsync(valid, static () => true, Token);
        receipt.Message.Outcome.Should().NotBe(ModelTurnOutcome.Succeeded);
        receipt.Message.AssistantResponse.Should().BeNull();
        f.Adapter.Calls.Should().Be(0);
        f.RetainCalls.Should().Be(1);
        host.IsQuiescent.Should().BeTrue();
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("consumed")]
    [InlineData("required")]
    [InlineData("unknown")]
    public async Task BudgetRecoveryMemoryReductionCannotRemoveMissingRequiredExpiredOrConsumedInput(string fault)
    {
        using var f = new Fixture();
        using var root = f.Root();
        var memories = new ContextMemories(f, 2);
        var time = new MemoryReviewTime();
        using var context = new SessionMemoryContext(memories, f, time);
        using var review = await context.ReviewAsync(new(f.Session, null), memories.Selection,
            "question", static () => true, Token);
        var id = memories.Selection[0].Id;
        if (fault is "expired") { time.Now = time.Now.AddMinutes(2); }
        if (fault is "consumed") { review.TryConsume().Should().BeTrue(); }
        if (fault is "required") { review.RemoveWholeMemory(id); id = memories.Selection[1].Id; }
        if (fault is "unknown") { id = Guid.NewGuid(); }
        review.Invoking(value => value.RemoveWholeMemory(id)).Should().Throw<InvalidOperationException>();
        f.RetainCalls.Should().Be(0);
        f.Adapter.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BudgetRecoveryRetiresBodiesWhenReplacementMaterializationFails(bool observeRetirement)
    {
        using var f = new Fixture();
        var history = BudgetHistory(f);
        var memories = new ContextMemories(f);
        var (broker, catalogue, _) = InstructionBroker(f, new InstructionSource());
        var time = new MemoryReviewTime();
        using var workflow = new MixedContextWorkflow(f.ConversationService(new TestAdmission(ModelTurnOutcome.Unavailable)),
            new(memories, f, time), broker, time);
        using var over = await workflow.ReviewAsync(new(f.Session, null), "original", history,
            catalogue.Artifacts.Single(), memories.Selection, static () => true, Token);
        var unit = over.Contributions.First(item => item.CanOmit);
        var retired = 0;
        if (observeRetirement) { over.Retired += () => retired++; }
        var reads = 0;
        time.OnGetUtcNow = () =>
        {
            if (++reads == 2) { throw new IOException("Deterministic source-clock failure."); }
        };
        await workflow.Invoking(value => value.RemoveWholeContributionAsync(over, unit, static () => true, Token))
            .Should().ThrowAsync<IOException>();
        time.OnGetUtcNow = null;
        over.Text.Should().BeEmpty();
        over.Memory.UserRequest.Should().BeEmpty();
        over.Memory.Records.Should().BeEmpty();
        retired.Should().Be(observeRetirement ? 1 : 0);
        f.RetainCalls.Should().Be(0);
        f.Adapter.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("admission")]
    [InlineData("expiry")]
    [InlineData("initial-publication")]
    public async Task BudgetRecoveryIndependentlyFencesFinalPublicationAndCancellation(string fault)
    {
        using var f = new Fixture();
        var history = BudgetHistory(f);
        var memories = new ContextMemories(f);
        var (broker, catalogue, _) = InstructionBroker(f, new InstructionSource());
        var time = new MemoryReviewTime();
        using var workflow = new MixedContextWorkflow(f.ConversationService(new TestAdmission(ModelTurnOutcome.Unavailable)),
            new(memories, f, time), broker, time);
        void ExpireAtPublication()
        {
            var reads = 0;
            time.OnGetUtcNow = () =>
            {
                if (++reads == 3) { time.Now = time.Now.AddMinutes(2); }
            };
        }
        if (fault is "initial-publication")
        {
            memories.OnRead = () => { if (memories.Reads == 2) { ExpireAtPublication(); } };
            await workflow.Invoking(value => value.ReviewAsync(new(f.Session, null), "original", history,
                catalogue.Artifacts.Single(), memories.Selection, static () => true, Token))
                .Should().ThrowAsync<OperationCanceledException>();
        }
        else
        {
            using var over = await workflow.ReviewAsync(new(f.Session, null), "original", history,
                catalogue.Artifacts.Single(), memories.Selection, static () => true, Token);
            var unit = over.Contributions.First(item => item.CanOmit);
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
            var checkedAfterRead = 0;
            var armed = false;
            if (fault is "cancel") { cancellation.Cancel(); }
            if (fault is "expiry") { memories.OnRead = ExpireAtPublication; }
            if (fault is "admission") { memories.OnRead = () => armed = true; }
            bool Admitted() => !armed || ++checkedAfterRead < 2;
            await workflow.Invoking(value => value.RemoveWholeContributionAsync(over, unit, Admitted, cancellation.Token))
                .Should().ThrowAsync<OperationCanceledException>();
            over.Text.Should().BeEmpty();
        }
        f.RetainCalls.Should().Be(0);
        f.Adapter.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData("ascii")]
    [InlineData("unicode")]
    [InlineData("escaping")]
    public void BudgetMeasurementUsesCompleteSerializedInputAndExactCapNotRawTextOrTokens(string form)
    {
        using var f = new Fixture();
        var history = BudgetHistory(f);
        var memories = new ContextMemories(f);
        var now = DateTimeOffset.UnixEpoch;
        const string instruction = "Use selected context.";
        var text = form switch { "unicode" => "\u00e9\U0001f600", "escaping" => "\"<&\\", _ => "x" };
        int Measure(string body) => MixedConversationContext.MeasureInput(f.Request, body, history, "instruction",
            instruction, memories.Snapshot.Boundary, memories.Snapshot.Records, now, now.AddSeconds(15));
        // Fill complete whole messages, never a production slicing/truncation operation.
        history = history.Select(item => item with { Message = item.Message with { Body = new string('h', 3000) } }).ToArray();
        var initial = Measure(text);
        var missing = ModelContextEnvelope.MaximumInputUtf8Bytes - initial;
        foreach (var index in Enumerable.Range(0, history.Length))
        {
            var add = Math.Min(1000, missing);
            history[index] = history[index] with { Message = history[index].Message with { Body = new string('h', 3000 + add) } };
            missing -= add;
        }
        missing.Should().Be(0);
        Measure(text).Should().Be(ModelContextEnvelope.MaximumInputUtf8Bytes);
        var exact = MixedConversationContext.CreateEnvelope(f.Request, text, history, "instruction", instruction,
            memories.Snapshot.Boundary, memories.Snapshot.Records, now, now.AddSeconds(15));
        exact.Serialize().Length.Should().Be(Measure(text));
        Encoding.UTF8.GetByteCount(text).Should().BeLessThan(Measure(text));
        Measure(text + "x").Should().Be(ModelContextEnvelope.MaximumInputUtf8Bytes + 1);
        var reject = () => MixedConversationContext.CreateEnvelope(f.Request, text + "x", history, "instruction", instruction,
            memories.Snapshot.Boundary, memories.Snapshot.Records, now, now.AddSeconds(15));
        reject.Should().Throw<InvalidDataException>().WithMessage("*nothing was dropped*");
    }
}
