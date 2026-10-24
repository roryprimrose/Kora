using Kora.Application.Agents;
using Kora.Application.ViewModels;
using Kora.Core.Agents;
using Kora.Core.Dependencies;
using Kora.Windows.Artifacts;
using Kora.Windows.Context;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Kora;

public sealed partial class App
{
    private AgentProfileEditorController? agentAuthoring;
    private void BindAgentProfileAuthoring(MainViewModel main)
    {
        IOwnedAgentProfileStore store = new WindowsOwnedAgentProfileStore(Services.GetRequiredService<IApplicationDataPaths>());
        var workflow = ActivatorUtilities.CreateInstance<AgentProfileSaveWorkflow>(Services, store);
        var ownership = Services.GetRequiredService<DesktopInstanceOwnershipBridge>();
        agentAuthoring = new(main, workflow, () => ownership.IsReady && !ownership.IsHandoffRecoveryRequired,
            () => ownership.AdmissionRevision, Services.GetRequiredService<ILogger<AgentProfileEditorController>>());
        agentAuthoring.Bind();
    }
}
