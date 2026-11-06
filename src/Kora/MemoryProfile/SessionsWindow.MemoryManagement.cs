using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace Kora;

internal sealed partial class SessionsWindow
{
    private async Task PresentMemoryManagementAsync(SessionsViewModel model, Func<Task> operation)
    {
        try
        {
            using var presenter = new ReplyPresentationFence(MemoryDetailReader);
            var surfaceCurrent = presenter.Capture();
            var sourceCurrent = model.CaptureMemoryPresentation();
            bool Current() => surfaceCurrent() && sourceCurrent() && IsActive
                && MemoryDetailReader.IsEffectivelyVisible && !string.IsNullOrEmpty(model.MemoryDetail);
            var ancestors = MemoryDetailReader.GetVisualAncestors().ToArray();
            void Changed(object? sender, AvaloniaPropertyChangedEventArgs args)
            {
                if (args.Property == IsActiveProperty || args.Property == Visual.IsVisibleProperty) { presenter.Retire(); }
            }
            foreach (var ancestor in ancestors) { ancestor.PropertyChanged += Changed; }
            try
            {
                await operation();
                if (!Current()) { return; }
                MemoryDetailReader.BringIntoView();
                if (!Current()) { return; }
                if (!MemoryDetailReader.Focus())
                { throw new InvalidOperationException("The exact native memory inspection or result could not receive focus."); }
                MemoryDetailReader.SelectionStart = 0;
                MemoryDetailReader.SelectionEnd = 0;
                MemoryDetailReader.CaretIndex = 0;
            }
            finally
            {
                foreach (var ancestor in ancestors) { ancestor.PropertyChanged -= Changed; }
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        { model.ReportMemoryPresentationFailure(exception); }
    }
}
