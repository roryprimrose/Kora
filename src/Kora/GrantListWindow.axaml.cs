using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Kora;

public sealed partial class GrantListWindow : Window
{
    private readonly StackPanel documentPanel;

    public GrantListWindow()
    {
        AvaloniaXamlLoader.Load(this);
        documentPanel = this.FindControl<StackPanel>("GrantDocumentPanel")
            ?? throw new InvalidOperationException("The grant document panel is unavailable.");
    }

    public void UpdateDocument(string markdown, string assistantName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(markdown);
        ArgumentException.ThrowIfNullOrWhiteSpace(assistantName);
        Title = $"{assistantName} grants";
        documentPanel.Children.Clear();
        foreach (var control in MarkdownDocumentRenderer.Render(markdown))
        {
            documentPanel.Children.Add(control);
        }
    }
}
