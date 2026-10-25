using Avalonia.Controls;

using Kora.Application.Presentation;
using Kora.Application.ViewModels;
using Kora.Core.Presentation;

namespace Kora;

internal sealed class SessionDetailView : IDetailView
{
    private readonly SessionInteractionView host;
    private readonly DetailViewerState state;
    private bool closed;
    internal DetailContentView Reader { get; }
    internal AdmittedDetailContent? Content => state.Content;
    public event EventHandler? Closed;

    internal SessionDetailView(SessionInteractionView host, DetailViewerState state, NativeDocumentResult result,
        Func<bool, int?, int, Task> copy, MainViewModel typographyHost)
    {
        this.host = host;
        this.state = state;
        Reader = new(state, result, copy, typographyHost);
        Reader.CloseRequested += (_, _) => ClearAndClose();
    }

    public void ShowOwned(Window? owner)
    {
        if (closed || owner is not ResponseWindow window || Content is not { } content)
        { throw new InvalidOperationException("The exact session detail source or primary window is unavailable."); }
        window.ShowSessions(expanded: true);
        if (!window.IsVisible || !window.IsSessionListPresented || !host.ShowDetails(this, content))
        { throw new InvalidOperationException("Session details cannot be shown in this selected conversation or work area."); }
    }

    public void Activate()
    {
        if (closed || Content is null) { throw new InvalidOperationException("The exact detail source was retired."); }
        Reader.FocusReader();
    }

    public void ClearAndClose()
    {
        if (closed) { return; }
        closed = true;
        if (Content is { } content) { host.RememberReadingPosition(content.Reference, Reader.CaptureReadingPosition()); }
        Reader.ClearPresentation();
        host.RemoveDetails(this);
        Closed?.Invoke(this, EventArgs.Empty);
    }
}
