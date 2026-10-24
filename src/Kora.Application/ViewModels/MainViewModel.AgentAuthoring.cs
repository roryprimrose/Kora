using Kora.Application.Agents;
using Kora.Core.Hosting;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    public event Func<AgentProfileAuthoringCommand, RequestOrigin, Func<bool>, Task>? AgentProfileAuthoringRequested;
    private Func<Task<bool>>? agentAuthoringShutdown;
    public void BindAgentProfileAuthoringShutdown(Func<Task<bool>>? shutdown) => agentAuthoringShutdown = shutdown;
}
