using Kora.Application.ViewModels;

using Microsoft.Extensions.Logging;

namespace Kora;

public sealed class ResponseWindowController : IDisposable
{
    private readonly MainViewModel viewModel;
    private readonly ILogger<ResponseWindowController> logger;
    private ResponseWindow? window;
    private bool disposed;

    public ResponseWindowController(
        MainViewModel viewModel,
        ILogger<ResponseWindowController> logger)
    {
        this.viewModel = viewModel;
        this.logger = logger;
        viewModel.WindowActionRequested += OnWindowActionRequested;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        viewModel.WindowActionRequested -= OnWindowActionRequested;
        if (window is not null)
        {
            window.Close();
            window = null;
        }
    }

    private void OnWindowActionRequested(object? sender, WindowAction action)
    {
        switch (action)
        {
            case WindowAction.Show when viewModel.IsVisualResponseVisible:
                window ??= new ResponseWindow(viewModel);
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
