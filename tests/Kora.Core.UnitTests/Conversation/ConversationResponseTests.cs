using System.Text.Json;

using AwesomeAssertions;

using Kora.Core.Commands;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;

namespace Kora.Core.UnitTests.Conversation;

public sealed class ConversationResponseTests
{
    private static readonly HostRequest Request = HostRequest.Create(RequestOrigin.LocalUi);
    private static ModelTurnProvenance Provenance() => new(new(Guid.NewGuid()), Request, ModelProviderSelection.OllamaCandidate);
    private static ConversationMessage Original() => new(Request, new(1), 1, 1, DateTimeOffset.UnixEpoch, "original USER", null, null);
    private static ConversationMessage Executing(ModelTurnProvenance provenance) => Original() with
    { Version = 2, Revision = 2, Execution = provenance, ExecutionMetadataRevision = 0 };

    [Fact]
    public void OriginalBytesRemainCompatibleAndAnswerIsSeparateBoundedVersionedData()
    {
        var original = Original();
        var legacy = JsonSerializer.Serialize(original);
        legacy.Should().NotContain("AssistantResponse").And.NotContain("Execution").And.NotContain("HasAssistantResponse");
        JsonSerializer.Deserialize<ConversationMessage>(legacy).Should().Be(original);
        var provenance = Provenance();
        var executing = Executing(provenance);
        executing.Validate();
        executing.ExpectedTaskState.Should().Be(HostTaskState.DispatchRecorded);
        const string answer = " exact e\u0301\r\n🙂 answer ";
        var response = ConversationResponse.FromResult(new(ModelTurnOutcome.Succeeded, ModelTurnReason.ProviderOutcome,
            provenance, new(answer, null)), provenance);
        var completed = executing with
        { Revision = 3, Outcome = ModelTurnOutcome.Succeeded, Reason = ModelTurnReason.ProviderOutcome, AssistantResponse = response };
        completed.Validate();
        completed.Body.Should().Be(original.Body);
        completed.AssistantResponse!.Body.Should().Be(answer);
        completed.ExpectedTaskState.Should().Be(HostTaskState.Succeeded);
        completed.HasAssistantResponse.Should().BeTrue();
        JsonSerializer.Deserialize<ConversationMessage>(JsonSerializer.Serialize(completed)).Should().Be(completed);
        new ConversationRangeFilter(MinimumRevision: 3, MaximumRevision: 3).Matches(completed).Should().BeTrue();
        new ConversationSearch("answer").Matches(completed, TestContext.Current.CancellationToken).Should().BeFalse(
            "original USER / typed HOST lexical search does not become assistant-body search");
    }

    [Theory]
    [InlineData(ModelTurnOutcome.Cancelled)]
    [InlineData(ModelTurnOutcome.TimedOut)]
    [InlineData(ModelTurnOutcome.Unknown)]
    public void CancellationAndDeadlineAfterDispatchDoNotClaimPhysicalStopOrAnAnswer(ModelTurnOutcome outcome)
    {
        var message = Executing(Provenance()) with
        { Revision = 3, Outcome = outcome, Reason = ModelTurnReason.ProviderOutcome };
        message.Validate();
        message.ExpectedTaskState.Should().Be(HostTaskState.Unknown);
        message.AssistantResponse.Should().BeNull();
    }

