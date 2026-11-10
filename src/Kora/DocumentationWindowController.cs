using Kora.Application.Documentation;
using Kora.Application.ViewModels;

using Microsoft.Extensions.Logging;
using Avalonia.Controls;

namespace Kora;

public sealed class DocumentationWindowController : IDisposable
{
    private readonly IUserDocumentationProvider documentation;
    private readonly MainViewModel viewModel;
    private readonly ILogger<DocumentationWindowController> logger;
    private readonly DetailWindowController? details;
    private DocumentationWindow? window;
    private bool disposed;
    private DocumentationView? view;
    private Window? viewOwner;
    private Func<Window>? getWorkbench;
    private Action<DocumentationView>? presentWorkbench;

    internal void BindWorkbench(Func<Window> owner, Action<DocumentationView> present)
    {
        getWorkbench = owner;
        presentWorkbench = present;
    }

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
        CloseHostedView();
        if (window is not null)
        {
            window.Closed -= OnWindowClosed;
            window.Close();
            window = null;
        }
    }

    private void OnDocumentationRequested(object? sender, EventArgs eventArgs)
    {
        if (disposed || !viewModel.CanRevealPrivatePresentation)
        {
            return;
        }
        DesktopLog.Information(logger, "Documentation window was requested");
        if (getWorkbench is not null && presentWorkbench is not null)
        {
            if (view is null)
            {
                viewOwner = getWorkbench();
                view = new DocumentationView(documentation, viewModel, viewOwner, details);
                viewOwner.Closed += OnHostedOwnerClosed;
            }
            presentWorkbench(view);
            return;
        }
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

    private void CloseHostedView()
    {
        if (view is null) { return; }
        if (viewOwner is not null) { viewOwner.Closed -= OnHostedOwnerClosed; }
        if (viewOwner is ResponseWindow workbench) { workbench.RemoveHostedView(view); }
        view.Dispose();
        view = null;
        viewOwner = null;
    }

    private void OnHostedOwnerClosed(object? sender, EventArgs args) => CloseHostedView();

    private void OnPrivacyClosureRequested(object? sender, EventArgs eventArgs)
    {
        CloseHostedView();
        window?.Close();
    }
}
