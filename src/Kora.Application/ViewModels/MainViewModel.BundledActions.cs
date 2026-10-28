using Kora.Application.Hosting;
using Kora.Core.Auditing;
using Kora.Core.Skills;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    private Func<FixedSessionAction, SecurityAuditInitiator, Func<bool>, Task>? bundledAction;
    private Func<Task>? bundledActionShutdown;
    private Func<string>? bundledActionStatus;

    public void BindBundledActions(Func<FixedSessionAction, SecurityAuditInitiator, Func<bool>, Task>? invoke,
        Func<Task>? shutdown = null, Func<string>? status = null)
    {
        bundledAction = invoke;
        bundledActionShutdown = shutdown;
        bundledActionStatus = status;
    }

    private Task InvokeBundledAction(Func<FixedSessionAction, SecurityAuditInitiator, Func<bool>, Task> invoke,
        FixedSessionAction action, SecurityAuditInitiator initiator,
        long? observedCallRevision, Func<bool>? sourceCurrent)
    {
        var call = CallPolicyRevision;
        var privacy = privacyObservation.Current.TopologyRevision;
        bool Current() => !disposed && (sourceCurrent?.Invoke() ?? true) && IsCallMutationHostEligible
            && IsModelCallDispatchEligible(initiator, observedCallRevision)
            && CallPolicyRevision == call && privacyObservation.Current.TopologyRevision == privacy
            && ReferenceEquals(bundledAction, invoke);
        return invoke(action, initiator, Current);
    }

    public async Task<ApplicationLifecycleShutdown?> PrepareBundledResourcesAsync(FixedSessionAction action,
        Func<bool> current, Func<Func<bool>, Task<ApplicationLifecycleShutdown?>> preparePower)
    {
        if (!Enum.IsDefined(action)) { throw new ArgumentOutOfRangeException(nameof(action)); }
        if (!current() || voluntaryShutdownRunning)
        { throw new InvalidOperationException("Another voluntary lifecycle operation or retired source prevents execution."); }
        if (action == FixedSessionAction.Lock)
        {
            return new(current, async () =>
            {
                ClearClipboardPreview();
                ClearFilePreview();
                await StopAudioAsync();
                await JoinVolatileContextAsync();
                if (!current()) { throw new InvalidOperationException("Lock authority retired while private resources joined."); }
            }, static () => { });
        }
        voluntaryShutdownRunning = true;
        ApplicationLifecycleShutdown? submitted = null;
        var transferred = false;
        var joined = false;
        try
        {
            if (ownedSkillAuthoringShutdown is not null && !await ownedSkillAuthoringShutdown()) { return null; }
            var conversationCurrent = await PrepareConversationShutdown();
            if (conversationCurrent is null || !current()) { return null; }
            submitted = await preparePower(() => current() && conversationCurrent());
            if (submitted is null) { return null; }
            if (instructionEditorShutdown is not null && !await instructionEditorShutdown()) { return null; }
            if (customizationShutdown is not null && !await customizationShutdown()) { return null; }
            var accepted = submitted;
            bool Current() => current() && conversationCurrent() && accepted.IsCurrent
                && (!joined || !IsListening && !IsSpeaking && !IsBusy);
            if (!Current()) { throw new InvalidOperationException("Computer-power review authority changed."); }
            transferred = true;
            return new(Current, async () =>
            {
                await accepted.StopAndJoinAsync();
                await CloseVoluntaryOperationsAsync();
                await JoinVoluntaryPreparationAsync();
                RetireWebResultDetails();
                await StopWebPageRetrievalAsync();
                await StopWebSummary();
                ClearClipboardPreview();
                ClearFilePreview();
                HoldVoiceInput("Microphone closed · computer-power operation");
                textToSpeech.InvalidateOutput();
                await StopAudioAsync();
                await JoinVolatileContextAsync();
                joined = true;
                if (!Current()) { throw new InvalidOperationException("Computer-power resource release or original authority is unconfirmed."); }
            }, () =>
            {
                accepted.Dispose();
                voluntaryShutdownRunning = false;
            });
        }
        finally
        {
            if (!transferred)
            {
                submitted?.Dispose();
                voluntaryShutdownRunning = false;
            }
        }
    }
}