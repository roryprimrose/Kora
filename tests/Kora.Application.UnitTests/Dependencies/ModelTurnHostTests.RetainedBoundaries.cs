using AwesomeAssertions;
using Kora.Application.Dependencies;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Providers;
using Kora.Core.Storage;

namespace Kora.Application.UnitTests.Dependencies;

public sealed partial class ModelTurnHostTests
{
    [Theory]
    [InlineData("missing-execution")]
    [InlineData("outcome")]
    [InlineData("response")]
    [InlineData("provenance")]
    [InlineData("request")]
    [InlineData("context")]
    [InlineData("generation")]
    [InlineData("revision")]
    [InlineData("target")]
    [InlineData("expired")]
    [InlineData("clock-back")]
    [InlineData("busy")]
    [InlineData("metadata")]
    public async Task RetainedExecutionRejectsUnboundAdmissionAndChangedPreDispatchState(string fault)
    {
        using var fixture = new Fixture();
        fixture.ProfileCatalogue.Status = ProviderCatalogueStatus.Current;
        fixture.ProfilePreferences.Value = new(ProviderService.Local, [Profile()]);
        var host = fixture.ConfiguredProfiles();
        var quiet = true;
        var admission = new TransformedAdmission(host, admitted =>
        {
            var original = admitted.Turn!;
            var context = original.Context;
            var provenance = fault is "request" ? original.Provenance with
            { Request = HostRequest.Create(RequestOrigin.LocalUi) } : original.Provenance;
            var turn = new ModelTurn(original.Owner, original.Issuer, provenance,
                fault is "context" ? new(context.Request, context.SystemPolicy, context.UserRequest,
                    context.Evidence, context.CreatedAt, context.ExpiresAt) : context,
                original.Wire, fault is "generation" ? new(2) : original.SessionGeneration,
                fault is "revision" ? new(9) : original.TaskRevision, original.ControlRevision, original.Registration);
            if (fault is "target") { turn.ConversationTarget = new(fixture.Session, null); }
            if (fault is "expired") { fixture.Time.Now = context.ExpiresAt; }
            if (fault is "clock-back") { fixture.Time.Now = context.CreatedAt.AddTicks(-1); }
            if (fault is "busy") { quiet = false; }
            if (fault is "metadata") { fixture.Metadata = new(fixture.Session.SessionId, new(1), new("changed")); }
            return new(admitted.Result with
            {
                Outcome = fault is "outcome" ? ModelTurnOutcome.Denied : admitted.Result.Outcome,
                Response = fault is "response" ? new("not an admission answer", null) : null,
                Provenance = fault is "provenance" ? provenance with { Turn = new(Guid.NewGuid()) } : provenance,
            }, turn);
        }, () => quiet);
        var service = fixture.ConversationService(admission, execution: fault is "missing-execution" ? null : host);
        if (fault is "expired" or "clock-back")
        {
            var receipt = await service.SubmitAsync(new(fixture.Session, null), "immutable original", static () => true, Token);
            receipt.Message.Outcome.Should().Be(ModelTurnOutcome.Denied);
            receipt.Message.Reason.Should().Be(ModelTurnReason.ContextExpired);
            receipt.Message.Execution.Should().BeNull();
        }
        else
        {
            await service.Invoking(value => value.SubmitAsync(new(fixture.Session, null), "immutable original", static () => true, Token))
                .Should().ThrowAsync<ConversationSubmissionUnconfirmedException>();
        }
        fixture.ExecutionStarts.Should().Be(0);
        fixture.Adapter.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData("body")]
    [InlineData("generation")]
    [InlineData("sequence")]
    [InlineData("task-request")]
    [InlineData("task-state")]
    [InlineData("task-revision")]
    [InlineData("readback")]
    [InlineData("expiry")]
    public async Task DispatchReceiptAndExactReadbackCannotBeSubstitutedBeforeAdapterUse(string fault)
    {
        using var fixture = new Fixture();
        fixture.ProfileCatalogue.Status = ProviderCatalogueStatus.Current;
        fixture.ProfilePreferences.Value = new(ProviderService.Local, [Profile()]);
        var host = fixture.ConfiguredProfiles();
        fixture.StartReceiptFault = fault;
        if (fault is "readback")
        { fixture.AfterExecutionStart = () => fixture.Retained = fixture.Retained! with { Body = "changed durable original" }; }
        if (fault is "expiry") { fixture.AfterExecutionStart = () => fixture.Time.Now = fixture.Time.Now.AddMinutes(1); }
        await fixture.ConversationService(host, execution: host).Invoking(value =>
            value.SubmitAsync(new(fixture.Session, null), "immutable original", static () => true, Token))
            .Should().ThrowAsync<ConversationSubmissionUnconfirmedException>();
        fixture.Adapter.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData("not-host-terminal")]
    [InlineData("provenance")]
    [InlineData("outcome")]
    [InlineData("reason")]
    [InlineData("failure-response")]
    public async Task AReplacementExecutionPortCannotLaunderUntrustedTerminalData(string fault)
    {
        using var fixture = new Fixture();
        fixture.ProfileCatalogue.Status = ProviderCatalogueStatus.Current;
        fixture.ProfilePreferences.Value = new(ProviderService.Local, [Profile()]);
        var host = fixture.ConfiguredProfiles();
        await fixture.ConversationService(host, execution: new InvalidExecution(fault)).Invoking(value =>
            value.SubmitAsync(new(fixture.Session, null), "immutable original", static () => true, Token))
            .Should().ThrowAsync<ConversationSubmissionUnconfirmedException>();
        fixture.ExecutionCompletions.Should().Be(0);
    }

