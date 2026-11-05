using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

using Kora.Application.Interaction;

namespace Kora;

public sealed partial class ExactGrantsWindow : Window
{
    public ExactGrantsWindow()
    {
        InitializeComponent();
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
        Closed += (_, _) =>
        {
            ExactInspection.Text = string.Empty;
            ExactBatchInspection.Text = string.Empty;
        };
    }

    public ExactGrantsWindow(ExactGrantsViewModel state) : this()
    {
        DataContext = state;
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
        if (DataContext is ExactGrantsViewModel state && sender is ListBox list)
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