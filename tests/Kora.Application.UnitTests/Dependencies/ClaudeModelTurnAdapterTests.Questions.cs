using AwesomeAssertions;
using Kora.Application.Dependencies;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.Dependencies;

public sealed partial class ClaudeModelTurnAdapterTests
{
    [Theory]
    [InlineData("exact", ModelTurnOutcome.Succeeded)]
    [InlineData("no-question-owner", ModelTurnOutcome.Denied)]
    [InlineData("ordinary", ModelTurnOutcome.Denied)]
    [InlineData("missing-runtime", ModelTurnOutcome.Unavailable)]
    [InlineData("no-transport", ModelTurnOutcome.Unavailable)]
    [InlineData("revoked-before-answer", ModelTurnOutcome.Denied)]
    [InlineData("revoked-after-answer", ModelTurnOutcome.Denied)]
    [InlineData("revoked-after-close", ModelTurnOutcome.DeniedEgress)]
    [InlineData("expired", ModelTurnOutcome.Denied)]
    public async Task HostedQuestionRequiresExplicitOperationAndLiveHostOwnerThroughJoinedContinuation(string fault, ModelTurnOutcome expected)
    {
        var f = new Fixture(mode: fault is "ordinary" ? ModelToolMode.None : ModelToolMode.HostedClarification);
        using var root = f.Root();
        var transport = new QuestionTransport
        {
            OnDispose = () => { if (fault is "revoked-after-close") { f.Time.Now = f.Context.ExpiresAt; } },
        };
        var adapter = new ClaudeModelTurnAdapter(fault is "missing-runtime" ? new Runtime()
                : new ObservationRuntime { Transport = fault is "no-transport" ? null : transport },
            f.Reviews, f.Time, NullLogger<BoundedModelTurnAdapter>.Instance)
            .Bind(f.Reviews.PrepareClaudeTurn(f.Context, f.Profile), _ => ValueTask.FromResult(true));
        var asks = 0;
        var validations = 0;
        var result = fault is "no-question-owner"
            ? await adapter.RunAsync(f.Provenance, f.Profile, f.Context.Serialize(),
                (_, _, _) => throw new InvalidOperationException("No observation authority."), Token)
            : await adapter.RunWithQuestionAsync(f.Provenance, f.Profile, f.Context.Serialize(),
                (_, _, _) => throw new InvalidOperationException("No observation authority."),
                _ => ValueTask.FromResult(++validations == (fault is "revoked-before-answer" ? 1 : fault is "revoked-after-answer" ? 2 : -1)
                    ? ModelTurnReason.HostAdmissionClosed : ModelTurnReason.None),
                (question, _) =>
                {
                    asks++;
                    f.Time.Now += TimeSpan.FromSeconds(fault is "expired" ? 120 : 20);
                    return Task.FromResult(new ModelQuestionAnswerMessage(question, new(["option-1"]), RequestOrigin.ActivatedVoice));
                }, Token);
        result.Outcome.Should().Be(expected);
        result.Response?.Answer.Should().Be(fault is "exact" ? "genuine clarified answer" : null);
        asks.Should().Be(fault is "exact" or "revoked-after-answer" or "revoked-after-close" or "expired" ? 1 : 0);
        transport.Requests.Should().HaveCount(fault is "exact" or "revoked-after-close" ? 2
            : fault is "revoked-before-answer" or "revoked-after-answer" or "expired" ? 1 : 0);
        transport.Disposed.Should().Be(fault is not ("ordinary" or "no-question-owner" or "missing-runtime" or "no-transport"));
    }

    private sealed class QuestionTransport : IModelToolTransport
    {
        internal List<ModelToolRequest> Requests { get; } = [];
        internal bool Disposed { get; private set; }
        internal Action? OnDispose { get; init; }
        public Task<ModelToolTransportReply> CompleteAsync(ModelToolRequest request, CancellationToken cancellationToken)
        {
            request.ClarificationAllowed.Should().BeTrue();
            request.Context.ToolMode.Should().Be(ModelToolMode.HostedClarification);
            request.Context.Tools.Should().BeEmpty();
            Requests.Add(request);
            ModelAssistantMessage message = request.Round == 1
                ? new ModelClarificationQuestion(new("Which format?", ["Compact", "Detailed"]), "question_1")
                : ModelToolProtocol.ParseAssistant("""{"kind":"answer","answer":"genuine clarified answer"}"""u8);
            if (request.Round == 2)
            {
                var answer = request.Messages[1].Should().BeOfType<ModelQuestionAnswerMessage>().Subject;
                answer.Answer.Choices.Should().Equal("option-1");
                answer.Channel.Should().Be(RequestOrigin.ActivatedVoice);
            }
            return Task.FromResult(new ModelToolTransportReply(ModelTurnOutcome.Succeeded, request.Provenance, message,
                new(ModelToolProtocol.SerializeRequest(request).Length, ModelToolProtocol.SerializeMessage(message).Length)));
        }
        public ValueTask DisposeAsync() { Disposed = true; OnDispose?.Invoke(); return ValueTask.CompletedTask; }
    }
}
