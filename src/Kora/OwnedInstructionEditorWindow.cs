using Avalonia.Controls;
using Avalonia.Automation;
using Avalonia.Layout;
using Avalonia.Media;

using Kora.Application.Definitions;
using Kora.Core.Artifacts;

namespace Kora;

internal sealed partial class OwnedInstructionEditorWindow : Window
{
    internal readonly ComboBox Files = new() { Width = 240 };
    internal readonly TextBox QueryInput = new() { Width = 240, PlaceholderText = "Literal filename, display name or artifact substring" };
    internal readonly ComboBox QueryModeInput = new()
    {
        Width = 180, ItemsSource = Enum.GetValues<OwnedDefinitionInventoryQueryMode>(),
        SelectedItem = OwnedDefinitionInventoryQueryMode.Literal,
    };
    internal readonly ComboBox OrderInput = new()
    {
        Width = 210, ItemsSource = Enum.GetValues<OwnedDefinitionInventoryOrder>(),
        SelectedItem = OwnedDefinitionInventoryOrder.FileNameAscending,
    };
    internal readonly Button ReviewUse = new() { Content = "Review use", Margin = new(3), IsEnabled = false };
    internal readonly Button DisableReviewedUse = new() { Content = "Disable reviewed use", Margin = new(3), IsEnabled = false };
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
    internal readonly TextBox Critique = new()
    {
        AcceptsReturn = true, MaxHeight = 100, MinHeight = 40, TextWrapping = TextWrapping.Wrap,
        PlaceholderText = "Describe how to refine the current owned Instruction body",
    };
    internal readonly TextBox Source = new()
    {
        AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, FontFamily = new("Consolas"),
        HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch,
    };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock validationStatus = new() { TextWrapping = TextWrapping.Wrap };
    private Window? activeReview;
    private Window? activeComparison;
    private bool selectingKind;

