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
    public int SelectedSessionArchiveDays { get => selectedSessionArchiveDays; set => SetProperty(ref selectedSessionArchiveDays, value); }
    public int SelectedSessionDeleteDays { get => selectedSessionDeleteDays; set => SetProperty(ref selectedSessionDeleteDays, value); }
    public string SessionRetentionStatus { get => sessionRetentionStatus; private set => SetProperty(ref sessionRetentionStatus, value); }
    public bool CanChangeSessionRetentionNative => sessionRetentionConfiguration is not null
        && CanChangeDiagnosticRetentionNative && sessionRetentionNativeLifetime();
    public AsyncCommand RefreshSessionRetentionCommand { get; }
    public AsyncCommand SaveSessionRetentionCommand { get; }
    public AsyncCommand ResetSessionRetentionCommand { get; }

    public void BindSessionRetentionNativeLifetime(Func<bool> lifetime)
    {
        sessionRetentionNativeLifetime = lifetime;
        OnPropertyChanged(nameof(CanChangeSessionRetentionNative));
    }

    private async Task ConfigureSessionRetentionAsync(bool save, bool reset)
    {
        if (!CanChangeSessionRetentionNative)
        {
            SessionRetentionStatus = "Session retention requires the owning unlocked native settings surface.";
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
                    SessionRetentionStatus = "Session-retention change denied by host admission; no policy change.";
                    return;
                }
            }
            var current = sessionRetentionConfiguration.Settings;
            SelectedSessionArchiveDays = current.ArchiveDays;
            SelectedSessionDeleteDays = current.DeleteDays;
            SessionRetentionStatus = $"Archive after {current.ArchiveDays} idle days; delete after {current.DeleteDays} idle days. "
                + "Changes apply only to new sessions or subsequent meaningful activity; existing due dates are unchanged. Apply-now unavailable.";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException
            or InvalidOperationException or ArgumentOutOfRangeException)
        {
            SessionRetentionStatus = "Session retention unavailable: " + exception.Message;
            ApplicationLog.Error(logger, exception, "Configuring future-only session retention");
            ShowFailure("Session retention unavailable.", exception.Message);
        }
    }
}
