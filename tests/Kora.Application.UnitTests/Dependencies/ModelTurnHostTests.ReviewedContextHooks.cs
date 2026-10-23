using AwesomeAssertions;

using Kora.Application.Dependencies;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.UnitTests.Dependencies;

public sealed partial class ModelTurnHostTests
{
    [Fact]
    public async Task ReviewedCaptureBindsIssuedTurnAfterSourceValidationBeforeDispatchAndProjectionPrecedesDurableAnswer()
    {
        using var f = new Fixture();
        var earlier = Earlier(f);
        var host = QualifiedHistoryHost(f);
        var service = f.ConversationService(host, execution: host);
        var validated = 0;
        ModelTurnProvenance? captured = null;
        var projections = 0;
        var receipt = await service.SubmitReviewedContextAsync(new(f.Session, null), "new request",
            (request, now) => ConversationHistoryContext.CreateEnvelope(request, "new request",
                [new(earlier, ConversationHistoryRole.User)], now, now.AddSeconds(15)),
            static () => true, _ => { validated++; return Task.CompletedTask; }, null, RequestOrigin.LocalUi, Token,
            provenance =>
            {
                validated.Should().BeGreaterThan(3);
                provenance.Request.Should().Be(f.Retained!.Request);
                f.ExecutionStarts.Should().Be(0);
                f.Adapter.Calls.Should().Be(0);
                captured.Should().BeNull();
                captured = provenance;
            },
            original =>
            {
                projections++;
                original.Provenance.Should().Be(captured);
                f.ExecutionCompletions.Should().Be(0);
                host.IsQuiescent.Should().BeTrue();
                return original with { Response = new("verified answer [retained source]", null) };
            });
        projections.Should().Be(1);
        receipt.Message.AssistantResponse!.Body.Should().Be("verified answer [retained source]");
        receipt.Message.AssistantResponse.Provenance.Should().Be(captured);
        f.Retained.Should().Be(receipt.Message);
    }

