using Kora.Application.ViewModels;

namespace Kora;

public sealed class GrantListWindowController : IDisposable
{
    private readonly MainViewModel viewModel;
    private GrantListWindow? window;

    public GrantListWindowController(MainViewModel viewModel)
    {
        this.viewModel = viewModel;
        viewModel.GrantDocumentRequested += OnGrantDocumentRequested;
        viewModel.GrantDocumentChanged += OnGrantDocumentChanged;
    }

    public void Dispose()
    {
        viewModel.GrantDocumentRequested -= OnGrantDocumentRequested;
        viewModel.GrantDocumentChanged -= OnGrantDocumentChanged;
        if (window is not null)
        {
            window.Closed -= OnWindowClosed;
            window.Close();
            window = null;
        }
    }

    private void OnGrantDocumentRequested(object? sender, string markdown)
    {
        if (!viewModel.CanRevealPrivatePresentation)
        {
            return;
        }
        if (window is null)
        {
            window = new GrantListWindow();
            window.Closed += OnWindowClosed;
        }

        window.UpdateDocument(markdown, viewModel.AssistantName);
        if (!window.IsVisible)
        {
            window.Show();
        }

        window.Activate();
    }

    private void OnWindowClosed(object? sender, EventArgs eventArgs)
    {
        if (ReferenceEquals(sender, window))
        {
            window = null;
        }
    }

    private void OnGrantDocumentChanged(object? sender, string markdown)
    {
        window?.UpdateDocument(markdown, viewModel.AssistantName);
    }
}
