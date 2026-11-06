using System.Text.Json;

using AwesomeAssertions;

using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Core.Providers;

namespace Kora.Core.UnitTests.Conversation;

public sealed class ExpiredOrdinaryRequestSourceTests
{
    [Theory]
    [InlineData(RequestOrigin.LocalUi)]
    [InlineData(RequestOrigin.ActivatedVoice)]
    public void ConfirmedBareTextExpiryAndSeparateHistoricalAttributionRoundTripWithoutAuthorityTransfer(RequestOrigin origin)
    {
        var old = Original(origin);
        var source = new ExpiredOrdinaryRequestSource(old, new(old.Request, new(2), HostTaskState.Denied));
        source.Validate();
        var freshRequest = new HostRequest(new(Guid.NewGuid()), old.Request.SessionId, new(Guid.NewGuid()), RequestOrigin.LocalUi);
        var fresh = new ConversationMessage(freshRequest, old.Generation, 2, 1, old.SubmittedAt,
            old.Body, null, null) { OrdinaryText = true, OriginalTextSource = old.Request };
        fresh.Validate();
        JsonSerializer.Deserialize<ConversationMessage>(JsonSerializer.Serialize(fresh)).Should().Be(fresh);
        fresh.Request.Origin.Should().Be(RequestOrigin.LocalUi);
        fresh.OriginalTextSource!.Origin.Should().Be(origin);
        var legacy = old with { OrdinaryText = false };
        JsonSerializer.Serialize(legacy).Should().NotContain("OrdinaryText").And.NotContain("OriginalTextSource");
    }

    [Theory]
    [InlineData("legacy")]
    [InlineData("pending")]
    [InlineData("cancelled")]
    [InlineData("unavailable")]
    [InlineData("interrupted")]
    [InlineData("wrong-reason")]
    [InlineData("wrong-task")]
    [InlineData("wrong-revision")]
    [InlineData("wrong-state")]
    [InlineData("dispatched")]
    [InlineData("handoff")]
    public void StatusAloneNeverEstablishesAnEligibleSource(string fault)
    {
        var message = Original(RequestOrigin.LocalUi);
        var task = new HostTaskRecord(message.Request, new(2), HostTaskState.Denied);
        if (fault is "legacy") { message = message with { OrdinaryText = false }; }
        if (fault is "pending") { message = message with { Revision = 1, Outcome = null, Reason = null }; }
        if (fault is "cancelled") { message = message with { Outcome = ModelTurnOutcome.Cancelled }; }
        if (fault is "unavailable") { message = message with { Outcome = ModelTurnOutcome.Unavailable }; }
        if (fault is "interrupted") { message = message with { Outcome = ModelTurnOutcome.Unknown, Reason = ModelTurnReason.HostInterrupted }; }
        if (fault is "wrong-reason") { message = message with { Reason = ModelTurnReason.PolicyChanged }; }
        if (fault is "wrong-task") { task = new(HostRequest.Create(RequestOrigin.LocalUi), new(2), HostTaskState.Denied); }
        if (fault is "wrong-revision") { task = new(message.Request, new(3), HostTaskState.Denied); }
        if (fault is "wrong-state") { task = new(message.Request, new(2), HostTaskState.Unknown); }
        if (fault is "dispatched")
        {
            message = message with { Version = 2, Revision = 3, ExecutionMetadataRevision = 0,
                Execution = new(new(Guid.NewGuid()), message.Request, ModelProviderSelection.OllamaCandidate) };
        }
        if (fault is "handoff")
        {
            var previous = new HostRequest(new(Guid.NewGuid()), message.Request.SessionId, new(Guid.NewGuid()), message.Request.Origin);
            var execution = new ModelTurnProvenance(new(Guid.NewGuid()), previous, ModelProviderSelection.OllamaCandidate);
            message = message with { Handoff = new(Guid.NewGuid(), execution, new(2),
                new HostQuestionKey(previous, new(Guid.NewGuid()), new(1)), DateTimeOffset.UnixEpoch,
                DateTimeOffset.UnixEpoch.AddMinutes(1), new string('a', 64),
                new ProviderProfileTuple(ProviderService.Copilot, new("account"), new(1),
                    new("model"), new("reasoning"), new("context")), ModelProviderSelection.CopilotCandidate) };
        }
        var source = new ExpiredOrdinaryRequestSource(message, task);
        source.Invoking(value => value.Validate()).Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData("not-ordinary")]
    [InlineData("session")]
    [InlineData("same-request")]
    [InlineData("same-task")]
    [InlineData("invocation")]
    [InlineData("origin")]
    public void HistoricalAttributionCannotBeRelabelledIntoNewAuthority(string fault)
    {
        var source = Original(RequestOrigin.LocalUi);
        var fresh = source with { Request = new(new(Guid.NewGuid()), source.Request.SessionId, new(Guid.NewGuid()), RequestOrigin.LocalUi),
            OriginalTextSource = source.Request };
        fresh = fault switch
        {
            "not-ordinary" => fresh with { OrdinaryText = false },
            "session" => fresh with { OriginalTextSource = HostRequest.Create(RequestOrigin.LocalUi) },
            "same-request" => fresh with { OriginalTextSource = new(fresh.Request.RequestId, source.Request.SessionId,
                source.Request.TaskId, RequestOrigin.LocalUi) },
            "same-task" => fresh with { OriginalTextSource = new(source.Request.RequestId, source.Request.SessionId,
                fresh.Request.TaskId, RequestOrigin.LocalUi) },
            "invocation" => fresh with { OriginalTextSource = new(source.Request.RequestId, source.Request.SessionId,
                source.Request.TaskId, RequestOrigin.LocalUi, new(Guid.NewGuid())) },
            _ => fresh with { OriginalTextSource = new(source.Request.RequestId, source.Request.SessionId,
                source.Request.TaskId, RequestOrigin.HostSystem) },
        };
        fresh.Invoking(value => value.Validate()).Should().Throw<InvalidDataException>();
    }

    private static ConversationMessage Original(RequestOrigin origin) => new(HostRequest.Create(origin), new(1),
        1, 2, DateTimeOffset.UnixEpoch, " immutable original\r\n", ModelTurnOutcome.Denied, ModelTurnReason.ContextExpired)
        { OrdinaryText = true };
}
