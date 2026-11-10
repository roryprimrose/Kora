using Avalonia.Controls;
using Avalonia.Markup.Xaml;

using Kora.Application.Documentation;
using Kora.Application.ViewModels;

using Microsoft.Extensions.DependencyInjection;

namespace Kora;

public sealed partial class DocumentationWindow : Window
{
    internal DocumentationView View { get; }
    internal T? FindControl<T>(string name) where T : Control => View.FindControl<T>(name);

    public DocumentationWindow() : this(App.Services.GetRequiredService<IUserDocumentationProvider>(),
        App.Services.GetRequiredService<MainViewModel>()) { }

    public DocumentationWindow(IUserDocumentationProvider documentation, MainViewModel viewModel,
        DetailWindowController? details = null)
    {
        AvaloniaXamlLoader.Load(this);
        DataContext = viewModel;
        View = new(documentation, viewModel, this, details);
        Content = View;
        View.TitleChanged += OnTitleChanged;
        Closed += (_, _) =>
        {
            View.TitleChanged -= OnTitleChanged;
            View.Dispose();
        };
        OnTitleChanged(this, EventArgs.Empty);
    }

    private void OnTitleChanged(object? sender, EventArgs args) => Title = View.DisplayTitle;
}
