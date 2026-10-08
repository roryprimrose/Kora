using Kora.Application.Configuration;
using Kora.Application.Infrastructure;
using Kora.Core;
using Kora.Core.Auditing;
using Kora.Core.Configuration;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Voice;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    private readonly WindowsSpeechRateConfigurationService? windowsSpeechRateConfiguration;
    private int selectedWindowsSpeechRate;
    private bool rateControlActive;
    private Func<bool> windowsSpeechRateNativeLifetime = static () => false;

    public void BindWindowsSpeechRateNativeLifetime(Func<bool> lifetime)
    {
        windowsSpeechRateNativeLifetime = lifetime;
        OnPropertyChanged(nameof(CanInspectWindowsSpeechRateNative));
        OnPropertyChanged(nameof(CanChangeWindowsSpeechRateNative));
    }

    public IReadOnlyList<int> WindowsSpeechRateChoices { get; } =
        Enumerable.Range(WindowsSpeechRate.Minimum, WindowsSpeechRate.Maximum - WindowsSpeechRate.Minimum + 1).ToArray();
    public int SelectedWindowsSpeechRate
    {
        get => selectedWindowsSpeechRate;
        set => SetProperty(ref selectedWindowsSpeechRate, new WindowsSpeechRate(value).Value);
    }
    public AsyncCommand RefreshWindowsSpeechRateCommand { get; }
    public AsyncCommand SaveWindowsSpeechRateCommand { get; }
    public AsyncCommand ResetWindowsSpeechRateCommand { get; }
    public bool CanInspectWindowsSpeechRate => windowsSpeechRateConfiguration is not null && IsCallMutationHostEligible
        && !rateControlActive && !IsResponseInteractionPending;
    public bool CanChangeWindowsSpeechRate => CanInspectWindowsSpeechRate && windowsSpeechRateConfiguration!.Get().Available;
    public bool CanInspectWindowsSpeechRateNative => CanInspectWindowsSpeechRate && windowsSpeechRateNativeLifetime();
    public bool CanChangeWindowsSpeechRateNative => CanChangeWindowsSpeechRate && windowsSpeechRateNativeLifetime();
    public string WindowsSpeechRateStatus => windowsSpeechRateConfiguration is null
        ? "Windows-native rate admission is unavailable; Kokoro synthesis is unchanged."
        : WindowsSpeechRateState.Serialize(windowsSpeechRateConfiguration.Get(), CallPolicyRevision, "observed");

    private bool IsWindowsSpeechRateOutputEligible => windowsSpeechRateConfiguration is null
        || windowsSpeechRateConfiguration.Get().AllowsProviderOutput(activeSpeechVoice!.ProviderId);

    private void OnWindowsSpeechRateChanged(object? sender, EventArgs args)
    {
        if (IsSpeaking) { forceVisualResponse = true; }
        uiDispatcher.Post(SynchronizeWindowsSpeechRate);
    }

    private void SynchronizeWindowsSpeechRate()
    {
        if (disposed) { return; }
        if (windowsSpeechRateConfiguration!.Get().Desired is { } rate) { SelectedWindowsSpeechRate = rate.Value; }
        OnPropertyChanged(nameof(WindowsSpeechRateStatus));
        OnPropertyChanged(nameof(CanInspectWindowsSpeechRate));
        OnPropertyChanged(nameof(CanChangeWindowsSpeechRate));
        OnPropertyChanged(nameof(CanInspectWindowsSpeechRateNative));
        OnPropertyChanged(nameof(CanChangeWindowsSpeechRateNative));
        NotifyOutputPolicyChanged();
    }

    internal async Task ExecuteWindowsSpeechRateCommandAsync(WindowsSpeechRateCommand command, SecurityAuditInitiator initiator,
        CancellationToken cancellationToken = default)
    {
        var nativeLifetime = windowsSpeechRateNativeLifetime;
        if (!CanInspectWindowsSpeechRate || initiator == SecurityAuditInitiator.LocalUser && !nativeLifetime())
        {
            if (!disposed) { Transcript = "Rate control requires the owning unlocked host, admitted audio session and no pending question/approval."; }
            return;
        }
        if (command.Operation == AppearanceCommandOperation.Clarify)
        {
            ShowFailure("Clarify the Windows rate setting.", command.Error!);
            return;
        }
        var origin = OriginalOrigin(initiator);
        if (initiator == SecurityAuditInitiator.VoiceCommand && HostActivity.Current is null) { origin = RequestOrigin.ActivatedVoice; }
        var hostEligible = CaptureAudioControlEligibility(origin);
        var nameRevision = assistantNameConfiguration.Get().Revision;
        var generation = voiceRecognition.Generation;
        bool Eligible() => hostEligible() && (initiator != SecurityAuditInitiator.LocalUser
            || ReferenceEquals(nativeLifetime, windowsSpeechRateNativeLifetime) && nativeLifetime())
            && assistantNameConfiguration.Get().Revision == nameRevision
            && (origin != RequestOrigin.ActivatedVoice || voiceRecognition.Generation == generation);
        Func<bool> eligible = Eligible;
        var preserveResponse = IsSpeaking;
        rateControlActive = true;
        OnPropertyChanged(nameof(CanInspectWindowsSpeechRate));
        OnPropertyChanged(nameof(CanChangeWindowsSpeechRate));
        try
        {
            await windowsSpeechRateConfiguration!.RefreshAsync(origin, eligible, cancellationToken);
            var saved = false;
            if (command.Operation is AppearanceCommandOperation.Set or AppearanceCommandOperation.Reset)
            {
                var proposal = windowsSpeechRateConfiguration.Propose(command.Operation == AppearanceCommandOperation.Reset ? null
                    : command.Value ?? throw new InvalidOperationException("An exact Windows-native rate is required."),
                    windowsSpeechRateConfiguration.Get().Revision, CallPolicyRevision);
                saved = await windowsSpeechRateConfiguration.ApplyAsync(proposal, origin, initiator, communicationPolicy, eligible, cancellationToken);
            }
            if (!eligible()) { return; }
            if (saved && IsSpeaking) { await textToSpeech.StopAsync(CancellationToken.None); }
            if (!eligible()) { return; }
            var result = WindowsSpeechRateState.Serialize(windowsSpeechRateConfiguration.Get(), CallPolicyRevision,
                command.Operation is AppearanceCommandOperation.Set or AppearanceCommandOperation.Reset ? saved ? "saved" : "denied" : "observed");
            if (preserveResponse)
            {
                Transcript = result;
                PreserveSpokenResponseFailure("Windows rate result is in status; the complete interrupted response remains visual.");
            }
            else { PresentResponse(AssistantState.Information, "Windows-native speech rate.", result, refreshOutput: false); }
        }
        catch (ArgumentOutOfRangeException exception)
        {
            if (!disposed)
            {
                if (preserveResponse || IsResponseInteractionPending)
                {
                    Transcript = "Use one canonical Windows-native rate from -10 through 10. " + exception.Message;
                    PreserveSpokenResponseFailure("The invalid rate request changed no preference; the complete original response remains visual.");
                }
                else { ShowFailure("Choose a Windows-native integer rate from -10 through 10.", exception.Message); }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException
            or OperationCanceledException or TimeoutException)
        {
            windowsSpeechRateConfiguration!.HoldUnavailable();
            if (disposed) { return; }
            if (preserveResponse || IsResponseInteractionPending)
            {
                Transcript = "Windows rate preference not confirmed. " + exception.Message;
                PreserveSpokenResponseFailure("Windows rate preference was not confirmed; the complete interrupted response remains visual.");
            }
            else { ShowFailure("Windows rate preference not confirmed.", exception.Message); }
        }
        finally
        {
            rateControlActive = false;
            SynchronizeWindowsSpeechRate();
        }
    }
}
