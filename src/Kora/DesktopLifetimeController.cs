using Kora.Application.ViewModels;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

using Microsoft.Extensions.Logging;

namespace Kora;

internal sealed class DesktopLifetimeController : IDisposable
{
    private readonly MainViewModel main;
    private readonly Action shutdown;
    private readonly ILogger<DesktopLifetimeController> logger;
    private bool disposed;

    internal DesktopLifetimeController(MainViewModel main, Action shutdown, ILogger<DesktopLifetimeController> logger)
    {
        this.main = main;
        this.shutdown = shutdown;
        this.logger = logger;
        main.WindowActionRequested += OnWindowActionRequested;
    }

    private void OnWindowActionRequested(object? sender, WindowAction action)
    {
        if (disposed || action != WindowAction.Close) { return; }
        using var activity = HostActivity.BeginOperation(
            HostActivityLayer.Desktop, HostOperation.Presentation, RequestOrigin.HostSystem);
        try
        {
            DesktopLog.Information(logger, "Shutting down the desktop application");
            shutdown();
            activity.Complete(HostOperationOutcome.Completed);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            activity.Complete(HostOperationOutcome.Failed);
            DesktopLog.Error(logger, exception, "Shutting down the desktop application");
            main.ReportHostInteractionFailure(
                "Desktop shutdown failed. Kora has not confirmed a clean exit. " + exception.Message);
        }
    }

    public void Dispose()
    {
        if (disposed) { return; }
        disposed = true;
        main.WindowActionRequested -= OnWindowActionRequested;
    }
}
