using AwesomeAssertions;

using Kora.Application.Dependencies;
using Kora.Application.Hosting;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Core.Storage;
using Kora.Core.Tools;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.Dependencies;

public sealed partial class ModelTurnHostTests
{
    [Theory]
    [InlineData("tools")]
    [InlineData("missing-target")]
    [InlineData("unsupported-adapter")]
    public async Task QuestionExecutionCannotBorrowToolsOrAnUnboundTargetOrUnsupportedAdapter(string fault)
    {
        using var f = new Fixture();
        using var root = f.Root();
        var context = f.Context(toolMode: fault is "tools" ? ModelToolMode.LocalWebPage : ModelToolMode.None);
        var turn = (await f.Host.AdmitAsync(ModelProviderSelection.OllamaCandidate, context, Token)).Turn!;
        turn.QuestionContinuation = new(f.Questions, NeverPresent, f.Request, f.Session.Generation,
            context.ExpiresAt, static () => true, f.Time);
        if (fault is not "missing-target") { turn.ConversationTarget = new(f.Session, null); }
        var result = await f.Host.RunAsync(turn, Token);
        result.Outcome.Should().Be(ModelTurnOutcome.Denied);
        result.Reason.Should().Be(ModelTurnReason.InvalidProviderResponse);
        f.Adapter.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData("missing-store")]
    [InlineData("tools")]
    [InlineData("evidence")]
    [InlineData("already-bound")]
    public async Task QuestionBindingRequiresFreshToolFreeEvidenceFreeTurnAndComposedInteractionStore(string fault)
    {
        using var f = new Fixture();
        var admission = new QuestionBindingAdmission(f, fault);
        var workspace = new SessionWorkspaceService(f, new(f), f, NullLogger<SessionWorkspaceService>.Instance);
        var service = new SessionConversationService(f, f, workspace, f, admission, f.Time, f,
            execution: f.Host, cancellationAudit: f, interactions: fault is "missing-store" ? null : f.Questions);
        var operation = () => service.SubmitAsync(new(f.Session, null), "original request", static () => true, Token,
            questionPresenter: NeverPresent);
        var failure = await operation.Should().ThrowAsync<ConversationSubmissionUnconfirmedException>();
        failure.Which.InnerException.Should().BeOfType<InvalidOperationException>()
            .Which.Message.Should().Contain("fresh local tool-free or read-only turn");
        f.ExecutionStarts.Should().Be(0);
        f.Adapter.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData("tools")]
    [InlineData("null-answer")]
    [InlineData("foreign-answer")]
    [InlineData("admission-after-answer")]
    [InlineData("answer-budget")]
    public async Task BoundedClarificationRefusesUnadmittedModeMalformedReplyRevocationAndExhaustedCompleteBudget(string fault)
    {
        using var f = new Fixture();
        using var root = f.Root();
        var transport = new ToolTransport(Clarification, FinalAnswer);
        if (fault is "answer-budget")
        {
            transport.ChangeReply = (request, reply) => reply with
            {
                Usage = reply.Usage! with { OutputUtf8Bytes = request.Budget.OutputUtf8Bytes - 1 },
            };
        }
        await using var loop = CreateLoop(f, transport);
        var provenance = new ModelTurnProvenance(new(Guid.NewGuid()), f.Request, ModelProviderSelection.OllamaCandidate);
        var live = true;
        var calls = 0;
        var result = await loop.RunWithQuestionAsync(provenance, null,
            f.Context(toolMode: fault is "tools" ? ModelToolMode.LocalWebPage : ModelToolMode.None).Serialize(),
            static (_, _, _) => throw new InvalidOperationException("No tool can execute in a question turn."),
            _ => ValueTask.FromResult(live ? ModelTurnReason.None : ModelTurnReason.HostAdmissionClosed),
            (question, _) =>
            {
                calls++;
                if (fault is "admission-after-answer") { live = false; }
                if (fault is "null-answer") { return Task.FromResult<ModelQuestionAnswerMessage>(null!); }
                var target = fault is "foreign-answer"
                    ? new ModelClarificationQuestion(new("Foreign question", ["Compact", "Detailed"])) : question;
                return Task.FromResult(new ModelQuestionAnswerMessage(target, new(["option-0"]), RequestOrigin.LocalUi));
            }, Token);
        result.Outcome.Should().NotBe(ModelTurnOutcome.Succeeded);
        result.Response.Should().BeNull();
        result.Reason.Should().Be(fault switch
        {
            "tools" => ModelTurnReason.ToolUseNotPermitted,
            "admission-after-answer" => ModelTurnReason.HostAdmissionClosed,
            "answer-budget" => ModelTurnReason.ToolLoopBudgetExceeded,
            _ => ModelTurnReason.InvalidProviderResponse,
        });
        calls.Should().Be(fault is "tools" ? 0 : 1);
        transport.Requests.Should().HaveCount(fault is "tools" ? 0 : 1);
    }

    private static Task<ConversationQuestionPresentation> NeverPresent(HostRequest request, HostRevision generation,
        QuestionSpec spec, DateTimeOffset expiry, Func<bool> admitted, CancellationToken token) =>
        throw new InvalidOperationException("No question may be presented at this refused boundary.");

    private sealed class QuestionBindingAdmission(Fixture fixture, string fault) : IModelTurnAdmission
    {
        public bool IsQuiescent => true;

        public async Task<ModelTurnAdmission> AdmitAsync(ModelTurnChoice choice, ModelContextEnvelope context,
            ModelHandoffReview? review, CancellationToken token)
        {
            var original = (await fixture.Host.AdmitAsync(ModelProviderSelection.OllamaCandidate, context, token)).Turn!;
            var actualContext = fault is "tools" or "evidence"
                ? new ModelContextEnvelope(context.Request, context.SystemPolicy, context.UserRequest,
                    fault is "evidence"
                        ? [new(new(Guid.NewGuid()), context.Request, new(1), "Unreviewed source", ModelEvidenceDisclosure.LocalOnly)]
                        : [],
                    context.CreatedAt, context.ExpiresAt, fault is "tools" ? ModelToolMode.LocalWebPage : ModelToolMode.None)
                : context;
            var turn = new ModelTurn(original.Owner, original.Issuer, original.Provenance, actualContext,
                actualContext.Serialize(), original.SessionGeneration, original.TaskRevision,
                original.ControlRevision, original.Registration);
            if (fault is "already-bound")
            {
                turn.QuestionContinuation = new(fixture.Questions, NeverPresent, context.Request,
                    original.SessionGeneration, context.ExpiresAt, static () => true, fixture.Time);
            }
            return new(new(ModelTurnOutcome.Succeeded, ModelTurnReason.None, original.Provenance), turn);
        }
    }
}
