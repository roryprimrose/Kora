using Kora.Application.Hosting;
using Kora.Application.Skills;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Communication;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    private ISkillReviewNavigation? skillReviewNavigation;
    private Func<long> skillReviewOwnershipRevision = static () => 0;
    private long skillReviewBindingRevision;

    internal bool IsSkillReviewCallEligible => !CallObservation.ManualControlEvidenceUnavailable
        && AutomaticCallState is CallState.Unavailable or CallState.Clear or CallState.Active or CallState.Suspected;

    public void BindSkillReviewNavigation(ISkillReviewNavigation? navigation, Func<long> ownershipRevision)
    {
        skillReviewNavigation = navigation;
        skillReviewOwnershipRevision = ownershipRevision;
        Interlocked.Increment(ref skillReviewBindingRevision);
    }

    private void ReportRetiredSkillReview(string text)
    {
        if (SkillReviewCommand.Parse(text, AssistantName) is not null)
        {
            Transcript = "Skill review navigation is unavailable because activated voice admission was retired.";
        }
    }

    private Func<bool> CaptureSkillReviewEligibility(ManualCallInput input, OwnedSkillQuestionContinuation? questions = null)
    {
        var binding = Interlocked.Read(ref skillReviewBindingRevision);
        var owner = skillReviewOwnershipRevision();
        return () => (questions?.IsCurrent() ?? input.Eligible()) && input.Revision == CallPolicyRevision
            && binding == Interlocked.Read(ref skillReviewBindingRevision) && owner == skillReviewOwnershipRevision()
            && IsSkillReviewCallEligible && !IsResponseInteractionPending;
    }

    private async Task ExecuteSkillReviewCommand(SkillReviewCommand command, string text,
        ManualCallInput input, Func<bool>? queuedEligibility)
    {
        if (IsResponseInteractionPending)
        {
            Transcript = "Skill review navigation is unavailable while a question or security review is pending.";
            return;
        }
        if (command.Surface is not { } surface)
        {
            Transcript = "Use an exact review-only command: " + SkillReviewCommand.Syntax + ". No names or paths are accepted.";
            return;
        }
        if (!isAssistantNameAvailable && commandRouter.IsActivationPrefixed(text, AssistantName)
            || input.Origin is not (RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice)
            || input.Origin == RequestOrigin.ActivatedVoice && !HasCurrentNameActivation(text))
        {
            Transcript = "Skill review navigation requires original typed input or current-name activated voice.";
            return;
        }
        var navigation = skillReviewNavigation;
        var eligible = queuedEligibility ?? CaptureSkillReviewEligibility(input);
        await uiDispatcher.InvokeAsync(() =>
        {
            HostRequestRunner.Run(input.Origin, () =>
            {
                if (!eligible() || navigation is null || !navigation.TryOpen(surface, eligible))
                {
                    Transcript = "Skill review navigation is unavailable; no inspector was confirmed opened.";
                    return;
                }
                Transcript = surface == SkillReviewSurface.BundledPackages
                    ? "Bundled skill package review opened. Nothing was enabled or invoked."
                    : "Shared skill source review opened. Registration metadata refresh has its own native status; no discovery, registration or invocation was requested.";
            }, operation: HostOperation.Presentation);
            return Task.CompletedTask;
        });
    }
}
