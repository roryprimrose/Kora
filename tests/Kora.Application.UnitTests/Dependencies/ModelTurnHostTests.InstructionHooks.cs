using AwesomeAssertions;

using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.UnitTests.Dependencies;

public sealed partial class ModelTurnHostTests
{
    private static ModelContextEnvelope ReviewedInstructionEnvelope(HostRequest request, DateTimeOffset now) =>
        new(request, "Host policy", "Request", [new(new(Guid.NewGuid()), request, new(1), "Guidance",
            ModelEvidenceDisclosure.LocalOnly)], now, now.AddSeconds(15), ModelToolMode.None);

    [Theory]
    [InlineData("null-factory")]
    [InlineData("null-validator")]
    [InlineData("request")]
    [InlineData("body")]
    [InlineData("tools")]
    [InlineData("empty")]
    [InlineData("hosted")]
    public async Task InstructionReviewedContextSeamRejectsInvalidInputBeforeRetention(string fault)
    {
        using var f = new Fixture();
        var service = f.ConversationService(f.PolicyHost, execution: f.PolicyHost);
        Func<HostRequest, DateTimeOffset, ModelContextEnvelope> factory = (request, now) =>
        {
            var selected = fault is "request" ? HostRequest.Create(RequestOrigin.LocalUi) : request;
            return new(selected, "Policy", fault is "body" ? "Changed" : "Request",
                fault is "empty" ? [] : [new(new(Guid.NewGuid()), selected, new(1), "Guidance",
                    fault is "hosted" ? ModelEvidenceDisclosure.HostedEligible : ModelEvidenceDisclosure.LocalOnly)],
                now, now.AddSeconds(15), fault is "tools" ? ModelToolMode.ReadOnly : ModelToolMode.None);
        };
        await service.Invoking(value => value.SubmitReviewedContextAsync(new(f.Session, null), "Request",
            fault is "null-factory" ? null! : factory, static () => true,
            fault is "null-validator" ? null! : _ => Task.CompletedTask,
            null, RequestOrigin.LocalUi, Token)).Should().ThrowAsync<Exception>();
        f.RetainCalls.Should().Be(0);
        f.Adapter.Calls.Should().Be(0);
    }

    [Fact]
    public async Task InstructionHookBindsValidatedIssuedProvenanceBeforeDispatchAndProjectsOnlyVerifiedHostAnswer()
    {
        using var f = new Fixture();
        var service = f.ConversationService(f.PolicyHost, execution: f.PolicyHost);
        ModelTurnProvenance? captured = null;
        var validations = 0;
        var receipt = await service.SubmitReviewedContextAsync(new(f.Session, null), "Request",
            ReviewedInstructionEnvelope, static () => true, _ => { validations++; return Task.CompletedTask; },
            null, RequestOrigin.LocalUi, Token, provenance =>
            {
                validations.Should().BeGreaterThan(3);
                provenance.Request.Should().Be(f.Retained!.Request);
                f.ExecutionStarts.Should().Be(0);
                f.Adapter.Calls.Should().Be(0);
                captured = provenance;
            }, result =>
            {
                result.Provenance.Should().Be(captured);
                f.ExecutionCompletions.Should().Be(0);
                return result with { Response = new("Projected host-verified answer", null) };
            });
        receipt.Message.AssistantResponse!.Body.Should().Be("Projected host-verified answer");
        receipt.Message.Execution.Should().Be(captured);
    }

