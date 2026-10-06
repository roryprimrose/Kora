using Kora.Application.Documentation;
using Kora.Application.ViewModels;

using Microsoft.Extensions.Logging;

namespace Kora;

public sealed class DocumentationWindowController : IDisposable
{
    private readonly IUserDocumentationProvider documentation;
    private readonly MainViewModel viewModel;
    private readonly ILogger<DocumentationWindowController> logger;
    private readonly DetailWindowController? details;
    private DocumentationWindow? window;
    private bool disposed;

    public DocumentationWindowController(
        IUserDocumentationProvider documentation,
        MainViewModel viewModel,
        ILogger<DocumentationWindowController> logger,
        DetailWindowController? details = null)
    {
        this.documentation = documentation;
        this.viewModel = viewModel;
        this.logger = logger;
        this.details = details;
        viewModel.DocumentationRequested += OnDocumentationRequested;
        viewModel.PrivacyClosureRequested += OnPrivacyClosureRequested;
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
        viewModel.PrivacyClosureRequested -= OnPrivacyClosureRequested;
        if (window is not null)
        {
            window.Closed -= OnWindowClosed;
            window.Close();
            window = null;
        }
    }

    private void OnDocumentationRequested(object? sender, EventArgs eventArgs)
    {
        if (!viewModel.CanRevealPrivatePresentation)
        {
            return;
        }
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
        var documentationWindow = new DocumentationWindow(documentation, viewModel, details);
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

    private void OnPrivacyClosureRequested(object? sender, EventArgs eventArgs) => window?.Close();
}
