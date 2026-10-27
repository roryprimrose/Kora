using Avalonia.Controls;
using Kora.Application.Configuration.SettingInspection;
using Kora.Application.ViewModels;

namespace Kora.SettingInspection;

public sealed class SettingInspectionControl : UserControl
{
    private readonly Button open = new() { Content = "Inspect confirmed current settings with a local model", IsEnabled = false };
    private SettingInspectionWindow? window;

    public SettingInspectionControl() { Content = open; }

    internal void Bind(MainViewModel host, SettingInspectionWorkflow workflow, TimeProvider time,
        SettingsWindow owner, Func<Func<bool>> captureLifetime)
    {
        open.IsEnabled = true;
        open.Click += (_, _) =>
        {
            if (!host.CanRevealPrivatePresentation || window is not null) { return; }
            var presenter = new SettingInspectionPresenter(workflow, host.CaptureSettingInspectionAdmission, captureLifetime, time);
            window = new(presenter, exception => host.ReportHostInteractionFailure(
                "Setting inspection failed: " + exception.GetType().Name + ". No successful answer or retry is inferred."), host);
            window.Closed += (_, _) => window = null;
            window.Show(owner);
        };
        owner.Closed += (_, _) => window?.Close();
        host.PrivacyClosureRequested += OnPrivacyClosure;
        owner.Closed += (_, _) => host.PrivacyClosureRequested -= OnPrivacyClosure;
        void OnPrivacyClosure(object? sender, EventArgs args) => window?.Close();
    }
}
