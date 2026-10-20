using Avalonia.Controls;
using Avalonia.Threading;

namespace Kora;

internal sealed partial class SessionsWindow
{
    internal bool FocusPreparedListQuery()
    {
        if (!Dispatcher.UIThread.CheckAccess() || !IsVisible || !ListQuery.IsEnabled) { return false; }
        Activate();
        ListQuery.BringIntoView();
        return ListQuery.Focus() && ListQuery.IsFocused;
    }
}
