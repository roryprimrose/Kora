using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;

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
            bool Current() => surfaceCurrent() && sourceCurrent() && model.CanReadConversation
                && IsActive && SessionMemoryCopyPanel.IsEffectivelyVisible;
            var ancestors = SessionMemoryCopyPanel.GetVisualAncestors().ToArray();
            void RootChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
            {
                if (args.Property == IsActiveProperty || args.Property == Visual.IsVisibleProperty) { presenter.Retire(); }
            }
            foreach (var ancestor in ancestors) { ancestor.PropertyChanged += RootChanged; }
            try
            {
                await operation();
                if (!Current()) { return; }
                var destination = string.IsNullOrEmpty(model.SessionMemoryCopyDescription)
                    ? SessionMemoryCopyStatus : SessionMemoryCopyReview;
                destination.BringIntoView();
                if (!Current()) { return; }
                if (!destination.Focus())
                { throw new InvalidOperationException("The exact native scope-copy review or outcome could not receive focus."); }
                destination.SelectionStart = 0;
                destination.SelectionEnd = 0;
                destination.CaretIndex = 0;
            }
            finally
            {
                foreach (var ancestor in ancestors) { ancestor.PropertyChanged -= RootChanged; }
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        { model.ReportSessionMemoryCopyPresentationFailure(exception); }
    }
}
