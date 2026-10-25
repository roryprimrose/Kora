using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

using Kora.Application.ViewModels;
using Kora.Core.Customizations;

namespace Kora;

internal sealed class CustomizationWindow : Window
{
    private static readonly (CustomizationKind Kind, string Label)[] kindOptions =
    [
        (CustomizationKind.Instruction, "Instructions"),
        (CustomizationKind.Skill, "Skills"),
        (CustomizationKind.Agent, "Agents"),
        (CustomizationKind.McpServer, "MCP Servers"),
        (CustomizationKind.Prompt, "Prompts"),
    ];
    internal readonly ListBox Kinds = new() { ItemsSource = kindOptions.Select(option => option.Label).ToArray(), SelectedIndex = 0 };
    internal readonly TextBox Search = new() { PlaceholderText = "Search names or sources" };
    internal readonly ComboBox Destination = new() { ItemsSource = new[] { CustomizationScope.Kora, CustomizationScope.User }, SelectedIndex = 0, MinWidth = 100 };
    internal readonly TextBox NewName = new() { PlaceholderText = "new-artifact-id", MinWidth = 150 };
    internal readonly ListBox Files = new() { MinHeight = 64, MaxHeight = 160 };
    internal readonly TextBox FileName = new() { PlaceholderText = @"references\notes.md", MinWidth = 180 };
    internal readonly TextBox Editor = new()
    {
        AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, FontFamily = new("Consolas"),
        HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch,
    };
    internal readonly TextBox Request = new() { PlaceholderText = "Describe or discuss a change with Kora...", AcceptsReturn = true, MinHeight = 64, MaxHeight = 120 };
    internal readonly TextBlock Status = new() { TextWrapping = TextWrapping.Wrap };
    internal readonly TextBlock Details = new() { TextWrapping = TextWrapping.Wrap };
    internal readonly TextBlock VoiceContext = new() { TextWrapping = TextWrapping.Wrap };
    internal readonly Button PushToTalkButton = new() { Content = "Hold to speak", Name = "EditorPushToTalkButton" };
    internal event Action? PresentationLost;
    private readonly PushToTalkButtonBinding pushToTalk;
    private readonly StackPanel catalogue = new() { Spacing = 8 };
    private readonly Dictionary<string, Button> actions = new(StringComparer.Ordinal);
    private Window? review;
    private bool openingReview;
    internal CustomizationSource? SelectedSource { get; private set; }
    internal CustomizationKind Kind
    {
        get => kindOptions[Math.Max(0, Kinds.SelectedIndex)].Kind;
        set => Kinds.SelectedIndex = Array.FindIndex(kindOptions, option => option.Kind == value);
    }
    internal CustomizationScope Scope => Destination.SelectedItem is CustomizationScope scope ? scope : CustomizationScope.Kora;

