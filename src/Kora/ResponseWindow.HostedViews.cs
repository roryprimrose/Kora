using Avalonia.Controls;
using Avalonia.Interactivity;

using Kora.Application.Hosting;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora;

public sealed partial class ResponseWindow
{
    internal WorkbenchActivity ActiveActivity { get; private set; } = WorkbenchActivity.Sessions;
    private Action? openPermissionGrants;
    private Action? openEvidence;
    private Action? openCustomizations;
    private Action? openWork;
    internal void BindWorkNavigation(Action? open)
    {
        openWork = open;
        UpdateActivityNavigation();
    }
    internal void BindCustomizationNavigation(Action? open)
    {
        openCustomizations = open;
        UpdateActivityNavigation();
    }
    internal void BindEvidenceNavigation(Action? open)
    {
        openEvidence = open;
        UpdateActivityNavigation();
    }
    internal void BindPermissionNavigation(Action? open)
    {
        openPermissionGrants = open;
        UpdateActivityNavigation();
    }

    internal void ShowHostedView(WorkbenchActivity activity, Control view)
    {
        ArgumentNullException.ThrowIfNull(view);
        if (!Enum.IsDefined(activity)) { throw new ArgumentOutOfRangeException(nameof(activity)); }
        if (sessionWindowClosed || !viewModel.CanRevealPrivatePresentation)
        { throw new InvalidOperationException("The original owning workbench presentation is unavailable."); }
        if (IsExpanded && IsVisible && HostedViewContent.IsVisible && ReferenceEquals(HostedViewContent.Content, view))
        {
            RestoreMinimizedState();
            view.IsVisible = true;
            Activate();
            UpdateLayout();
            return;
        }
        ShowSessions(expanded: true);
        if (sessionWindowClosed || !IsExpanded || !IsVisible || !viewModel.CanRevealPrivatePresentation)
        { throw new InvalidOperationException("The requested workbench view could not be presented."); }
        SessionWorkspace.HideDetails();
        SessionWorkspace.IsVisible = false;
        SessionDictation.IsVisible = false;
        CurrentInteractionContent.IsVisible = false;
        HostedViewContent.Content = view;
        HostedViewContent.IsVisible = true;
        view.IsVisible = true;
        ActiveActivity = activity;
        sessionPresentationEpoch++;
        UpdateActivityNavigation();
        UpdateLayout();
    }

    internal void RemoveHostedView(Control view)
    {
        if (!ReferenceEquals(HostedViewContent.Content, view)) { return; }
        ClearHostedView();
        if (!sessionWindowClosed && IsExpanded && viewModel.CanRevealPrivatePresentation)
        { SessionWorkspace.IsVisible = true; }
        else if (!sessionWindowClosed && !viewModel.CanRevealPrivatePresentation) { HideResponse(); }
    }

    private void ClearHostedView()
    {
        if (HostedViewContent.Content is Control previous) { previous.IsVisible = false; }
        HostedViewContent.Content = null;
        HostedViewContent.IsVisible = false;
        ActiveActivity = WorkbenchActivity.Sessions;
        UpdateActivityNavigation();
    }

    private void UpdateActivityNavigation()
    {
        WorkbenchActivities.IsVisible = IsExpanded;
        SessionsActivity.IsEnabled = ActiveActivity != WorkbenchActivity.Sessions;
        SettingsActivity.IsEnabled = ActiveActivity != WorkbenchActivity.Settings;
        HelpActivity.IsEnabled = ActiveActivity != WorkbenchActivity.Help;
        PermissionsActivity.IsEnabled = openPermissionGrants is not null && ActiveActivity != WorkbenchActivity.Permissions;
        AuditActivity.IsEnabled = openEvidence is not null && ActiveActivity != WorkbenchActivity.Audit;
        CustomizationsActivity.IsEnabled = openCustomizations is not null && ActiveActivity != WorkbenchActivity.Customizations;
        WorkActivity.IsEnabled = openWork is not null && ActiveActivity != WorkbenchActivity.Work;
    }

    private void OnSessionsActivity(object? sender, RoutedEventArgs args) =>
        HostRequestRunner.Run(RequestOrigin.LocalUi, viewModel.ShowSessions,
            HostActivityLayer.Desktop, HostOperation.Presentation);
    private void OnSettingsActivity(object? sender, RoutedEventArgs args) =>
        HostRequestRunner.Run(RequestOrigin.LocalUi, viewModel.ShowSettings,
            HostActivityLayer.Desktop, HostOperation.Presentation);
    private void OnHelpActivity(object? sender, RoutedEventArgs args) =>
        HostRequestRunner.Run(RequestOrigin.LocalUi, viewModel.ShowDocumentation,
            HostActivityLayer.Desktop, HostOperation.Presentation);
    private void OnPermissionsActivity(object? sender, RoutedEventArgs args) =>
        HostRequestRunner.Run(RequestOrigin.LocalUi, () =>
            (openPermissionGrants ?? throw new InvalidOperationException("Permission grants navigation is not bound."))(),
            HostActivityLayer.Desktop, HostOperation.Presentation);
    private void OnAuditActivity(object? sender, RoutedEventArgs args) =>
        HostRequestRunner.Run(RequestOrigin.LocalUi, () =>
            (openEvidence ?? throw new InvalidOperationException("Audit/evidence navigation is not bound."))(),
            HostActivityLayer.Desktop, HostOperation.Presentation);
    private void OnCustomizationsActivity(object? sender, RoutedEventArgs args) =>
        HostRequestRunner.Run(RequestOrigin.LocalUi, () =>
            (openCustomizations ?? throw new InvalidOperationException("Customization navigation is not bound."))(),
            HostActivityLayer.Desktop, HostOperation.Presentation);
    private void OnWorkActivity(object? sender, RoutedEventArgs args) =>
        HostRequestRunner.Run(RequestOrigin.LocalUi, () =>
            (openWork ?? throw new InvalidOperationException("Work overview navigation is not bound."))(),
            HostActivityLayer.Desktop, HostOperation.Presentation);
}
