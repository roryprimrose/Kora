using Kora.Application.ViewModels;
using Kora.Application.Presentation;
using Kora.Core.Presentation;

using Microsoft.Extensions.Logging;

namespace Kora;

public sealed class ResponseWindowController : IDisposable, ICurrentResponsePresentation
{
    private readonly MainViewModel viewModel;
    private readonly ILogger<ResponseWindowController> logger;
    private ResponseWindow? window;
    private bool disposed;
    private readonly DetailWindowController? details;

    public ResponseWindowController(
        MainViewModel viewModel,
        ILogger<ResponseWindowController> logger,
        DetailWindowController? detailController = null)
    {
        this.viewModel = viewModel;
        this.logger = logger;
        details = detailController;
        viewModel.WindowActionRequested += OnWindowActionRequested;
        viewModel.CurrentResponseChanged += OnCurrentResponseChanged;
        viewModel.BindCurrentResponsePresentation(this);
    }

    public Func<bool> CaptureLifetime()
    {
        var captured = window;
        return () => !disposed && captured is { IsVisible: true } && ReferenceEquals(captured, window);
    }

    public bool Dismiss(Func<bool> admitted)
    {
        if (disposed || window is not { IsVisible: true } current || !admitted()) { return false; }
        current.HideResponse();
        return !current.IsVisible;
    }

    public bool OpenDetails(AdmittedDetailContent content, Func<bool> admitted) =>
        !disposed && window is { IsVisible: true } current && admitted()
        && details?.OpenCurrentResponse(content, current, admitted) == true;

    private void OnCurrentResponseChanged(object? sender, EventArgs eventArgs) =>
        details?.RetireUnavailableCurrentResponses();

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        viewModel.WindowActionRequested -= OnWindowActionRequested;
        viewModel.CurrentResponseChanged -= OnCurrentResponseChanged;
        if (window is not null)
        {
            window.Close();
            window = null;
        }
        details?.RetireUnavailableCurrentResponses();
    }

    private void OnWindowActionRequested(object? sender, WindowAction action)
    {
        if (action is WindowAction.Show or WindowAction.ShowPresence && !viewModel.CanRevealPrivatePresentation)
        {
            return;
        }
        switch (action)
        {
            case WindowAction.Show when viewModel.IsVisualResponseVisible:
                window ??= new ResponseWindow(viewModel, details);
                DesktopLog.Debug(logger, "Showing the visual response window");
                window.ShowResponse();

                break;
            case WindowAction.Hide:
                window?.HideResponse();
                break;
            case WindowAction.ShowPresence:
            case WindowAction.Show:
            case WindowAction.Close:
                break;
            default:
                throw new InvalidOperationException($"Unknown window action: {action}.");
        }
    }
}
