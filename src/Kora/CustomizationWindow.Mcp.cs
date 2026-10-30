using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;

namespace Kora;

internal sealed partial class CustomizationWindow
{
    internal readonly TextBox McpUrl = new() { PlaceholderText = "https://server.example/mcp" };
    internal readonly CheckBox McpHttpType = new() { Content = "Include explicit type: http" };
    private readonly StackPanel McpFields = new() { Spacing = 8, Orientation = Orientation.Vertical, IsVisible = false };

    private void BindMcpFields(Action<string> run)
    {
        McpFields.Children.Add(new TextBlock
        {
            Text = "Private Kora-owned HTTPS entry. URL is shown only in these native fields and exact native reviews. Saving disables and excludes the server; authentication and connector qualification remain unavailable.",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
        });
        McpFields.Children.Add(McpUrl);
        McpFields.Children.Add(McpHttpType);
        AutomationProperties.SetName(McpUrl, "Private exact HTTPS MCP endpoint; never shared with a model");
        AutomationProperties.SetName(McpHttpType, "Optional explicit MCP transport type http");
        McpUrl.PropertyChanged += (_, change) =>
        { if (change.Property == TextBox.TextProperty) { run("Mcp input"); } };
        McpHttpType.PropertyChanged += (_, change) =>
        { if (change.Property == ToggleButton.IsCheckedProperty) { run("Mcp input"); } };
    }
}
