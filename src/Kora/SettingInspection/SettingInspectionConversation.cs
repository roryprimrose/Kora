using Kora.Application.Dependencies;
using Kora.Application.Configuration.SettingInspection;
using Kora.Application.Hosting;
using Kora.Core.Configuration.SettingInspection;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.SettingInspection;

internal sealed class SettingInspectionConversation(SessionWorkspaceService sessions, SessionConversationService conversations,
    IModelTurnExecution execution) : ISettingInspectionConversation
{
    public async Task<ConversationSubmissionReceipt> SubmitAsync(SettingInspectionReview review, Func<bool> current,
        Func<CancellationToken, Task> validate, CancellationToken token)
    {
        using var destination = HostActivity.BeginRoot(review.Destination, HostActivityLayer.Application,
            HostOperation.Request, [new(HostActivity.RequireCurrent().Activity!.Context)]);
        try
        {
            await validate(token).ConfigureAwait(false);
            var target = await sessions.CreateLocalContextSessionAsync(review.Destination, current, token).ConfigureAwait(false);
            await validate(token).ConfigureAwait(false);
            HostRequest? original = null;
            ModelContextEnvelope Bind(HostRequest request, DateTimeOffset now)
            {
                if (original is not null) { throw new InvalidOperationException("Setting inspection is single-use."); }
                original = request;
                return review.Bind(request, target.Authority.Generation, now);
            }
            var receipt = await conversations.SubmitReviewedContextAsync(target, review.Question, Bind, current,
                validate, [new(destination.Activity!.Context)], review.Original.Origin, token,
                resolveResponse: result => Resolve(original, result)).ConfigureAwait(false);
            destination.Complete(receipt.Message.Outcome == ModelTurnOutcome.Succeeded
                ? HostOperationOutcome.Completed : receipt.Message.Outcome == ModelTurnOutcome.Cancelled
                    ? HostOperationOutcome.Cancelled : HostOperationOutcome.Failed);
            return receipt;
        }
        finally { await execution.WaitForQuiescenceAsync(CancellationToken.None).ConfigureAwait(false); }
    }

    internal static ModelTurnResult Resolve(HostRequest? request, ModelTurnResult result)
    {
        if (result.Outcome != ModelTurnOutcome.Succeeded) { return result; }
        if (request is null || result.Provenance is not { } provenance || !ReferenceEquals(provenance.Request, request)
            || provenance.Selection.Provider != ModelProviderIdentity.Ollama
            || result.Response is not { Answer: { } answer, Action: null, GrantChange: null, Question: null }
            || string.IsNullOrWhiteSpace(answer))
        { return new(ModelTurnOutcome.Unknown, ModelTurnReason.InvalidProviderResponse, result.Provenance); }
        return result;
    }

    public Task JoinAsync() => execution.WaitForQuiescenceAsync(CancellationToken.None);
}
