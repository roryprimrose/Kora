using Kora.Application.Agents;
using Kora.Application.ViewModels;
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
            new WindowsAgentProfileReader(Services.GetRequiredService<WindowsLocalFileInspector>()));
        var ownership = Services.GetRequiredService<DesktopInstanceOwnershipBridge>();
        agentRuns = new(main, workflow, Services.GetRequiredService<Kora.Application.Hosting.SessionWorkspaceService>(),
            () => ownership.IsReady && !ownership.IsHandoffRecoveryRequired, () => ownership.AdmissionRevision,
            Services.GetRequiredService<ILogger<AgentRunController>>());
        agentRuns.Bind();
    }
}