    [Theory]
    [InlineData("invalid-binding")]
    [InlineData("source")]
    [InlineData("cancel-before")]
    [InlineData("metadata")]
    [InlineData("live-during-metadata")]
    public async Task InstructionHookCannotCaptureBeforeExactAdmissionSourceSessionAndCancellationChecks(string fault)
    {
        using var f = new Fixture();
        var admission = new TransformedAdmission(f.PolicyHost, value =>
        {
            if (fault is "invalid-binding") { value.Turn!.TaskRevision = new(9); }
            return value;
        }, static () => true);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var validations = 0;
        var captures = 0;
        var live = true;
        await f.ConversationService(admission, execution: f.PolicyHost).Invoking(service =>
            service.SubmitReviewedContextAsync(new(f.Session, null), "Request", ReviewedInstructionEnvelope,
                () => live, _ =>
                {
                    if (++validations == 4)
                    {
                        if (fault is "source") { throw new InvalidOperationException("Source revoked"); }
                        if (fault is "cancel-before") { cancellation.Cancel(); }
                        if (fault is "metadata") { f.Metadata = new(f.Session.SessionId, new(1), new("Changed")); }
                        if (fault is "live-during-metadata") { f.OnRead = () => live = false; }
                    }
                    return Task.CompletedTask;
                }, null, RequestOrigin.LocalUi, cancellation.Token, _ => captures++))
            .Should().ThrowAsync<ConversationSubmissionUnconfirmedException>();
        captures.Should().Be(0);
        f.ExecutionStarts.Should().Be(0);
        f.Adapter.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData("refuse")]
    [InlineData("privacy")]
    [InlineData("cancel")]
    [InlineData("expiry")]
    public async Task InstructionHookClosureCannotDispatchAfterTheCaptureCallback(string fault)
    {
        using var f = new Fixture();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        await f.ConversationService(f.PolicyHost, execution: f.PolicyHost).Invoking(service =>
            service.SubmitReviewedContextAsync(new(f.Session, null), "Request", ReviewedInstructionEnvelope,
                static () => true, _ => Task.CompletedTask, null, RequestOrigin.LocalUi, cancellation.Token, _ =>
                {
                    if (fault is "refuse") { throw new InvalidOperationException("Capture refused"); }
                    if (fault is "privacy") { f.CanControl = false; }
                    if (fault is "cancel") { cancellation.Cancel(); }
                    if (fault is "expiry") { f.Time.Now = f.Time.Now.AddMinutes(1); }
                })).Should().ThrowAsync<ConversationSubmissionUnconfirmedException>();
        f.ExecutionStarts.Should().Be(0);
        f.Adapter.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("provenance")]
    [InlineData("reason")]
    [InlineData("outcome")]
    [InlineData("unknown-response")]
    [InlineData("throw")]
    public async Task InstructionResponseProjectionCannotForgeAuthorityOrPromoteInvalidTerminalData(string fault)
    {
        using var f = new Fixture();
        await f.ConversationService(f.PolicyHost, execution: f.PolicyHost).Invoking(service =>
            service.SubmitReviewedContextAsync(new(f.Session, null), "Request", ReviewedInstructionEnvelope,
                static () => true, _ => Task.CompletedTask, null, RequestOrigin.LocalUi, Token,
                resolveResponse: result => fault switch
                {
                    "null" => null!,
                    "provenance" => result with { Provenance = result.Provenance! with { Turn = new(Guid.NewGuid()) } },
                    "reason" => result with { Reason = (ModelTurnReason)999 },
                    "outcome" => result with { Outcome = ModelTurnOutcome.Denied, Response = null },
                    "throw" => throw new InvalidDataException("Unverifiable response"),
                    _ => result with { Outcome = ModelTurnOutcome.Unknown },
                })).Should().ThrowAsync<ConversationSubmissionUnconfirmedException>();
        f.ExecutionCompletions.Should().Be(0);
        f.Retained!.AssistantResponse.Should().BeNull();
    }

    [Fact]
    public async Task InstructionResponseProjectionMaySuppressAnAnswerWithoutInventingASuccessOrRunningOnFailure()
    {
        using var f = new Fixture();
        var service = f.ConversationService(f.PolicyHost, execution: f.PolicyHost);
        var unknown = await service.SubmitReviewedContextAsync(new(f.Session, null), "Request", ReviewedInstructionEnvelope,
            static () => true, _ => Task.CompletedTask, null, RequestOrigin.LocalUi, Token,
            resolveResponse: result => result with { Outcome = ModelTurnOutcome.Unknown, Response = null });
        unknown.Message.Outcome.Should().Be(ModelTurnOutcome.Unknown);
        unknown.Message.AssistantResponse.Should().BeNull();
        f.Adapter.Reply = new(ModelTurnOutcome.Unknown, ModelProviderSelection.OllamaCandidate);
        var calls = 0;
        var failed = await service.SubmitReviewedContextAsync(new(f.Session, null), "Request", ReviewedInstructionEnvelope,
            static () => true, _ => Task.CompletedTask, null, RequestOrigin.LocalUi, Token,
            resolveResponse: result => { calls++; return result; });
        calls.Should().Be(0);
        failed.Message.Outcome.Should().Be(ModelTurnOutcome.Unknown);
    }
}
