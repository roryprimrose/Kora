using Avalonia.Controls;
using Avalonia.Threading;

namespace Kora;

internal sealed partial class LocalFilePreviewWindow
{
    private Func<bool>? isSearchCurrent;
    private ComboBox? searchModes;

    internal bool PrepareSearch(string query)
    {
        if (!Dispatcher.UIThread.CheckAccess() || !IsVisible || queryBox is null || searchResults is null
            || searchModes is null || isSearchCurrent?.Invoke() != true)
        {
            return false;
        }
        // Text/mode edits only invalidate pending results. Only the separate native button executes retrieval.
        searchModes.SelectedIndex = 0;
        queryBox.Text = null;
        queryBox.Text = query;
        searchSelection!.Invalidate();
        searchResults.Text = "PREPARED / REVIEW NEEDED. No search has run. Review this exact revision and click Search separately.";
        FocusSearch();
        return true;
    }
}
