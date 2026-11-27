using System.Text.Json;
using AwesomeAssertions;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;

namespace Kora.Core.UnitTests.Conversation;

public sealed class HostedConversationHistoryContextTests
{
    private static readonly HostRequest Request = HostRequest.Create(RequestOrigin.LocalUi);
    private static HostedConversationHistoryContext Create(string body = "new question") =>
        new(Request, body, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddMinutes(1));

    private static ConversationMessage Message(long sequence, string body = "original", bool local = false)
    {
        var request = new HostRequest(new(Guid.NewGuid()), Request.SessionId, new(Guid.NewGuid()), RequestOrigin.LocalUi);
        var provenance = new ModelTurnProvenance(new(Guid.NewGuid()), request,
            ModelProviderSelection.OllamaCandidate with { Provider = local ? ModelProviderIdentity.Ollama : ModelProviderIdentity.Copilot });
        return new(request, new(1), sequence, 3, DateTimeOffset.UnixEpoch, body, ModelTurnOutcome.Succeeded, ModelTurnReason.ProviderOutcome)
        { Version = 2, Execution = provenance, ExecutionMetadataRevision = 0, AssistantResponse = new(provenance, "genuine answer") };
    }

    [Fact]
    public void NewestWholeMessagesAreSelectedAndEmittedChronologicallyWithSharedCountBound()
    {
        var history = Create();
        for (var sequence = 10; sequence > 0; sequence--)
        {
            var message = Message(sequence);
            history.Include(new(message, ConversationHistoryRole.Assistant));
            history.Include(new(message, ConversationHistoryRole.User));
        }
        history.Selections.Should().HaveCount(16);
        history.Selections.Select(item => item.Message.Sequence).Should().Equal(Enumerable.Range(3, 8).SelectMany(value => new long[] { value, value }));
        history.Context.Evidence.Should().OnlyContain(item => item.Disclosure == ModelEvidenceDisclosure.HostedEligible
            && item.HistorySource == null && item.Request == Request);
        history.Context.RequireCompleteInputLimit();
    }

    [Fact]
    public void CompleteByteBudgetOmitsOlderWholeMessagesWithoutTruncatingOrChangingNewInput()
    {
        var history = Create("  unchanged\r\nnew input ");
        var accepted = new List<long>();
        for (var sequence = 16; sequence > 0; sequence--)
        {
            var message = Message(sequence, new string('x', 4000));
            if (history.Include(new(message, ConversationHistoryRole.User))) { accepted.Add(sequence); }
        }

        accepted.Count.Should().BeInRange(1, 15);
        accepted.Should().Equal(Enumerable.Range(17 - accepted.Count, accepted.Count).Reverse().Select(value => (long)value));
        history.Context.Serialize().Length.Should().BeLessThanOrEqualTo(ModelContextEnvelope.MaximumInputUtf8Bytes);
        history.Context.UserRequest.Should().Be("  unchanged\r\nnew input ");
        foreach (var evidence in history.Context.Evidence)
        {
            using var content = JsonDocument.Parse(evidence.Content);
            content.RootElement.GetProperty("Body").GetString().Should().Be(new string('x', 4000));
        }
    }

    [Fact]
    public void ExactCompleteLimitIsAdmittedAndOneExtraByteOmitsTheOldestWholeMessage()
    {
        var messages = Enumerable.Range(2, 8).Select(sequence => Message(sequence, new string('x', 3500))).ToArray();
        var measured = Create("q");
        foreach (var message in messages.Reverse()) { measured.Include(new(message, ConversationHistoryRole.User)); }
        var remaining = ModelContextEnvelope.MaximumInputUtf8Bytes - measured.Context.Serialize().Length;
        remaining.Should().BePositive();
        for (var index = 0; remaining > 0; index++)
        {
            var added = Math.Min(remaining, ConversationText.MaximumUtf16Characters - messages[index].Body.Length);
            messages[index] = messages[index] with { Body = messages[index].Body + new string('x', added) };
            remaining -= added;
        }
        var exact = Create("q");
        var over = Create("qq");
        foreach (var message in messages.Reverse())
        {
            exact.Include(new(message, ConversationHistoryRole.User)).Should().BeTrue();
            over.Include(new(message, ConversationHistoryRole.User));
        }
        exact.Context.Serialize().Length.Should().Be(ModelContextEnvelope.MaximumInputUtf8Bytes);
        over.Selections.Should().HaveCount(7);
        over.Selections.Min(item => item.Message.Sequence).Should().Be(3);
        exact.Include(new(Message(1), ConversationHistoryRole.User)).Should().BeFalse();
    }

    [Fact]
    public void OverBudgetNewMessageAloneFailsAndForeignHistoryCannotBeDisclosed()
    {
        var over = () => Create(new string('x', ModelContextEnvelope.MaximumInputUtf8Bytes));
        over.Should().Throw<InvalidDataException>();
        var history = Create();
        var foreign = Message(1);
        var request = new HostRequest(foreign.Request.RequestId, new(Guid.NewGuid()), foreign.Request.TaskId,
            foreign.Request.Origin);
        var provenance = foreign.Execution! with { Request = request };
        foreign = foreign with { Request = request, Execution = provenance, AssistantResponse = new(provenance, "foreign answer") };
        history.Invoking(value => value.Include(new(foreign, ConversationHistoryRole.User))).Should().Throw<InvalidDataException>();
        history.Context.Evidence.Should().BeEmpty();
    }

    [Fact]
    public void UnknownLocalAnswersAndFailedOrPendingHostOutcomesAreNotEvidence()
    {
        var history = Create();
        var local = Message(1, local: true);
        history.Include(new(local, ConversationHistoryRole.Assistant)).Should().BeFalse();
        history.Include(new(local, ConversationHistoryRole.User)).Should().BeTrue();
        var request = new HostRequest(new(Guid.NewGuid()), Request.SessionId, new(Guid.NewGuid()), RequestOrigin.LocalUi);
        var failed = new ConversationMessage(request, new(1), 2, 2, DateTimeOffset.UnixEpoch,
            "failed", ModelTurnOutcome.Denied, ModelTurnReason.HostAdmissionClosed);
        history.Include(new(failed, ConversationHistoryRole.User)).Should().BeFalse();
        history.Include(new(failed with { Revision = 1, Outcome = null, Reason = null }, ConversationHistoryRole.User)).Should().BeFalse();
        history.Context.Evidence.Should().ContainSingle();
    }

    [Fact]
    public void HostedHistoryDoesNotChangeExistingReviewedLocalOnlyClassification()
    {
        var source = Message(1);
        var local = ConversationHistoryContext.CreateEnvelope(Request, "new question",
            [new(source, ConversationHistoryRole.User)], DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddMinutes(1));
        local.Evidence.Should().OnlyContain(item => item.Disclosure == ModelEvidenceDisclosure.LocalOnly && item.HistorySource != null);
        ClaudeObservationTools.Supports(local).Should().BeFalse();
        var history = Create();
        history.Include(new(source, ConversationHistoryRole.User));
        ClaudeObservationTools.Supports(history.Context).Should().BeTrue();
        HostedConversationHistoryContext.Supports(new(Request, HostedConversationHistoryContext.Policy, "q",
            local.Evidence, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddMinutes(1), ModelToolMode.None)).Should().BeFalse();
    }
}
