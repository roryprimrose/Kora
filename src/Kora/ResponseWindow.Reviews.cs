using Avalonia.Controls;

namespace Kora;

public sealed partial class ResponseWindow
{
    private NativeReviewTabsView? nativeReviews;
    private Control[]? conversationColumnControls;
    private bool nativeReviewPaneRequested;

    internal void ShowNativeReview(Control owner, string title, Control body, Func<bool> admitted, Action<bool> presented,
        bool activate = true)
    {
        if (sessionWindowClosed || !IsVisible || !viewModel.CanRevealPrivatePresentation || !admitted())
        { throw new InvalidOperationException("The original native review workbench is unavailable."); }
        if (!IsExpanded) { SetExpanded(true, preservePresentation: true); }
        nativeReviewPaneRequested = true;
        if (activate) { Activate(); }
        if (nativeReviews is null)
        {
            nativeReviews = new(this);
            NativeReviewContent.Content = nativeReviews;
            nativeReviews.EntriesChanged += (_, _) => UpdateNativeReviewLayout();
            nativeReviews.ReturnRequested += (_, _) =>
            {
                nativeReviewPaneRequested = false;
                UpdateNativeReviewLayout();
                if (SessionWorkspace.IsConversationPresented) { SessionWorkspace.Focus(); }
            };
            SizeChanged += (_, _) => UpdateNativeReviewLayout();
            Closed += (_, _) => nativeReviews.Dispose();
        }
        nativeReviews.ShowReview(owner, title, body, admitted, presented, requireActivation: activate);
        UpdateNativeReviewLayout();
        UpdateLayout();
        if (activate && body.IsEffectivelyVisible && admitted()) { body.Focus(); }
    }

    internal void RemoveNativeReview(Control owner) => nativeReviews?.RemoveReview(owner);
    internal void UpdateNativeReviewTitle(Control owner, string title) => nativeReviews?.UpdateTitle(owner, title);

    // Captured before any side swap so the set stays the original conversation column.
    private Control[] ConversationColumnControls => conversationColumnControls ??=
    [
        .. WorkbenchContent.Children.Where(child => Grid.GetColumn(child) == 1 && !ReferenceEquals(child, NativeReviewContent)),
    ];

    private void UpdateNativeReviewLayout()
    {
        ShowNativeReviews.IsVisible = IsExpanded && nativeReviews?.Count > 0 && !nativeReviewPaneRequested;
        var shown = IsExpanded && nativeReviewPaneRequested && nativeReviews?.Count > 0;
        var narrow = shown && Bounds.Width < 1000;
        var side = shown && !narrow;
        var conversationOnRight = viewModel.IsConversationOnRight;
        // Column 0 is the activity bar; the conversation and review pane trade columns 1 and 3 around the divider.
        var conversationColumn = conversationOnRight ? 3 : 1;
        var reviewColumn = conversationOnRight ? 1 : 3;
        WorkbenchContent.ColumnDefinitions[conversationColumn].Width = GridLength.Star;
        WorkbenchContent.ColumnDefinitions[2].Width = side ? new(6) : new(0);
        WorkbenchContent.ColumnDefinitions[reviewColumn].Width = side ? new(400) : new(0);
        foreach (var conversation in ConversationColumnControls) { Grid.SetColumn(conversation, conversationColumn); }
        NativeReviewContent.IsVisible = shown;
        NativeReviewDivider.IsVisible = side;
        Grid.SetColumn(NativeReviewContent, narrow ? conversationColumn : reviewColumn);
        if (narrow)
        {
            if (SessionWorkspace.IsVisible) { SessionWorkspace.HideDetails(); }
            NativeReviewContent.MinHeight = 220;
        }
        else { NativeReviewContent.MinHeight = 0; }
        nativeReviews?.RefreshPresentation();
    }

    private void OnShowNativeReviews(object? sender, Avalonia.Interactivity.RoutedEventArgs args)
    {
        nativeReviewPaneRequested = true;
        UpdateNativeReviewLayout();
    }
}
