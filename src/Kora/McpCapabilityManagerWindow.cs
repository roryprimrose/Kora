using System.ComponentModel;

using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;

using Kora.Application.Mcp;
using Kora.Core.Mcp;

namespace Kora;

internal sealed class McpCapabilityManagerWindow : Window
{
    private readonly McpCapabilityManagerView inventory;
    private readonly CheckBox server = new() { Content = "Server enabled in Kora", IsChecked = true };
    private readonly StackPanel editor = new() { Spacing = 12 };
    private readonly StackPanel review = new() { Spacing = 12, IsVisible = false };
    private readonly Dictionary<McpCapability, CheckBox> items = [];
    private readonly Dictionary<McpCapabilityCategory, TextBlock> summaries = [];
    private readonly Dictionary<McpCapabilityCategory, StackPanel> categoryPanels = [];
    private readonly McpCapabilityRows displayed = new();
    internal readonly TextBlock CapabilityFilterSummary = new();
    private McpCapabilitySelection selection;
    private bool updating;

    private McpCapabilityManagerWindow(McpCapabilityManagerView inventory)
    {
        this.inventory = inventory;
        selection = inventory.Selection;
        server.IsChecked = inventory.ServerIncluded;
        Title = "MCP capabilities - " + inventory.ServerName;
        Width = 860;
        Height = 680;
        editor.Children.Add(new TextBlock
        {
            Text = "These are consideration preferences, not connection, content, model or execution approval."
                + " Server disablement preserves category/item choices. Category-wide actions replace individual exceptions;"
                + " newly discovered items inherit that category's default. Nothing is written before separate Save confirmation.",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
        });
        editor.Children.Add(server);
        server.IsCheckedChanged += (_, _) => { if (!updating) { RefreshSelections(); } };
        editor.Children.Add(CapabilityFilterSummary);
        foreach (var category in Enum.GetValues<McpCapabilityCategory>()) { AddCategory(category); }
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var prepare = new Button { Content = "Review changes" };
        prepare.Click += (_, _) => ReviewChanges();
        var cancel = new Button { Content = "Cancel" };
        cancel.Click += (_, _) => Close();
        actions.Children.Add(prepare);
        actions.Children.Add(cancel);
        editor.Children.Add(actions);
        var root = new StackPanel { Spacing = 12, Margin = new Thickness(16) };
        root.Children.Add(editor);
        root.Children.Add(review);
        Content = new ScrollViewer { Content = root };
        RefreshSelections();
        Closed += (_, _) => displayed.ClearPrivate();
    }

    private void AddCategory(McpCapabilityCategory category)
    {
        var summary = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        var panel = new StackPanel { Spacing = 8 };
        categoryPanels.Add(category, panel);
        editor.Children.Add(panel);
        summaries.Add(category, summary);
        panel.Children.Add(summary);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        foreach (var enabled in new[] { true, false })
        {
            var button = new Button { Content = (enabled ? "Enable all " : "Disable all ") + category };
            button.Click += (_, _) =>
            {
                selection = selection.SetCategory(inventory.ServerId, category, enabled);
                RefreshSelections();
            };
            actions.Children.Add(button);
        }
        panel.Children.Add(actions);
        foreach (var capability in inventory.Items.Where(item => item.Category == category))
        {
            var check = new CheckBox
            {
                Content = capability.Kind + ": " + capability.Name
                    + (capability.UnavailableReason is { } reason ? " - Unavailable: " + reason : string.Empty),
            };
            AutomationProperties.SetName(check, capability.Kind + " " + capability.Name + " considered by Kora");
            check.IsCheckedChanged += (_, _) =>
            {
                if (updating) { return; }
                selection = selection.SetItem(inventory.ServerId, capability.Kind, capability.Id, check.IsChecked == true);
                RefreshSelections();
            };
            items.Add(capability, check);
            panel.Children.Add(check);
        }
    }

    private void RefreshSelections()
    {
        updating = true;
        try
        {
            foreach (var (capability, check) in items)
            { check.IsChecked = selection.IsEnabled(inventory.ServerId, capability.Kind, capability.Id); }
            foreach (var (category, summary) in summaries)
            {
                var known = inventory.Items.Where(item => item.Category == category).ToArray();
                var enabled = known.Count(item => selection.IsEnabled(inventory.ServerId, item.Kind, item.Id));
                var saved = selection.Categories.SingleOrDefault(item =>
                    string.Equals(item.ServerId, inventory.ServerId, StringComparison.Ordinal) && item.Category == category);
                var provided = category switch
                {
                    McpCapabilityCategory.Tools => inventory.Capabilities.Tools,
                    McpCapabilityCategory.Resources => inventory.Capabilities.Resources,
                    McpCapabilityCategory.Prompts => inventory.Capabilities.Prompts,
                    _ => throw new InvalidOperationException("The saved MCP category is unsupported."),
                };
                summary.Text = category + ": " + (known.Length == 0 ? provided ? "no items in this discovery" : "not provided by this server"
                    : enabled == 0 ? "all disabled" : enabled == known.Length ? "all enabled" : "mixed")
                    + ". Newly discovered items default " + (saved?.Enabled != false ? "enabled." : "disabled.");
            }
        }
        finally { updating = false; }
        displayed.SetSnapshot(inventory.Items.Select(capability =>
        {
            var selected = selection.IsEnabled(inventory.ServerId, capability.Kind, capability.Id);
            return new McpCapabilityRow(inventory.ServerId, inventory.ServerName, inventory.ServerName, "MCP",
                capability, selected, selected && server.IsChecked == true && capability.UnavailableReason is null,
                capability.UnavailableReason);
        }));
        ApplyCapabilityVisibility();
    }

