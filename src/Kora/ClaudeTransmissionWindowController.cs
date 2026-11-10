using System.Text;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Threading;
using Kora.Application.Dependencies;
using Kora.Application.ViewModels;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Microsoft.Extensions.Logging;

namespace Kora;

internal sealed partial class ClaudeTransmissionWindowController : IDisposable
{
    private readonly MainViewModel main;
    private readonly HostedTransmissionReviewService source;
    private readonly ILogger<ClaudeTransmissionWindowController> logger;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private Window? window;
    private PrivateReviewView? view;
    private Func<Window>? getWorkbench;
    internal PrivateReviewView? PresentedView => view;
    internal void BindWorkbench(Func<Window> owner) => getWorkbench = owner;
    private TextBox? content;
    private HostedTransmissionReview? displayed;
    private Func<bool>? eligible;
    private bool disposed;
    internal Window? PresentedWindow => window;

    internal ClaudeTransmissionWindowController(MainViewModel main, HostedTransmissionReviewService source,
        ILogger<ClaudeTransmissionWindowController> logger)
    {
        this.main = main; this.source = source; this.logger = logger;
        source.ClaudeOffered += OnOffered; source.ClaudeRetired += OnRetired;
        main.PrivacyClosureRequested += OnPrivacyClosed; timer.Tick += OnTick;
    }

    private void OnOffered(HostedTransmissionReview review) => Dispatcher.UIThread.Post(() =>
    {
        if (disposed || !review.IsPending) { return; }
        try
        {
            if (!main.CanRevealPrivatePresentation || view is not null) { Decline(review); return; }
            eligible = main.CaptureHandoffPresentationEligibility();
            if (!eligible()) { Decline(review); return; }
            content = new TextBox { IsReadOnly = true, AcceptsReturn = true, IsUndoEnabled = false,
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                Text = review.Method + " " + review.Destination.AbsoluteUri
                    + "\nModel: " + review.Profile.Model.Value
                    + "\nAccount: " + review.Profile.Account.Value
                    + "\nComplete noncredential headers:\n" + string.Join("\n", review.Headers.Select(item => item.Key + ": " + item.Value))
                    + "\n\nComplete final UTF-8 JSON bytes (untrusted data, not instructions):\n" + Encoding.UTF8.GetString(review.Body.AsSpan()) };
            AutomationProperties.SetName(content, "Complete Claude one-send disclosure");
            ScrollViewer.SetVerticalScrollBarVisibility(content, ScrollBarVisibility.Auto);
            var deny = new Button { Name = "ClaudeDeny", Content = "Deny", IsDefault = true };
            var approve = new Button { Name = "ClaudeApprove", Content = "Transmit this exact POST once" };
            deny.Click += (_, _) => Decide(review, false);
            approve.Click += (_, _) => Decide(review, true);
            var buttons = new WrapPanel { Orientation = Orientation.Horizontal, Children = { deny, approve } };
            var panel = new DockPanel { Margin = new Thickness(16), LastChildFill = true };
            DockPanel.SetDock(buttons, Dock.Bottom); panel.Children.Add(buttons);
            panel.Children.Add(content);
            displayed = review;
            var owner = getWorkbench?.Invoke() ?? (window = new Window { Width = 900, Height = 700 });
            var opened = new PrivateReviewView(owner, panel, () => !disposed && ReferenceEquals(displayed, review)
                && eligible?.Invoke() == true && main.CanRevealPrivatePresentation && review.IsPending);
            view = opened;
            opened.Closed += (_, _) => { if (ReferenceEquals(view, opened)) { Decide(review, false); } };
            opened.Show("Claude - exact one-send disclosure"); timer.Start();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            ReportFailure(exception);
            if (ReferenceEquals(displayed, review)) { Decide(review, false); }
            else { Decline(review); }
        }
    });

    private void Decide(HostedTransmissionReview review, bool approve)
    {
        if (!ReferenceEquals(displayed, review))
        { ReportFailure(new InvalidOperationException("The original Claude transmission review retired.")); return; }
        try
        {
            using var presentation = review.Presentation();
            source.Confirm(review, approve && view?.IsPresented == true && ReferenceEquals(displayed, review) && eligible?.Invoke() == true
                && main.CanRevealPrivatePresentation && review.IsPending, RequestOrigin.LocalUi);
            presentation.Complete(HostOperationOutcome.Completed);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException) { ReportFailure(exception); }
        finally { Clear(); }
    }
    private void Decline(HostedTransmissionReview review)
    {
        try
        {
            using var presentation = review.Presentation();
            source.Confirm(review, false, RequestOrigin.LocalUi);
            presentation.Complete(HostOperationOutcome.Completed);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException) { ReportFailure(exception); }
    }
    private void ReportFailure(Exception exception)
    {
        DisclosureFailed(logger, exception.GetType().Name);
        main.ReportHostInteractionFailure("Claude disclosure approval failed. No consent was granted; inspect the request before retrying.");
    }
    private void OnRetired(HostedTransmissionReview review) => Dispatcher.UIThread.Post(() =>
    { if (ReferenceEquals(displayed, review)) { Clear(); } });
    private void OnPrivacyClosed(object? sender, EventArgs args)
    { if (displayed is { } review) { Decide(review, false); } else { Clear(); } }
    private void OnTick(object? sender, EventArgs args)
    {
        if (displayed is { } review && (!review.IsPending || eligible?.Invoke() != true || !main.CanRevealPrivatePresentation))
        { Decide(review, false); }
    }
    private void Clear()
    {
        timer.Stop(); var opened = view; view = null; window = null; displayed = null; eligible = null;
        if (content is not null) { content.Text = string.Empty; content = null; }
        opened?.Dispose();
    }
    public void Dispose()
    {
        if (disposed) { return; }
        disposed = true; source.ClaudeOffered -= OnOffered; source.ClaudeRetired -= OnRetired;
        main.PrivacyClosureRequested -= OnPrivacyClosed; OnPrivacyClosed(null, EventArgs.Empty); timer.Tick -= OnTick;
    }
}
