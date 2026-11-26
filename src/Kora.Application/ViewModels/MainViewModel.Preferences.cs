using Kora.Application.Diagnostics;
using Kora.Core.Auditing;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    public Kora.Application.Hosting.SessionWorkspaceService? SessionControlWorkspace => sessionCommands;
    public Kora.Core.Storage.SessionWorkspaceEntry? CurrentPermissionControlTarget => sessionCommands?.PermissionControlTarget;
    private Kora.Application.Configuration.SessionPermissionDefaultPreferences? sessionPermissionDefaults;
    private Kora.Core.Authorization.SessionPermission.SessionPermissionLevel newSessionPermission;
    public IReadOnlyList<string> NewSessionPermissionChoices { get; } = ["Review all", "Model review", "Approve all"];
    public int NewSessionPermission
    {
        get => (int)newSessionPermission;
        set
        {
            if (!Enum.IsDefined((Kora.Core.Authorization.SessionPermission.SessionPermissionLevel)value))
            { throw new ArgumentOutOfRangeException(nameof(value)); }
            var desired = (Kora.Core.Authorization.SessionPermission.SessionPermissionLevel)value;
            if (desired == newSessionPermission) { return; }
            QueuePreference(nameof(NewSessionPermission), async () =>
            {
                var preferences = sessionPermissionDefaults
                    ?? throw new InvalidOperationException("Default session permission preferences are unavailable.");
                if (await SavePreferenceAsync(() => preferences.Save(desired), "session.permission.default",
                    SecurityAuditInitiator.LocalUser, "new-session permission default"))
                { newSessionPermission = desired; }
            });
        }
    }

    public void BindSessionPermissionDefaults(Kora.Application.Configuration.SessionPermissionDefaultPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        if (sessionPermissionDefaults is not null)
        { throw new InvalidOperationException("Default session permission preferences are already bound."); }
        newSessionPermission = preferences.Load();
        sessionPermissionDefaults = preferences;
        OnPropertyChanged(nameof(NewSessionPermission));
    }

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
        SecurityAuditInitiator initiator, string settingName,
        Action<string, string>? reportFailure = null)
    {
        Action<string, string> notifyFailure = reportFailure ?? ShowFailure;
        if (preferenceWriteActive)
        {
            notifyFailure("Preference change busy.", "Wait for the current preference result and explicitly retry this change.");
            return false;
        }
        var eligible = CapturePreferenceEligibility(initiator);
        var origin = OriginalOrigin(initiator);
        preferenceWriteActive = true;
        var isModelPreference = actionId is ModelExecutionPreferenceAction;
        modelPreferenceWriteActive = isModelPreference;
        try
        {
            var work = preferenceWriter.WriteAsync(persist, actionId, initiator, origin, eligible,
                isModelPreference ? "preference-write-failed" : null);
            preferencePersistence = work;
            await work;
            if (!eligible())
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
                        ModelExecutionPreferenceAction => "Model settings could not be saved.",
                        _ => $"The {settingName} could not be saved.",
                    };
                    notifyFailure(title, exception.Message);
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



            }
        }
    }
}
