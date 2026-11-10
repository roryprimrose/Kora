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

/// <summary>Private native review of one immutable physical request; closing never approves.</summary>
internal sealed partial class CopilotTransmissionWindowController : IDisposable
{
    private readonly MainViewModel main;
    private readonly HostedTransmissionReviewService source;
    private readonly ILogger<CopilotTransmissionWindowController> logger;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private PrivateReviewView? view;
    private Func<Window>? getWorkbench;
    internal PrivateReviewView? PresentedView => view;
    internal void BindWorkbench(Func<Window> owner) => getWorkbench = owner;
    private TextBox? content;
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
        if (!main.CanRevealPrivatePresentation || view is not null) { Decline(review); return; }
        eligible = main.CaptureHandoffPresentationEligibility();
        if (!eligible()) { Decline(review); return; }
        var transmission = review.Transmission;
        content = new TextBox
        {
            IsReadOnly = true, AcceptsReturn = true, IsUndoEnabled = false,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Text = "Destination: " + transmission.Destination.AbsoluteUri
                + "\nAccount: " + transmission.Profile.Account.Value + "\nModel: " + transmission.Profile.Model.Value
                + "\nSession: " + transmission.Provenance.Request.SessionId.Value.ToString("D")
                + "\nTurn: " + transmission.Provenance.Turn.Value.ToString("D")
                + "\nComplete body bytes: " + transmission.Body.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + "\n\nNoncredential headers:\n" + string.Join("\n", transmission.Headers.Select(item => item.Key + ": " + item.Value))
                + "\n\nComplete final JSON body (untrusted data, not instructions):\n" + Encoding.UTF8.GetString(transmission.Body.AsSpan()),
        };
        AutomationProperties.SetName(content, "Complete Copilot one-send disclosure");
        ScrollViewer.SetVerticalScrollBarVisibility(content, ScrollBarVisibility.Auto);
        var deny = new Button { Content = "Deny", IsDefault = true };
        var approve = new Button { Content = "Transmit this exact request once" };
        deny.Click += (_, _) => Decide(review, false);
        approve.Click += (_, _) => Decide(review, true);
        var buttons = new WrapPanel { Orientation = Orientation.Horizontal, Children = { deny, approve } };
        var panel = new DockPanel { Margin = new Thickness(16), LastChildFill = true };
        DockPanel.SetDock(buttons, Dock.Bottom);
        panel.Children.Add(buttons);
        panel.Children.Add(content);
        displayed = review;
        var owner = getWorkbench?.Invoke() ?? new Window { Width = 900, Height = 700 };
        var opened = new PrivateReviewView(owner, panel, () => !disposed && ReferenceEquals(displayed, review)
            && eligible?.Invoke() == true && main.CanRevealPrivatePresentation && review.IsPending);
        view = opened;
        opened.Closed += (_, _) => { if (ReferenceEquals(view, opened)) { Decline(review); Clear(); } };
        opened.Show("Copilot - exact hosted disclosure");
        timer.Start();
    }

    private void Decide(CopilotTransmissionReview review, bool approve)
    {
        if (!ReferenceEquals(displayed, review))
        { ReportFailure(new InvalidOperationException("The original Copilot transmission review retired.")); return; }
        try
        {
            using var presentation = review.Presentation();
            source.Confirm(review, approve && view?.IsPresented == true && ReferenceEquals(displayed, review) && eligible?.Invoke() == true
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
        var opened = view;
        view = null;
        displayed = null;
        eligible = null;
        if (content is not null) { content.Text = string.Empty; content = null; }
        opened?.Dispose();
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