    internal OwnedInstructionEditorWindow(Action<string> run)
    {
        Title = "Kora-owned instruction and prompt editor";
        Width = 960;
        Height = 720;
        var controls = new WrapPanel { Orientation = Orientation.Horizontal };
        controls.Children.Add(KindInput);
        KindInput.SelectionChanged += (_, _) =>
        {
            RetireMetadataFind();
            if (!selectingKind) { run("Kind"); }
        };
        controls.Children.Add(Files);
        controls.Children.Add(NameInput);
        controls.Children.Add(CopyNameInput);
        foreach (var action in new[] { "List", "Refresh", "Create", "Inspect", "Validate", "Compare", "Rebase", "Save", "Save as new copy", "Create Kora copy from profile instruction", "Cancel profile copy", "Reload", "Delete" })
        {
            var button = new Button { Content = action, Margin = new(3) };
            button.Click += (_, _) => run(action);
            controls.Children.Add(button);
        }
        ReviewUse.Click += (_, _) => run("Review use");
        DisableReviewedUse.Click += (_, _) => run("Disable reviewed use");
        controls.Children.Add(ReviewUse);
        controls.Children.Add(DisableReviewedUse);
        var panel = new DockPanel { Margin = new(12) };
        DockPanel.SetDock(controls, Dock.Top);
        panel.Children.Add(controls);
        var search = new WrapPanel { Orientation = Orientation.Horizontal };
        search.Children.Add(new TextBlock { Text = "Owned metadata search: ", VerticalAlignment = VerticalAlignment.Center });
        search.Children.Add(new TextBlock { Text = "Order: ", VerticalAlignment = VerticalAlignment.Center });
        AutomationProperties.SetName(OrderInput, "Owned metadata order: FileNameAscending (default), FileNameDescending, DisplayNameAscending or DisplayNameDescending; ordinal extensionless owned filename IDs; absent display names last; filename ascending ties; metadata only");
        search.Children.Add(OrderInput);
        search.Children.Add(new TextBlock { Text = "Navigation mode: ", VerticalAlignment = VerticalAlignment.Center });
        AutomationProperties.SetName(QueryModeInput, "Owned metadata navigation mode: Literal, ExactOwnedId or ExactArtifactId; metadata only");
        search.Children.Add(QueryModeInput);
        AutomationProperties.SetName(QueryInput, "Literal metadata substring; ordinal ignore case; metadata only");
        search.Children.Add(QueryInput);
        search.Children.Add(UseStateInput);
        search.Children.Add(NextPage);
        var clear = new Button { Content = "Clear search", Margin = new(3) };
        AutomationProperties.SetName(clear, "Clear owned name query and order; FileNameAscending, Literal mode, empty query, All use state");
        clear.Click += (_, _) => { ClearSearch(); run("Filter"); };
        search.Children.Add(clear);
        QueryInput.PropertyChanged += (_, change) => { if (change.Property == TextBox.TextProperty) { run("Filter"); } };
        UseStateInput.SelectionChanged += (_, _) => run("Filter");
        OrderInput.SelectionChanged += (_, _) => run("Filter");
        QueryModeInput.SelectionChanged += (_, _) =>
        {
            var label = QueryModeInput.SelectedItem switch
            {
                OwnedDefinitionInventoryQueryMode.Literal => "Literal filename, display name or artifact substring; ordinal ignore case",
                OwnedDefinitionInventoryQueryMode.ExactOwnedId => "Exact canonical lowercase owned name ID; no extension or path; ordinal case-sensitive",
                OwnedDefinitionInventoryQueryMode.ExactArtifactId => "Exact observed artifact ID; ordinal case-sensitive; unknown or unmapped rows excluded",
                _ => "Select a valid owned metadata navigation mode",
            };
            QueryInput.PlaceholderText = label;
            AutomationProperties.SetName(QueryInput, label + "; metadata only");
            run("Filter");
        };
        search.KeyDown += (_, key) =>
        {
            if (key.Key == Avalonia.Input.Key.Escape)
            { key.Handled = true; ClearSearch(); run("Filter"); }
        };
        NextPage.Click += (_, _) => run("Next");
        Files.SelectionChanged += (_, _) =>
        {
            RetireMetadataFind();
            inventoryDetails.Text = (Files.SelectedItem as OwnedDefinitionInventoryRow)?.Details ?? string.Empty;
        };
        DockPanel.SetDock(search, Dock.Top);
        panel.Children.Add(search);
        DockPanel.SetDock(inventoryStatus, Dock.Top);
        panel.Children.Add(inventoryStatus);
        DockPanel.SetDock(inventoryDetails, Dock.Top);
        panel.Children.Add(inventoryDetails);
        var metadataFindControls = BuildMetadataFind();
        DockPanel.SetDock(metadataFindControls, Dock.Top);
        panel.Children.Add(metadataFindControls);
        var disclosure = new TextBlock
        {
            Text = "Source: Kora-owned roaming Instructions/*.instructions.md or Prompts/*.prompt.md; select kind explicitly. Profile files and bundled definitions are read-only. "
                + "Observed catalogue use state; inspect revalidates disk and exact revision. Search/filter is metadata-only, not a freshness, compatibility or integrity guarantee. "
                + "Observed UTC is snapshot time only, not freshness, trust, approval, ownership or use authority. "
                + "Save as new copy retains the original draft and creates a separate disabled same-kind owned file. Create Kora copy from profile instruction separately selects one personal native v1 instruction, asks for bounded read consent, then reviews the entire disabled copy. Manual editing needs no model. "
                + "Explicit Refine proposes only the current owned Instruction body through a qualified local tool-free model; complete review and normal Save remain separate. No dictation, scripts, profile writes, rename or restore. "
                + "Save is not activation. Review permits explicit selection for a new request only. Disable reviewed use holds the exact saved revision without changing its source; a fresh review is required to enable it again. "
                + "Prompts are user requests, not system guidance. Native v1 stays literal; v2 supports required ${input:name} text only, with private expanded preview and separate use admission.",
            TextWrapping = TextWrapping.Wrap, Margin = new(0, 10),
        };
        DockPanel.SetDock(disclosure, Dock.Top);
        panel.Children.Add(disclosure);
        var refinementControls = new StackPanel { Spacing = 4 };
        refinementControls.Children.Add(new TextBlock { Text = "Local body refinement critique (volatile; Instruction only)" });
        AutomationProperties.SetName(Critique, "Unsent local instruction body refinement critique");
        refinementControls.Children.Add(Critique);
        var refine = new Button { Content = "Refine current Instruction body" };
        AutomationProperties.SetName(refine, "Review local disclosure and generate a complete instruction body proposal without saving");
        refine.Click += (_, _) => run("Refine");
        refinementControls.Children.Add(refine);
        var cancelRefinement = new Button { Content = "Cancel refinement" };
        cancelRefinement.Click += (_, _) => run("Cancel refinement");
        refinementControls.Children.Add(cancelRefinement);
        DockPanel.SetDock(refinementControls, Dock.Top);
        panel.Children.Add(refinementControls);
        BindSourceSize();
        DockPanel.SetDock(SourceSizeStatus, Dock.Bottom);
        panel.Children.Add(SourceSizeStatus);
        AddGoToLine(panel, controls);
        DockPanel.SetDock(validationStatus, Dock.Bottom);
        panel.Children.Add(validationStatus);
        DockPanel.SetDock(status, Dock.Bottom);
        panel.Children.Add(status);
        var bufferFind = CreateBufferFind();
        DockPanel.SetDock(bufferFind, Dock.Top);
        panel.Children.Add(bufferFind);
        panel.Children.Add(Source);
        Content = panel;
    }

