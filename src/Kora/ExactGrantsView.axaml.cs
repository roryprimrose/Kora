using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

using Kora.Application.Interaction;

namespace Kora;

public sealed partial class ExactGrantsView : UserControl, IDisposable
{
    private readonly NativeViewLifetime lifetime;
    private bool disposed;
    internal bool IsNativePresented => lifetime.IsPresented;

    public ExactGrantsView() => throw new InvalidOperationException("Permission grants require an explicit host-owned view.");

    public ExactGrantsView(Window owner, Func<bool> admitted, ExactGrantsViewModel? state = null)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(admitted);
        InitializeComponent();
        SizeChanged += (_, _) => PermissionLayoutRoot.Height = Math.Max(PermissionLayoutRoot.MinHeight, Bounds.Height - 40);
        if (state is not null) { DataContext = state; }
        lifetime = new(this, owner, admitted);
        lifetime.PresentationChanged += OnPresentationChanged;
        foreach (var reader in new[] { ExactInspection, ExactBatchInspection })
        {
            reader.AddHandler(TextBox.CopyingToClipboardEvent, BlockReviewTransfer, RoutingStrategies.Direct | RoutingStrategies.Bubble | RoutingStrategies.Tunnel);
            reader.AddHandler(TextBox.CuttingToClipboardEvent, BlockReviewTransfer, RoutingStrategies.Direct | RoutingStrategies.Bubble | RoutingStrategies.Tunnel);
            reader.AddHandler(TextBox.PastingFromClipboardEvent, BlockReviewTransfer, RoutingStrategies.Direct | RoutingStrategies.Bubble | RoutingStrategies.Tunnel);
            reader.AddHandler(ContextRequestedEvent, BlockReviewTransfer, RoutingStrategies.Direct | RoutingStrategies.Bubble | RoutingStrategies.Tunnel);
            reader.AddHandler(KeyDownEvent, BlockReviewTransferKeys, RoutingStrategies.Tunnel);
            reader.PropertyChanged += (_, args) =>
            {
                if (args.Property == TextBox.TextProperty)
                {
                    reader.SelectionStart = reader.SelectionEnd = reader.CaretIndex = 0;
                }
            };
        }
    }

    private void OnPresentationChanged(object? sender, EventArgs args)
    {
        if (!disposed && !lifetime.IsPresented && DataContext is ExactGrantsViewModel state)
        { state.RetirePresentation(); }
    }

    public void Dispose()
    {
        if (disposed) { return; }
        disposed = true;
        lifetime.PresentationChanged -= OnPresentationChanged;
        lifetime.Dispose();
        ExactInspection.Text = string.Empty;
        ExactBatchInspection.Text = string.Empty;
        DataContext = null;
    }

    private static void BlockReviewTransfer(object? sender, RoutedEventArgs args) => args.Handled = true;

    private static void BlockReviewTransferKeys(object? sender, KeyEventArgs args)
    {
        var control = (args.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) != KeyModifiers.None;
        var shift = args.KeyModifiers.HasFlag(KeyModifiers.Shift);
        if ((control && args.Key is Key.C or Key.X or Key.V or Key.Insert)
            || (shift && args.Key is Key.Insert or Key.Delete) || args.Key == Key.Apps || (shift && args.Key == Key.F10))
        { args.Handled = true; }
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs args)
    {
        if (!disposed && lifetime.IsPresented && DataContext is ExactGrantsViewModel state && sender is ListBox list)
        {
            // Clearing the page resets the native list as well; it is not a new user selection.
            if (state.Busy && state.PermissionRows.IsEmpty) { return; }
            try
            {
                state.SetPermissionSelection(list.SelectedItems?.OfType<PermissionGrantRow>() ?? []);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                state.ReportFailure(exception);
            }
        }
    }
}