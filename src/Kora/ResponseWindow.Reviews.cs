using Avalonia.Controls;

namespace Kora;

public sealed partial class ResponseWindow
{
    private NativeReviewTabsView? nativeReviews;
    private bool nativeReviewPaneRequested;

    internal void ShowNativeReview(Control owner, string title, Control body, Func<bool> admitted, Action<bool> presented)
    {
        if (sessionWindowClosed || !IsVisible || !viewModel.CanRevealPrivatePresentation || !admitted())
        { throw new InvalidOperationException("The original native review workbench is unavailable."); }
        if (!IsExpanded) { SetExpanded(true, preservePresentation: true); }
        nativeReviewPaneRequested = true;
        Activate();
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
        nativeReviews.ShowReview(owner, title, body, admitted, presented);
        UpdateNativeReviewLayout();
        UpdateLayout();
        if (body.IsEffectivelyVisible && admitted()) { body.Focus(); }
    }

    internal void RemoveNativeReview(Control owner) => nativeReviews?.RemoveReview(owner);
    internal void UpdateNativeReviewTitle(Control owner, string title) => nativeReviews?.UpdateTitle(owner, title);

    private void UpdateNativeReviewLayout()
    {
        ShowNativeReviews.IsVisible = IsExpanded && nativeReviews?.Count > 0 && !nativeReviewPaneRequested;
        var shown = IsExpanded && nativeReviewPaneRequested && nativeReviews?.Count > 0;
        var narrow = shown && Bounds.Width < 1000;
        NativeReviewContent.IsVisible = shown;
        NativeReviewDivider.IsVisible = shown && !narrow;
        WorkbenchContent.ColumnDefinitions[2].Width = shown && !narrow ? new(6) : new(0);
        WorkbenchContent.ColumnDefinitions[3].Width = shown && !narrow ? new(400) : new(0);
        Grid.SetColumn(NativeReviewContent, narrow ? 1 : 3);
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
