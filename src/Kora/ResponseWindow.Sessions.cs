using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Kora;

public sealed partial class ResponseWindow
{
    private Size compactSize = new(560, 690);
    private Size expandedSize = new(1120, 820);
    private long sessionPresentationEpoch;
    private bool sessionWindowClosed;
    internal bool IsExpanded { get; private set; }
    internal bool IsSessionListPresented => IsVisible && SessionWorkspace.IsVisible;
    internal SessionInteractionView SessionView => SessionWorkspace;
    internal bool PrepareDetailsLayout()
    {
        const double minimumWidth = 1040;
        var area = getWorkingArea();
        if (area is not null && area.Value.Width / RenderScaling < minimumWidth)
        {
            viewModel.ReportHostInteractionFailure("Details unavailable: the current display work area cannot fit the minimum 1040-wide side-by-side conversation and Details layout.");
            return false;
        }
        MinWidth = minimumWidth;
        Width = Math.Max(Width, minimumWidth);
        KeepOnScreen();
        return true;
    }
    internal Func<bool> CaptureSessionListPresentation()
    {
        var epoch = sessionPresentationEpoch;
        var presented = IsSessionListPresented;
        return () => presented && IsSessionListPresented && epoch == sessionPresentationEpoch;
    }

    internal void AttachSessions(SessionsViewModel model, SessionsPresentationOwner owner,
        Action management, Action newConversation)
    {
        SessionWorkspace.DataContext = model;
        SessionDictation.DataContext = model;
        SessionWorkspace.ManagementRequested += (_, _) => management();
        SessionWorkspace.NewConversationRequested += (_, _) => newConversation();
        owner.Attach(this);
    }

    internal void ShowSessions(bool expanded, bool activate = true)
    {
        if (sessionWindowClosed || !viewModel.CanRevealPrivatePresentation) { return; }
        if (!IsVisible)
        {
            try { ShowActivated = activate; Show(); }
            catch (InvalidOperationException) when (sessionWindowClosed)
            {
                // Native opening can finish after a reentrant close has destroyed its backend.
                HideResponse();
                return;
            }
        }
        if (sessionWindowClosed || !viewModel.CanRevealPrivatePresentation)
        {
            HideResponse();
            return;
        }
        SetExpanded(expanded);
        if (activate) { Activate(); }
    }

    private void SetExpanded(bool expanded)
    {
        var area = getWorkingArea();
        if (expanded && area is { } available
            && (available.Width / RenderScaling < 820 || available.Height / RenderScaling < 560))
        {
            CompactLayoutStatus.Text = "Sessions unavailable: the current display work area cannot fit the minimum 820 by 560 conversation layout. The current response and controls remain available here.";
            return;
        }
        CompactLayoutStatus.Text = string.Empty;
        ResponseViewport.HorizontalScrollBarVisibility = expanded
            ? Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled : Avalonia.Controls.Primitives.ScrollBarVisibility.Auto;
        ResponseViewport.VerticalScrollBarVisibility = expanded
            ? Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled : Avalonia.Controls.Primitives.ScrollBarVisibility.Auto;
        ResponseLayout.MinWidth = expanded ? 0 : 480;
        ResponseLayout.MinHeight = expanded ? 0 : 570;
        if (IsExpanded != expanded)
        {
            if (IsExpanded) { expandedSize = new(Width, Height); }
            else { compactSize = new(Width, Height); }
            IsExpanded = expanded;
            CanResize = expanded;
            MinWidth = expanded ? 820 : 480;
            MinHeight = expanded ? 560 : 570;
            var size = expanded ? expandedSize : compactSize;
            Width = size.Width;
            Height = size.Height;
            KeepOnScreen();
        }
        ResponseLayout.Width = expanded ? double.NaN : Math.Max(480, Width);
        if (SessionWorkspace.IsVisible != expanded) { sessionPresentationEpoch++; }
        if (!expanded) { SessionWorkspace.HideDetails(); }
        SessionWorkspace.IsVisible = expanded;
        SessionDictation.IsVisible = expanded;
        CurrentInteractionContent.IsVisible = !expanded;
        CollapseSessions.IsVisible = expanded;
        ShowCurrentInteraction.IsVisible = expanded;
        HideInteraction.IsVisible = expanded;
        ExpandSessions.IsVisible = !expanded;
        RestartResponseTimeout();
    }

    private void KeepOnScreen()
    {
        if (getWorkingArea() is not { } area) { return; }
        var scale = RenderScaling;
        if (!IsExpanded)
        {
            MinWidth = Math.Min(480, area.Width / scale);
            MinHeight = Math.Min(570, area.Height / scale);
        }
        Width = Math.Min(Width, Math.Max(MinWidth, area.Width / scale));
        Height = Math.Min(Height, Math.Max(MinHeight, area.Height / scale));
        var width = (int)Math.Ceiling(Width * scale);
        var height = (int)Math.Ceiling(Height * scale);
        Position = new(Math.Max(area.X, Math.Min(Position.X, area.Right - width)),
            Math.Max(area.Y, Math.Min(Position.Y, area.Bottom - height)));
    }

    private void FitCompactWorkingArea()
    {
        if (IsExpanded) { return; }
        if (getWorkingArea() is { } area
            && (Width * RenderScaling > area.Width || Height * RenderScaling > area.Height))
        {
            positionInitialized = false;
            KeepOnScreen();
            positionInitialized = true;
        }
        ResponseLayout.Width = Math.Max(480, Width);
    }

    private void OnExpandSessionsClicked(object? sender, RoutedEventArgs args) => viewModel.ShowSessions();
    private void OnCollapseSessionsClicked(object? sender, RoutedEventArgs args) => SetExpanded(false);
    private void OnCurrentInteractionClicked(object? sender, RoutedEventArgs args)
    {
        sessionPresentationEpoch++;
        SessionWorkspace.IsVisible = false;
        SessionDictation.IsVisible = false;
        CurrentInteractionContent.IsVisible = true;
        ExpandSessions.IsVisible = true;
    }

    private void OnHideInteractionClicked(object? sender, RoutedEventArgs args) => HideResponse();

    private void OnSessionPresentationChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.Property == IsVisibleProperty)
        {
            sessionPresentationEpoch++;
            if (!IsVisible) { SessionWorkspace.HideDetails(); }
        }
    }
}
