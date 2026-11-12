using Avalonia;
using Avalonia.Automation;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Metadata;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Markup.Xaml.MarkupExtensions;

namespace Kora.Controls;

public sealed class SettingsBrowser : UserControl, IDisposable
{
    private readonly TextBox search = new() { PlaceholderText = "Search settings by ID, title, description or value" };
    private readonly TreeView navigation = new();
    private readonly StackPanel pages = new() { Spacing = 16 };
    private readonly TextBlock resultStatus = new() { TextWrapping = TextWrapping.Wrap };
    private readonly ScrollViewer results;
    private readonly List<Entry> entries = [];
    private readonly Dictionary<string, Entry> settingsById = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Entry> settingsByAlias = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<TreeViewItem, Scope> scopes = [];
    private readonly List<(Control Control, Scope Scope)> context = [];
    private readonly List<TextBlock> observedText = [];
    private bool initialized;
    private bool disposed;
    private bool filtering;

    public SettingsBrowser()
    {
        Styles.Add(new Style(selector => selector.OfType<TreeViewItem>())
        {
            Setters = { new Setter(ForegroundProperty, new DynamicResourceExtension("KoraMutedForegroundBrush")) },
        });
        Styles.Add(new Style(selector => selector.OfType<TreeViewItem>().Class(":selected"))
        {
            Setters = { new Setter(ForegroundProperty, new DynamicResourceExtension("KoraForegroundBrush")) },
        });
        AutomationProperties.SetName(search, "Search settings");
        AutomationProperties.SetName(navigation, "Settings sections");
        AutomationProperties.SetName(resultStatus, "Settings search results");
        search.Name = "SettingsFilter";
        navigation.Name = "SettingsSections";
        results = new ScrollViewer
        {
            Content = pages,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
        };
        var body = new Grid { ColumnDefinitions = new("210,*"), ColumnSpacing = 16 };
        body.Children.Add(navigation);
        Grid.SetColumn(results, 1);
        body.Children.Add(results);
        var layout = new Grid { RowDefinitions = new("Auto,Auto,*"), RowSpacing = 12 };
        layout.Children.Add(search);
        Grid.SetRow(resultStatus, 1);
        layout.Children.Add(resultStatus);
        Grid.SetRow(body, 2);
        layout.Children.Add(body);
        Content = layout;
        search.TextChanged += OnSearchChanged;
        navigation.SelectionChanged += OnSelectionChanged;
    }

    [Content]
    public AvaloniaList<SettingsPage> Items { get; } = [];

    public event EventHandler? SelectionChanged;
    public event EventHandler? ResultsChanged;

    public SettingsPage? SelectedItem
    {
        get => SelectedScope.Page;
        set
        {
            Initialize();
            navigation.SelectedItem = scopes.Single(pair =>
                ReferenceEquals(pair.Value.Page, value) && pair.Value.Group.Length == 0).Key;
        }
    }

    public int SelectedIndex
    {
        get => SelectedItem is { } page ? Items.IndexOf(page) : -1;
        set => SelectedItem = value == -1 ? null : Items[value];
    }

    public string Filter
    {
        get => search.Text ?? string.Empty;
        set => search.Text = value;
    }

    public int MatchCount { get; private set; }
    internal TextBox SearchField => search;
    internal TreeView SectionTree => navigation;
    internal IReadOnlyCollection<string> SettingIds => settingsById.Keys;
    internal string? ExactSettingId
    {
        get
        {
            var query = Filter.Trim();
            var entry = FindSetting(query);
            return entry?.Ids.FirstOrDefault(candidate => string.Equals(candidate, query, StringComparison.OrdinalIgnoreCase))
                ?? (entry?.Ids is [var singleId] ? singleId : null);
        }
    }
    public bool IsSettingVisible(string id) => FindSetting(id) is { } entry
        && entry.Control.IsVisible && entry.Page.IsVisible;
    private Scope SelectedScope => navigation.SelectedItem is TreeViewItem item && scopes.TryGetValue(item, out var scope)
        ? scope : new(null, string.Empty);

