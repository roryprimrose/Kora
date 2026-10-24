using Kora.Application.Configuration;
using Kora.Application.Diagnostics;
using Kora.Application.Infrastructure;
using Kora.Core.Configuration;
using Kora.Core.Hosting;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    private readonly SessionRetentionConfigurationService? sessionRetentionConfiguration;
    private int selectedSessionArchiveDays = 1;
    private int selectedSessionDeleteDays = 30;
    private Func<bool> sessionRetentionNativeLifetime = static () => false;
    private string sessionRetentionStatus = "Session retention unavailable.";

    public IReadOnlyList<int> SessionRetentionChoices { get; } = Enumerable.Range(1, SessionRetentionSettings.MaximumDays).ToArray();
    public int? SavedSessionArchiveDays => sessionRetentionConfiguration?.Available == true
        ? sessionRetentionConfiguration.Settings.ArchiveDays : null;
    public int? SavedSessionDeleteDays => sessionRetentionConfiguration?.Available == true
        ? sessionRetentionConfiguration.Settings.DeleteDays : null;
    public bool IsSessionRetentionDraftChanged => SavedSessionArchiveDays is { } archive && SavedSessionDeleteDays is { } delete
        && (SelectedSessionArchiveDays != archive || SelectedSessionDeleteDays != delete);
    public int SelectedSessionArchiveDays
    {
        get => selectedSessionArchiveDays;
        set { if (SetProperty(ref selectedSessionArchiveDays, value)) { OnPropertyChanged(nameof(IsSessionRetentionDraftChanged)); } }
    }
    public int SelectedSessionDeleteDays
    {
        get => selectedSessionDeleteDays;
        set { if (SetProperty(ref selectedSessionDeleteDays, value)) { OnPropertyChanged(nameof(IsSessionRetentionDraftChanged)); } }
    }
    public string SessionRetentionStatus { get => sessionRetentionStatus; private set => SetProperty(ref sessionRetentionStatus, value); }
    public bool CanChangeSessionRetentionNative => sessionRetentionConfiguration is not null
        && CanChangeDiagnosticRetentionNative && sessionRetentionNativeLifetime();
    public AsyncCommand RefreshSessionRetentionCommand { get; }
    public AsyncCommand SaveSessionRetentionCommand { get; }
    public AsyncCommand ResetSessionRetentionCommand { get; }

    public void BindSessionRetentionNativeLifetime(Func<bool> lifetime)
    {
        sessionRetentionPlanner?.Retire("Native settings lifetime changed; preview retired.");
        sessionRetentionNativeLifetime = lifetime;
        OnPropertyChanged(nameof(CanChangeSessionRetentionNative));
        NotifySessionRetentionPlan();
    }

    private async Task ConfigureSessionRetentionAsync(bool save, bool reset)
    {
        sessionRetentionPlanner?.Retire("Future-policy control or refresh requested; preview retired.");
        if (!CanChangeSessionRetentionNative)
        {
            SessionRetentionStatus = "Open Settings in the active, unlocked Kora window to change these limits.";
            return;
        }
        var lifetime = sessionRetentionNativeLifetime;
        var hostEligible = CaptureConfigurationControlEligibility(RequestOrigin.LocalUi);
        bool Eligible() => hostEligible() && ReferenceEquals(lifetime, sessionRetentionNativeLifetime) && lifetime();
        try
        {
            sessionRetentionConfiguration!.Observe();
            if (save)
            {
                var settings = reset ? (SessionRetentionSettings?)null : new(SelectedSessionArchiveDays, SelectedSessionDeleteDays);
                if (!await sessionRetentionConfiguration.SaveAsync(settings, communicationPolicy, CallPolicyRevision,
                    Eligible, CancellationToken.None))
                {
                    SessionRetentionStatus = "The change was not allowed because access changed. No settings were changed.";
                    return;
                }
            }
            var current = sessionRetentionConfiguration.Settings;
            SelectedSessionArchiveDays = current.ArchiveDays;
            SelectedSessionDeleteDays = current.DeleteDays;
            SessionRetentionStatus = $"Archive after {current.ArchiveDays} idle days; delete after {current.DeleteDays} idle days. "
                + "Applies to new sessions or after activity in an existing session. Existing dates are not changed immediately.";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException
            or InvalidOperationException or ArgumentOutOfRangeException)
        {
            SessionRetentionStatus = "Session retention unavailable: " + exception.Message;
            ApplicationLog.Error(logger, exception, "Configuring future-only session retention");
            ShowFailure("Session retention unavailable.", exception.Message);
        }
        finally
        {
            OnPropertyChanged(nameof(SavedSessionArchiveDays));
            OnPropertyChanged(nameof(SavedSessionDeleteDays));
            OnPropertyChanged(nameof(IsSessionRetentionDraftChanged));
            NotifySessionRetentionPlan();
        }
    }
}
