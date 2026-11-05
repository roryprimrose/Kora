using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Layout;
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
    private readonly TextBlock originalSource;
    private readonly Action<string>? reportStatus;
    private readonly TextBlock disclosure = new() { Text = ExternalBrowserReview.Disclosure, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private bool launching;

    internal ExternalBrowserReviewWindow(MainViewModel model, ExternalBrowserReview review, Action<string>? reportStatus = null)
    {
        this.model = model;
        this.review = review;
        this.reportStatus = reportStatus;
        Title = "Review external browser opening";
        Width = 620;
        Height = 620;
        MinWidth = 420;
        MinHeight = 500;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        address = new TextBox
        {
            Text = review.Destination.ExactAddress, IsReadOnly = true, AcceptsReturn = true,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
        };
        ScrollViewer.SetVerticalScrollBarVisibility(address, ScrollBarVisibility.Auto);
        ScrollViewer.SetHorizontalScrollBarVisibility(address, ScrollBarVisibility.Disabled);
        AutomationProperties.SetName(address, "Exact external HTTPS destination");
        if (review.FinalSource is not null)
        {
            address.ContextMenu = null;
            address.ContextFlyout = null;
            address.CopyingToClipboard += BlockFinalClipboard;
            address.CuttingToClipboard += BlockFinalClipboard;
            address.PastingFromClipboard += BlockFinalClipboard;
            address.AddHandler(ContextRequestedEvent, (_, args) => args.Handled = true, RoutingStrategies.Tunnel);
        }
        originalSource = new TextBlock
        {
            Text = review.FinalSource is { } final
                ? "Untrusted generated final answer: " + final.SourceTitle
                    + "\nOriginal selected source token (not retrieval provenance):\n" + final.SourceToken
                : "Original source: exact host-retrieved web snapshot; not a generated final answer.",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
        };
        AutomationProperties.SetName(originalSource, "Original exact source for this separate navigation review");
        var sourceReview = new ScrollViewer
        {
            MaxHeight = 100, Focusable = true, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = new StackPanel
            {
                Spacing = 8,
                Children = { new TextBlock { Text = "External website - separate browser privacy review",
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap }, originalSource },
            },
        };
        AutomationProperties.SetName(sourceReview, "Scrollable exact original source review");
        var privacyReview = new ScrollViewer
        {
            MaxHeight = 140, Focusable = true, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = disclosure,
        };
        AutomationProperties.SetName(privacyReview, "Scrollable external browser privacy disclosure");
        var caption = new FuncDataTemplate<string>((text, _) =>
            new TextBlock { Text = text, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        confirm.ContentTemplate = cancel.ContentTemplate = caption;
        confirm.HorizontalAlignment = cancel.HorizontalAlignment = HorizontalAlignment.Stretch;
        var actions = new StackPanel { Spacing = 8, Children = { confirm, cancel } };
        var layout = new Grid { Margin = new Thickness(16), RowSpacing = 10, RowDefinitions = new RowDefinitions("Auto,*,Auto,Auto") };
        layout.Children.Add(sourceReview);
        Grid.SetRow(address, 1); layout.Children.Add(address);
        Grid.SetRow(privacyReview, 2); layout.Children.Add(privacyReview);
        Grid.SetRow(actions, 3); layout.Children.Add(actions);
        Content = layout;
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
            model.CancelExternalBrowserReview(review.Id);
            address.Text = string.Empty;
            originalSource.Text = string.Empty;
            review.FinalSource?.Retire();
            disclosure.Text = string.Empty;
        };
    }

    private void OnPrivacyClosure(object? sender, EventArgs args) => Close();

    private void BlockFinalClipboard(object? sender, RoutedEventArgs args)
    {
        args.Handled = true;
        Report("Final-answer navigation review grants no clipboard authority; automatic copy, cut and paste are blocked.");
    }

    private void Report(string status)
    {
        model.ReportWebResultDetailStatus(status);
        reportStatus?.Invoke(status);
    }

    private void OnSourceChanged(object? sender, EventArgs args)
    {
        CheckAdmission();
    }

    private void OnAdmissionTick(object? sender, EventArgs args) => CheckAdmission();

    private void CheckAdmission()
    {
        try
        {
            if (!launching && !model.IsExternalBrowserReviewCurrent(review))
            {
                reportStatus?.Invoke("External-browser review retired: the exact source, privacy, expiry or network admission changed. Nothing was opened.");
                Close();
            }
            else if (!model.CanRevealPrivatePresentation) { Close(); }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Report("External-browser review retired: host, source or network readiness could not be verified.");
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
            Report(outcome switch
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
            Report("External-browser launch outcome unknown: host confirmation or audit failed. It may have opened; no automatic retry.");
        }
        finally { Close(); }
    }
}
