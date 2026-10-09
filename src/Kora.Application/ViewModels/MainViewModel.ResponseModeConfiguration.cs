using Kora.Application.Configuration;
using Kora.Application.Diagnostics;
using Kora.Application.Infrastructure;
using Kora.Core;
using Kora.Core.Auditing;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Voice;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    private readonly ResponseModeConfigurationService? responseModeConfiguration;
    private ResponseModeChoice? selectedResponseModeChoice;
    private bool responseModeControlActive;

    public AsyncCommand RefreshResponseModeCommand { get; }
    public AsyncCommand SaveResponseModeCommand { get; }
    public AsyncCommand ResetResponseModeCommand { get; }
    public IReadOnlyList<ResponseModeChoice> ResponseModeChoices => responseModeConfiguration?.Choices ?? [];
    public ResponseModeChoice? SelectedResponseModeChoice
    {
        get => selectedResponseModeChoice;
        set => SetProperty(ref selectedResponseModeChoice, value);
    }
    public bool CanChangeResponseMode => responseModeConfiguration is not null && !disposed
        && IsCallMutationHostEligible && !responseModeControlActive && !IsResponseInteractionPending;
    public string ResponseModeConfigurationStatus => responseModeConfiguration is null
        ? "Response-mode control admission is unavailable; existing saved state is retained."
        : ResponseModeCommandResult.Serialize(DescribeResponseMode());

    private ResponseModeCommandResult DescribeResponseMode(string outcome = "observed") =>
        responseModeConfiguration!.Get(CallPolicyRevision, outcome, QueueResponseMode, TaskResponseMode) with
        {
            SpeechEligible = IsSpeechResponseEnabled,
            MandatoryVisual = IsCallVisualOverrideActive || forceVisualResponse || !IsSpeechOutputAvailable,
            OutputPolicy = ResponseOutputStatus,
            Effective = responseModeConfiguration.Get().Available ? EffectiveResponseMode : null,
        };

    private void OnResponseModeConfigurationChanged(object? sender, EventArgs args)
    {
        if (IsSpeaking) { forceVisualResponse = true; }
        uiDispatcher.Post(SynchronizeResponseModeConfiguration);
    }

    private void SynchronizeResponseModeConfiguration()
    {
        if (disposed) { return; }
        var current = responseModeConfiguration!.Get();
        if (current.Available && current.Desired is { } mode)
        {
            suppressResponseModeSave = true;
            try { DefaultResponseMode = mode; }
            finally { suppressResponseModeSave = false; }
        }
        SelectedResponseModeChoice = ResponseModeChoices.FirstOrDefault(choice => choice.Mode == current.Desired);
        OnPropertyChanged(nameof(ResponseModeChoices));
        OnPropertyChanged(nameof(ResponseModeConfigurationStatus));
        OnPropertyChanged(nameof(CanChangeResponseMode));
        NotifyOutputPolicyChanged();
    }

    private async Task RunNativeResponseModeAsync(AppearanceCommandOperation operation)
    {
        var selected = operation == AppearanceCommandOperation.Set ? SelectedResponseModeChoice : null;
        await ExecuteResponseModeCommandAsync(new(operation), SecurityAuditInitiator.LocalUser,
            selected, CancellationToken.None);
    }

    internal async Task ExecuteResponseModeCommandAsync(ResponseModeCommand command, SecurityAuditInitiator initiator,
        ResponseModeChoice? nativeChoice = null, CancellationToken cancellationToken = default)
    {
        if (!CanChangeResponseMode)
        {
            if (!disposed) { Transcript = "Response-mode control requires the current owning unlocked host, admitted audio session and no pending question/approval."; }
            return;
        }
        if (command.Operation == AppearanceCommandOperation.Clarify)
        {
            ShowFailure("Clarify the response setting.", command.Error!);
            return;
        }
        var service = responseModeConfiguration!;
        var origin = OriginalOrigin(initiator);
        if (initiator == SecurityAuditInitiator.VoiceCommand && HostActivity.Current is null) { origin = RequestOrigin.ActivatedVoice; }
        var remainsAdmitted = CaptureAudioControlEligibility(origin);
        bool Eligible() => !disposed && remainsAdmitted();
        var callRevision = CallPolicyRevision;
        var preserveResponse = IsSpeaking;
        responseModeControlActive = true;
        OnPropertyChanged(nameof(CanChangeResponseMode));
        try
        {
            if (nativeChoice is null) { await service.RefreshAsync(origin, Eligible, cancellationToken); }
            var saved = false;
            if (command.Operation is AppearanceCommandOperation.Set or AppearanceCommandOperation.Reset)
            {
                var mode = command.Operation == AppearanceCommandOperation.Reset ? ResponseOutputMode.Hybrid : command.Value;
                var choice = nativeChoice ?? service.Choices.Single(item => item.Mode == mode);
                saved = await service.SelectAsync(choice, callRevision, origin, initiator, communicationPolicy, Eligible, cancellationToken);
            }
            if (!Eligible()) { throw new InvalidOperationException("Response-mode host admission changed before presentation."); }
            preserveResponse |= IsSpeaking;
            if (saved && IsSpeaking) { await textToSpeech.StopAsync(CancellationToken.None); }
            SynchronizeResponseModeConfiguration();
            var result = ResponseModeCommandResult.Serialize(DescribeResponseMode(
                command.Operation is AppearanceCommandOperation.Set or AppearanceCommandOperation.Reset ? saved ? "saved" : "denied" : "observed"));
            if (preserveResponse)
            {
                Transcript = result;
                PreserveSpokenResponseFailure("Response preference result is in Settings/transcript; the complete interrupted response remains visual.");
            }
            else
            {
                PresentResponse(AssistantState.Information, "Response configuration.", result, refreshOutput: false);
                forceVisualResponse = true;
                NotifyOutputPolicyChanged();
                if (CanRevealPrivatePresentation) { WindowActionRequested?.Invoke(this, WindowAction.Show); }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException
            or InvalidOperationException or OperationCanceledException or TimeoutException or ArgumentOutOfRangeException)
        {
            service.HoldUnavailable();
            ApplicationLog.Error(logger, exception, "Inspecting or saving device-default response mode");
            if (!disposed)
            {
                if (preserveResponse || IsResponseInteractionPending)
                {
                    Transcript = "Response preference not confirmed. " + exception.Message;
                    PreserveSpokenResponseFailure("Response preference not confirmed; the complete response remains visual.");
                }
                else { ShowFailure("Response preference not confirmed.", exception.Message); }
            }
        }
        finally
        {
            responseModeControlActive = false;
            SynchronizeResponseModeConfiguration();
        }
    }
}
