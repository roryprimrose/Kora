using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Kora.Application.Hosting;
using Kora.Application.Network.ExternalBrowser;
using Kora.Application.ViewModels;
using Kora.Core.Hosting;
using Kora.Core.Network.ExternalBrowser;

namespace Kora;

internal sealed class ExternalBrowserReviewWindow : Window
{
    private readonly MainViewModel model;
    private readonly ExternalBrowserReview review;
    private readonly DispatcherTimer timer;
    private readonly Button confirm = new() { Content = "Open this exact destination once" };
    private readonly Button cancel = new() { Content = "Cancel" };
    private readonly TextBox address;
    private readonly TextBlock disclosure = new() { Text = ExternalBrowserReview.Disclosure, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private bool launching;

    internal ExternalBrowserReviewWindow(MainViewModel model, ExternalBrowserReview review)
    {
        this.model = model;
        this.review = review;
        Title = "Review external browser opening";
        Width = 620;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        address = new TextBox { Text = review.Destination.ExactAddress, IsReadOnly = true, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        AutomationProperties.SetName(address, "Exact external HTTPS destination");
        Content = new StackPanel
        {
            Margin = new Thickness(20), Spacing = 12,
            Children = { new TextBlock { Text = "External website - separate browser privacy review" }, address, disclosure, confirm, cancel },
        };
        confirm.Click += OnConfirm;
        cancel.Click += (_, _) => Close();
        timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        timer.Tick += OnAdmissionTick;
        model.PrivacyClosureRequested += OnPrivacyClosure;
        model.WebResultDetailsChanged += OnSourceChanged;
        Opened += (_, _) => timer.Start();
        Closed += (_, _) =>
        {
            timer.Stop();
            timer.Tick -= OnAdmissionTick;
            confirm.Click -= OnConfirm;
            model.PrivacyClosureRequested -= OnPrivacyClosure;
            model.WebResultDetailsChanged -= OnSourceChanged;
            model.CancelExternalBrowserReview();
            address.Text = string.Empty;
            disclosure.Text = string.Empty;
        };
    }

    private void OnPrivacyClosure(object? sender, EventArgs args) => Close();

    private void OnSourceChanged(object? sender, EventArgs args)
    {
        CheckAdmission();
    }

    private void OnAdmissionTick(object? sender, EventArgs args) => CheckAdmission();

    private void CheckAdmission()
    {
        try
        {
            if (!launching && !model.IsExternalBrowserReviewCurrent(review)) { Close(); }
            else if (!model.CanRevealPrivatePresentation) { Close(); }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            model.ReportWebResultDetailStatus("External-browser review retired: host, source or network readiness could not be verified.");
            Close();
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD100",
        Justification = "Native Click boundary explicitly reports an unknown launch and never retries.")]
    private async void OnConfirm(object? sender, RoutedEventArgs args)
    {
        if (launching) { return; }
        launching = true;
        confirm.IsEnabled = false;
        cancel.IsEnabled = false;
        try
        {
            var outcome = ExternalBrowserLaunchOutcome.Unknown;
            await HostRequestRunner.RunAsync(RequestOrigin.LocalUi, async () =>
                outcome = await model.ConfirmExternalBrowserAsync(review.Id));
            model.ReportWebResultDetailStatus(outcome switch
            {
                ExternalBrowserLaunchOutcome.Accepted => "Windows accepted the external-browser request. Page loading is not verified; Kora cannot stop or close its lifecycle.",
                ExternalBrowserLaunchOutcome.Failed => "Windows declined the external-browser request. No page load was verified.",
                ExternalBrowserLaunchOutcome.Denied => "External-browser opening denied: review, source, privacy or network admission changed.",
                ExternalBrowserLaunchOutcome.Cancelled => "External-browser opening cancelled before launch.",
                _ => "External-browser launch outcome unknown. It may have opened; Kora cannot stop or close it. No automatic retry.",
            });
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            model.ReportWebResultDetailStatus("External-browser launch outcome unknown: host confirmation or audit failed. It may have opened; no automatic retry.");
        }
        finally { Close(); }
    }
}
