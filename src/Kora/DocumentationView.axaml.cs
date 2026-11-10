using System.ComponentModel;

using Avalonia.Controls;
using Avalonia.Markup.Xaml;

using Kora.Application.Documentation;
using Kora.Application.ViewModels;


namespace Kora;

public sealed partial class DocumentationView : UserControl, IDisposable
{
    private readonly MainViewModel viewModel;
    private readonly StackPanel documentPanel;
    private readonly ScrollViewer documentScrollViewer;
    private UserDocumentationPage? currentPage;
    private readonly Window owner;
    private bool disposed;
    internal event EventHandler? TitleChanged;
    internal string DisplayTitle => $"{viewModel.AssistantName} Documentation";

    public DocumentationView() => throw new InvalidOperationException("Documentation requires an explicit host-owned view.");

    public DocumentationView(
        IUserDocumentationProvider documentation,
        MainViewModel viewModel,
        Window owner,
        DetailWindowController? details = null)
    {
        ArgumentNullException.ThrowIfNull(documentation);
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(owner);
        this.viewModel = viewModel;
        this.owner = owner;
        AvaloniaXamlLoader.Load(this);
        DataContext = viewModel;
        documentPanel = this.FindControl<StackPanel>("DocumentPanel")
            ?? throw new InvalidOperationException("The documentation content panel is unavailable.");
        documentScrollViewer = this.FindControl<ScrollViewer>("DocumentScrollViewer")
            ?? throw new InvalidOperationException("The documentation scroll viewer is unavailable.");
        var navigationPanel = this.FindControl<StackPanel>("NavigationPanel")
            ?? throw new InvalidOperationException("The documentation navigation panel is unavailable.");
        details ??= (Avalonia.Application.Current as App)?.Details;
        var detailController = details;
        var openDetails = this.FindControl<Button>("OpenDetails")
            ?? throw new InvalidOperationException("The native detail button is unavailable.");
        openDetails.IsEnabled = detailController is not null;
        openDetails.Click += (_, _) =>
        {
            if (!disposed && IsEffectivelyVisible && owner.IsVisible && owner.WindowState != WindowState.Minimized
                && viewModel.CanRevealPrivatePresentation && currentPage is not null && detailController is not null)
            {
                this.FindControl<TextBlock>("DetailOpenStatus")!.Text =
                    detailController.OpenEmbeddedPage(currentPage, owner);
            }
        };

        foreach (var page in documentation.GetPages())
        {
            var button = new Button
            {
                Content = page.Title,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Left,
            };
            button.Click += (_, _) => OpenPage(page);
            navigationPanel.Children.Add(button);
        }

        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        owner.Closed += OnClosed;
        UpdateTitle();
        RenderPage(documentation.GetStartPage());
    }

    private void OpenPage(UserDocumentationPage page)
    {
        if (disposed || !IsEffectivelyVisible || !owner.IsVisible || owner.WindowState == WindowState.Minimized
            || !viewModel.CanRevealPrivatePresentation)
        {
            viewModel.ReportHostInteractionFailure("Documentation navigation unavailable: the original view is not presented.");
            return;
        }
        RenderPage(page);
    }

    private void RenderPage(UserDocumentationPage page)
    {
        currentPage = page;
        this.FindControl<TextBlock>("DetailOpenStatus")!.Text = string.Empty;
        documentPanel.Children.Clear();
        foreach (var control in MarkdownDocumentRenderer.Render(page.Markdown))
        {
            documentPanel.Children.Add(control);
        }

        documentScrollViewer.Offset = default;
    }

    private void OnViewModelPropertyChanged(
        object? sender,
        PropertyChangedEventArgs eventArgs)
    {
        if (string.Equals(
            eventArgs.PropertyName,
            nameof(MainViewModel.AssistantName),
            StringComparison.Ordinal))
        {
            UpdateTitle();
        }
    }

    private void UpdateTitle() => TitleChanged?.Invoke(this, EventArgs.Empty);

    private void OnClosed(object? sender, EventArgs eventArgs) => Dispose();

    public void Dispose()
    {
        if (disposed) { return; }
        disposed = true;
        currentPage = null;
        documentPanel.Children.Clear();
        this.FindControl<StackPanel>("NavigationPanel")!.Children.Clear();
        this.FindControl<TextBlock>("DetailOpenStatus")!.Text = string.Empty;
        DataContext = null;
        viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        owner.Closed -= OnClosed;
    }
}
