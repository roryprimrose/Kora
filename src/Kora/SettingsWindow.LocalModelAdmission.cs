using Avalonia.Controls;
using Avalonia.Interactivity;

using Kora.Application.ViewModels;
using Kora.Application.Dependencies;
using Kora.Core.Hosting;

using Microsoft.Extensions.DependencyInjection;

namespace Kora;

public sealed partial class SettingsWindow
{
    private OllamaAdmissionReview? localAdmissionReview;
    private bool localAdmissionBusy;

    private async void OnReviewLocalAdmission(object? sender, RoutedEventArgs args) =>
        await RunLocalAdmission(async (workflow, eligible) =>
        {
            localAdmissionReview = await workflow.Review(RequestOrigin.LocalUi, eligible, CancellationToken.None);
        });

    private async void OnConfirmLocalAdmission(object? sender, RoutedEventArgs args) =>
        await RunLocalAdmission(async (workflow, eligible) =>
        {
            var review = localAdmissionReview ?? throw new InvalidOperationException("Review the exact local runtime before confirming.");
            localAdmissionReview = null;
            await workflow.Confirm(review, RequestOrigin.LocalUi, eligible, CancellationToken.None);
        });

    private async void OnRevokeLocalAdmission(object? sender, RoutedEventArgs args) =>
        await RunLocalAdmission(async (workflow, eligible) =>
        {
            localAdmissionReview = null;
            await workflow.Revoke(RequestOrigin.LocalUi, eligible, CancellationToken.None);
        });

    private async Task RunLocalAdmission(Func<OllamaAdmissionWorkflow, Func<bool>, Task> operation)
    {
        if (localAdmissionBusy) { return; }
        localAdmissionBusy = true;
        var status = this.FindControl<TextBlock>("LocalAdmissionStatus")!;
        try
        {
            var workflow = App.Services.GetRequiredService<OllamaAdmissionWorkflow>();
            bool Eligible() => DataContext is MainViewModel { CanChangeProviderProfile: true };
            if (!Eligible()) { throw new InvalidOperationException("Local model admission requires the current unlocked owning host."); }
            await operation(workflow, Eligible);
            status.Text = workflow.Status;
        }
        catch (Exception exception)
        {
            localAdmissionReview = null;
            status.Text = "Local admission failed or remains unavailable. Inspect diagnostics/audit, revoke any unconfirmed saved choice, then review again.";
            Kora.Application.Diagnostics.ApplicationLog.Error(
                App.Services.GetRequiredService<Microsoft.Extensions.Logging.ILogger<SettingsWindow>>(),
                exception, "Reviewing local model admission");
        }
        finally { localAdmissionBusy = false; }
    }
}