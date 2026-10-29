using Kora.Application.Dependencies;
using Kora.Application.Diagnostics.EvidenceAnswers;
using Kora.Application.Hosting;
using Kora.Core.Auditing;
using Kora.Core.Storage;
using Kora.EvidenceAnswers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Kora;

public sealed partial class App
{
    private static EvidenceAnswerWorkflow CreateEvidenceAnswers() => new(
        Services.GetRequiredService<Kora.Application.Context.LocalSourceUsePolicy>(),
        new EvidenceAnswerConversation(Services.GetRequiredService<SessionWorkspaceService>(),
            Services.GetRequiredService<SessionConversationService>(),
            Services.GetRequiredService<IModelTurnExecution>()),
        Services.GetRequiredService<ISessionWorkspaceAccess>(),
        Services.GetRequiredService<ISecurityAuditLog>(),
        Services.GetRequiredService<TimeProvider>(),
        Services.GetRequiredService<ILogger<EvidenceAnswerWorkflow>>());
}
