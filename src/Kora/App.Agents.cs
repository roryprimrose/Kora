using Kora.Application.Agents;
using Kora.Application.ViewModels;
using Kora.Core.Artifacts;
using Kora.Core.Configuration;
using Kora.Windows.Artifacts;
using Kora.Windows.Context;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Kora;

public sealed partial class App
{
    private AgentRunController? agentRuns;
    private void BindAgentRuns(MainViewModel main)
    {
        var workflow = ActivatorUtilities.CreateInstance<BoundedAgentWorkflow>(Services,
            new WindowsAgentProfileReader(Services.GetRequiredService<WindowsLocalFileInspector>()),
            ActivatorUtilities.CreateInstance<AgentInstructionReferenceResolver>(Services),
            CreateAgentSkillReferenceResolver());
        var ownership = Services.GetRequiredService<DesktopInstanceOwnershipBridge>();
        agentRuns = new(main, workflow, Services.GetRequiredService<Kora.Application.Hosting.SessionWorkspaceService>(),
            () => ownership.IsReady && !ownership.IsHandoffRecoveryRequired, () => ownership.AdmissionRevision,
            Services.GetRequiredService<ILogger<AgentRunController>>(),
            new NamedAgentResolver(Services.GetRequiredService<ArtifactCatalogue>(), (selected, token) =>
                Task.Run(() => new WindowsDiskArtifactDiscovery(Services.GetRequiredService<Kora.Core.Dependencies.IApplicationDataPaths>(),
                    Services.GetRequiredService<IArtifactDirectoryPreferences>().Load()).ResolveAgentPath(selected, token), token)));
        agentRuns.Bind();
        BindAgentProfileAuthoring(main);
        BindAgentRunSet(main);
    }
}
