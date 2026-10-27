using AwesomeAssertions;

using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;

namespace Kora.Core.UnitTests.Conversation;

public sealed class FailedRequestRetrySourceTests
{
    [Theory]
    [InlineData(ModelTurnOutcome.Unavailable)]
    [InlineData(ModelTurnOutcome.AuthenticationRequired)]
    [InlineData(ModelTurnOutcome.QuotaExceeded)]
    [InlineData(ModelTurnOutcome.DeniedEgress)]
    [InlineData(ModelTurnOutcome.Denied)]
    public void FailedRetrySourceRequiresRecordedProviderAndExactTerminalTaskWithoutRenewingAuthority(ModelTurnOutcome outcome)
    {
        var source = Source(outcome);
        source.Validate();
        source.Provider.Should().Be(ModelProviderSelection.OllamaCandidate);
        source.Message.Body.Should().Be("original USER input");
    }

    [Theory]
    [InlineData("legacy")]
    [InlineData("active")]
    [InlineData("success")]
    [InlineData("cancelled")]
    [InlineData("timeout")]
    [InlineData("unknown")]
    [InlineData("foreign-task")]
    [InlineData("active-task")]
    [InlineData("task-state")]
    [InlineData("task-revision")]
    public void MissingProviderOrNonFailedOrMismatchedSourceCannotBecomeRetryPermission(string fault)
    {
        var source = Source(ModelTurnOutcome.Denied);
        source = fault switch
        {
            "legacy" => source with { Message = source.Message with { Version = 1, Revision = 2,
                Execution = null, ExecutionMetadataRevision = null } },
            "active" => source with { Message = source.Message with { Revision = 2, Outcome = null, Reason = null } },
            "success" => source with { Message = source.Message with { Outcome = ModelTurnOutcome.Succeeded,
                AssistantResponse = new(source.Message.Execution!, "genuine prior answer") } },
            "cancelled" => source with { Message = source.Message with { Outcome = ModelTurnOutcome.Cancelled } },
            "timeout" => source with { Message = source.Message with { Outcome = ModelTurnOutcome.TimedOut } },
            "unknown" => source with { Message = source.Message with { Outcome = ModelTurnOutcome.Unknown } },
            "foreign-task" => source with { Task = new(HostRequest.Create(RequestOrigin.LocalUi), new(3), HostTaskState.Denied) },
            "active-task" => source with { Task = new(source.Message.Request, new(2), HostTaskState.DispatchRecorded) },
            "task-state" => source with { Task = new(source.Message.Request, new(3), HostTaskState.Succeeded) },
            _ => source with { Task = new(source.Message.Request, new(4), HostTaskState.Denied) },
        };
        source.Invoking(value => value.Validate()).Should().Throw<InvalidOperationException>();
    }

    private static FailedRequestRetrySource Source(ModelTurnOutcome outcome)
    {
        var request = HostRequest.Create(RequestOrigin.LocalUi);
        var message = new ConversationMessage(request, new(1), 1, 3, DateTimeOffset.UnixEpoch, "original USER input",
            outcome, ModelTurnReason.ProviderOutcome)
        { Version = 2, Execution = new(new(Guid.NewGuid()), request, ModelProviderSelection.OllamaCandidate), ExecutionMetadataRevision = 0 };
        return new(message, new(request, new(3), message.ExpectedTaskState));
    }
}
