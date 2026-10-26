using System.Diagnostics;
using Kora.Application.Dependencies;
using Kora.Application.Network;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Network;
using Kora.Core.Storage;

namespace Kora.Application.Hosting;

public sealed partial class SessionConversationService
{
    /// <summary>Separate host-only tool-capable source consumer; the instruction-only consumer stays tool-free.</summary>
    internal Task<ConversationSubmissionReceipt> SubmitReviewedToolSkillAsync(SessionWorkspaceEntry target, string body,
        Func<HostRequest, DateTimeOffset, ModelContextEnvelope> createContext, Uri destination, WebPageApprovalPresenter presenter,
        Func<bool> admitted, Func<CancellationToken, Task> validateSource, IEnumerable<ActivityLink> links,
        RequestOrigin origin, CancellationToken token) =>
        SubmitReviewedToolSkillAsync(target, body, createContext, new LocalWebPageScope([destination]), presenter,
            admitted, validateSource, links, origin, token);

    internal async Task<ConversationSubmissionReceipt> SubmitReviewedToolSkillAsync(SessionWorkspaceEntry target, string body,
        Func<HostRequest, DateTimeOffset, ModelContextEnvelope> createContext, LocalWebPageScope scope, WebPageApprovalPresenter presenter,
        Func<bool> admitted, Func<CancellationToken, Task> validateSource, IEnumerable<ActivityLink> links,
        RequestOrigin origin, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(presenter);
        ArgumentNullException.ThrowIfNull(scope);
        if (webPageAction is null || interactions is null || turns is not ModelTurnHost modelHost)
        { throw new InvalidOperationException("The governed local page action and native approval host are unavailable. Nothing was submitted."); }
        LocalWebPageWorkflow? invocation = null;
        var terminalCommitted = false;
        async Task Validate(CancellationToken checkToken)
        {
            await validateSource(checkToken).ConfigureAwait(false);
            if (!terminalCommitted && invocation is not null) { await invocation.ValidateAsync(checkToken).ConfigureAwait(false); }
        }
        ModelContextEnvelope Create(HostRequest request, DateTimeOffset now)
        {
            var context = createContext(request, now);
            if (context.Request != request || !string.Equals(context.UserRequest, body, StringComparison.Ordinal)
                || context.ToolMode != ModelToolMode.LocalWebPage || context.Evidence.Length != 2
                || context.WebPages is null || !context.WebPages.Destinations.SequenceEqual(scope.Destinations)
                || context.Evidence.Any(item => item.Disclosure != ModelEvidenceDisclosure.LocalOnly))
            { throw new InvalidDataException("Reviewed tool skill context requires exact original input, local-only instruction and destination evidence, and the single bounded host tool mode."); }
            return context;
        }
        void Bind(ModelTurn turn, Func<bool> live)
        {
            var workflowInvocation = new HostId<InvocationIdentity>(Guid.NewGuid());
            invocation = new(scope.Destinations.Select(destination => (destination,
                new LocalWebPageInvocation(webPageAction, interactions, presenter, turn.Provenance.Request, target.Authority.Generation,
                    destination, turn.Context.ExpiresAt, live, time, workflowInvocation, scope, validateSource))).ToArray());
            modelHost.BindWebPage(turn, invocation);
        }
        ModelTurnResult Resolve(ModelTurnResult result) => invocation?.Completed == true
            ? result : new(ModelTurnOutcome.Unknown, ModelTurnReason.ToolResultRejected, result.Provenance);
        try
        {
            return await SubmitCoreAsync(target, body, admitted, Validate, links, origin, Create, ModelTurnChoice.Local, token,
                resolveResponse: Resolve, bindWebPage: Bind, terminalCommitted: () => terminalCommitted = true).ConfigureAwait(false);
        }
        finally
        {
            if (invocation is not null) { await modelHost.WaitForQuiescenceAsync(CancellationToken.None).ConfigureAwait(false); }
        }
    }
}
