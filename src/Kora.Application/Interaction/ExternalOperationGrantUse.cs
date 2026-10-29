using Kora.Core.Authorization;
using Kora.Core.Hosting;
using Kora.Core.Interaction;

namespace Kora.Application.Interaction;

internal static class ExternalOperationGrantUse
{
    internal static async ValueTask<HostInteractionDecision> ConsumeAsync(HostAuthorizationService authorization,
        HostOperationProposal proposal, HostRevision generation, HostQuestionRecord? presented,
        HostInteractionDecision decision, bool requireLocalApproval, CancellationToken token)
    {
        if (decision.Grant is not { } grant || grant.ApprovedProposal.Binding != proposal.Binding
            || grant.ApprovedProposal.Effect != proposal.Effect
            || grant.Scope != OperationGrantScope.Perpetual
                && (grant.ApprovedProposal.Request.SessionId != proposal.Request.SessionId || grant.SessionGeneration != generation))
        { return new(HostInteractionOutcome.Denied, "exact-external-grant-required"); }
        if (decision.Outcome == HostInteractionOutcome.Consumed && presented is null
            && grant.Scope is OperationGrantScope.Session or OperationGrantScope.Perpetual
            && decision.Admission is { } admission && admission.Proposal == proposal
            && admission.GrantId == grant.Id && admission.GrantRevision == grant.Revision)
        {
            var current = await authorization.ValidateAdmissionAsync(proposal.Request, admission, token).ConfigureAwait(false);
            return current.Outcome == HostInteractionOutcome.Approved ? decision : current;
        }
        var choice = grant.Scope switch
        {
            OperationGrantScope.Once => "once",
            OperationGrantScope.Session => "session",
            OperationGrantScope.Perpetual => "perpetual",
            _ => throw new InvalidDataException("The external operation grant scope is invalid."),
        };
        if (decision.Outcome != HostInteractionOutcome.Approved || grant.ApprovedProposal != proposal
            || presented?.Proposal != proposal || presented.Key.Request != proposal.Request
            || presented.SessionGeneration != generation
            || decision.Question is not { Status: QuestionStatus.Answered } answer
            || answer.Key.QuestionId != presented.Key.QuestionId || answer.Key.Request != proposal.Request
            || answer.Proposal != proposal || answer.Draft is not { Text: null } draft
            || !draft.Choices.SequenceEqual([choice])
            || answer.AnswerChannel is not (RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice)
            || requireLocalApproval && answer.AnswerChannel != RequestOrigin.LocalUi)
        { return new(HostInteractionOutcome.Denied, "exact-external-approval-required"); }
        return await authorization.ConsumeAsync(proposal.Request, grant.Id, grant.Revision, token).ConfigureAwait(false);
    }
}
