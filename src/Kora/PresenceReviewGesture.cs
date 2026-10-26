using Avalonia.Input;
using Kora.Application.Presentation;
using Kora.Application.ViewModels;

namespace Kora;

internal sealed class PresenceReviewGesture(MainViewModel model)
{
    private ModelActionReviewTarget? pressedTarget;

    internal PresencePointerIntent Press(bool interceptsMouse, KeyModifiers modifiers, MouseButton button)
    {
        pressedTarget = null;
        if (!interceptsMouse || !model.CanRevealPrivatePresentation || !modifiers.HasFlag(KeyModifiers.Control))
        { return PresencePointerIntent.None; }
        if (button == MouseButton.Left) { return PresencePointerIntent.Drag; }
        if (button != MouseButton.Right) { return PresencePointerIntent.None; }
        pressedTarget = model.CaptureModelActionReview();
        return PresencePointerIntent.Review;
    }

    internal ModelActionReviewTarget? Release(bool interceptsMouse, KeyModifiers modifiers, MouseButton button, bool inside)
    {
        var target = pressedTarget;
        pressedTarget = null;
        return interceptsMouse && inside && button == MouseButton.Right && modifiers.HasFlag(KeyModifiers.Control)
            && model.CanRevealPrivatePresentation ? target : null;
    }

    internal void Cancel() => pressedTarget = null;
}