    internal CustomizationWindow(Action<string> run, MainViewModel main)
    {
        Title = "Kora Customizations";
        Width = 1240; Height = 840; MinWidth = 900; MinHeight = 600;
        var root = new Grid { ColumnDefinitions = new("150,320,*"), Margin = new(16), ColumnSpacing = 16 };
        root.Children.Add(Kinds);
        Kinds.SelectionChanged += (_, _) => run("Filter");
        var browse = new DockPanel();
        var browseTop = new StackPanel { Spacing = 8 };
        browseTop.Children.Add(new TextBlock { Text = "Customizations", FontSize = 22, FontWeight = FontWeight.SemiBold });
        browseTop.Children.Add(Search);
        var add = new WrapPanel();
        add.Children.Add(Destination); add.Children.Add(NewName);
        AddButton(add, "Add", "Create", run);
        AddButton(add, "Refresh", "Refresh", run);
        browseTop.Children.Add(add);
        DockPanel.SetDock(browseTop, Dock.Top); browse.Children.Add(browseTop);
        browse.Children.Add(new ScrollViewer { Content = catalogue });
        Grid.SetColumn(browse, 1); root.Children.Add(browse);

        var detail = new DockPanel();
        var header = new StackPanel { Spacing = 8 };
        header.Children.Add(Details);
        header.Children.Add(VoiceContext);
        header.Children.Add(PushToTalkButton);
        pushToTalk = new(this, PushToTalkButton, main);
        pushToTalk.SetAvailable(false);
        AutomationProperties.SetName(PushToTalkButton, "Speak to the selected artifact; hold while speaking");
        ToolTip.SetTip(PushToTalkButton, "Hold to speak to this artifact. Release to review local interpretation; nothing is saved or enabled.");
        var sourceActions = new WrapPanel();
        foreach (var action in new[] { "Review and save", "Reload", "Create Kora copy", "Disable", "Enable", "Review recovery" })
        { AddButton(sourceActions, action, action, run); }
        header.Children.Add(sourceActions); header.Children.Add(Files);
        var components = new WrapPanel();
        components.Children.Add(FileName);
        foreach (var action in new[] { "Add file", "Rename file", "Remove file" }) { AddButton(components, action, action, run); }
        header.Children.Add(components);
        DockPanel.SetDock(header, Dock.Top); detail.Children.Add(header);
        var bottom = new StackPanel { Spacing = 8, Margin = new(0, 8, 0, 0) };
        bottom.Children.Add(Request);
        var authoring = new WrapPanel();
        AddButton(authoring, "Ask Kora (review local disclosure)", "Refine", run);
        bottom.Children.Add(authoring); bottom.Children.Add(Status);
        DockPanel.SetDock(bottom, Dock.Bottom); detail.Children.Add(bottom);
        detail.Children.Add(Editor);
        Grid.SetColumn(detail, 2); root.Children.Add(detail);
        Content = root;
        Search.TextChanged += (_, _) => run("Filter");
        AutomationProperties.SetName(Kinds, "Customization type");
        AutomationProperties.SetName(Search, "Search customization names and sources; no content reads");
        AutomationProperties.SetName(Files, "Selected artifact components");
        AutomationProperties.SetName(Editor, "Selected artifact source; untrusted definition content");
        AutomationProperties.SetName(Request, "Definition authoring request");
        AutomationProperties.SetName(NewName, "New artifact ID");
        AutomationProperties.SetName(Destination, "New artifact destination");
        SetSource(null, false);
        Deactivated += (_, _) => { if (!openingReview && review?.IsActive != true) { PresentationLost?.Invoke(); } };
        PropertyChanged += (_, change) =>
        { if (change.Property == IsVisibleProperty && !IsVisible) { PresentationLost?.Invoke(); } };
    }

    internal void SetVoiceCaptureAvailable(bool value) => pushToTalk.SetAvailable(value);

    private void AddButton(Panel panel, string caption, string action, Action<string> run)
    {
        var button = new Button { Content = caption, Margin = new(2) };
        actions.Add(action, button);
        button.Click += (_, _) => run(action);
        panel.Children.Add(button);
    }

    internal void SetSource(CustomizationSource? source, bool recovery)
    {
        var writable = source is { IsReadOnly: false };
        var components = writable && source?.Kind == CustomizationKind.Skill && !recovery;
        Editor.IsReadOnly = !writable || recovery;
        actions["Review and save"].IsEnabled = writable;
        actions["Reload"].IsEnabled = writable;
        actions["Create Kora copy"].IsEnabled = source is not null && !recovery;
        actions["Disable"].IsEnabled = source is not null;
        actions["Enable"].IsEnabled = source is not null && !recovery;
        actions["Review recovery"].IsEnabled = writable;
        actions["Add file"].IsEnabled = components;
        actions["Rename file"].IsEnabled = components && !string.Equals(Files.SelectedItem as string, source?.PrimaryFile, StringComparison.Ordinal);
        actions["Remove file"].IsEnabled = actions["Rename file"].IsEnabled;
        actions["Refine"].IsEnabled = writable && !recovery;
        FileName.IsEnabled = components;
        Request.IsEnabled = writable && !recovery;
    }

