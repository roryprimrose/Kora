using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Kora;

public sealed partial class ResponseWindow
{
    private Size compactSize = new(560, 690);
    private Size expandedSize = new(1120, 820);
    private long sessionPresentationEpoch;
    private volatile bool sessionWindowClosed;
    internal bool IsExpanded { get; private set; }
    internal bool IsSessionWorkspacePresented => IsVisible && WindowState != WindowState.Minimized && SessionWorkspace.IsVisible;
    internal bool IsSessionListPresented => IsSessionWorkspacePresented && SessionWorkspace.IsSessionSidebarPresented;
    internal SessionInteractionView SessionView => SessionWorkspace;
    internal bool PrepareDetailsLayout()
    {
        SessionWorkspace.UpdateResponsiveLayout();
        return IsExpanded && IsSessionWorkspacePresented;
    }
    internal Func<bool> CaptureSessionListPresentation()
    {
        var epoch = sessionPresentationEpoch;
        var presented = IsSessionListPresented;
        return () => presented && IsSessionListPresented && epoch == sessionPresentationEpoch;
    }
    internal Func<bool> CaptureSessionWorkspacePresentation()
    {
        var epoch = Interlocked.Read(ref sessionPresentationEpoch);
        var presented = IsSessionWorkspacePresented;
        return () => presented && epoch == Interlocked.Read(ref sessionPresentationEpoch);
    }

    internal void AttachSessions(SessionsViewModel model, SessionsPresentationOwner owner,
        Action management, Action newConversation)
    {
        SessionWorkspace.DataContext = model;
        SessionDictation.DataContext = model;
        SessionWorkspace.ManagementRequested += (_, _) => management();
        SessionWorkspace.NewConversationRequested += (_, _) => newConversation();
        SessionWorkspace.PresentationChanged += (_, _) => sessionPresentationEpoch++;
        owner.Attach(this);
    }

    internal void ShowSessions(bool expanded, bool activate = true, bool preservePresentation = false)
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
        RestoreMinimizedState();
        SetExpanded(expanded, preservePresentation);
        if (activate) { Activate(); }
    }

    private void SetExpanded(bool expanded, bool preservePresentation = false)
    {
        var wasExpanded = IsExpanded;
        var area = getWorkingArea();
        if (expanded && area is { } available
            && (available.Width / RenderScaling < 320 || available.Height / RenderScaling < 300))
        {
            CompactLayoutStatus.Text = "Workbench unavailable: the display cannot fit the minimum 320 by 300 layout. Current response controls remain available.";
            return;
        }
        CompactLayoutStatus.Text = string.Empty;
        if (!preservePresentation) { ClearHostedView(); }
        ResponseViewport.HorizontalScrollBarVisibility = expanded
            ? Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled : Avalonia.Controls.Primitives.ScrollBarVisibility.Auto;
        ResponseViewport.VerticalScrollBarVisibility = expanded
            ? Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled : Avalonia.Controls.Primitives.ScrollBarVisibility.Auto;
        ResponseLayout.MinWidth = expanded ? 0 : 480;
        ResponseLayout.MinHeight = expanded ? 0 : 570;
        if (IsExpanded != expanded)
        {
            placementRevision++;
            CaptureModePlacement();
            if (WindowState == WindowState.Normal)
            {
                if (IsExpanded) { expandedSize = new(Width, Height); }
                else { compactSize = new(Width, Height); }
            }
            applyingPlacement = true;
            try
            {
                WindowState = WindowState.Normal;
                IsExpanded = expanded;
                CanResize = expanded;
                MinWidth = expanded ? 320 : 480;
                MinHeight = expanded ? 300 : 570;
                var size = expanded ? expandedSize : compactSize;
                var bounds = expanded ? expandedBounds : compactBounds;
                Width = placementLoaded && bounds is not null ? bounds.Width : size.Width;
                Height = placementLoaded && bounds is not null ? bounds.Height : size.Height;
                if (placementLoaded && bounds is not null) { Position = new(bounds.X, bounds.Y); }
                KeepOnScreen();
                if (placementLoaded && expanded && expandedRestoreState == WindowState.Maximized)
                { WindowState = WindowState.Maximized; }
            }
            finally { applyingPlacement = false; }
            CaptureNormalBounds();
        }
        ConfigureChrome(expanded);
        ExpandedControls.IsVisible = expanded;
        ResponseLayout.Width = expanded ? double.NaN : Math.Max(480, Width);
        var conversation = expanded || SessionWorkspace.DataContext is SessionsViewModel { SelectedSessionRecord: not null };
        if (wasExpanded != expanded || SessionWorkspace.IsVisible != conversation) { sessionPresentationEpoch++; }
        if (!expanded) { SessionWorkspace.HideDetails(); }
        SessionWorkspace.SetCompactPresentation(!expanded);
        SessionWorkspace.Height = expanded ? double.NaN : Math.Max(420, Height - 160);
        if (!preservePresentation)
        {
            SessionWorkspace.IsVisible = conversation;
            SessionDictation.IsVisible = conversation;
            CurrentInteractionContent.IsVisible = !conversation;
        }
        CollapseSessions.IsVisible = expanded;
        ShowCurrentInteraction.IsVisible = conversation;
        HideInteraction.IsVisible = conversation;
        ExpandSessions.IsVisible = !expanded;
        SessionWorkspace.UpdateResponsiveLayout();
        UpdateNativeReviewLayout();
        FlushPlacement();
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
        => PresentCurrentInteraction();

    private void PresentCurrentInteraction()
    {
        ClearHostedView();
        sessionPresentationEpoch++;
        SessionWorkspace.IsVisible = false;
        SessionDictation.IsVisible = false;
        CurrentInteractionContent.IsVisible = true;
        ExpandSessions.IsVisible = true;
    }

    private void OnHideInteractionClicked(object? sender, RoutedEventArgs args) => HideResponse();

    private void OnSessionPresentationChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.Property == IsVisibleProperty || args.Property == WindowStateProperty)
        {
            sessionPresentationEpoch++;
            if (!IsVisible) { SessionWorkspace.HideDetails(); }
        }
    }
}
