namespace Kora;

internal sealed partial class SessionsWindow
{
    internal async Task<bool> OpenComposerComparison(SessionsViewModel model, Func<bool> admitted)
    {
        if (!IsVisible || !admitted() || model.CompareComposer(admitted) is not { } comparison) { return false; }
        var dialog = new ConversationDraftComparisonWindow(this, model, comparison);
        dialog.Open(this);
        if (!comparison.Available || !admitted() || !dialog.IsVisible)
        {
            dialog.Hide();
            model.CloseComposerComparison(comparison);
            return false;
        }
        dialog.Activate();
        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(static () => { },
            Avalonia.Threading.DispatcherPriority.Background);
        if (IsVisible && dialog.IsVisible && dialog.IsActive && comparison.Available && admitted()) { return true; }
        dialog.Hide();
        model.CloseComposerComparison(comparison);
        return false;
    }
}
