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

    internal bool HasUniqueForegroundWindow => UniqueForegroundSurface(conversationRequired: false) is not null;

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
        { UpdateForegroundTarget(); }
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
        { RetireForegroundTarget(); return; }
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
