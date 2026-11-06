using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;

using Kora.Core.Customizations;
using Kora.Core.Mcp;

namespace Kora;

internal sealed partial class CustomizationWindow
{
    private readonly McpCapabilityRows capabilityRows = new();
    private readonly StackPanel capabilityCatalogue = new() { Spacing = 8 };
    internal readonly ComboBox CapabilityCategory = new()
    {
        ItemsSource = new[] { "All capabilities", "Tools", "Resources", "Prompts" },
        SelectedIndex = 0,
    };
    internal readonly TextBlock CapabilityCount = new();
    internal McpCapabilityRow? SelectedCapability => capabilityRows.Selected;
    internal McpCapabilityCategory? SelectedCapabilityCategory => CapabilityCategory.SelectedIndex switch
    {
        1 => McpCapabilityCategory.Tools,
        2 => McpCapabilityCategory.Resources,
        3 => McpCapabilityCategory.Prompts,
        _ => null,
    };
    private Action? manageCapability;
    private bool capabilityPrivacyClosed;
    private bool capabilitySnapshotActive;
    private readonly Button manageCapabilityButton = new() { Content = "Review selected server capabilities" };

    internal void SetCapabilityMetadata(IReadOnlyList<McpDisplayedServer> servers, Action manage)
    {
        if (capabilityPrivacyClosed) { return; }
        capabilitySnapshotActive = servers.Count > 0;
        manageCapability = manage;
        capabilityRows.SetSnapshot(servers.SelectMany(server => server.Rows));
        RenderCapabilityMetadata();
    }

    private void BindCapabilityMetadata(Action<string> run, Panel browseTop, Panel root)
    {
        AutomationProperties.SetName(CapabilityCategory, "MCP capability category; filtering does not connect or discover");
        AutomationProperties.SetName(CapabilityCount, "Matching admitted MCP capability count");
        CapabilityCategory.IsVisible = false;
        CapabilityCategory.SelectionChanged += (_, _) => run("Filter");
        manageCapabilityButton.Click += (_, _) => manageCapability?.Invoke();
        browseTop.Children.Add(CapabilityCategory);
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(CapabilityCount);
        panel.Children.Add(capabilityCatalogue);
        root.Children.Add(panel);
    }

    private void RenderCapabilityMetadata()
    {
        if (capabilityPrivacyClosed) { return; }
        var visibleCategory = Kind == CustomizationKind.McpServer && capabilitySnapshotActive;
        CapabilityCategory.IsVisible = visibleCategory;
        capabilityCatalogue.IsVisible = visibleCategory;
        CapabilityCount.IsVisible = visibleCategory;
        if (!visibleCategory)
        {
            capabilityCatalogue.Children.Clear();
            CapabilityCount.Text = string.Empty;
            return;
        }
        capabilityRows.ApplyFilter(Search.Text, SelectedCapabilityCategory);
        capabilityCatalogue.Children.Clear();
        foreach (var group in capabilityRows.Visible.GroupBy(row => row.ServerId, StringComparer.Ordinal))
        {
            var rows = new StackPanel { Spacing = 4 };
            foreach (var row in group)
            {
                var button = new Button
                {
                    Content = row.Capability.Kind + ": " + row.Label,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                };
                ToolTip.SetTip(button, row.SourceLabel + "\n" + row.Capability.Id
                    + (row.AvailabilityReason is { } reason ? "\n" + reason : string.Empty));
                button.Click += (_, _) =>
                {
                    capabilityRows.Select(row);
                    manageCapabilityButton.IsEnabled = true;
                    CapabilityCount.Text = capabilityRows.CountText + "\nSelected: " + row.Capability.Name
                        + ". Saved choice " + (row.IsSelected ? "enabled" : "disabled")
                        + "; currently " + (row.IsEffectiveConsideration ? "considered, not approved for execution." : "unavailable.");
                };
                rows.Children.Add(button);
            }
            capabilityCatalogue.Children.Add(new Expander
            {
                Header = group.First().ServerName + " (" + group.Count() + " capabilities)",
                IsExpanded = true,
                Content = rows,
            });
        }
        if (capabilityRows.Visible.IsEmpty)
        {
            capabilityCatalogue.Children.Add(new TextBlock
            {
                Text = capabilityRows.Total == 0 ? "No admitted capability metadata. External discovery must be reviewed separately in Sessions."
                    : "No matching capabilities",
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            });
        }
        manageCapabilityButton.IsEnabled = SelectedCapability is not null;
        capabilityCatalogue.Children.Add(manageCapabilityButton);
        CapabilityCount.Text = capabilityRows.CountText;
    }

    private void ClearCapabilityMetadata()
    {
        capabilityPrivacyClosed = true;
        capabilitySnapshotActive = false;
        capabilityRows.ClearPrivate();
        capabilityCatalogue.Children.Clear();
        CapabilityCount.Text = string.Empty;
        manageCapability = null;
        CapabilityCategory.SelectedIndex = 0;
    }
}