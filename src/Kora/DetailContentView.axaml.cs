using System.ComponentModel;

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;

using Kora.Application.Presentation;
using Kora.Application.ViewModels;
using Kora.Core.Presentation;
using Kora.Core.Context;
using Kora.Controls;

namespace Kora;

public sealed partial class DetailContentView : UserControl
{
    internal event EventHandler? CloseRequested;
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
    private readonly NamedTextBlock findSpan;

    // XAML tooling requires a public default constructor. It cannot admit or open content.
    public DetailContentView() => throw new InvalidOperationException("Use the explicit host-owned native detail route.");

    internal DetailContentView(DetailViewerState state, NativeDocumentResult result, Func<bool, int?, int, Task> copySource,
        MainViewModel? typographyHost = null)
    {
        this.state = state;
        this.copySource = copySource;
        typographyHost?.EnsureReaderTypographyAvailable();
        AvaloniaXamlLoader.Load(this);
        reader = Required<TextBox>("ContentReader");
        search = Required<TextBox>("SearchInput");
        findSpan = Required<NamedTextBlock>("FindSpan");
        source = Required<CheckBox>("SourceToggle");
        readingText = Required<CheckBox>("ReadingTextToggle");
        disclosure = Required<CheckBox>("DisclosureConfirmation");
        renderedDocument = Required<StackPanel>("RenderedDocument");
        renderedViewport = Required<ScrollViewer>("RenderedViewport");
        var content = state.Content ?? throw new InvalidOperationException("The detail revision is closed.");
        var renderedControls = content.Kind == DetailContentKind.StaticHtml
            ? StaticHtmlDocumentRenderer.CreateControls(result)
            : MarkdownDocumentRenderer.CreateControls(result, selectable: false);
        foreach (var control in renderedControls)
        {
            if (typographyHost is not null && control is TextBlock text
                && (!result.IsFallback || !ReferenceEquals(control, renderedControls[0])))
            {
                NativeReaderTypography.Bind(text, typographyHost);
            }
            renderedDocument.Children.Add(control);
        }
        if (typographyHost is not null) { NativeReaderTypography.Bind(reader, typographyHost); }
        if (typographyHost is not null) { NativeReaderTypography.Bind(findSpan, typographyHost); }
        Required<TextBlock>("DetailTitle").Text = content.Title;
        Required<TextBlock>("ProvenanceLabel").Text =
            $"Origin: {content.Origin} · Sensitivity: {content.Sensitivity} · {content.Provenance}";
        Required<TextBlock>("ReferenceLabel").Text =
            $"Item: {content.Reference.ItemId.Value:D} · Revision: {content.Reference.Revision} · Not session authority"
            + (content.HistorySession is { } session ? $" · Retained session: {session.Value:D}" : string.Empty);
        Required<TextBlock>("DigestLabel").Text = $"SHA-256: {content.Digest} · UTF-8 bytes: {content.Utf8Bytes}";
        if (content.ConversationMessage is { } message)
        {
            Required<TextBlock>("ReferenceLabel").Text +=
                $" · Retained session: {message.Request.SessionId.Value:D} · Original request: {message.Request.RequestId.Value:D}"
                + $" · Original task: {message.Request.TaskId.Value:D} · Sequence: {message.Sequence}"
                + $" · Submitted UTC: {message.SubmittedAt:O} · Original generation: {message.Generation.Value}";
            var host = Required<TextBlock>("ConversationHostLabel");
            host.IsVisible = true;
            host.Text = state.HostText;
            var assistant = Required<TextBlock>("ConversationAssistantLabel");
            assistant.IsVisible = content.Origin != DetailContentOrigin.RetainedAssistant && message.AssistantResponse is not null;
            assistant.Text = content.Origin == DetailContentOrigin.RetainedAssistant || message.AssistantResponse is not { } answer ? string.Empty
                : $"ASSISTANT - finalized local answer | host turn {answer.Provenance.Turn.Value:D}\n" + state.AssistantText;
            Required<CheckBox>("FindHostToggle").IsVisible = true;
            Required<TextBlock>("CopyPolicyLabel").Text = "Passive retained conversation Details grant no clipboard/export disclosure authority. Exact-source and selection copy are blocked.";
            Required<Button>("CopySource").IsEnabled = false;
            Required<Button>("CopySelection").IsEnabled = false;
        }
        Required<TextBlock>("RendererStatus").Text = state.Status;
        disclosure.IsVisible = content.ConversationMessage is null && content.Sensitivity == DetailSensitivity.DisclosureConfirmationRequired;
        source.IsCheckedChanged += (_, _) => state.SetSource(source.IsChecked == true);
        readingText.IsCheckedChanged += (_, _) => Refresh();
        Required<Button>("PreviousMatch").Click += (_, _) => Find(SnapshotFindDirection.Previous);
        Required<Button>("NextMatch").Click += (_, _) => Find(SnapshotFindDirection.Next);
        Required<Button>("FirstMatch").Click += (_, _) => Find(SnapshotFindDirection.First);
        Required<Button>("CancelFind").Click += (_, _) => CancelLiteralFind(false);
        Required<Button>("ClearFind").Click += (_, _) => CancelLiteralFind(true);
        Required<CheckBox>("FindHostToggle").IsCheckedChanged += (_, _) =>
            state.SetFindHost(Required<CheckBox>("FindHostToggle").IsChecked == true);
        search.PropertyChanged += (_, args) =>
        {
            if (!cleared && args.Property == TextBox.TextProperty) { state.EditFindQuery(search.Text ?? string.Empty); }
        };
        Required<Button>("CloseDetail").Click += (_, _) => RequestClose();
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
        Refresh();
    }