    [Theory]
    [InlineData("outcome")]
    [InlineData("reason")]
    [InlineData("answer")]
    [InlineData("execution")]
    [InlineData("generation")]
    [InlineData("sequence")]
    [InlineData("revision")]
    [InlineData("task-revision")]
    public async Task TerminalReceiptMismatchCannotBePublishedAsAConfirmedAssistantAnswer(string fault)
    {
        using var fixture = new Fixture();
        fixture.ProfileCatalogue.Status = ProviderCatalogueStatus.Current;
        fixture.ProfilePreferences.Value = new(ProviderService.Local, [Profile()]);
        var host = fixture.ConfiguredProfiles();
        fixture.TerminalReceiptFault = fault;
        await fixture.ConversationService(host, execution: host).Invoking(value =>
            value.SubmitAsync(new(fixture.Session, null), "immutable original", static () => true, Token))
            .Should().ThrowAsync<ConversationSubmissionUnconfirmedException>();
        fixture.Adapter.Calls.Should().Be(1);
    }

    [Theory]
    [InlineData(ModelTurnOutcome.Cancelled)]
    [InlineData(ModelTurnOutcome.Unknown)]
    public async Task CooperativeNonSuccessIsRetainedWithoutAnAnswerOrPhysicalStopClaim(ModelTurnOutcome outcome)
    {
        using var fixture = new Fixture();
        fixture.ProfileCatalogue.Status = ProviderCatalogueStatus.Current;
        fixture.ProfilePreferences.Value = new(ProviderService.Local, [Profile()]);
        var host = fixture.ConfiguredProfiles();
        fixture.Adapter.Reply = new(outcome, ModelProviderSelection.OllamaCandidate);
        var receipt = await fixture.ConversationService(host, execution: host).SubmitAsync(new(fixture.Session, null),
            "immutable original", static () => true, Token);
        receipt.Message.Outcome.Should().Be(outcome);
        receipt.Message.AssistantResponse.Should().BeNull();
        receipt.Task.State.Should().Be(HostTaskState.Unknown);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CapturedProfileWithoutItsConsumedAuthorityServicesIsExplicitlyUnavailable(bool hasProfileStore)
    {
        using var fixture = new Fixture();
        using var root = fixture.Root();
        var host = fixture.Qualified(ModelProviderSelection.OllamaCandidate, composeProfileStoreOnly: hasProfileStore);
        var admission = await host.AdmitAsync(ModelProviderSelection.OllamaCandidate, fixture.Context(), Token);
        admission.Turn!.Profile = Profile();
        var result = await host.RunAsync(admission.Turn, Token);
        result.Outcome.Should().Be(ModelTurnOutcome.Unavailable);
        result.Reason.Should().Be(ModelTurnReason.SessionNotAdmitted);
        fixture.Adapter.Calls.Should().Be(0);
    }

    [Fact]
    public async Task NamedRetainedExecutionKeepsItsExactMetadataRevisionThroughTerminalReadback()
    {
        using var fixture = new Fixture();
        fixture.Metadata = new(fixture.Session.SessionId, new(7), new("private exact target"));
        fixture.ProfileCatalogue.Status = ProviderCatalogueStatus.Current;
        fixture.ProfilePreferences.Value = new(ProviderService.Local, [Profile()]);
        var host = fixture.ConfiguredProfiles();
        var receipt = await fixture.ConversationService(host, execution: host).SubmitAsync(new(fixture.Session, fixture.Metadata),
            "immutable original", static () => true, Token);
        receipt.Message.ExecutionMetadataRevision.Should().Be(7);
        receipt.Message.Outcome.Should().Be(ModelTurnOutcome.Succeeded);
    }

    private sealed class TransformedAdmission(IModelTurnAdmission inner,
        Func<ModelTurnAdmission, ModelTurnAdmission> transform, Func<bool> quiet) : IModelTurnAdmission
    {
        public bool IsQuiescent => quiet() && inner.IsQuiescent;
        public async Task<ModelTurnAdmission> AdmitAsync(ModelTurnChoice choice, ModelContextEnvelope context,
            ModelHandoffReview? review, CancellationToken token) =>
            transform(await inner.AdmitAsync(choice, context, review, token));
    }

    private sealed class InvalidExecution(string fault) : IModelTurnExecution
    {
        public Task WaitForQuiescenceAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public Task<ModelTurnResult> RunAsync(ModelTurn turn, Func<bool> admitted, CancellationToken token,
            Func<CancellationToken, Task>? validateSource = null)
        {
            var result = new ModelTurnResult(fault is "outcome" ? (ModelTurnOutcome)999 : ModelTurnOutcome.Unknown,
                fault is "reason" ? (ModelTurnReason)999 : ModelTurnReason.ProviderOutcome,
                fault is "provenance" ? turn.Provenance with { Turn = new(Guid.NewGuid()) } : turn.Provenance,
                fault is "failure-response" ? new("not a successful answer", null) : null);
            if (fault is not "not-host-terminal") { turn.TerminalResult = result; }
            return Task.FromResult(result);
        }
    }
}
