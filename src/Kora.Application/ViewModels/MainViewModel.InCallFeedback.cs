using Kora.Application.Configuration;
using Kora.Application.Diagnostics;
using Kora.Application.Infrastructure;
using Kora.Core;
using Kora.Core.Auditing;
using Kora.Core.Communication;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    private readonly InCallFeedbackConfigurationService? inCallFeedbackConfiguration;
    private InCallFeedbackChoice? selectedInCallFeedbackChoice;
    private bool inCallFeedbackControlActive;
    private Func<bool> inCallFeedbackNativeLifetime = static () => false;

    public void BindInCallFeedbackNativeLifetime(Func<bool> lifetime)
    {
        inCallFeedbackNativeLifetime = lifetime;
        OnPropertyChanged(nameof(CanInspectInCallFeedbackNative));
        OnPropertyChanged(nameof(CanChangeInCallFeedbackNative));
    }

    public AsyncCommand RefreshInCallFeedbackCommand { get; }
    public AsyncCommand SaveInCallFeedbackCommand { get; }
    public AsyncCommand ResetInCallFeedbackCommand { get; }
    public IReadOnlyList<InCallFeedbackChoice> InCallFeedbackChoices => inCallFeedbackConfiguration?.Choices ?? [];
    public InCallFeedbackChoice? SelectedInCallFeedbackChoice
    {
        get => selectedInCallFeedbackChoice;
        set => SetProperty(ref selectedInCallFeedbackChoice, value);
    }
    public bool CanInspectInCallFeedback => inCallFeedbackConfiguration is not null && IsCallMutationHostEligible
        && !inCallFeedbackControlActive && !IsResponseInteractionPending;
    public bool CanInspectInCallFeedbackNative => CanInspectInCallFeedback && inCallFeedbackNativeLifetime();
    public bool CanChangeInCallFeedbackNative => CanInspectInCallFeedbackNative && inCallFeedbackConfiguration!.Get().Available;
    public string InCallFeedbackStatus => inCallFeedbackConfiguration is null
        ? "Call feedback admission is unavailable; the UI default and independent hard speech/privacy gates remain."
        : InCallFeedbackCommandResult.Serialize(DescribeInCallFeedback());

    private InCallFeedbackMode DesiredInCallFeedback => inCallFeedbackConfiguration?.Get().Desired ?? InCallFeedbackRules.Default;
    public bool IsInCallFeedbackOverrideApplied => communicationPolicy.Current.EffectiveState is CallState.Active or CallState.Suspected
        && DesiredInCallFeedback != InCallFeedbackMode.Inherit
        && (inCallFeedbackConfiguration is null || inCallFeedbackConfiguration.Get().Available);

    private InCallFeedbackCommandResult DescribeInCallFeedback(string outcome = "observed") =>
        inCallFeedbackConfiguration!.Get(CallPolicyRevision, outcome) with
        {
            CallState = communicationPolicy.Current.EffectiveState,
            Applied = IsInCallFeedbackOverrideApplied,
            Effective = inCallFeedbackConfiguration.Get().Available ? EffectiveResponseMode : null,
            SpeechEligible = IsSpeechResponseEnabled,
            MandatoryVisual = IsCallVisualOverrideActive || forceVisualResponse || !IsSpeechOutputAvailable,
            OutputPolicy = ResponseOutputStatus,
        };

    private void OnInCallFeedbackChanged(object? sender, EventArgs args)
    {
        if (IsSpeaking) { forceVisualResponse = true; }
        uiDispatcher.Post(SynchronizeInCallFeedback);
    }

    private void SynchronizeInCallFeedback()
    {
        if (disposed) { return; }
        SelectedInCallFeedbackChoice = InCallFeedbackChoices.FirstOrDefault(choice => choice.Mode == DesiredInCallFeedback);
        OnPropertyChanged(nameof(InCallFeedbackChoices));
        NotifyOutputPolicyChanged();
    }

    internal async Task ExecuteInCallFeedbackCommandAsync(InCallFeedbackCommand command, SecurityAuditInitiator initiator,
        InCallFeedbackChoice? nativeChoice = null, CancellationToken cancellationToken = default)
    {
        var nativeLifetime = inCallFeedbackNativeLifetime;
        if (!CanInspectInCallFeedback || initiator == SecurityAuditInitiator.LocalUser && !nativeLifetime())
        {
            if (!disposed) { Transcript = "Call feedback requires the owning unlocked host, admitted audio session, current native surface and no pending question/approval."; }
            return;
        }
        if (command.Operation == AppearanceCommandOperation.Clarify)
        {
            ShowFailure("Clarify the call feedback setting.", command.Error!);
            return;
        }
        var service = inCallFeedbackConfiguration!;
        var origin = OriginalOrigin(initiator);
        var hostEligible = CaptureAudioControlEligibility(origin);
        var nameRevision = assistantNameConfiguration.Get().Revision;
        var generation = voiceRecognition.Generation;
        bool Eligible() => hostEligible() && (initiator != SecurityAuditInitiator.LocalUser
            || ReferenceEquals(nativeLifetime, inCallFeedbackNativeLifetime) && nativeLifetime())
            && assistantNameConfiguration.Get().Revision == nameRevision
            && (origin != RequestOrigin.ActivatedVoice || voiceRecognition.Generation == generation);
        var callRevision = CallPolicyRevision;
        var preserveResponse = IsSpeaking;
        inCallFeedbackControlActive = true;
        NotifyOutputPolicyChanged();
        try
        {
            if (nativeChoice is null) { await service.RefreshAsync(origin, callRevision, Eligible, cancellationToken); }
            var saved = false;
            if (command.Operation is AppearanceCommandOperation.Set or AppearanceCommandOperation.Reset)
            {
                var reset = command.Operation == AppearanceCommandOperation.Reset;
                var mode = reset ? InCallFeedbackRules.Default : command.Value;
                var choice = nativeChoice ?? service.Choices.Single(item => item.Mode == mode);
                saved = await service.SelectAsync(choice, reset, callRevision, origin, initiator, communicationPolicy, Eligible, cancellationToken);
            }
            if (!Eligible()) { throw new InvalidOperationException("Call feedback admission changed before presentation."); }
            preserveResponse |= IsSpeaking;
            if (saved && IsSpeaking) { await textToSpeech.StopAsync(CancellationToken.None); }
            if (!Eligible()) { throw new InvalidOperationException("Call feedback admission changed during output retirement."); }
            SynchronizeInCallFeedback();
            var result = InCallFeedbackCommandResult.Serialize(DescribeInCallFeedback(
                command.Operation is AppearanceCommandOperation.Set or AppearanceCommandOperation.Reset ? saved ? "saved" : "denied" : "observed"));
            if (preserveResponse)
            {
                Transcript = result;
                PreserveSpokenResponseFailure("Call feedback result is in Settings/transcript; the complete interrupted response remains visual.");
            }
            else
            {
                PresentResponse(AssistantState.Information, "In-call feedback configuration.", result, refreshOutput: false);
                forceVisualResponse = true;
                NotifyOutputPolicyChanged();
                if (CanRevealPrivatePresentation) { WindowActionRequested?.Invoke(this, WindowAction.Show); }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException
            or InvalidOperationException or OperationCanceledException or TimeoutException or ArgumentOutOfRangeException)
        {
            service.HoldUnavailable();
            ApplicationLog.Error(logger, exception, "Inspecting or saving in-call feedback");
            if (!disposed)
            {
                if (preserveResponse || IsResponseInteractionPending)
                {
                    Transcript = "Call feedback preference not confirmed. " + exception.Message;
                    PreserveSpokenResponseFailure("Call feedback was not confirmed; the complete response remains visual.");
                }
                else { ShowFailure("Call feedback preference not confirmed.", exception.Message); }
            }
        }
        finally
        {
            inCallFeedbackControlActive = false;
            SynchronizeInCallFeedback();
        }
    }
}