    internal void FilterCapabilities(string? filter, McpCapabilityCategory? category)
    {
        displayed.ApplyFilter(filter, category);
        ApplyCapabilityVisibility();
    }

    internal void ClearCapabilityFilter()
    {
        displayed.ClearFilter();
        ApplyCapabilityVisibility();
    }

    private void ApplyCapabilityVisibility()
    {
        var visible = displayed.Visible.Select(row => row.Capability).ToHashSet();
        foreach (var (capability, check) in items) { check.IsVisible = visible.Contains(capability); }
        foreach (var (category, panel) in categoryPanels)
        { panel.IsVisible = displayed.Category is null || displayed.Category == category; }
        CapabilityFilterSummary.Text = displayed.CountText;
    }

    private void ReviewChanges()
    {
        var text = "Server: " + inventory.ServerName + "\nServer enabled: " + inventory.ServerIncluded + " -> " + (server.IsChecked == true)
            + "\n\nCategory defaults and prior item exceptions:\n";
        foreach (var category in Enum.GetValues<McpCapabilityCategory>())
        {
            var before = inventory.Selection.Categories.SingleOrDefault(item =>
                string.Equals(item.ServerId, inventory.ServerId, StringComparison.Ordinal) && item.Category == category);
            var after = selection.Categories.SingleOrDefault(item =>
                string.Equals(item.ServerId, inventory.ServerId, StringComparison.Ordinal) && item.Category == category);
            text += category + ": default " + (before?.Enabled != false) + " -> " + (after?.Enabled != false)
                + "; saved individual choices " + (before?.Items.Length ?? 0) + " -> " + (after?.Items.Length ?? 0) + "\n";
        }
        text += "\nComplete discovered item selections:\n" + string.Join("\n", inventory.Items.Select(item =>
            item.Kind + ": " + item.Name + "\n" + item.Id + "\n"
            + inventory.Selection.IsEnabled(inventory.ServerId, item.Kind, item.Id) + " -> "
            + selection.IsEnabled(inventory.ServerId, item.Kind, item.Id)));
        text += "\n\nSaving changes only Kora's device-local preferences. No external configuration, source content,"
            + " account, permission, content retrieval or model use is approved. Pending operations must be reviewed again.";
        review.Children.Clear();
        review.Children.Add(new TextBox
        {
            Text = text,
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            IsUndoEnabled = false,
            MinHeight = 350,
        });
        var confirm = new Button { Content = "Confirm Save of these preferences" };
        confirm.Click += (_, _) => Close(new McpCapabilityChange(server.IsChecked == true, selection));
        var back = new Button { Content = "Back to editing" };
        back.Click += (_, _) => { review.IsVisible = false; editor.IsVisible = true; };
        var cancel = new Button { Content = "Cancel without saving" };
        cancel.Click += (_, _) => Close();
        review.Children.Add(confirm);
        review.Children.Add(back);
        review.Children.Add(cancel);
        editor.IsVisible = false;
        review.IsVisible = true;
    }

    internal static Task<McpCapabilityChange?> ShowAsync(Window owner, INotifyPropertyChanged model,
        McpCapabilityInventory inventory, Func<bool> admitted, CancellationToken token)
        => ShowAsync(owner, model, McpCapabilityManagerView.FromInventory(inventory), admitted, token);

    internal static async Task<McpCapabilityChange?> ShowAsync(Window owner, INotifyPropertyChanged model,
        McpCapabilityManagerView inventory, Func<bool> admitted, CancellationToken token)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        { return await Dispatcher.UIThread.InvokeAsync(() => ShowAsync(owner, model, inventory, admitted, token)); }
        token.ThrowIfCancellationRequested();
        if (!owner.IsVisible || !admitted()) { throw new OperationCanceledException("MCP capability presentation is unavailable.", token); }
        var window = new McpCapabilityManagerWindow(inventory);
        void Check()
        {
            if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(Check); return; }
            if (!owner.IsVisible || !admitted() || token.IsCancellationRequested) { window.Close(); }
        }
        void ModelChanged(object? sender, PropertyChangedEventArgs args) => Check();
        void OwnerChanged(object? sender, AvaloniaPropertyChangedEventArgs args) => Check();
        void OwnerClosed(object? sender, EventArgs args) => window.Close();
        model.PropertyChanged += ModelChanged;
        owner.PropertyChanged += OwnerChanged;
        owner.Closed += OwnerClosed;
        using var cancellation = token.Register(() => Dispatcher.UIThread.Post(() => window.Close()));
        try
        {
            var change = await window.ShowDialog<McpCapabilityChange?>(owner);
            token.ThrowIfCancellationRequested();
            return admitted() && owner.IsVisible ? change : null;
        }
        finally
        {
            model.PropertyChanged -= ModelChanged;
            owner.PropertyChanged -= OwnerChanged;
            owner.Closed -= OwnerClosed;
            window.Content = null;
        }
    }
}