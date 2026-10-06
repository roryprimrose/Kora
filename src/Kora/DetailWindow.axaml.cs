using System.ComponentModel;

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

using Kora.Application.Presentation;
using Kora.Core.Presentation;

namespace Kora;

public sealed partial class DetailWindow : Window, IDetailView
{
    private readonly DetailViewerState state;
    private readonly Func<bool, int?, int, Task> copySource;
    private readonly TextBox reader;
    private readonly TextBox search;
    private readonly CheckBox source;
    private readonly CheckBox readingText;
    private readonly CheckBox disclosure;
    private readonly StackPanel renderedDocument;
    private readonly ScrollViewer renderedViewport;
    private bool cleared;

    // XAML tooling requires a public default constructor. It cannot admit or open content.
    public DetailWindow() => throw new InvalidOperationException("Use the explicit host-owned native detail route.");

    internal DetailWindow(DetailViewerState state, NativeDocumentResult result, Func<bool, int?, int, Task> copySource)
    {
        this.state = state;
        this.copySource = copySource;
        AvaloniaXamlLoader.Load(this);
        reader = Required<TextBox>("ContentReader");
        search = Required<TextBox>("SearchInput");
        source = Required<CheckBox>("SourceToggle");
        readingText = Required<CheckBox>("ReadingTextToggle");
        disclosure = Required<CheckBox>("DisclosureConfirmation");
        renderedDocument = Required<StackPanel>("RenderedDocument");
        renderedViewport = Required<ScrollViewer>("RenderedViewport");
        foreach (var control in MarkdownDocumentRenderer.CreateControls(result, selectable: false))
        {
            renderedDocument.Children.Add(control);
        }
        var content = state.Content ?? throw new InvalidOperationException("The detail revision is closed.");
        Title = $"{content.Title} — details — revision {content.Reference.Revision}";
        Required<TextBlock>("DetailTitle").Text = content.Title;
        Required<TextBlock>("ProvenanceLabel").Text =
            $"Origin: {content.Origin} · Sensitivity: {content.Sensitivity} · {content.Provenance}";
        Required<TextBlock>("ReferenceLabel").Text =
            $"Item: {content.Reference.ItemId.Value:D} · Revision: {content.Reference.Revision} · Not session authority";
        Required<TextBlock>("DigestLabel").Text = $"SHA-256: {content.Digest} · UTF-8 bytes: {content.Utf8Bytes}";
        Required<TextBlock>("RendererStatus").Text = state.Status;
        disclosure.IsVisible = content.Sensitivity == DetailSensitivity.DisclosureConfirmationRequired;
        source.IsCheckedChanged += (_, _) => state.SetSource(source.IsChecked == true);
        readingText.IsCheckedChanged += (_, _) => Refresh();
        Required<Button>("PreviousMatch").Click += (_, _) => Find(backwards: true);
        Required<Button>("NextMatch").Click += (_, _) => Find(backwards: false);
        Required<Button>("CloseDetail").Click += (_, _) => Close();
        Required<Button>("CopySource").Click += OnCopyRequested;
        Required<Button>("CopySelection").Click += OnCopySelectionRequested;
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);
        // Also intercept programmatic/accessibility TextBox.Copy and platform context commands.
        reader.CopyingToClipboard += BlockAutomaticCopy;
        search.CopyingToClipboard += BlockAutomaticCopy;
        reader.CuttingToClipboard += BlockAutomaticCopy;
        search.CuttingToClipboard += BlockAutomaticCopy;
        reader.PastingFromClipboard += BlockClipboardRead;
        search.PastingFromClipboard += BlockClipboardRead;
        reader.AddHandler(ContextRequestedEvent, BlockContextMenu, RoutingStrategies.Tunnel);
        search.AddHandler(ContextRequestedEvent, BlockContextMenu, RoutingStrategies.Tunnel);
        state.PropertyChanged += OnStateChanged;
        Closed += OnClosed;
        Opened += (_, _) => source.Focus();
        Refresh();
    }

    void IDetailView.ShowOwned(Window? owner)
    {
        if (owner is null) { throw new InvalidOperationException("A native detail viewer requires a host window owner."); }
        Show(owner);
    }

    public void ClearAndClose()
    {
        ClearPresentation();
        Close();
    }

    private T Required<T>(string name) where T : Control =>
        this.FindControl<T>(name) ?? throw new InvalidOperationException("A native detail control is unavailable.");

    private void OnStateChanged(object? sender, PropertyChangedEventArgs eventArgs) => Refresh();

    private void Refresh()
    {
        if (state.Content is null) { ClearPresentation(); return; }
        if (!string.Equals(reader.Text, state.ActiveText, StringComparison.Ordinal))
        {
            reader.SelectionStart = 0;
            reader.SelectionEnd = 0;
            reader.Text = state.ActiveText;
            search.Text = string.Empty;
        }
        var structured = !state.IsSource && readingText.IsChecked != true
            && state.RenderState == DetailRenderState.Rendered;
        renderedViewport.IsVisible = structured;
        reader.IsVisible = !structured;
        Required<TextBlock>("RenderStatus").Text = state.Status;
    }

    private void Find(bool backwards)
    {
        if (state.Search(search.Text ?? string.Empty, backwards))
        {
            readingText.IsChecked = true;
            reader.CaretIndex = state.MatchStart;
            reader.SelectionStart = state.MatchStart;
            reader.SelectionEnd = state.MatchStart + state.MatchLength;
            reader.Focus();
        }
    }

    private async void OnCopyRequested(object? sender, RoutedEventArgs eventArgs)
    {
        await CopyAsync();
    }

    private async void OnCopySelectionRequested(object? sender, RoutedEventArgs eventArgs) =>
        await CopyAsync(selection: true);

    private async Task CopyAsync(bool selection = false)
    {
        if (cleared) { return; }
        if (selection)
        {
            if (!reader.IsVisible)
            {
                state.ReportStatus("Use Continuous text or Exact source to select a range before copying.");
                return;
            }
            var start = Math.Min(reader.SelectionStart, reader.SelectionEnd);
            var length = Math.Abs(reader.SelectionEnd - reader.SelectionStart);
            await copySource(disclosure.IsChecked == true, start, length);
        }
        else
        {
            await copySource(disclosure.IsChecked == true, null, 0);
        }
        disclosure.IsChecked = false;
    }

    private void OnKeyDown(object? sender, KeyEventArgs eventArgs)
    {
        var action = DetailKeyboardNavigation.Resolve(eventArgs.Key, eventArgs.KeyModifiers);
        if (action == DetailKeyboardNavigation.Action.None) { return; }
        eventArgs.Handled = true;
        switch (action)
        {
            case DetailKeyboardNavigation.Action.CopySelection:
                if (reader.IsFocused) { OnCopySelectionRequested(sender, eventArgs); }
                else { state.ReportStatus("Focus the continuous content reader and select text before copying."); }
                break;
            case DetailKeyboardNavigation.Action.BlockCut:
                state.ReportStatus("This passive reader cannot cut content; use Copy selection.");
                break;
            case DetailKeyboardNavigation.Action.BlockPaste: BlockClipboardRead(sender, eventArgs); break;
            case DetailKeyboardNavigation.Action.SelectContent:
                readingText.IsChecked = true;
                reader.Focus();
                reader.SelectAll();
                break;
            case DetailKeyboardNavigation.Action.FocusSearch: search.Focus(); break;
            case DetailKeyboardNavigation.Action.ToggleSource: source.IsChecked = source.IsChecked != true; break;
            case DetailKeyboardNavigation.Action.PreviousMatch: Find(backwards: true); break;
            case DetailKeyboardNavigation.Action.NextMatch: Find(backwards: false); break;
            case DetailKeyboardNavigation.Action.Close: Close(); break;
        }
    }

    private void BlockAutomaticCopy(object? sender, RoutedEventArgs eventArgs)
    {
        eventArgs.Handled = true;
        state.ReportStatus("Automatic copy is disabled. Use native Copy selection or Copy exact source with the disclosure gate.");
    }

    private void BlockClipboardRead(object? sender, RoutedEventArgs eventArgs)
    {
        eventArgs.Handled = true;
        state.ReportStatus("Clipboard reads/paste are disabled in passive details. Type a search query instead.");
    }

    private static void BlockContextMenu(object? sender, ContextRequestedEventArgs eventArgs) =>
        eventArgs.Handled = true;

    private void OnClosed(object? sender, EventArgs eventArgs) => ClearPresentation();

    private void ClearPresentation()
    {
        if (cleared) { return; }
        cleared = true;
        state.PropertyChanged -= OnStateChanged;
        state.Close();
        reader.SelectionStart = 0;
        reader.SelectionEnd = 0;
        reader.Text = string.Empty;
        renderedDocument.Children.Clear();
        search.Text = string.Empty;
        disclosure.IsChecked = false;
        foreach (var name in new[] { "DetailTitle", "ProvenanceLabel", "ReferenceLabel", "DigestLabel", "RendererStatus", "RenderStatus" })
        {
            Required<TextBlock>(name).Text = string.Empty;
        }
        Title = "Details closed";
        DataContext = null;
    }
}