    public void Initialize()
    {
        if (initialized) { return; }
        initialized = true;
        var all = AddNode(navigation.Items, "All settings", new(null, string.Empty));
        all.IsExpanded = true;
        foreach (var page in Items)
        {
            pages.Children.Add(page);
            var node = AddNode(all.Items, page.Header, new(page, string.Empty));
            var groups = new HashSet<string>(StringComparer.Ordinal);
            var controls = page.GetLogicalDescendants().OfType<Control>().ToArray();
            foreach (var control in controls)
            {
                var group = SettingsSearch.GetGroup(control);
                var id = SettingsSearch.GetId(control);
                if (group.Length > 0 && groups.Add(group))
                { AddNode(node.Items, group, new(page, group)); }
                if (id.Length == 0)
                {
                    if (group.Length > 0) { context.Add((control, new(page, group))); }
                    continue;
                }
                if (control.GetLogicalAncestors().OfType<Control>().TakeWhile(parent => !ReferenceEquals(parent, page))
                    .Any(parent => SettingsSearch.GetId(parent).Length > 0))
                { continue; }
                var labels = control.GetLogicalDescendants().OfType<TextBlock>().ToArray();
                var text = labels
                    .Where(block => block.Classes.Contains("settings-title") || block.Classes.Contains("settings-description")
                        || block.FontSize >= 18)
                    .ToArray();
                if (text.Length == 0) { text = labels.Take(2).ToArray(); }
                if (control is TextBlock block) { text = [block]; }
                var entry = new Entry(control, page, group, SplitIds(id), SplitIds(SettingsSearch.GetAliases(control)), text);
                entries.Add(entry);
                foreach (var settingId in entry.Ids)
                {
                    if (!settingsById.TryAdd(settingId, entry))
                    { throw new InvalidOperationException("Settings contains a duplicate individual ID: " + settingId); }
                }
                foreach (var alias in entry.Aliases)
                {
                    if (!settingsByAlias.TryAdd(alias, entry))
                    { throw new InvalidOperationException("Settings contains a duplicate compatibility alias: " + alias); }
                }
                control.PropertyChanged += OnEntryChanged;
                foreach (var label in text)
                {
                    label.PropertyChanged += OnLabelChanged;
                    observedText.Add(label);
                }
            }
        }
        if (settingsById.Keys.Any(settingsByAlias.ContainsKey))
        { throw new InvalidOperationException("A settings compatibility alias collides with an individual ID."); }
        navigation.SelectedItem = all;
        ApplyFilter();
    }

    internal void BindSettingIdCopy(Func<string, Task> copy, Action<Exception> reportFailure)
    {
        foreach (var entry in entries)
        {
            foreach (var button in entry.Control.GetLogicalDescendants().OfType<SettingActionsButton>())
            {
                if (!button.CanCopySettingId) { continue; }
                var ids = string.IsNullOrWhiteSpace(button.SettingIds) ? entry.Ids : SplitIds(button.SettingIds);
                if (ids.Any(id => !entry.Ids.Contains(id, StringComparer.Ordinal)))
                { throw new InvalidOperationException("A settings cog references an undefined individual ID."); }
                button.BindSettingIdCopy(ids, copy, reportFailure);
            }
        }
    }

