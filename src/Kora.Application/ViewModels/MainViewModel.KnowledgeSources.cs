using Kora.Application.Hosting;
using Kora.Application.KnowledgeSources;
using Kora.Core.Hosting;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    private Func<Task>? knowledgeSourcesShutdown;
    private long knowledgeAnswerRevision;
    public long KnowledgeSourceAnswerRevision => Interlocked.Read(ref knowledgeAnswerRevision);
    public event Func<KnowledgeSourceAnswerCommand, Func<bool>, Task>? KnowledgeSourceAnswerRequested;

    public Func<bool> CaptureKnowledgeSourceAnswerEligibility(RequestOrigin origin)
    {
        var call = CallPolicyRevision;
        var revision = KnowledgeSourceAnswerRevision;
        return () => revision == KnowledgeSourceAnswerRevision && !IsProtectedCall && IsClipboardEligible(origin, call);
    }

    private async Task ExecuteKnowledgeSourceAnswer(KnowledgeSourceAnswerCommand command, string text,
        ManualCallInput input, Func<bool>? queuedEligibility)
    {
        var sourceCurrent = queuedEligibility ?? CaptureSkillReviewEligibility(input);
        var hostCurrent = CaptureKnowledgeSourceAnswerEligibility(input.Origin);
        bool Current() => sourceCurrent() && hostCurrent();
        if (IsResponseInteractionPending)
        {
            Transcript = "Named-source answer review is unavailable while a question or approval is pending.";
            return;
        }
        if (!Current()
            || !isAssistantNameAvailable && commandRouter.IsActivationPrefixed(text, AssistantName)
            || input.Origin == RequestOrigin.ActivatedVoice && !HasCurrentNameActivation(text))
        {
            ReportHostInteractionFailure("Named-source answers require original private typed input or current-name activated voice, without a pending interaction.");
            return;
        }
        if (command.Invalid || KnowledgeSourceAnswerRequested is not { } route)
        {
            ReportHostInteractionFailure(command.Invalid
                ? "Use answer using source \"Exact source name\": unchanged question. No model was invoked."
                : "Named-source answer review is unavailable. No fallback was invoked.");
            return;
        }
        await uiDispatcher.InvokeAsync(() => HostRequestRunner.RunAsync(input.Origin, () => route(command, Current)));
    }

    public void BindKnowledgeSourcesShutdown(Func<Task>? shutdown) => knowledgeSourcesShutdown = shutdown;

    private async Task JoinKnowledgeSourcesAsync()
    {
        if (knowledgeSourcesShutdown is { } shutdown) { await shutdown().ConfigureAwait(false); }
    }
}
