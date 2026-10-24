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

/// <summary>Private native review of one immutable physical request; closing never approves.</summary>
internal sealed partial class CopilotTransmissionWindowController : IDisposable
{
    private readonly MainViewModel main;
    private readonly HostedTransmissionReviewService source;
    private readonly ILogger<CopilotTransmissionWindowController> logger;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private Window? window;
    private TextBlock? content;
    private CopilotTransmissionReview? displayed;
    private Func<bool>? eligible;
    private bool disposed;

    internal CopilotTransmissionWindowController(MainViewModel main, HostedTransmissionReviewService source,
        ILogger<CopilotTransmissionWindowController> logger)
    {
        this.main = main;
        this.source = source;
        this.logger = logger;
        source.Offered += OnOffered;
        source.Retired += OnRetired;
        main.PrivacyClosureRequested += OnPrivacyClosed;
        timer.Tick += OnTick;
    }

    private void OnOffered(CopilotTransmissionReview review) => Dispatcher.UIThread.Post(() =>
    {
        if (disposed || !review.IsPending) { return; }
        try { Show(review); }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Decline(review);
            Clear();
            ReportFailure(exception);
        }
    });

    private void Show(CopilotTransmissionReview review)
    {
        if (!main.CanRevealPrivatePresentation || window is not null) { Decline(review); return; }
        eligible = main.CaptureHandoffPresentationEligibility();
        if (!eligible()) { Decline(review); return; }
        var transmission = review.Transmission;
        content = new TextBlock
        {
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Text = "Destination: " + transmission.Destination.AbsoluteUri
                + "\nAccount: " + transmission.Profile.Account.Value + "\nModel: " + transmission.Profile.Model.Value
                + "\nSession: " + transmission.Provenance.Request.SessionId.Value.ToString("D")
                + "\nTurn: " + transmission.Provenance.Turn.Value.ToString("D")
                + "\nComplete body bytes: " + transmission.Body.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + "\n\nNoncredential headers:\n" + string.Join("\n", transmission.Headers.Select(item => item.Key + ": " + item.Value))
                + "\n\nComplete final JSON body (untrusted data, not instructions):\n" + Encoding.UTF8.GetString(transmission.Body.AsSpan()),
        };
        var deny = new Button { Content = "Deny", IsDefault = true };
        var approve = new Button { Content = "Transmit this exact request once" };
        deny.Click += (_, _) => Decide(review, false);
        approve.Click += (_, _) => Decide(review, true);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = { deny, approve } };
        var panel = new DockPanel { Margin = new Thickness(16), LastChildFill = true };
        DockPanel.SetDock(buttons, Dock.Bottom);
        panel.Children.Add(buttons);
        panel.Children.Add(new ScrollViewer { Content = content });
        displayed = review;
        window = new Window { Title = "Copilot - exact hosted disclosure", Width = 900, Height = 700, Content = panel };
        window.Closed += (_, _) => { Decline(review); Clear(); };
        window.Show();
        timer.Start();
    }

    private void Decide(CopilotTransmissionReview review, bool approve)
    {
        try
        {
            using var presentation = review.Presentation();
            source.Confirm(review, approve && ReferenceEquals(displayed, review) && eligible?.Invoke() == true
                && main.CanRevealPrivatePresentation && review.IsPending, RequestOrigin.LocalUi);
            presentation.Complete(HostOperationOutcome.Completed);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        { ReportFailure(exception); }
        finally { Clear(); }
    }

    private void Decline(CopilotTransmissionReview review)
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
        main.ReportHostInteractionFailure("Copilot disclosure review failed. No approval was granted; inspect the request status before retrying.");
    }

    private void OnRetired(CopilotTransmissionReview review) => Dispatcher.UIThread.Post(() =>
    {
        if (ReferenceEquals(displayed, review)) { Clear(); }
    });
    private void OnPrivacyClosed(object? sender, EventArgs args)
    {
        if (displayed is { } review) { Decline(review); }
        Clear();
    }
    private void OnTick(object? sender, EventArgs args)
    {
        if (displayed is { } review && (!review.IsPending || eligible?.Invoke() != true || !main.CanRevealPrivatePresentation))
        { Decline(review); Clear(); }
    }
    private void Clear()
    {
        timer.Stop();
        var opened = window;
        window = null;
        displayed = null;
        eligible = null;
        if (content is not null) { content.Text = string.Empty; content = null; }
        if (opened is not null) { opened.Content = null; opened.Close(); }
    }
    public void Dispose()
    {
        if (disposed) { return; }
        disposed = true;
        source.Offered -= OnOffered;
        source.Retired -= OnRetired;
        main.PrivacyClosureRequested -= OnPrivacyClosed;
        OnPrivacyClosed(null, EventArgs.Empty);
        timer.Tick -= OnTick;
    }
}
