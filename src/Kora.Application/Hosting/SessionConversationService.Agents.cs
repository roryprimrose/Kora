using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.Hosting;

public sealed partial class SessionConversationService
{
    internal Task<ConversationSubmissionReceipt> SubmitAgentAsync(SessionWorkspaceEntry target, string input,
        Func<HostRequest, DateTimeOffset, ModelContextEnvelope> createContext, Func<bool> current,
        Func<CancellationToken, Task> validate, RequestOrigin origin,
        Action<ModelTurnProvenance> onAdmitted, CancellationToken token)
    {
        ModelContextEnvelope Create(HostRequest request, DateTimeOffset now)
        {
            var context = createContext(request, now);
            if (context.Request != request || !string.Equals(context.UserRequest, input, StringComparison.Ordinal)
                || context.Constraints is null || context.Evidence.IsEmpty
                || context.Evidence.Any(item => item.Disclosure != ModelEvidenceDisclosure.LocalOnly))
            { throw new InvalidDataException("Agent execution requires exact original input, local-only profile evidence and immutable host constraints."); }
            return context;
        }
        return SubmitCoreAsync(target, input, current, validate, null, origin, Create, ModelTurnChoice.Local,
            token, onAdmitted);
    }
}
