using Avalonia.Controls;
using Avalonia.Threading;

using Kora.Application.Hosting;

namespace Kora;

internal sealed class OriginalMessageReuseWindow : Window, IDisposable
{
    private readonly OriginalMessageReuseView view;
    internal OriginalMessageReuseWindow(Window sourceOwner, OriginalMessageReuseReview review)
    {
        Title = "Review original USER text reuse — not task replay";
        Width = 1000; Height = 760; MinWidth = 680; MinHeight = 500;
        view = new(sourceOwner, this, review);
        Content = view;
        view.CloseRequested += (_, _) => Dispatcher.UIThread.Post(() => Close(view.Accepted));
        Closed += (_, _) => view.Dispose();
    }
    internal Task<bool> Open(Window owner)
    {
        try { return ShowDialog<bool>(owner); }
        catch { view.Dispose(); throw; }
    }
    public void Dispose() { Close(); view.Dispose(); }
}