    internal void Report(string text) => status.Text = text;
    internal void ClearSearch()
    {
        OrderInput.SelectedItem = OwnedDefinitionInventoryOrder.FileNameAscending;
        QueryModeInput.SelectedItem = OwnedDefinitionInventoryQueryMode.Literal;
        QueryInput.Text = string.Empty; UseStateInput.SelectedItem = OwnedDefinitionUseFilter.All;
    }
    internal void ReportValidation(string text) => validationStatus.Text = text;
    internal void ClearValidation()
    {
        RetireMetadataFind();
        validationStatus.Text = string.Empty;
        activeComparison?.Close();
    }
    internal void ReportInventory(string text) => inventoryStatus.Text = text;
    internal void ShowInventory(OwnedDefinitionInventoryPage page)
    {
        RetireMetadataFind();
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
        RetireMetadataFind();
        Files.SelectedItem = null; Files.ItemsSource = null; NextPage.IsEnabled = false; inventoryDetails.Text = string.Empty;
    }
    internal void SelectKind(ArtifactKind kind)
    {
        selectingKind = true;
        try { KindInput.SelectedItem = kind; }
        finally { selectingKind = false; }
    }
    internal void CancelReview()
    {
        RetireMetadataFind();
        activeReview?.Close();
    }
    internal void SetUseReviewAvailable(bool available)
    {
        ReviewUse.IsEnabled = available;
        DisableReviewedUse.IsEnabled = available;
    }
    internal void ClearPrivateText()
    {
        RetireBufferFind(clearQuery: true);
        CancelGoToLine();
        CancelRebaseReview();
        ClearInventory(); ClearSearch();
        SetUseReviewAvailable(false);
        Source.Text = string.Empty; NameInput.Text = string.Empty; CopyNameInput.Text = string.Empty;
        Critique.Text = string.Empty;
        status.Text = string.Empty; inventoryStatus.Text = string.Empty;
        ClearValidation();
        SourceSizeStatus.Text = string.Empty;
    }

