using Kora.Application.Configuration;
using Kora.Application.Diagnostics;
using Kora.Application.Infrastructure;
using Kora.Core;
using Kora.Core.Auditing;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    private readonly ProviderModeConfigurationService? providerModeConfiguration;
    private ProviderModeChoice? selectedProviderModeChoice;
    private bool providerModeControlActive;

    public AsyncCommand RefreshProviderModeCommand { get; }
    public AsyncCommand SaveProviderModeCommand { get; }
    public AsyncCommand ResetProviderModeCommand { get; }
    public IReadOnlyList<ProviderModeChoice> ProviderModeChoices => providerModeConfiguration?.Choices ?? [];
    public ProviderModeChoice? SelectedProviderModeChoice
    {
        get => selectedProviderModeChoice;
        set => SetProperty(ref selectedProviderModeChoice, value);
    }
    public bool CanChangeProviderMode => providerModeConfiguration is not null && !disposed
        && IsCallMutationHostEligible && !providerModeControlActive && !IsResponseInteractionPending;
    public string ProviderModeConfigurationStatus => providerModeConfiguration is null
        ? "Provider-mode control admission is unavailable; existing saved state is retained."
        : ProviderModeCommandResult.Serialize(providerModeConfiguration.Get(CallPolicyRevision));

    private void OnProviderModeConfigurationChanged(object? sender, EventArgs args) =>
        uiDispatcher.Post(SynchronizeProviderModeConfiguration);

    private void SynchronizeProviderModeConfiguration()
    {
        if (disposed) { return; }
        SelectedProviderModeChoice = ProviderModeChoices.FirstOrDefault(choice => choice.Mode == providerModeConfiguration!.Get().Desired);
        OnPropertyChanged(nameof(ProviderModeChoices));
        OnPropertyChanged(nameof(ProviderModeConfigurationStatus));
        OnPropertyChanged(nameof(CanChangeProviderMode));
    }

    private Task RunNativeProviderModeAsync(AppearanceCommandOperation operation) =>
        ExecuteProviderModeCommandAsync(new(operation), SecurityAuditInitiator.LocalUser,
            operation == AppearanceCommandOperation.Set
                ? SelectedProviderModeChoice ?? throw new InvalidOperationException("Inspect and choose one exact provider mode.")
                : null);

    internal async Task ExecuteProviderModeCommandAsync(ProviderModeCommand command, SecurityAuditInitiator initiator,
        ProviderModeChoice? nativeChoice = null, CancellationToken cancellationToken = default)
    {
        if (!CanChangeProviderMode)
        {
            if (!disposed) { Transcript = "Provider-mode control requires the current owning unlocked host, admitted control session and no pending question/approval."; }
            return;
        }
        if (command.Operation == AppearanceCommandOperation.Clarify)
        {
            ShowFailure("Clarify the provider setting.", command.Error!);
            return;
        }
        var service = providerModeConfiguration!;
        var origin = OriginalOrigin(initiator);
        if (initiator == SecurityAuditInitiator.VoiceCommand && HostActivity.Current is null) { origin = RequestOrigin.ActivatedVoice; }
        var remainsAdmitted = CaptureAudioControlEligibility(origin);
        bool Eligible() => !disposed && remainsAdmitted();
        var callRevision = CallPolicyRevision;
        providerModeControlActive = true;
        OnPropertyChanged(nameof(CanChangeProviderMode));
        try
        {
            if (nativeChoice is null) { await service.RefreshAsync(origin, Eligible, cancellationToken); }
            var saved = false;
            if (command.Operation is AppearanceCommandOperation.Set or AppearanceCommandOperation.Reset)
            {
                var mode = command.Operation == AppearanceCommandOperation.Reset ? ModelProviderModePreference.Default : command.Value;
                var choice = nativeChoice ?? service.Choices.Single(item => item.Mode == mode);
                saved = await service.SelectAsync(choice, callRevision, origin, initiator, communicationPolicy, Eligible, cancellationToken);
            }
            if (!Eligible()) { throw new InvalidOperationException("Provider-mode host admission changed before presentation."); }
            SynchronizeProviderModeConfiguration();
            var result = ProviderModeCommandResult.Serialize(service.Get(callRevision,
                command.Operation is AppearanceCommandOperation.Set or AppearanceCommandOperation.Reset ? saved ? "saved" : "denied" : "observed"));
            if (IsSpeaking)
            {
                Transcript = result;
                PreserveSpokenResponseFailure("Provider preference result is in Settings/transcript; the complete response remains visual.");
            }
            else
            {
                PresentResponse(AssistantState.Information, "Provider configuration.", result, refreshOutput: false);
                forceVisualResponse = true;
                NotifyOutputPolicyChanged();
                if (CanRevealPrivatePresentation) { WindowActionRequested?.Invoke(this, WindowAction.Show); }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException
            or InvalidOperationException or OperationCanceledException or TimeoutException or ArgumentOutOfRangeException)
        {
            service.HoldUnavailable();
            ApplicationLog.Error(logger, exception, "Inspecting or saving device-local provider mode");
            if (!disposed)
            {
                if (IsSpeaking || IsResponseInteractionPending)
                {
                    Transcript = "Provider preference not confirmed. " + exception.Message;
                    PreserveSpokenResponseFailure("Provider preference not confirmed; the complete response remains visual.");
                }
                else { ShowFailure("Provider preference not confirmed.", exception.Message); }
            }
        }
        finally
        {
            providerModeControlActive = false;
            SynchronizeProviderModeConfiguration();
        }
    }
}
