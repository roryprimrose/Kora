using System.ComponentModel;

using Avalonia.Controls;
using Avalonia.Markup.Xaml;

using Kora.Application.Documentation;
using Kora.Application.ViewModels;

using Microsoft.Extensions.DependencyInjection;

namespace Kora;

public sealed partial class DocumentationWindow : Window
{
    private readonly MainViewModel viewModel;
    private readonly StackPanel documentPanel;
    private readonly ScrollViewer documentScrollViewer;
    private UserDocumentationPage? currentPage;

    public DocumentationWindow()
        : this(
            App.Services.GetRequiredService<IUserDocumentationProvider>(),
            App.Services.GetRequiredService<MainViewModel>())
    {
    }

    public DocumentationWindow(
        IUserDocumentationProvider documentation,
        MainViewModel viewModel,
        DetailWindowController? details = null)
    {
        this.viewModel = viewModel;
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
            if (currentPage is not null && detailController is not null)
            {
                this.FindControl<TextBlock>("DetailOpenStatus")!.Text =
                    detailController.OpenEmbeddedPage(currentPage, this);
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
            button.Click += (_, _) => ShowPage(page);
            navigationPanel.Children.Add(button);
        }

        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        Closed += OnClosed;
        UpdateTitle();
        ShowPage(documentation.GetStartPage());
    }

    private void ShowPage(UserDocumentationPage page)
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

    private void UpdateTitle() =>
        Title = $"{viewModel.AssistantName} Documentation";

    private void OnClosed(object? sender, EventArgs eventArgs)
    {
        currentPage = null;
        documentPanel.Children.Clear();
        this.FindControl<StackPanel>("NavigationPanel")!.Children.Clear();
        this.FindControl<TextBlock>("DetailOpenStatus")!.Text = string.Empty;
        DataContext = null;
        viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        Closed -= OnClosed;
    }
}