    internal void FocusReader() => source.Focus();
    internal DetailReadingPosition CaptureReadingPosition() => new(source.IsChecked == true, readingText.IsChecked == true,
        renderedViewport.Offset, reader.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault()?.Offset ?? default, reader.CaretIndex);

    internal void RestoreReadingPosition(DetailReadingPosition position)
    {
        if (cleared || state.Content is null) { return; }
        source.IsChecked = position.Source;
        readingText.IsChecked = position.Continuous;
        UpdateLayout();
        reader.CaretIndex = Math.Min(position.Caret, reader.Text?.Length ?? 0);
        renderedViewport.Offset = position.RenderedOffset;
        if (reader.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault() is { } viewport)
        { viewport.Offset = position.TextOffset; }
    }

    private void RequestClose()
    {
        ClearPresentation();
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private T Required<T>(string name) where T : Control =>
        this.FindControl<T>(name) ?? throw new InvalidOperationException("A native detail control is unavailable.");

    private void OnStateChanged(object? sender, PropertyChangedEventArgs eventArgs) => Refresh();

    private void Refresh()
    {
        if (state.Content is null) { RequestClose(); return; }
        if (!string.Equals(reader.Text, state.ActiveText, StringComparison.Ordinal))
        {
            reader.SelectionStart = 0;
            reader.SelectionEnd = 0;
            reader.Text = state.ActiveText;
        }
        if (!string.Equals(search.Text ?? string.Empty, state.SearchQuery, StringComparison.Ordinal)) { search.Text = state.SearchQuery; }
        findSpan.Text = state.MatchStart < 0 ? null : state.FindText.Substring(state.MatchStart, state.MatchLength);
        findSpan.IsVisible = state.MatchStart >= 0;
        Required<NamedTextBlock>("FindSummary").Text = state.FindSummary;
        Required<Button>("NextMatch").IsEnabled = state.CanNavigateFind;
        Required<Button>("PreviousMatch").IsEnabled = state.CanNavigateFind;
        var structured = !state.IsSource && readingText.IsChecked != true
            && state.RenderState == DetailRenderState.Rendered;
        renderedViewport.IsVisible = structured;
        reader.IsVisible = !structured;
        Required<TextBlock>("RenderStatus").Text = state.Status;
    }

    private void CancelLiteralFind(bool clearQuery)
    {
        if (cleared || state.Content is null) { RequestClose(); return; }
        state.CancelFind(clearQuery);
        search.Focus();
    }

    private void Find(SnapshotFindDirection direction)
    {
        if (cleared || state.Content is null) { RequestClose(); return; }
        var generation = state.Generation;
        if (state.Find(search.Text ?? string.Empty, direction))
        {
            UpdateLayout();
            if (!state.FindAvailable || generation != state.Generation) { RequestClose(); return; }
            findSpan.BringIntoView();
            if (!state.FindAvailable || generation != state.Generation) { RequestClose(); return; }
            if (!findSpan.Focus())
            {
                state.CancelFind();
                state.ReportStatus("Literal Find focus unavailable; no match or focus claimed.");
            }
            if (!state.FindAvailable || generation != state.Generation) { RequestClose(); }
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD100",
        Justification = "Avalonia Click requires void; CopyAsync reports failures without claiming a clipboard write.")]
    private async void OnCopyRequested(object? sender, RoutedEventArgs eventArgs)
    {
        await CopyAsync();
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD100",
        Justification = "Avalonia Click requires void; CopyAsync reports failures without claiming a clipboard write.")]
    private async void OnCopySelectionRequested(object? sender, RoutedEventArgs eventArgs) =>
        await CopyAsync(selection: true);

    private async Task CopyAsync(bool selection = false)
    {
        try
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
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            state.ReportStatus("Copy failed or is unconfirmed; no successful clipboard write is claimed.");
        }
    }

    internal void OnKeyDown(object? sender, KeyEventArgs eventArgs)
    {
        if (eventArgs.Key == Key.Enter && ReferenceEquals(eventArgs.Source, search))
        {
            eventArgs.Handled = true;
            Find(SnapshotFindDirection.First);
            return;
        }
        var action = DetailKeyboardNavigation.Resolve(eventArgs.Key, eventArgs.KeyModifiers);
        if (action == DetailKeyboardNavigation.Action.None) { return; }
        eventArgs.Handled = true;
        if (cleared || state.Content is null) { RequestClose(); return; }
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
            case DetailKeyboardNavigation.Action.FocusSearch:
                if (state.FindAvailable) { search.Focus(); }
                else { RequestClose(); }
                break;
            case DetailKeyboardNavigation.Action.ToggleSource: source.IsChecked = source.IsChecked != true; break;
            case DetailKeyboardNavigation.Action.PreviousMatch: Find(SnapshotFindDirection.Previous); break;
            case DetailKeyboardNavigation.Action.NextMatch: Find(SnapshotFindDirection.Next); break;
            case DetailKeyboardNavigation.Action.Close: RequestClose(); break;
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

    internal void ClearPresentation()
    {
        if (cleared) { return; }
        cleared = true;
        state.PropertyChanged -= OnStateChanged;
        state.Close();
        reader.SelectionStart = 0;
        reader.SelectionEnd = 0;
        reader.Text = string.Empty;
        NativeStrikethroughText.Retire(renderedDocument.Children);
        renderedDocument.Children.Clear();
        search.Text = string.Empty;
        findSpan.Text = null;
        findSpan.IsVisible = false;
        disclosure.IsChecked = false;
        foreach (var name in new[] { "DetailTitle", "ProvenanceLabel", "ReferenceLabel", "DigestLabel", "ConversationHostLabel", "ConversationAssistantLabel", "RendererStatus", "RenderStatus", "FindSummary" })
        {
            Required<TextBlock>(name).Text = string.Empty;
        }
        DataContext = null;
    }
}
