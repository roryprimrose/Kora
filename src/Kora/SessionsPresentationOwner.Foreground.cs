using System.ComponentModel;

using Avalonia;
using Avalonia.Controls;

using Kora.Application.Hosting;
using Kora.Application.ViewModels;
using Kora.Core.Conversation;

namespace Kora;

internal sealed partial class SessionsPresentationOwner
{
    private readonly MainViewModel? requestCommands;
    private ForegroundConversationTarget? foregroundTarget;
    private Window? foregroundSurface;
    private long foregroundSourceRevision;
    private SessionRoutingPresentationLease? routingPresentation;

    internal SessionRoutingPresentationLease? RetainRoutingPresentation()
    {
        if (routingPresentation is not null) { throw new InvalidOperationException("The original routing presentation is already retained."); }
        if (foregroundTarget is not { IsCurrent: true } target || foregroundSurface is not { } surface
            || !ReferenceEquals(UniqueForegroundSurface(conversationRequired: true), surface)) { return null; }
        return routingPresentation = new(surface, target, UpdateForegroundTarget, () =>
        {
            routingPresentation = null;
            UpdateForegroundTarget();
        });
    }

    internal bool HasUniqueForegroundWindow => UniqueForegroundSurface(conversationRequired: false) is not null;
    internal Func<bool> CaptureForegroundSourceEligibility()
    {
        var source = UniqueForegroundSurface(conversationRequired: true);
        var revision = Interlocked.Read(ref foregroundSourceRevision);
        // Deferred source validation reads event-observed currency, never Avalonia properties off-dispatcher.
        return () => source is not null && !revoked && revision == Interlocked.Read(ref foregroundSourceRevision);
    }

    private Window? UniqueForegroundSurface(bool conversationRequired)
    {
        if (revoked) { return null; }
        var candidates = surfaces.Where(surface => surface.IsVisible && surface.IsActive
            && (!conversationRequired || surface is not ResponseWindow response || response.IsSessionListPresented))
            .Take(2).ToArray();
        return candidates.Length == 1 ? candidates[0] : null;
    }

    internal ForegroundConversationTarget? CaptureForegroundConversationTarget() => Volatile.Read(ref foregroundTarget);

    private void ObserveForegroundSurface(Window surface)
    {
        surface.PropertyChanged += OnForegroundSurfaceChanged;
        if (surface is ResponseWindow response) { response.SessionView.PropertyChanged += OnForegroundSurfaceChanged; }
        RequestPanel(surface)?.Bind(requestCommands);
        UpdateForegroundTarget();
    }

    private void ForgetForegroundSurface(Window surface)
    {
        Interlocked.Increment(ref foregroundSourceRevision);
        surface.PropertyChanged -= OnForegroundSurfaceChanged;
        if (surface is ResponseWindow response) { response.SessionView.PropertyChanged -= OnForegroundSurfaceChanged; }
        RequestPanel(surface)?.Bind(null);
        RetireForegroundTarget();
    }

    private static SessionRequestReviewPanel? RequestPanel(Window surface) => surface is ResponseWindow response
        ? response.SessionView.FindControl<SessionRequestReviewPanel>("OriginalRequestReview")
        : surface.FindNameScope()?.Find<SessionRequestReviewPanel>("OriginalRequestReview");

    private void OnForegroundSurfaceChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.Property == Window.IsActiveProperty || args.Property == Visual.IsVisibleProperty)
        { Interlocked.Increment(ref foregroundSourceRevision); UpdateForegroundTarget(); }
    }

    private void OnForegroundModelChanged(object? sender, PropertyChangedEventArgs args) => UpdateForegroundTarget();

    private void RetireForegroundTarget()
    {
        Interlocked.Exchange(ref foregroundTarget, null)?.Retire();
        foregroundSurface = null;
        requestCommands?.PublishForegroundConversationTarget(null);
    }

    private void UpdateForegroundTarget()
    {
        var surface = UniqueForegroundSurface(conversationRequired: true);
        if (surface is null || !model.CanCompose || model.SelectedSessionRecord is not { } entry)
        {
            if (model.CanCompose && routingPresentation?.Retains(foregroundTarget, surface) == true) { return; }
            RetireForegroundTarget();
            return;
        }
        if (ReferenceEquals(surface, foregroundSurface) && foregroundTarget is { IsCurrent: true }) { return; }
        RetireForegroundTarget();
        var source = model.CaptureDraftPresentationEligibility();
        var target = new ForegroundConversationTarget(entry, () => source() && model.CanCompose);
        foregroundSurface = surface;
        Volatile.Write(ref foregroundTarget, target);
        requestCommands?.PublishForegroundConversationTarget(target);
    }

    private void OnForegroundRequestSubmitted(ConversationSubmissionReceipt receipt) =>
        model.PresentOriginalRequestSubmission(receipt);
}