    internal void ShowSources(IReadOnlyList<CustomizationSource> sources, Func<CustomizationSource, bool> disabled, Action<CustomizationSource> select)
    {
        catalogue.Children.Clear();
        foreach (var scope in Enum.GetValues<CustomizationScope>())
        {
            var matches = sources.Where(source => source.Kind == Kind && source.Scope == scope
                && (source.Name.Contains(Search.Text ?? string.Empty, StringComparison.OrdinalIgnoreCase)
                    || source.Location.Contains(Search.Text ?? string.Empty, StringComparison.OrdinalIgnoreCase))).ToArray();
            var rows = new StackPanel { Spacing = 4 };
            foreach (var source in matches)
            {
                var label = new StackPanel();
                label.Children.Add(new TextBlock { Text = source.Name + (disabled(source) ? " (Disabled)" : string.Empty), TextWrapping = TextWrapping.Wrap });
                label.Children.Add(new TextBlock { Text = source.Location, FontSize = 11, Opacity = 0.7, TextWrapping = TextWrapping.Wrap });
                var button = new Button { Content = label, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left };
                button.Click += (_, _) => { SelectedSource = source; select(source); };
                rows.Children.Add(button);
            }
            if (matches.Length == 0) { rows.Children.Add(new TextBlock { Text = "No matching artifacts", Opacity = 0.6 }); }
            catalogue.Children.Add(new Expander
            {
                Header = (scope == CustomizationScope.BuiltIn ? "Built-in" : scope.ToString()) + " (" + matches.Length + ")",
                IsExpanded = true, Content = rows, HorizontalAlignment = HorizontalAlignment.Stretch,
            });
        }
    }

    internal async Task<string> Review(string title, string text, CancellationToken token, params string[] choices)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        { return await Dispatcher.UIThread.InvokeAsync(() => Review(title, text, token, choices)); }
        token.ThrowIfCancellationRequested();
        CancelReview();
        var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var opened = new Window { Title = title, Width = 860, Height = 660 };
        var closingReview = false;
        opened.Closing += (_, _) => closingReview = true;
        opened.Deactivated += (_, _) => { if (!closingReview && !IsActive) { PresentationLost?.Invoke(); } };
        opened.PropertyChanged += (_, change) =>
        { if (change.Property == IsVisibleProperty && !opened.IsVisible && !closingReview) { PresentationLost?.Invoke(); } };
        var panel = new DockPanel { Margin = new(16) };
        var buttons = new WrapPanel();
        foreach (var choice in choices)
        {
            var button = new Button { Content = choice, Margin = new(4) };
            button.Click += (_, _) => { completion.TrySetResult(choice); opened.Close(); };
            buttons.Children.Add(button);
        }
        DockPanel.SetDock(buttons, Dock.Bottom); panel.Children.Add(buttons);
        var body = new TextBox { Text = text, IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap };
        panel.Children.Add(body);
        opened.Content = panel;
        opened.Closed += (_, _) =>
        {
            body.Text = string.Empty;
            if (ReferenceEquals(review, opened)) { review = null; }
            completion.TrySetResult("Cancel");
        };
        review = opened;
        using var registration = token.Register(() => Dispatcher.UIThread.Post(() => opened.Close()));
        openingReview = true;
        try { opened.Show(this); opened.Activate(); }
        finally { openingReview = false; }
        return await completion.Task;
    }

    internal void CancelReview() { review?.Close(); review = null; }

    internal void ClearPrivateText()
    {
        CancelReview();
        Editor.Text = string.Empty; Request.Text = string.Empty; NewName.Text = string.Empty;
        FileName.Text = string.Empty; Search.Text = string.Empty;
        Details.Text = string.Empty; Status.Text = string.Empty; VoiceContext.Text = string.Empty;
        Files.ItemsSource = null; catalogue.Children.Clear(); SelectedSource = null;
    }
}
