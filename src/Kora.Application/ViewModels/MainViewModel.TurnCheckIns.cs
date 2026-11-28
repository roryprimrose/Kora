using Kora.Application.Diagnostics;
using Kora.Application.Hosting;
using Kora.Core.Auditing;
using Kora.Core.Commands;
using Kora.Core.Conversation;
using Kora.Core.Hosting;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    /// <summary>The single running-turn check-in owner's exact capture, freshness and answer operations.</summary>
    public sealed record TurnCheckInPort(
        Func<IReadOnlyList<ConversationCheckInTarget>> Capture,
        Func<ConversationCheckInTarget, bool> IsCurrent,
        Func<ConversationCheckInTarget, ConversationCheckInChoice, Func<bool>, RequestOrigin, Task<ConversationExecutionStatus>> AnswerAsync)
    {
        /// <summary>Binds the canonical session conversation owner.</summary>
        public static TurnCheckInPort For(SessionConversationService conversations)
        {
            ArgumentNullException.ThrowIfNull(conversations);
            return new(conversations.CapturePendingCheckIns, conversations.IsCheckInCurrent,
                (target, choice, admitted, origin) => conversations.AnswerCheckInAsync(target, choice, admitted, CancellationToken.None, origin));
        }
    }

    private TurnCheckInPort? turnCheckInConversations;
    private long turnCheckInBinding;

    /// <summary>Binds the single owner of running-turn check-ins for exact typed or spoken replies.</summary>
    public void BindTurnCheckIns(TurnCheckInPort port)
    {
        ArgumentNullException.ThrowIfNull(port);
        turnCheckInConversations = port;
        Interlocked.Increment(ref turnCheckInBinding);
    }

    private async Task ExecuteTurnCheckInReplyAsync(TurnCheckInReply reply, string input, SecurityAuditInitiator initiator)
    {
        const string unavailable = "Check-in reply refused. Use one exact current check-in; the running turn continues unchanged.";
        var origin = OriginalOrigin(initiator);
        var nameRevision = assistantNameConfiguration.Get().Revision;
        var callRevision = CallPolicyRevision;
        var binding = Interlocked.Read(ref turnCheckInBinding);
        var conversations = turnCheckInConversations;
        // Spoken check-in replies follow the protected-call spoken-approval policy, like permission cards.
        bool Admitted() => !disposed && CanRevealPrivatePresentation && IsHostInputEligible
            && !IsResponseInteractionPending && composerDictation?.IsActive != true && promptInputVoice?.IsActive != true
            && questionVoiceReplies?.IsActive != true && activeCustomizationVoice is null
            && nameRevision == assistantNameConfiguration.Get().Revision && callRevision == CallPolicyRevision
            && binding == Interlocked.Read(ref turnCheckInBinding) && ReferenceEquals(conversations, turnCheckInConversations)
            && (origin != RequestOrigin.ActivatedVoice || IsVoiceEnabled && voiceOwnershipEligible()
                && (!IsProtectedCall || CallObservation.Settings.AllowSpokenApprovalsDuringCalls));
        if (reply.Choice is not { } choice || conversations is null || !isAssistantNameAvailable
            || initiator is not (SecurityAuditInitiator.TypedCommand or SecurityAuditInitiator.VoiceCommand)
            || origin is not (RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice)
            || origin == RequestOrigin.ActivatedVoice && !HasCurrentNameActivation(input)
            || !Admitted())
        { Transcript = unavailable; return; }

        // The owner answers each exact check-in at most once, so a concurrent reply resolves as stale there.
        try
        {
            var targets = conversations.Capture();
            if (targets.Count != 1) { Transcript = unavailable; return; }
            var target = targets[0];
            var readback = "Check-in for task " + target.Execution.Request.TaskId.Value.ToString("D") + ": " + (choice.Operation switch
            {
                ConversationCheckInOperation.KeepGoing => "keep going for another " + Minutes(target.CheckIn.Interval.Minutes) + ".",
                ConversationCheckInOperation.KeepGoingFor => "keep going for " + Minutes(choice.Minutes!.Value.Minutes) + ".",
                ConversationCheckInOperation.KeepRunning => "keep running until it finishes, without more check-ins.",
                _ => "stop this turn.",
            });
            Transcript = readback;
            if (origin == RequestOrigin.ActivatedVoice)
            {
                await SpeakExactQuestionAsync(readback, () => Admitted() && conversations.IsCurrent(target), CancellationToken.None);
            }
            if (!Admitted() || !conversations.IsCurrent(target)) { Transcript = unavailable; return; }
            await conversations.AnswerAsync(target, choice, Admitted, origin);
            Transcript = choice.Operation == ConversationCheckInOperation.Stop
                ? "Stop requested for the exact running turn. Owned resources are joined before its outcome is retained."
                : "Check-in answered. The running turn continues; no other work was changed.";
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException
            or UnauthorizedAccessException or OperationCanceledException or ArgumentException)
        {
            ApplicationLog.Error(logger, exception, "Answering the exact turn check-in");
            Transcript = unavailable;
        }
    }

    private static string Minutes(int value) =>
        value.ToString(System.Globalization.CultureInfo.InvariantCulture) + (value == 1 ? " minute" : " minutes");
}
