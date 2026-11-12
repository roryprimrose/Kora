using Kora.Application.Configuration;
using Kora.Application.Hosting;
using Kora.Application.KnowledgeSources;
using Kora.Application.ViewModels;
using Kora.Core.Auditing;
using Kora.Core.Dependencies;
using Kora.Core.KnowledgeSources;
using Kora.KnowledgeSources;
using Kora.Windows.KnowledgeSources;
using Microsoft.Extensions.DependencyInjection;

namespace Kora;

public sealed partial class App
{
    private SessionKnowledgeSourceController CreateSessionKnowledgeSources(MainViewModel main) =>
        new(main, GetPresenceOwner, () =>
        {
            var preferences = new LocalKnowledgeSourceCaptureLimitsPreferences(Services.GetRequiredService<IApplicationDataPaths>());
            var store = new WindowsKnowledgeSourceStore(Services.GetRequiredService<IApplicationDataPaths>(), preferences);
            var manager = new KnowledgeSourceManager(Services.GetRequiredService<IKnowledgeSourceInspector>(), store,
                Services.GetRequiredService<ISecurityAuditLog>(), TimeProvider.System, preferences);
            return new(Services.GetRequiredService<SessionWorkspaceService>(), manager, store,
                ActivatorUtilities.CreateInstance<KnowledgeSourceAnswerWorkflow>(Services, manager), TimeProvider.System);
        });
}
