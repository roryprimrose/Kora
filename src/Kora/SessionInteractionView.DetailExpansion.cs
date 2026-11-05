using Avalonia.Controls;

namespace Kora;

internal sealed partial class SessionInteractionView
{
    private (GridLength Conversation, GridLength Details)? splitBeforeExpansion;

    private void ToggleDetailsExpansion()
    {
        if (!DetailsVisible || !IsEffectivelyVisible || observedModel?.CanPresentDetails != true
            || evidenceAdmission?.Invoke() == false || TopLevel.GetTopLevel(this)?.IsVisible != true)
        {
            DetailNavigationStatus.Text = "Expand details unavailable: explicitly open an admitted result in the visible session workspace.";
            return;
        }
        if (splitBeforeExpansion is not null)
        {
            RestoreDetailsSplit();
            RefreshReturnNavigation();
            return;
        }
        var available = ReadingPanes.Bounds.Width - DetailsDivider.Width;
        if (!double.IsFinite(available) || available < ConversationPane.MinWidth + DetailsPane.MinWidth)
        {
            DetailNavigationStatus.Text = "Expand details unavailable: the current reading area cannot fit both minimum pane widths.";
            return;
        }
        splitBeforeExpansion = (ReadingPanes.ColumnDefinitions[0].Width, ReadingPanes.ColumnDefinitions[2].Width);
        ReadingPanes.ColumnDefinitions[0].Width = new(ConversationPane.MinWidth);
        ReadingPanes.ColumnDefinitions[2].Width = new(1, GridUnitType.Star);
        ExpandSessionDetails.Content = "Restore split";
        RefreshReturnNavigation();
    }

    private void RestoreDetailsSplit()
    {
        if (splitBeforeExpansion is not { } previous) { return; }
        splitBeforeExpansion = null;
        ReadingPanes.ColumnDefinitions[0].Width = previous.Conversation;
        ReadingPanes.ColumnDefinitions[2].Width = previous.Details;
        ExpandSessionDetails.Content = "Expand details";
    }
}