    [Theory]
    [InlineData("invalid-binding")]
    [InlineData("source")]
    [InlineData("cancel-before")]
    [InlineData("expired")]
    [InlineData("unavailable")]
    public async Task UnadmittedOrClosedReviewedSourceNeverAcquiresCapturePermission(string fault)
    {
        using var f = new Fixture();
        var earlier = Earlier(f);
        var host = QualifiedHistoryHost(f);
        var admission = new TransformedAdmission(host, value =>
        {
            if (fault is "invalid-binding") { value.Turn!.TaskRevision = new(9); }
            if (fault is "expired") { f.Time.Now = f.Time.Now.AddMinutes(1); }
            return fault is "unavailable" ? new(new(ModelTurnOutcome.Unavailable, ModelTurnReason.QualificationPending)) : value;
        }, static () => true);
        var service = f.ConversationService(admission, execution: host);
        var captureCalls = 0;
        var validations = 0;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        async Task<ConversationSubmissionReceipt> Submit() => await service.SubmitReviewedContextAsync(
            new(f.Session, null), "request",
            (request, now) => ConversationHistoryContext.CreateEnvelope(request, "request",
                [new(earlier, ConversationHistoryRole.User)], now, now.AddSeconds(15)), static () => true,
            _ =>
            {
                validations++;
                if (validations == 4)
                {
                    if (fault is "source") { throw new InvalidOperationException("Revoked pending source"); }
                    if (fault is "cancel-before") { cancellation.Cancel(); }
                }
                return Task.CompletedTask;
            }, null, RequestOrigin.LocalUi, cancellation.Token, _ => captureCalls++);
        if (fault is "unavailable") { (await Submit()).Message.Outcome.Should().Be(ModelTurnOutcome.Unavailable); }
        else { await this.Invoking(_ => Submit()).Should().ThrowAsync<ConversationSubmissionUnconfirmedException>(); }
        captureCalls.Should().Be(0);
        f.Adapter.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData("disable")]
    [InlineData("private")]
    [InlineData("cancel")]
    [InlineData("expiry")]
    public async Task CaptureRefusalOrClosureCannotDispatchOrPromoteQueuedContext(string fault)
    {
        using var f = new Fixture();
        var earlier = Earlier(f);
        var host = QualifiedHistoryHost(f);
        var service = f.ConversationService(host, execution: host);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        await service.Invoking(value => value.SubmitReviewedContextAsync(new(f.Session, null), "request",
            (request, now) => ConversationHistoryContext.CreateEnvelope(request, "request",
                [new(earlier, ConversationHistoryRole.User)], now, now.AddSeconds(15)),
            static () => true, _ => Task.CompletedTask, null, RequestOrigin.LocalUi, cancellation.Token, _ =>
            {
                if (fault is "disable") { throw new InvalidOperationException("Disabled before admission capture"); }
                if (fault is "private") { f.CanControl = false; }
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
    public async Task HostProjectionCannotAlterAuthorityOrInventTerminalState(string fault)
    {
        using var f = new Fixture();
        var earlier = Earlier(f);
        var host = QualifiedHistoryHost(f);
        var service = f.ConversationService(host, execution: host);
        await service.Invoking(value => value.SubmitReviewedContextAsync(new(f.Session, null), "request",
            (request, now) => ConversationHistoryContext.CreateEnvelope(request, "request",
                [new(earlier, ConversationHistoryRole.User)], now, now.AddSeconds(15)),
            static () => true, _ => Task.CompletedTask, null, RequestOrigin.LocalUi, Token,
            resolveResponse: original => fault switch
            {
                "null" => null!,
                "provenance" => original with { Provenance = original.Provenance! with { Turn = new(Guid.NewGuid()) } },
                "reason" => original with { Reason = (ModelTurnReason)999 },
                "outcome" => original with { Outcome = ModelTurnOutcome.Denied, Response = null },
                _ => original with { Outcome = ModelTurnOutcome.Unknown },
            })).Should().ThrowAsync<ConversationSubmissionUnconfirmedException>();
        f.ExecutionCompletions.Should().Be(0);
        f.Retained!.AssistantResponse.Should().BeNull();
    }

    [Theory]
    [InlineData("credential")]
    [InlineData("oversize")]
    [InlineData("action")]
    [InlineData("question")]
    [InlineData("unknown")]
    public async Task ProjectedAnswerStillRequiresCredentialAnswerOnlyAndCompleteOutputBudgetValidation(string fault)
    {
        using var f = new Fixture();
        var earlier = Earlier(f);
        var host = QualifiedHistoryHost(f);
        var service = f.ConversationService(host, execution: host);
        var receipt = await service.SubmitReviewedContextAsync(new(f.Session, null), "request",
            (request, now) => ConversationHistoryContext.CreateEnvelope(request, "request",
                [new(earlier, ConversationHistoryRole.User)], now, now.AddSeconds(15)),
            static () => true, _ => Task.CompletedTask, null, RequestOrigin.LocalUi, Token,
            resolveResponse: original => original with
            {
                Outcome = fault is "unknown" ? ModelTurnOutcome.Unknown : original.Outcome,
                Reason = fault is "unknown" ? ModelTurnReason.InvalidProviderResponse : original.Reason,
                Response = fault switch
                {
                    "credential" => new("api_key=synthetic-only-value", null),
                    "oversize" => new(new string('x', 4096), null),
                    "action" => new("not authority", Kora.Core.Commands.BuiltInAction.LockMachine),
                    "question" => new("not answer only", null, Question: new("Choose", ["A", "B"])),
                    _ => null,
                },
            });
        receipt.Message.Outcome.Should().Be(ModelTurnOutcome.Unknown);
        receipt.Message.Reason.Should().Be(ModelTurnReason.InvalidProviderResponse);
        receipt.Message.AssistantResponse.Should().BeNull();
    }

    [Theory]
    [InlineData(ModelTurnOutcome.Denied)]
    [InlineData(ModelTurnOutcome.Cancelled)]
    [InlineData(ModelTurnOutcome.Unknown)]
    public async Task ProjectionIsNeverCalledForProviderFailureOrAcceptedRunningCancellation(ModelTurnOutcome failure)
    {
        using var f = new Fixture();
        var earlier = Earlier(f);
        var host = QualifiedHistoryHost(f);
        var service = f.ConversationService(host, execution: host);
        f.Adapter.Reply = new(failure, ModelProviderSelection.OllamaCandidate);
        var projections = 0;
        var receipt = await service.SubmitReviewedContextAsync(new(f.Session, null), "request",
            (request, now) => ConversationHistoryContext.CreateEnvelope(request, "request",
                [new(earlier, ConversationHistoryRole.User)], now, now.AddSeconds(15)),
            static () => true, _ => Task.CompletedTask, null, RequestOrigin.LocalUi, Token,
            resolveResponse: original => { projections++; return original with { Outcome = ModelTurnOutcome.Succeeded, Response = new("invented", null) }; });
        projections.Should().Be(0);
        receipt.Message.Outcome.Should().Be(failure);
        receipt.Message.AssistantResponse.Should().BeNull();
    }

    [Fact]
    public async Task OwningTerminalIdentityIsValidatedBeforeTrustedProjectionIsInvoked()
    {
        using var f = new Fixture();
        var earlier = Earlier(f);
        var host = QualifiedHistoryHost(f);
        var service = f.ConversationService(host, execution: new InvalidExecution("not-host-terminal"));
        var projections = 0;
        await service.Invoking(value => value.SubmitReviewedContextAsync(new(f.Session, null), "request",
            (request, now) => ConversationHistoryContext.CreateEnvelope(request, "request",
                [new(earlier, ConversationHistoryRole.User)], now, now.AddSeconds(15)),
            static () => true, _ => Task.CompletedTask, null, RequestOrigin.LocalUi, Token,
            resolveResponse: original => { projections++; return original; }))
            .Should().ThrowAsync<ConversationSubmissionUnconfirmedException>();
        projections.Should().Be(0);
    }

    [Fact]
    public async Task AcceptedReviewedTurnCancellationWinsBeforeProjectionAndJoinsActualProvider()
    {
        using var f = new Fixture();
        var earlier = Earlier(f);
        var host = QualifiedHistoryHost(f);
        var service = f.ConversationService(host, execution: host);
        var pending = new TaskCompletionSource<ModelProviderReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Adapter.Pending = pending.Task;
        var target = new SessionWorkspaceEntry(f.Session, null);
        var projections = 0;
        var submission = service.SubmitReviewedContextAsync(target, "request",
            (request, now) => ConversationHistoryContext.CreateEnvelope(request, "request",
                [new(earlier, ConversationHistoryRole.User)], now, now.AddSeconds(15)),
            static () => true, _ => Task.CompletedTask, null, RequestOrigin.LocalUi, Token,
            resolveResponse: original => { projections++; return original; });
        var running = (await service.ReadRunningConversationAsync(target, static () => true, Token))!;
        await service.CancelRunningConversationAsync(running.Target, static () => true, Token);
        submission.IsCompleted.Should().BeFalse();
        f.ExecutionCompletions.Should().Be(0);
        pending.SetResult(LateAnswer());
        var receipt = await submission;
        projections.Should().Be(0);
        receipt.Message.Outcome.Should().Be(ModelTurnOutcome.Cancelled);
        receipt.Message.AssistantResponse.Should().BeNull();
        host.IsQuiescent.Should().BeTrue();
    }
}