    [Theory]
    [InlineData("version")]
    [InlineData("request")]
    [InlineData("origin")]
    [InlineData("null-body")]
    [InlineData("turn")]
    [InlineData("provider")]
    [InlineData("selection")]
    [InlineData("blank")]
    [InlineData("oversize")]
    [InlineData("surrogate")]
    [InlineData("control")]
    [InlineData("credential")]
    [InlineData("null-provenance")]
    [InlineData("invocation")]
    [InlineData("invalid-selection")]
    public void InvalidAnswerCannotBecomeRetainedConversation(string fault)
    {
        var p = Provenance();
        var response = new ConversationResponse(p, "genuine data");
        response = fault switch
        {
            "version" => response with { Version = 2 },
            "request" => response with { Provenance = p with { Request = null! } },
            "null-provenance" => response with { Provenance = null! },
            "invocation" => response with { Provenance = p with { Request = new(Request.RequestId, Request.SessionId,
                Request.TaskId, RequestOrigin.LocalUi, new(Guid.NewGuid())) } },
            "invalid-selection" => response with { Provenance = p with { Selection = p.Selection with { Revision = default } } },
            "origin" => response with { Provenance = p with { Request = HostRequest.Create(RequestOrigin.HostSystem) } },
            "null-body" => response with { Body = null! },
            "turn" => response with { Provenance = p with { Turn = default } },
            "provider" => response with { Provenance = p with { Selection = ModelProviderSelection.CopilotCandidate } },
            "selection" => response with { Provenance = p with { Selection = null! } },
            "blank" => response with { Body = " \r\n" },
            "oversize" => response with { Body = new string('x', 4096) },
            "surrogate" => response with { Body = "\ud800" },
            "control" => response with { Body = "answer\0" },
            _ => response with { Body = "api_key=synthetic-only-value" },
        };
        if (fault is "credential")
        { response.Invoking(value => value.Validate()).Should().Throw<ConversationCredentialException>(); }
        else { response.Invoking(value => value.Validate()).Should().Throw<InvalidDataException>(); }
    }

    [Theory]
    [InlineData("action")]
    [InlineData("question")]
    [InlineData("grant")]
    [InlineData("missing-answer")]
    [InlineData("foreign")]
    [InlineData("failure")]
    [InlineData("null-answer")]
    public void OnlyExactSuccessfulAnswerOnlyHostResultIsAccepted(string fault)
    {
        var p = Provenance();
        var result = new ModelTurnResult(ModelTurnOutcome.Succeeded, ModelTurnReason.ProviderOutcome, p, new("answer", null));
        result = fault switch
        {
            "action" => result with { Response = new("not an action receipt", BuiltInAction.ShowVersion) },
            "question" => result with { Response = new("not a final answer", null, Question: new("Choose", ["A", "B"])) },
            "grant" => result with { Response = new("not a grant", null,
                new(GrantChangeOperation.Add, BuiltInAction.ShowVersion, ModelApprovalScope.Session)) },
            "missing-answer" => result with { Response = null },
            "null-answer" => result with { Response = new(null, null) },
            "foreign" => result with { Provenance = Provenance() },
            _ => result with { Outcome = ModelTurnOutcome.Unknown },
        };
        new Action(() => ConversationResponse.FromResult(result, p)).Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData("missing-execution")]
    [InlineData("missing-metadata")]
    [InlineData("negative-metadata")]
    [InlineData("foreign-execution")]
    [InlineData("foreign-answer")]
    [InlineData("missing-answer")]
    [InlineData("premature-answer")]
    [InlineData("legacy")]
    [InlineData("revision")]
    [InlineData("invalid-execution-selection")]
    [InlineData("missing-execution-selection")]
    [InlineData("premature-terminal-revision")]
    [InlineData("terminal-at-dispatch-revision")]
    public void MessageCannotLaunderAssistantDataOrSkipLifecycle(string fault)
    {
        var p = Provenance();
        var response = new ConversationResponse(p, "answer");
        var message = Executing(p) with
        { Revision = 3, Outcome = ModelTurnOutcome.Succeeded, Reason = ModelTurnReason.ProviderOutcome, AssistantResponse = response };
        message = fault switch
        {
            "missing-execution" => message with { Execution = null },
            "missing-metadata" => message with { ExecutionMetadataRevision = null },
            "negative-metadata" => message with { ExecutionMetadataRevision = -1 },
            "foreign-execution" => message with { Execution = p with { Request = HostRequest.Create(RequestOrigin.LocalUi) } },
            "foreign-answer" => message with { AssistantResponse = response with { Provenance = Provenance() } },
            "missing-answer" => message with { AssistantResponse = null },
            "premature-answer" => message with { Revision = 2, Outcome = null, Reason = null },
            "legacy" => message with { Version = 1 },
            "invalid-execution-selection" => message with
            { Execution = p with { Selection = p.Selection with { Model = default } } },
            "missing-execution-selection" => message with { Execution = p with { Selection = null! } },
            "premature-terminal-revision" => message with { Revision = 1 },
            "terminal-at-dispatch-revision" => message with { Revision = 2 },
            _ => message with { Revision = 4 },
        };
        message.Invoking(value => value.Validate()).Should().Throw<InvalidDataException>();
    }
}
