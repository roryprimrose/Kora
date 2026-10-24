using Kora.Application.Agents;
using Kora.Application.Hosting;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    public event Func<AgentRunCommand, RequestOrigin, Func<bool>, Task>? AgentRunRequested;
    private Func<Task>? agentShutdown;
    public void BindAgentShutdown(Func<Task>? close) => agentShutdown = close;
    private Task CloseAgentRun() => agentShutdown?.Invoke() ?? Task.CompletedTask;

    private async Task ExecuteAgentCommand(AgentRunCommand command, string text, ManualCallInput input,
        Func<bool>? queuedEligibility)
    {
        var eligible = queuedEligibility ?? CaptureSkillReviewEligibility(input);
        if (IsResponseInteractionPending || !eligible()
            || !isAssistantNameAvailable && commandRouter.IsActivationPrefixed(text, AssistantName)
            || input.Origin is not (RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice)
            || input.Origin == RequestOrigin.ActivatedVoice && !commandRouter.IsActivationPrefixed(text, AssistantName))
        { Transcript = "Agent review requires original private typed input or current-name activated voice, without a pending approval."; return; }
        if (AgentRunRequested is not { } route)
        { Transcript = "Bounded agent execution is unavailable in this host."; return; }
        await uiDispatcher.InvokeAsync(() => HostRequestRunner.RunAsync(input.Origin,
            () => route(command, input.Origin, eligible), operation: HostOperation.Request));
    }
}
