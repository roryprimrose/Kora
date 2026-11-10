using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

using Kora.Core.Context;
using Kora.Core.Storage;

namespace Kora;

internal sealed partial class AttachmentCatalogueView : UserControl, IDisposable
{
    internal Window Owner { get; }
    private readonly NativeViewLifetime presentation;
    private bool disposed;
    internal bool IsNativePresented => presentation.IsPresented;
    internal event EventHandler? Closed;
    internal event EventHandler? PresentationLost;
    internal void RefreshPresentation() => presentation.Refresh();
    internal void Activate() => Owner.Activate();
    internal void Close() => Dispose();
    internal Task Operation { get; private set; } = Task.CompletedTask;
    internal AttachmentCatalogueView(Window owner, AttachmentCatalogueViewModel model, Func<bool> admitted)
    {
        Owner = owner;
        InitializeComponent();
        DataContext = model;
        presentation = new(this, owner, () => !disposed && admitted());
        presentation.PresentationChanged += (_, _) =>
        {
            if (presentation.IsPresented) { return; }
            model.Revoke();
            PresentationLost?.Invoke(this, EventArgs.Empty);
        };
        owner.Closed += OnOwnerClosed;
        SizeChanged += (_, _) =>
        {
            var narrow = Bounds.Width < 900;
            CataloguePanes.ColumnDefinitions = new(narrow ? "*" : "430,*");
            CataloguePanes.RowDefinitions = new(narrow ? "*,*" : "*");
            CataloguePanes.Height = narrow ? 500 : 300;
            Grid.SetColumn(MetadataDetailsPane, narrow ? 0 : 1);
            Grid.SetRow(MetadataDetailsPane, narrow ? 1 : 0);
        };
        MetadataFindFirst.Click += (_, _) => FindMetadata(SnapshotFindDirection.First);
        MetadataFindNext.Click += (_, _) => FindMetadata(SnapshotFindDirection.Next);
        MetadataFindPrevious.Click += (_, _) => FindMetadata(SnapshotFindDirection.Previous);
        MetadataFindClear.Click += (_, _) => model.ClearMetadataFind();
        MetadataFindPanel.AddHandler(KeyDownEvent, (_, args) =>
        {
            if (args.Key == Key.Escape)
            {
                model.ClearMetadataFind();
                args.Handled = true;
            }
            else if (args.Key == Key.F3 || args.Key == Key.Enter && MetadataFindQuery.IsFocused)
            {
                FindMetadata(args.Key == Key.Enter ? SnapshotFindDirection.First
                    : args.KeyModifiers.HasFlag(KeyModifiers.Shift) ? SnapshotFindDirection.Previous : SnapshotFindDirection.Next);
                args.Handled = true;
            }
            else if (args.KeyModifiers.HasFlag(KeyModifiers.Control) && args.Key is Key.C or Key.X or Key.V or Key.Insert
                || args.KeyModifiers.HasFlag(KeyModifiers.Shift) && args.Key is Key.Insert or Key.Delete
                || args.Key == Key.Apps || args.KeyModifiers.HasFlag(KeyModifiers.Shift) && args.Key == Key.F10)
            { args.Handled = true; }
        }, RoutingStrategies.Tunnel);
        MetadataFindPanel.AddHandler(ContextRequestedEvent, (_, args) => args.Handled = true, RoutingStrategies.Tunnel);
        MetadataFindPanel.AddHandler(TextBox.CopyingToClipboardEvent, BlockTransfer, RoutingStrategies.Direct | RoutingStrategies.Bubble | RoutingStrategies.Tunnel);
        MetadataFindPanel.AddHandler(TextBox.CuttingToClipboardEvent, BlockTransfer, RoutingStrategies.Direct | RoutingStrategies.Bubble | RoutingStrategies.Tunnel);
        MetadataFindPanel.AddHandler(TextBox.PastingFromClipboardEvent, BlockTransfer, RoutingStrategies.Direct | RoutingStrategies.Bubble | RoutingStrategies.Tunnel);
        RefreshPage.Click += async (_, _) => await (Operation = model.Refresh());
        NextPage.Click += async (_, _) => await (Operation = model.Refresh(next: true));
        CancelRead.Click += (_, _) => model.Cancel();
        ClearQuery.Click += (_, _) => model.ClearQuery();
        ClearAttachmentPageOrder.Click += (_, _) => model.ClearPageOrder();
        SlotRecords.SelectionChanged += (_, _) => model.Select(SlotRecords.SelectedItem as SessionAttachmentSummary);
        OpenExact.Click += async (_, _) => await (Operation = model.Navigate());
        CloseView.Click += (_, _) => Close();
        KeyDown += (_, args) =>
        {
            if (args.Key == Key.Enter && (AttachmentQueryText.IsFocused
                || AttachmentMinimumBytes.IsFocused || AttachmentMaximumBytes.IsFocused) && model.CanSearch)
            {
                Operation = model.Refresh();
                args.Handled = true;
            }
            if (args.Key == Key.Escape && !args.Handled) { Close(); args.Handled = true; }
        };

        void FindMetadata(SnapshotFindDirection direction)
        {
            model.FindMetadata(direction);
            if (!model.HasMetadataFindMatch) { return; }
            UpdateLayout();
            model.SynchronizeAuthority();
            if (!model.HasMetadataFindMatch) { return; }
            MetadataFindSpan.BringIntoView();
            model.SynchronizeAuthority();
            if (!model.HasMetadataFindMatch) { return; }
            if (!MetadataFindSpan.Focus())
            { model.MetadataFindFocusUnavailable(); }
            model.SynchronizeAuthority();
        }
    }

    private static void BlockTransfer(object? sender, RoutedEventArgs args) => args.Handled = true;

    internal void PrepareExactSession(Guid sessionId, Func<bool> admitted)
    {
        if (!IsNativePresented || !admitted() || DataContext is not AttachmentCatalogueViewModel model)
        { throw new InvalidOperationException("The original native catalogue is unavailable."); }
        model.PrepareExactSession(sessionId);
        UpdateLayout();
        AttachmentQueryText.BringIntoView();
        if (!IsNativePresented || !admitted() || !AttachmentQueryText.Focus() || !AttachmentQueryText.IsFocused
            || !ReferenceEquals(DataContext, model) || model.Mode != AttachmentCatalogueMode.ExactId
            || !string.Equals(model.QueryText, sessionId.ToString("D"), StringComparison.Ordinal)
            || !string.Equals(AttachmentQueryText.Text, model.QueryText, StringComparison.Ordinal) || !admitted())
        {
            model.PreparationUnavailable();
            throw new InvalidOperationException("Native exact-session query focus was not acknowledged.");
        }
    }

    private void OnOwnerClosed(object? sender, EventArgs args) => Dispose();
    public void Dispose()
    {
        if (disposed) { return; }
        disposed = true;
        Owner.Closed -= OnOwnerClosed;
        presentation.Dispose();
        if (DataContext is AttachmentCatalogueViewModel model) { model.Dispose(); }
        Closed?.Invoke(this, EventArgs.Empty);
        if (Owner is ResponseWindow primary) { primary.RemoveHostedView(this); }
        else if (Owner is AttachmentCatalogueWindow) { Owner.Close(); }
    }
}
