using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Kora;

internal sealed partial class SessionInteractionView
{
    private bool narrowSidebarRequested;
    private bool previouslyNarrow;
    private bool compactPresentation;
    internal void SetCompactPresentation(bool compact)
    {
        if (compactPresentation == compact) { return; }
        compactPresentation = compact;
        narrowSidebarRequested = false;
        if (compact) { ConversationTabs.SelectedItem = ConversationTab; }
        RetireResponsivePresentation();
        UpdateResponsiveLayout();
    }
    internal event EventHandler? PresentationChanged;
    internal bool IsSessionSidebarPresented => SessionSidebar.IsEffectivelyVisible;
    private bool IsNarrow => Bounds.Width > 0 && Bounds.Width < 1000;

    internal void UpdateResponsiveLayout()
    {
        var narrow = IsNarrow;
        var sidebar = !compactPresentation && (!narrow || narrowSidebarRequested);
        var sidebarChanged = SessionSidebar.IsVisible != sidebar;
        SessionSidebar.IsVisible = sidebar;
        SessionSidebarDivider.IsVisible = !compactPresentation && !narrow;
        SessionLayout.ColumnDefinitions[0].Width = sidebar ? new GridLength(230) : new GridLength(0);
        SessionLayout.ColumnDefinitions[1].Width = sidebar && !narrow ? GridLength.Auto : new GridLength(0);
        ToggleSessionSidebar.IsVisible = narrow && !compactPresentation;
        HideSessionSidebar.IsVisible = narrow && sidebar && !compactPresentation;
        ActivityTab.IsVisible = !compactPresentation;
        ReadSessionLogs.IsVisible = !compactPresentation;
        ReadSessionAudit.IsVisible = !compactPresentation;
        ConversationWorkGuidance.IsVisible = !compactPresentation;
        if (narrow && sidebar)
        {
            SessionLayout.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
            ReadingPanes.IsVisible = false;
        }
        else { ReadingPanes.IsVisible = true; }
        ConversationPane.IsVisible = !narrow || !DetailsVisible;
        DetailsDivider.IsVisible = DetailsVisible && !narrow;
        ShowChatPane.IsVisible = DetailsVisible && narrow;
        DetailsPane.MinWidth = narrow ? 0 : 400;
        if (narrow || previouslyNarrow)
        {
            ReadingPanes.ColumnDefinitions[0].Width = narrow && DetailsVisible ? new GridLength(0) : conversationWidth;
            ReadingPanes.ColumnDefinitions[1].Width = DetailsVisible && !narrow ? new GridLength(6) : new GridLength(0);
            ReadingPanes.ColumnDefinitions[2].Width = DetailsVisible ? detailWidth : new GridLength(0);
        }
        previouslyNarrow = narrow;
        if (sidebarChanged) { PresentationChanged?.Invoke(this, EventArgs.Empty); }
    }

    private void OnToggleSessionSidebar(object? sender, RoutedEventArgs args)
    {
        narrowSidebarRequested = !narrowSidebarRequested;
        HideDetails();
        RetireResponsivePresentation();
        UpdateResponsiveLayout();
    }

    private void OnShowChatPane(object? sender, RoutedEventArgs args)
    {
        HideDetailsAndRestoreFocus();
        RetireResponsivePresentation();
        UpdateResponsiveLayout();
    }

    private void RetireResponsivePresentation()
    {
        Interlocked.Increment(ref conversationPresentationRevision);
        replyPresentation.Retire();
        conversationReader.Suspend();
        PresentationChanged?.Invoke(this, EventArgs.Empty);
    }
}
