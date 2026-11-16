using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Kora.Application.Infrastructure;
using Kora.Application.Voice;
using Kora.Core.Hosting;
using Kora.Windows.Dependencies;
using Kora.Windows.Skills.RuntimeSetup;
using Microsoft.Extensions.DependencyInjection;

namespace Kora;

public sealed partial class SettingsView
{
    private void BindDevelopmentPreview()
    {
        this.FindControl<TextBlock>("DevelopmentPreviewStatus")!.IsVisible = DevelopmentPreviewBuild.Enabled;
        this.FindControl<Button>("DevelopmentPreviewSetup")!.IsVisible = DevelopmentPreviewBuild.Enabled;
    }

    private void OnDevelopmentPreviewSetup(object? sender, RoutedEventArgs args)
    {
        if (!DevelopmentPreviewBuild.Enabled) { throw new InvalidOperationException("This is not a development-preview build."); }
        var presentation = CapturePresentation();
        var admission = App.Services.GetRequiredService<DevelopmentPreviewAdmission>();
        var window = new Window { Title = "Development Preview - explicit opt-in", Width = 660, Height = 620,
            WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var local = new CheckBox { Content = "Local model: allow unqualified answers through the normal host", IsChecked = false };
        var localName = new TextBox { PlaceholderText = "Exact installed Ollama model name (no download or model start)", MaxLength = 200 };
        var claude = new CheckBox { Content = "Claude: enable native account setup and independent per-send review", IsChecked = false };
        var wake = new CheckBox { Content = "Wake: test Kora-name detection with verified prepared assets", IsChecked = false };
        var worker = new CheckBox { Content = "Bundled worker: test fixed actions after protected runtime preparation", IsChecked = false };
        var consent = new CheckBox { Content = new TextBlock
        {
            Text = "I accept incomplete model/acoustic/release qualification and possible account usage charges. "
                + "Permissions, per-send reviews, microphone consent and fixed-action confirmations remain required.",
            TextWrapping = TextWrapping.Wrap,
        }, IsChecked = false };
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var enable = new Button { Content = "Enable only the selected preview capabilities" };
        var revoke = new Button { Content = "Revoke preview admission" };
        var close = new Button { Content = "Close" };
        var pending = false;
        var lifetime = new CancellationTokenSource();
        var native = new NativeViewLifetime(window, window, () => !disposed && model.CanChangeProviderProfile);
        bool Live() => presentation() && !disposed && model.CanChangeProviderProfile && native.IsPresented && native.IsActive;
        window.Content = new ScrollViewer { Content = new StackPanel
        {
            Margin = new(22), Spacing = 12,
            Children =
            {
                new TextBlock { Text = "This is an explicitly reviewed development path, not a passing qualification attestation. "
                    + "Admission lasts at most one host hour; restart or privacy/authority loss requires new review. "
                    + "No listening, account authentication, model inference or fixed action starts from this review.",
                    TextWrapping = TextWrapping.Wrap },
                local, localName, claude, wake, worker, consent, status, enable, revoke, close,
            },
        } };
        enable.Command = new AsyncCommand(async () =>
        {
            native.Refresh();
            var source = native.Capture();
            bool ReviewLive() => source() && Live();
            if (!Live() || consent.IsChecked != true)
            { throw new InvalidOperationException("Current private native presentation and explicit preview consent are required."); }
            var selected = (local.IsChecked == true ? DevelopmentPreviewCapability.LocalModel : DevelopmentPreviewCapability.None)
                | (claude.IsChecked == true ? DevelopmentPreviewCapability.Claude : DevelopmentPreviewCapability.None)
                | (wake.IsChecked == true ? DevelopmentPreviewCapability.Wake : DevelopmentPreviewCapability.None)
                | (worker.IsChecked == true ? DevelopmentPreviewCapability.BundledWorker : DevelopmentPreviewCapability.None);
            var name = localName.Text ?? "";
            pending = true;
            enable.IsEnabled = revoke.IsEnabled = close.IsEnabled = false;
            local.IsEnabled = localName.IsEnabled = claude.IsEnabled = wake.IsEnabled = worker.IsEnabled = consent.IsEnabled = false;
            try
            {
                var host = App.Services.GetRequiredService<AudioControlAdmission>();
                var grant = model.CaptureHandoffPresentationEligibility();
                await host.RunObservedAsync(RequestOrigin.LocalUi, ReviewLive, async token =>
                {
                    if ((selected & DevelopmentPreviewCapability.BundledWorker) != DevelopmentPreviewCapability.None)
                    {
                        var state = await App.Services.GetRequiredService<WindowsBundledRuntimeSetup>().InspectAsync(token);
                        if (state.Preparation != BundledRuntimePreparation.Prepared)
                        { throw new InvalidOperationException("Prepare and verify the actual protected worker runtime first; no replacement is installed."); }
                    }
                    admission.Admit(selected, ReviewLive, () => model.CanRevealPrivatePresentation && grant());
                    try
                    {
                        if ((selected & DevelopmentPreviewCapability.LocalModel) != DevelopmentPreviewCapability.None)
                            await App.Services.GetRequiredService<DevelopmentOllamaAdmissionSource>().AdmitAsync(name, ReviewLive, token);
                        return true;
                    }
                    catch { admission.Revoke(); throw; }
                }, (_, _, result) => result, lifetime.Token);
                status.Text = "Selected preview capabilities admitted. Refresh service/model choices for local or hosted use; "
                    + "use the existing speech setup and explicit listening consent for wake. Fixed actions still require their own exact package/grant review.";
            }
            finally
            {
                pending = false;
                enable.IsEnabled = revoke.IsEnabled = close.IsEnabled = true;
                local.IsEnabled = localName.IsEnabled = claude.IsEnabled = wake.IsEnabled = worker.IsEnabled = consent.IsEnabled = true;
            }
        }, exception =>
        {
            admission.Revoke();
            status.Text = "Preview setup failed: " + exception.Message;
            model.ReportHostInteractionFailure("Development preview setup failed. No operation completion is claimed. Failure type: " + exception.GetType().Name);
        });
        revoke.Command = new AsyncCommand(async () =>
        {
            native.Refresh();
            var source = native.Capture();
            bool ReviewLive() => source() && Live();
            await App.Services.GetRequiredService<AudioControlAdmission>().RunAsync(RequestOrigin.LocalUi, ReviewLive, (_, _) =>
            {
                admission.RevokeFromNative(ReviewLive);
                return true;
            }, CancellationToken.None);
            status.Text = "Preview admission revoked. No external runtime was stopped.";
        }, exception =>
        {
            admission.Revoke();
            status.Text = "Preview authority closed; revocation audit could not complete. Failure type: " + exception.GetType().Name;
            model.ReportHostInteractionFailure(status.Text);
        });
        close.Click += (_, _) => window.Close();
        window.Closing += (_, eventArgs) => { if (pending) { eventArgs.Cancel = true; lifetime.Cancel(); } };
        window.Closed += (_, _) => { native.Dispose(); lifetime.Dispose(); };
        window.Show(owner);
    }
}
