using Kora.Application.Diagnostics;
using Kora.Core.Auditing;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    private sealed record PendingPreference(Func<Task> Apply, Func<bool> Eligible, Func<HostActivity> Begin);

    private readonly Dictionary<string, PendingPreference> pendingPreferences = new(StringComparer.Ordinal);
    private bool drainingPreferences;
    private bool preferenceWriteActive;
    private bool modelPreferenceWriteActive;
    private bool modelPreferencesUnavailable;
    private Task preferencePersistence = Task.CompletedTask;

    public Task PreferenceUpdate { get; private set; } = Task.CompletedTask;

    private bool AdmitModelPreferenceChange()
    {
        if (!modelPreferencesUnavailable) { return true; }
        ShowFailure("Model preferences are unavailable.",
            "A preference write or its required audit was not confirmed. Inspect saved state and audit receipts before restarting; no automatic retry or rollback is claimed.");
        return false;
    }

    private Func<bool> CapturePreferenceEligibility(SecurityAuditInitiator initiator)
    {
        var privacy = privacyObservation.Current.TopologyRevision;
        var owner = auditImpactOwnerRevision();
        var call = CallPolicyRevision;
        var recovery = Interlocked.Read(ref voiceRecoveryRevision);
        var origin = OriginalOrigin(initiator);
        return () => !disposed && IsHostInputEligible && callOwnershipEligible()
            && privacyObservation.Current.TopologyRevision == privacy && auditImpactOwnerRevision() == owner
            && CallPolicyRevision == call && (origin != RequestOrigin.ActivatedVoice
                || Interlocked.Read(ref voiceRecoveryRevision) == recovery);
    }

    private void QueuePreference(string propertyName, Func<Task> apply)
    {
        if (disposed || !IsHostInputEligible)
        {
            if (!disposed)
            {
                ShowFailure("Preference change unavailable.", "The active unlocked owning host is required. Initiate a fresh change.");
                OnPropertyChanged(propertyName);
            }
            return;
        }
        pendingPreferences[propertyName] = new(apply, CapturePreferenceEligibility(SecurityAuditInitiator.LocalUser),
            HostActivity.CaptureContinuation(HostActivityLayer.Application, HostOperation.Storage, RequestOrigin.LocalUi));
        if (!drainingPreferences) { PreferenceUpdate = DrainPreferencesAsync(); }
    }

    private async Task DrainPreferencesAsync()
    {
        drainingPreferences = true;
        try
        {
            while (pendingPreferences.Count > 0)
            {
                var next = pendingPreferences.First();
                pendingPreferences.Remove(next.Key);
                if (disposed || lifecycleAdmissionClosed)
                {
                    pendingPreferences.Clear();
                    return;
                }
                try
                {
                    using var activity = next.Value.Begin();
                    if (!next.Value.Eligible())
                    {
                        throw new InvalidOperationException("The queued preference lost its original host admission. Initiate a fresh change.");
                    }
                    await next.Value.Apply();
                    activity.Complete(HostOperationOutcome.Completed);
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    if (disposed) { return; }
                    try { ApplicationLog.Error(logger, exception, "Applying a native preference change"); }
                    finally { ShowFailure("Preference change not confirmed.", exception.Message); }
                }
                finally { if (!disposed) { OnPropertyChanged(next.Key); } }
            }
        }
        finally { drainingPreferences = false; }
    }

    private async Task<bool> SavePreferenceAsync(Action persist, string actionId,
        SecurityAuditInitiator initiator, string settingName, Func<bool>? additionalEligibility = null)
    {
        if (preferenceWriteActive)
        {
            ShowFailure("Preference change busy.", "Wait for the current preference result and explicitly retry this change.");
            return false;
        }
        var eligible = CapturePreferenceEligibility(initiator);
        var origin = OriginalOrigin(initiator);
        preferenceWriteActive = true;
        var isModelPreference = actionId is ModelApprovalPreferenceAction or ModelExecutionPreferenceAction;
        modelPreferenceWriteActive = isModelPreference;
        try
        {
            var work = preferenceWriter.WriteAsync(persist, actionId, initiator, origin,
                () => eligible() && (additionalEligibility?.Invoke() ?? true),
                isModelPreference ? "preference-write-failed" : null);
            preferencePersistence = work;
            await work;
            if (!eligible() || additionalEligibility?.Invoke() == false)
            {
                throw new InvalidOperationException("The preference may have committed, but its original admission changed before confirmation. Inspect saved state before a fresh change.");
            }
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException
            or InvalidOperationException or AggregateException)
        {
            if (isModelPreference)
            {
                modelPreferencesUnavailable = true;
            }
            if (!disposed && !lifecycleAdmissionClosed)
            {
                try { ApplicationLog.Error(logger, exception, "Saving an audited preference"); }
                finally
                {
                    var title = actionId switch
                    {
                        ModelApprovalPreferenceAction => "Model approval settings could not be saved.",
                        ModelExecutionPreferenceAction => "Model settings could not be saved.",
                        _ => $"The {settingName} could not be saved.",
                    };
                    ShowFailure(title, exception.Message);
                }
            }
            return false;
        }
        finally
        {
            preferencePersistence = Task.CompletedTask;
            preferenceWriteActive = false;
            modelPreferenceWriteActive = false;
            if (!disposed && isModelPreference)
            {
                OnPropertyChanged(nameof(LocalModelsEnabled));
                OnPropertyChanged(nameof(HostedModelsEnabled));
                OnPropertyChanged(nameof(RequireAssistantNameForVoiceApproval));
                OnPropertyChanged(nameof(AlwaysAllowedModelActions));
                OnPropertyChanged(nameof(SessionAllowedModelActions));
            }
        }
    }
}
