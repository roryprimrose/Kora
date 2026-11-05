using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.Hosting;

public sealed partial class SessionConversationService
{
    internal async Task ValidateManagementWorkAsync(SessionRemainingWorkAnswer answer, Func<bool> admitted, CancellationToken token)
    {
        RequireRemainingWorkAdmission(admitted, token);
        if (answer.OmittedTasks != 0 || answer.OmittedQuestions != 0
            || answer.OtherQueueRecords != 0 || answer.OmittedQueueRecords != 0)
        { throw new InvalidOperationException("Management answering requires the complete bounded ordinary work ledger without diagnostic queue records. Use exact manual status."); }
        var current = await workspace.ReadWorkAsync(answer.Session.Authority.SessionId, token).ConfigureAwait(false);
        RequireRemainingWorkSubject(current, answer.Session);
        RequireRemainingWorkAdmission(admitted, token);
        // The separate destination's canonical writes advance the global authority head, not this source's exact records.
        if (!MatchesRemainingWorkRecords(answer, current) || !IsRemainingWorkCurrent(answer, admitted))
        { throw RemainingWorkChanged(); }
    }

    internal Task<ConversationSubmissionReceipt> SubmitManagementAnswerAsync(SessionWorkspaceEntry destination, string question,
        Func<HostRequest, DateTimeOffset, ModelContextEnvelope> createContext, Func<bool> admitted,
        Func<CancellationToken, Task> validate, ModelProviderSelection provider, CancellationToken token) =>
        SubmitReviewedContextAsync(destination, question, createContext, admitted, validate, null, RequestOrigin.LocalUi,
            token, provenance =>
            {
                if (provenance.Selection != provider)
                { throw new InvalidOperationException("The reviewed local management catalogue selection changed; no substitution."); }
            }, joinCancelledResources: true);
}