    public void ShowFilter(string query)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        SelectedItem = null;
        Filter = query;
        ApplyFilter();
        results.Offset = default;
        search.Focus();
    }

    public void ShowGroup(SettingsPage page, string group)
    {
        Initialize();
        Filter = string.Empty;
        scopes.Single(pair => ReferenceEquals(pair.Value.Page, page) && pair.Value.Group.Length == 0).Key.IsExpanded = true;
        navigation.SelectedItem = scopes.Single(pair =>
            ReferenceEquals(pair.Value.Page, page) && string.Equals(pair.Value.Group, group, StringComparison.Ordinal)).Key;
    }

    public void Dispose()
    {
        if (disposed) { return; }
        disposed = true;
        search.TextChanged -= OnSearchChanged;
        navigation.SelectionChanged -= OnSelectionChanged;
        foreach (var entry in entries) { entry.Control.PropertyChanged -= OnEntryChanged; }
        foreach (var label in observedText) { label.PropertyChanged -= OnLabelChanged; }
    }

    private TreeViewItem AddNode(ItemCollection items, string title, Scope scope)
    {
        var node = new TreeViewItem { Header = title };
        AutomationProperties.SetName(node, title);
        scopes.Add(node, scope);
        items.Add(node);
        return node;
    }

    private void OnSearchChanged(object? sender, TextChangedEventArgs args) => ApplyFilter();
    private void OnEntryChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.Property == SettingsSearch.ValueProperty) { ApplyFilter(); }
    }
    private void OnLabelChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.Property == TextBlock.TextProperty) { ApplyFilter(); }
    }
    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs args)
    {
        if (!ReferenceEquals(args.Source, navigation)) { return; }
        ApplyFilter();
        results.Offset = default;
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ApplyFilter()
    {
        if (!initialized || disposed || filtering) { return; }
        filtering = true;
        try
        {
            var query = Filter.Trim();
            var exact = FindSetting(query);
            if (exact is null && query.Length > 0)
            {
                var named = entries.Where(entry => entry.Text.Any(block => block.Classes.Contains("settings-title")
                    && string.Equals(block.Text, query, StringComparison.OrdinalIgnoreCase)))
                    .Take(2).ToArray();
                if (named.Length == 1) { exact = named[0]; }
            }
            if (exact is not null && SelectedScope.Page is not null)
            { navigation.SelectedItem = scopes.Single(pair => pair.Value.Page is null).Key; }
            var scope = SelectedScope;
            var terms = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            var matchingScopes = new HashSet<Scope>();
            foreach (var entry in entries)
            {
                var text = string.Join(" ", entry.Ids) + " " + string.Join(" ", entry.Aliases) + " "
                    + entry.Page.Header + " " + entry.Group + " "
                    + string.Join(" ", entry.Text.Select(block => block.Text)) + " " + SettingsSearch.GetValue(entry.Control);
                var matches = exact is not null ? ReferenceEquals(entry, exact)
                    : terms.All(term => text.Contains(term, StringComparison.OrdinalIgnoreCase));
                if (matches)
                {
                    matchingScopes.Add(new(entry.Page, string.Empty));
                    matchingScopes.Add(new(entry.Page, entry.Group));
                }
                entry.Control.IsVisible = matches && (scope.Page is null || ReferenceEquals(scope.Page, entry.Page))
                    && (scope.Group.Length == 0 || string.Equals(scope.Group, entry.Group, StringComparison.Ordinal));
            }
            foreach (var (node, category) in scopes)
            { node.IsVisible = category.Page is null || matchingScopes.Contains(category); }
            foreach (var page in Items)
            { page.IsVisible = entries.Any(entry => ReferenceEquals(entry.Page, page) && entry.Control.IsVisible); }
            foreach (var item in context)
            {
                item.Control.IsVisible = entries.Any(entry => ReferenceEquals(entry.Page, item.Scope.Page)
                    && string.Equals(entry.Group, item.Scope.Group, StringComparison.Ordinal) && entry.Control.IsVisible);
            }
            MatchCount = entries.Count(entry => entry.Control.IsVisible);
            resultStatus.Text = MatchCount == 0
                ? "No settings match. Clear the search or choose All settings."
                : $"{MatchCount} settings shown";
            ResultsChanged?.Invoke(this, EventArgs.Empty);
        }
        finally { filtering = false; }
    }

    private Entry? FindSetting(string id) => settingsById.GetValueOrDefault(id) ?? settingsByAlias.GetValueOrDefault(id);

    private static string[] SplitIds(string ids) => ids.Split(' ', StringSplitOptions.RemoveEmptyEntries);

    private sealed record Scope(SettingsPage? Page, string Group);
    private sealed record Entry(Control Control, SettingsPage Page, string Group, string[] Ids, string[] Aliases, TextBlock[] Text);
}
