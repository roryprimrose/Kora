using System.Text;
using Avalonia;
using Avalonia.Controls;
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
    private TextBlock? content;
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
            if (!main.CanRevealPrivatePresentation || window is not null) { Decide(review, false); return; }
            eligible = main.CaptureHandoffPresentationEligibility();
            if (!eligible()) { Decide(review, false); return; }
            content = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                Text = review.Method + " " + review.Destination.AbsoluteUri
                    + "\nModel: " + review.Profile.Model.Value
                    + "\nAccount: " + review.Profile.Account.Value
                    + "\nComplete noncredential headers:\n" + string.Join("\n", review.Headers.Select(item => item.Key + ": " + item.Value))
                    + "\n\nComplete final UTF-8 JSON bytes (untrusted data, not instructions):\n" + Encoding.UTF8.GetString(review.Body.AsSpan()) };
            var deny = new Button { Name = "ClaudeDeny", Content = "Deny", IsDefault = true };
            var approve = new Button { Name = "ClaudeApprove", Content = "Transmit this exact POST once" };
            deny.Click += (_, _) => Decide(review, false);
            approve.Click += (_, _) => Decide(review, true);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = { deny, approve } };
            var panel = new DockPanel { Margin = new Thickness(16), LastChildFill = true };
            DockPanel.SetDock(buttons, Dock.Bottom); panel.Children.Add(buttons);
            panel.Children.Add(new ScrollViewer { Content = content });
            displayed = review;
            var opened = window = new Window { Title = "Claude - exact one-send disclosure", Width = 900, Height = 700, Content = panel };
            opened.Closed += (_, _) => Decide(review, false);
            opened.Show(); timer.Start();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        { ReportFailure(exception); Decide(review, false); }
    });

    private void Decide(HostedTransmissionReview review, bool approve)
    {
        try
        {
            using var presentation = review.Presentation();
            source.Confirm(review, approve && ReferenceEquals(displayed, review) && eligible?.Invoke() == true
                && main.CanRevealPrivatePresentation && review.IsPending, RequestOrigin.LocalUi);
            presentation.Complete(HostOperationOutcome.Completed);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException) { ReportFailure(exception); }
        finally { Clear(); }
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
        timer.Stop(); var opened = window; window = null; displayed = null; eligible = null;
        if (content is not null) { content.Text = string.Empty; content = null; }
        if (opened is not null) { opened.Content = null; opened.Close(); }
    }
    public void Dispose()
    {
        if (disposed) { return; }
        disposed = true; source.ClaudeOffered -= OnOffered; source.ClaudeRetired -= OnRetired;
        main.PrivacyClosureRequested -= OnPrivacyClosed; OnPrivacyClosed(null, EventArgs.Empty); timer.Tick -= OnTick;
    }
}