    internal void ShowComparison(OwnedDraftComparison comparison, Func<bool> current)
    {
        if (!IsVisible || !current()) { return; }
        activeComparison?.Close();
        var dialog = new Window
        {
            Title = $"Passive owned {comparison.Validation.Kind} comparison",
            Width = 1000, Height = 720, MinWidth = 640, MinHeight = 500,
        };
        var root = new DockPanel { Margin = new(12) };
        var explanation = new TextBlock { Text = comparison.Summary, TextWrapping = TextWrapping.Wrap };
        var disclosure = new ScrollViewer { Content = explanation, MaxHeight = 180 };
        DockPanel.SetDock(disclosure, Dock.Top);
        root.Children.Add(disclosure);
        var close = new Button { Content = "Close", Margin = new(0, 10) };
        close.Click += (_, _) => dialog.Close();
        DockPanel.SetDock(close, Dock.Bottom);
        root.Children.Add(close);
        var sources = new Grid { ColumnDefinitions = new("*,*") };
        Func<bool>? eligibility = current;
        bool Current() => IsVisible && dialog.IsVisible && eligibility?.Invoke() == true;
        var before = new SnapshotComparisonFindPane("Frozen inspected baseline", comparison.Original?.Document.Text, Current, dialog.Close);
        var after = new SnapshotComparisonFindPane("Exact raw memory draft (not prepared Save bytes)", comparison.Draft, Current, dialog.Close);
        Grid.SetColumn(after, 1);
        sources.Children.Add(before); sources.Children.Add(after);
        root.Children.Add(sources);
        dialog.Content = root;
        activeComparison = dialog;
        var timer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        EventHandler tick = (_, _) => { if (!Current()) { dialog.Close(); } };
        timer.Tick += tick;
        EventHandler ownerClosed = (_, _) => dialog.Close();
        Closed += ownerClosed;
        dialog.KeyDown += (_, key) =>
        {
            if (key.Key == Avalonia.Input.Key.Escape) { key.Handled = true; dialog.Close(); }
        };
        dialog.Closed += (_, _) =>
        {
            timer.Stop();
            timer.Tick -= tick;
            Closed -= ownerClosed;
            eligibility = null;
            before.Dispose(); after.Dispose(); explanation.Text = string.Empty;
            dialog.Content = null;
            if (ReferenceEquals(activeComparison, dialog)) { activeComparison = null; }
        };
        dialog.Show(this);
        if (!current())
        {
            // Opening events can revoke the owner before Avalonia finishes setting visibility.
            dialog.Hide();
            dialog.Close();
        }
        else if (dialog.IsVisible) { timer.Start(); }
    }

    internal async Task<int> Choose(string title, string details, string? original, string? proposed,
        string[] choices, CancellationToken token, Func<Window, CancellationToken, Task<bool>>? spokenSave = null)
    {
        token.ThrowIfCancellationRequested();
        if (!Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        { return await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => Choose(title, details, original, proposed, choices, token, spokenSave)); }
        if (!IsVisible) { throw new InvalidOperationException("The private instruction review window is no longer visible."); }
        var dialog = new Window { Title = title, Width = 900, Height = 680, CanResize = true };
        var root = new DockPanel { Margin = new(12) };
        var explanation = new TextBlock { Text = details, TextWrapping = TextWrapping.Wrap };
        DockPanel.SetDock(explanation, Dock.Top);
        root.Children.Add(explanation);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Margin = new(0, 10) };
        using var reviewLifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        Exception? voiceFailure = null;
        Task voiceOperation = Task.CompletedTask;
        for (var index = 0; index < choices.Length; index++)
        {
            var answer = index;
            var button = new Button { Content = choices[index] };
            button.Click += (_, _) => dialog.Close(answer);
            buttons.Children.Add(button);
        }
        if (spokenSave is not null)
        {
            var voice = new Button { Content = "Reviewed complete source: exact save question" };
            voice.Click += async (_, _) =>
            {
                buttons.IsEnabled = false;
                voiceOperation = Answer();
                await voiceOperation;
            };
            async Task Answer()
            {
                try { dialog.Close(await spokenSave(dialog, reviewLifetime.Token) ? 0 : choices.Length - 1); }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                { voiceFailure = exception; dialog.Close(); }
            }
            buttons.Children.Add(voice);
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
        dialog.Closed += (_, _) => reviewLifetime.Cancel();
        var reply = dialog.ShowDialog<int?>(this);
        using var registration = token.Register(() => Avalonia.Threading.Dispatcher.UIThread.Post(() => dialog.Close()));
        try
        {
            var result = await reply ?? choices.Length - 1;
#pragma warning disable VSTHRD003 // The review owns this exact question operation through capture retirement.
            await voiceOperation;
#pragma warning restore VSTHRD003
            if (voiceFailure is not null)
            { throw new InvalidOperationException("Exact save question failed: " + voiceFailure.Message + " Draft retained; recover through native Save.", voiceFailure); }
            return result;
        }
        finally
        {
            if (ReferenceEquals(activeReview, dialog)) { activeReview = null; }
            before.Text = string.Empty; after.Text = string.Empty; explanation.Text = string.Empty;
        }
    }
}
