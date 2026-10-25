using Kora.Application.Agents;
using Kora.Application.Dependencies;
using Kora.Application.Hosting;
using Kora.Application.ViewModels;
using Kora.Core.Auditing;
using Kora.Core.Conversation;
using Kora.Core.Storage;
using Kora.Core.Dependencies;
using Kora.Core.Providers;
using Kora.Windows.Artifacts;
using Kora.Windows.Context;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Kora;

public sealed partial class App
{
    internal sealed class UnavailableLocalAgentConcurrencyQualification : ILocalAgentConcurrencyQualification
    {
        public LocalAgentCapacity? Observe(ModelProviderSelection selection, ProviderProfileTuple? profile, IModelTurnAdapter adapter) => null;
        public bool IsCurrent(LocalAgentCapacity capacity) => false;
    }

    private AgentRunSetController? independentAgentRuns;

    private void BindAgentRunSet(MainViewModel main)
    {
        var host = Services.GetRequiredService<ModelTurnHost>();
        var factory = new AgentRunSetFactory(host, new UnavailableLocalAgentConcurrencyQualification(), admission =>
        {
            var conversation = new SessionConversationService(
                Services.GetRequiredService<ISessionConversationStore>(),
                Services.GetRequiredService<ISessionWorkspaceStore>(),
                Services.GetRequiredService<SessionWorkspaceService>(),
                Services.GetRequiredService<ISessionWorkspaceAccess>(), admission,
                Services.GetRequiredService<TimeProvider>(),
                Services.GetRequiredService<ILogger<SessionConversationService>>(),
                admission, Services.GetRequiredService<ISecurityAuditLog>());
            return new BoundedAgentWorkflow(
                new WindowsAgentProfileReader(Services.GetRequiredService<WindowsLocalFileInspector>()),
                Services.GetRequiredService<ISessionWorkspaceStore>(),
                Services.GetRequiredService<ISessionWorkspaceAccess>(), conversation, admission,
                Services.GetRequiredService<ISecurityAuditLog>(), Services.GetRequiredService<TimeProvider>(),
                ActivatorUtilities.CreateInstance<AgentInstructionReferenceResolver>(Services),
                Services.GetRequiredService<Kora.Core.Artifacts.IArtifactUsePolicy>());
        });
        var ownership = Services.GetRequiredService<DesktopInstanceOwnershipBridge>();
        independentAgentRuns = new(main, factory, Services.GetRequiredService<SessionWorkspaceService>(),
            () => ownership.IsReady && !ownership.IsHandoffRecoveryRequired, () => ownership.AdmissionRevision,
            Services.GetRequiredService<ILogger<AgentRunSetController>>());
        independentAgentRuns.Bind();
    }
}
