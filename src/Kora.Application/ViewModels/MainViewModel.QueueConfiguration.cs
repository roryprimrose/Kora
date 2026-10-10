using System.Globalization;
using Kora.Application.Configuration;
using Kora.Application.Diagnostics;
using Kora.Application.Infrastructure;
using Kora.Core;
using Kora.Core.Auditing;
using Kora.Core.Configuration;
using Kora.Core.Hosting;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    private readonly SessionQueueConfigurationService? queueConfiguration;
    private Func<bool> queueNativeLifetime = static () => false;
    private Func<bool>? queueNativeAdmission;
    private long queueNativeRevision;
    private long queueNativeCallRevision;
    private int selectedQueuePending = SessionQueueLimits.MaximumPendingPerSession;
    private int selectedQueueSlots = SessionQueueLimits.DefaultExecutionSlots;
    private int selectedQueueLifetimeMinutes = SessionQueueLimits.DefaultPendingLifetimeMinutes;
    private int selectedQueueActiveBudgetMinutes = SessionQueueLimits.DefaultActiveBudgetMinutes;
    private bool queueConfigurationActive;

    public void BindQueueConfigurationNativeLifetime(Func<bool> lifetime)
    {
        queueNativeLifetime = lifetime;
        queueNativeAdmission = null;
        OnPropertyChanged(nameof(CanChangeQueueConfigurationNative));
    }

    public IReadOnlyList<int> QueuePendingChoices { get; } = Enumerable.Range(1, SessionQueueLimits.MaximumPendingPerSession).ToArray();
    public IReadOnlyList<int> QueueSlotChoices { get; } = Enumerable.Range(1, SessionQueueLimits.MaximumExecutionSlots).ToArray();
    public IReadOnlyList<int> QueueLifetimeMinuteChoices { get; } = Enumerable.Range(1, SessionQueueLimits.MaximumPendingLifetimeMinutes).ToArray();
    public IReadOnlyList<int> QueueActiveBudgetMinuteChoices { get; } = Enumerable.Range(1, SessionQueueLimits.MaximumActiveBudgetMinutes).ToArray();
    public int SelectedQueuePending
    {
        get => selectedQueuePending;
        set => SetProperty(ref selectedQueuePending, new SessionQueueLimits(value).PendingPerSession);
    }
    public int SelectedQueueSlots
    {
        get => selectedQueueSlots;
        set => SetProperty(ref selectedQueueSlots, new SessionQueueLimits(executionSlots: value).ExecutionSlots);
    }
    public int SelectedQueueLifetimeMinutes
    {
        get => selectedQueueLifetimeMinutes;
        set => SetProperty(ref selectedQueueLifetimeMinutes, new SessionQueueLimits(pendingLifetimeMinutes: value).PendingLifetimeMinutes);
    }
    public AsyncCommand RefreshQueueConfigurationCommand { get; }
    public AsyncCommand SaveQueuePendingCommand { get; }
    public AsyncCommand ResetQueuePendingCommand { get; }
    public AsyncCommand SaveQueueSlotsCommand { get; }
    public AsyncCommand ResetQueueSlotsCommand { get; }
    public AsyncCommand SaveQueueLifetimeCommand { get; }
    public AsyncCommand ResetQueueLifetimeCommand { get; }
    public AsyncCommand SaveQueueActiveBudgetCommand { get; }
    public AsyncCommand ResetQueueActiveBudgetCommand { get; }
    public int SelectedQueueActiveBudgetMinutes
    {
        get => selectedQueueActiveBudgetMinutes;
        set => SetProperty(ref selectedQueueActiveBudgetMinutes, new SessionQueueLimits(activeBudgetMinutes: value).ActiveBudgetMinutes);
    }
    public bool CanChangeQueueConfiguration => queueConfiguration is not null && IsCallMutationHostEligible
        && !queueConfigurationActive && !IsResponseInteractionPending;
    public bool CanChangeQueueConfigurationNative => CanChangeQueueConfiguration && queueNativeLifetime();
    public string QueueConfigurationStatus => queueConfiguration is null
        ? "Fixed local-version queue configuration unavailable; no change."
        : SessionQueueConfigurationState.Serialize(queueConfiguration.Get(), CallPolicyRevision, "observed");

    private void OnQueueConfigurationChanged(object? sender, EventArgs args) => uiDispatcher.Post(SynchronizeQueueConfiguration);
    private void SynchronizeQueueConfiguration()
    {
        if (disposed) { return; }
        OnPropertyChanged(nameof(QueueConfigurationStatus));
        OnPropertyChanged(nameof(CanChangeQueueConfiguration));
        OnPropertyChanged(nameof(CanChangeQueueConfigurationNative));
    }

    internal async Task ExecuteQueueConfigurationCommandAsync(SessionQueueConfigurationCommand command,
        SecurityAuditInitiator initiator, CancellationToken token = default)
    {
        var native = initiator == SecurityAuditInitiator.LocalUser;
        var nativeLifetime = queueNativeLifetime;
        if (!CanChangeQueueConfiguration || native && !nativeLifetime())
        {
            if (!disposed) { Transcript = "Queue settings require the owning unlocked host and no pending exact question/approval."; }
            return;
        }
        if (command.Operation == AppearanceCommandOperation.Clarify)
        {
            ShowFailure("Clarify fixed queue settings.", command.Error!);
            return;
        }
        var origin = OriginalOrigin(initiator);
        var hostEligible = CaptureConfigurationControlEligibility(origin);
        bool Eligible() => hostEligible() && (!native
            || ReferenceEquals(nativeLifetime, queueNativeLifetime) && nativeLifetime());
        var mutation = command.Operation is AppearanceCommandOperation.Set or AppearanceCommandOperation.Reset;
        queueConfigurationActive = true;
        SynchronizeQueueConfiguration();
        try
        {
            Func<bool> admissionEligible = Eligible;
            if (native && mutation)
            {
                admissionEligible = queueNativeAdmission ?? throw new InvalidOperationException("Refresh the native queue settings before choosing Save or Reset.");
                if (!admissionEligible() || !Eligible()) { throw new InvalidOperationException("Native queue draft expired; refresh and choose again."); }
            }
            else
            {
                await queueConfiguration!.RefreshAsync(origin, admissionEligible, token);
                if (native)
                {
                    queueNativeAdmission = admissionEligible;
                    queueNativeRevision = queueConfiguration.Get().Revision;
                    queueNativeCallRevision = CallPolicyRevision;
                    SelectedQueuePending = queueConfiguration.Get().Effective!.PendingPerSession;
                    SelectedQueueSlots = queueConfiguration.Get().Effective!.ExecutionSlots;
                    SelectedQueueLifetimeMinutes = queueConfiguration.Get().Effective!.PendingLifetimeMinutes;
                    SelectedQueueActiveBudgetMinutes = queueConfiguration.Get().Effective!.ActiveBudgetMinutes;
                }
            }
            var saved = false;
            if (mutation)
            {
                var candidate = queueConfiguration!.Propose(command.Option
                    ?? throw new InvalidOperationException("An exact supported queue option is required."),
                    command.Operation == AppearanceCommandOperation.Reset ? null
                        : command.Value ?? throw new InvalidOperationException("An exact integer queue value is required."),
                    native ? queueNativeRevision : queueConfiguration.Get().Revision,
                    native ? queueNativeCallRevision : CallPolicyRevision);
                saved = await queueConfiguration.ApplyAsync(candidate, origin, initiator, communicationPolicy, admissionEligible, token);
                if (native) { queueNativeAdmission = null; }
            }
            if (!Eligible() || !admissionEligible()) { return; }
            var result = SessionQueueConfigurationState.Serialize(queueConfiguration!.Get(), CallPolicyRevision,
                mutation ? saved ? "saved" : "denied" : "observed");
            if (IsSpeaking) { Transcript = result; }
            else { PresentResponse(AssistantState.Information, "Fixed local-version queue settings.", result, refreshOutput: false); }
        }
        catch (ArgumentOutOfRangeException exception)
        {
            PresentQueueConfigurationFailure("Choose queue pending capacity 1-10, fixed read-only slots 1-2, future pending lifetime 1-120 or future active budget 1-60 minutes.", exception.Message);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException
            or InvalidOperationException or OperationCanceledException or TimeoutException)
        {
            queueNativeAdmission = null;
            queueConfiguration!.HoldUnavailable();
            ApplicationLog.Error(logger, exception, "Configuring fixed local-version queue");
            PresentQueueConfigurationFailure("Queue settings not confirmed.", exception.Message);
        }
        finally { queueConfigurationActive = false; SynchronizeQueueConfiguration(); }
    }

    private void PresentQueueConfigurationFailure(string title, string error)
    {
        if (disposed) { return; }
        if (IsResponseInteractionPending || IsSpeaking) { Transcript = title + " " + error; }
        else { ShowFailure(title, error); }
    }
}
