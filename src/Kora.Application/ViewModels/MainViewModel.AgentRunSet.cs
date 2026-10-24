using Kora.Application.Agents;
using Kora.Core.Hosting;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    public event Func<AgentRunSetCommand, RequestOrigin, Func<bool>, Task>? AgentRunSetRequested;
    private Func<Task>? agentRunSetShutdown;
    public void BindAgentRunSetShutdown(Func<Task>? close) => agentRunSetShutdown = close;

    private Task CloseAgentRunSet() => agentRunSetShutdown?.Invoke() ?? Task.CompletedTask;
}
