using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

using Kora.Application.Definitions;
using Kora.Core.Artifacts;

namespace Kora;

internal sealed class OwnedInstructionEditorWindow : Window
{
    internal readonly ComboBox Files = new() { Width = 240 };
    internal readonly TextBox QueryInput = new() { Width = 240, PlaceholderText = "Find filename, name or exact ID" };
    internal readonly ComboBox UseStateInput = new()
    {
        Width = 180, ItemsSource = Enum.GetValues<OwnedDefinitionUseFilter>(), SelectedItem = OwnedDefinitionUseFilter.All,
    };
    internal readonly Button NextPage = new() { Content = "Next", IsEnabled = false };
    private readonly TextBlock inventoryStatus = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock inventoryDetails = new() { TextWrapping = TextWrapping.Wrap };
    internal readonly ComboBox KindInput = new()
    {
        Width = 140, ItemsSource = new[] { ArtifactKind.Instruction, ArtifactKind.Prompt },
        SelectedItem = ArtifactKind.Instruction,
    };
    internal readonly TextBox NameInput = new() { Width = 240, PlaceholderText = "lowercase-owned-id" };
    internal readonly TextBox CopyNameInput = new() { Width = 240, PlaceholderText = "new-copy-owned-id" };
    internal readonly TextBox Source = new()
    {
        AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, FontFamily = new("Consolas"),
        HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch,
    };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap };
    private Window? activeReview;
    private bool selectingKind;

    internal OwnedInstructionEditorWindow(Action<string> run)
    {
        Title = "Kora-owned instruction and prompt editor";
        Width = 960;
        Height = 720;
        var controls = new WrapPanel { Orientation = Orientation.Horizontal };
        controls.Children.Add(KindInput);
        KindInput.SelectionChanged += (_, _) => { if (!selectingKind) { run("Kind"); } };
        controls.Children.Add(Files);
        controls.Children.Add(NameInput);
        controls.Children.Add(CopyNameInput);
        foreach (var action in new[] { "List", "Refresh", "Create", "Inspect", "Save", "Save as new copy", "Reload", "Delete", "Review use", "Disable reviewed use" })
        {
            var button = new Button { Content = action, Margin = new(3) };
            button.Click += (_, _) => run(action);
            controls.Children.Add(button);
        }
        var panel = new DockPanel { Margin = new(12) };
        DockPanel.SetDock(controls, Dock.Top);
        panel.Children.Add(controls);
        var search = new WrapPanel { Orientation = Orientation.Horizontal };
        search.Children.Add(new TextBlock { Text = "Owned metadata search: ", VerticalAlignment = VerticalAlignment.Center });
        search.Children.Add(QueryInput);
        search.Children.Add(UseStateInput);
        search.Children.Add(NextPage);
        var clear = new Button { Content = "Clear search", Margin = new(3) };
        clear.Click += (_, _) => { QueryInput.Text = string.Empty; UseStateInput.SelectedItem = OwnedDefinitionUseFilter.All; run("Filter"); };
        search.Children.Add(clear);
        QueryInput.PropertyChanged += (_, change) => { if (change.Property == TextBox.TextProperty) { run("Filter"); } };
        UseStateInput.SelectionChanged += (_, _) => run("Filter");
        NextPage.Click += (_, _) => run("Next");
        Files.SelectionChanged += (_, _) => inventoryDetails.Text = (Files.SelectedItem as OwnedDefinitionInventoryRow)?.Details ?? string.Empty;
        DockPanel.SetDock(search, Dock.Top);
        panel.Children.Add(search);
        DockPanel.SetDock(inventoryStatus, Dock.Top);
        panel.Children.Add(inventoryStatus);
        DockPanel.SetDock(inventoryDetails, Dock.Top);
        panel.Children.Add(inventoryDetails);
        var disclosure = new TextBlock
        {
            Text = "Source: Kora-owned roaming Instructions/*.instructions.md or Prompts/*.prompt.md; select kind explicitly. Profile files and bundled definitions are read-only. "
                + "Observed catalogue use state; inspect revalidates disk and exact revision. Search/filter is metadata-only, not a freshness, compatibility or integrity guarantee. "
                + "Save as new copy retains the original draft and creates a separate disabled same-kind owned file. No profile fork, model, dictation, scripts, rename or restore. "
                + "Save is not activation. Review permits explicit selection for a new request only. Disable reviewed use holds the exact saved revision without changing its source; a fresh review is required to enable it again. "
                + "Prompts are literal saved user requests, not system guidance or parameters.",
            TextWrapping = TextWrapping.Wrap, Margin = new(0, 10),
        };
        DockPanel.SetDock(disclosure, Dock.Top);
        panel.Children.Add(disclosure);
        DockPanel.SetDock(status, Dock.Bottom);
        panel.Children.Add(status);
        panel.Children.Add(Source);
        Content = panel;
    }

    internal void Report(string text) => status.Text = text;
    internal void ReportInventory(string text) => inventoryStatus.Text = text;
    internal void ShowInventory(OwnedDefinitionInventoryPage page)
    {
        var selected = Files.SelectedItem;
        Files.ItemsSource = page.Rows;
        Files.SelectedItem = selected is OwnedDefinitionInventoryRow row && page.Rows.Contains(row) ? selected : null;
        NextPage.IsEnabled = page.NextOffset is not null;
        inventoryStatus.Text = $"{page.Rows.Count} visible of {page.TotalMatches} matches; {page.Offset} earlier rows omitted. "
            + (page.NextOffset is not null ? "More complete metadata rows available with Next. " : string.Empty)
            + "Observed catalogue only; no disk reads for search/filter.";
    }
    internal void ClearInventory()
    {
        Files.SelectedItem = null; Files.ItemsSource = null; NextPage.IsEnabled = false; inventoryDetails.Text = string.Empty;
    }
    internal void SelectKind(ArtifactKind kind)
    {
        selectingKind = true;
        try { KindInput.SelectedItem = kind; }
        finally { selectingKind = false; }
    }
    internal void CancelReview() => activeReview?.Close();
    internal void ClearPrivateText()
    {
        ClearInventory(); QueryInput.Text = string.Empty; UseStateInput.SelectedItem = OwnedDefinitionUseFilter.All;
        Source.Text = string.Empty; NameInput.Text = string.Empty; CopyNameInput.Text = string.Empty;
        status.Text = string.Empty; inventoryStatus.Text = string.Empty;
    }

    internal async Task<int> Choose(string title, string details, string? original, string? proposed,
        string[] choices, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        { return await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => Choose(title, details, original, proposed, choices, token)); }
        if (!IsVisible) { throw new InvalidOperationException("The private instruction review window is no longer visible."); }
        var dialog = new Window { Title = title, Width = 900, Height = 680, CanResize = true };
        var root = new DockPanel { Margin = new(12) };
        var explanation = new TextBlock { Text = details, TextWrapping = TextWrapping.Wrap };
        DockPanel.SetDock(explanation, Dock.Top);
        root.Children.Add(explanation);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Margin = new(0, 10) };
        for (var index = 0; index < choices.Length; index++)
        {
            var answer = index;
            var button = new Button { Content = choices[index] };
            button.Click += (_, _) => dialog.Close(answer);
            buttons.Children.Add(button);
        }
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);
        var sources = new Grid { ColumnDefinitions = new("*,*") };
        var before = new TextBox { Text = original ?? "(missing/new file)", IsReadOnly = true, AcceptsReturn = true, FontFamily = new("Consolas") };
        var after = new TextBox { Text = proposed ?? "(delete)", IsReadOnly = true, AcceptsReturn = true, FontFamily = new("Consolas") };
        Grid.SetColumn(after, 1);
        sources.Children.Add(before);
        sources.Children.Add(after);
        root.Children.Add(sources);
        dialog.Content = root;
        activeReview = dialog;
        var reply = dialog.ShowDialog<int?>(this);
        using var registration = token.Register(() => Avalonia.Threading.Dispatcher.UIThread.Post(() => dialog.Close()));
        try { return await reply ?? choices.Length - 1; }
        finally
        {
            if (ReferenceEquals(activeReview, dialog)) { activeReview = null; }
            before.Text = string.Empty; after.Text = string.Empty; explanation.Text = string.Empty;
        }
    }
}
