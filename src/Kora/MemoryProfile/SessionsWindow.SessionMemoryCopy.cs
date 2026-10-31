using Avalonia.Controls;

namespace Kora;

internal sealed partial class SessionsWindow
{
    private async Task PresentSessionMemoryCopyAsync(SessionsViewModel model, Func<Task> operation)
    {
        try
        {
            using var presenter = new ReplyPresentationFence(SessionMemoryCopyPanel);
            var surfaceCurrent = presenter.Capture();
            var sourceCurrent = model.CaptureSessionMemoryCopyPresentation();
            await operation();
            if (!surfaceCurrent() || !sourceCurrent() || !model.CanReadConversation
                || !IsActive || !SessionMemoryCopyPanel.IsEffectivelyVisible)
            { return; }
            var destination = string.IsNullOrEmpty(model.SessionMemoryCopyDescription)
                ? SessionMemoryCopyStatus : SessionMemoryCopyReview;
            destination.BringIntoView();
            if (!destination.Focus())
            { throw new InvalidOperationException("The exact native scope-copy review or outcome could not receive focus."); }
            destination.SelectionStart = 0;
            destination.SelectionEnd = 0;
            destination.CaretIndex = 0;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        { model.ReportSessionMemoryCopyPresentationFailure(exception); }
    }
}
