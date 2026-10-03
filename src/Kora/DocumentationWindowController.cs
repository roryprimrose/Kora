using Kora.Application.Documentation;
using Kora.Application.ViewModels;

using Microsoft.Extensions.Logging;

namespace Kora;

public sealed class DocumentationWindowController : IDisposable
{
    private readonly IUserDocumentationProvider documentation;
    private readonly MainViewModel viewModel;
    private readonly ILogger<DocumentationWindowController> logger;
    private DocumentationWindow? window;
    private bool disposed;

    public DocumentationWindowController(
        IUserDocumentationProvider documentation,
        MainViewModel viewModel,
        ILogger<DocumentationWindowController> logger)
    {
        this.documentation = documentation;
        this.viewModel = viewModel;
        this.logger = logger;
        viewModel.DocumentationRequested += OnDocumentationRequested;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        DesktopLog.Debug(logger, "Disposing the documentation window controller");
        viewModel.DocumentationRequested -= OnDocumentationRequested;
        if (window is not null)
        {
            window.Closed -= OnWindowClosed;
            window.Close();
            window = null;
        }
    }

    private void OnDocumentationRequested(object? sender, EventArgs eventArgs)
    {
        DesktopLog.Information(logger, "Documentation window was requested");
        window ??= CreateWindow();
        if (!window.IsVisible)
        {
            window.Show();
        }

        window.Activate();
    }

    private DocumentationWindow CreateWindow()
    {
        var documentationWindow = new DocumentationWindow(documentation, viewModel);
        documentationWindow.Closed += OnWindowClosed;
        return documentationWindow;
    }

    private void OnWindowClosed(object? sender, EventArgs eventArgs)
    {
        if (ReferenceEquals(window, sender))
        {
            DesktopLog.Debug(logger, "Documentation window closed");
            window = null;
        }
    }
}
