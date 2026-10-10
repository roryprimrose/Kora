using Avalonia.Controls;
using Avalonia.Markup.Xaml;

using Kora.Application.Interaction;

namespace Kora;

public sealed partial class ExactGrantsWindow : Window
{
    public ExactGrantsWindow()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public ExactGrantsWindow(ExactGrantsViewModel state) : this()
    {
        DataContext = state;
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs args)
    {
        if (DataContext is ExactGrantsViewModel state && sender is ListBox list)
        {
            // Clearing the page resets the native list as well; it is not a new user selection.
            if (state.Busy && state.Records.IsEmpty) { return; }
            try
            {
                state.SetBatchSelection(list.SelectedItems?.OfType<Kora.Core.Authorization.ExactGrantInspection>() ?? []);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                state.ReportFailure(exception);
            }
        }
    }
}