using Kora.Application.ViewModels;
using Kora.Application.Presentation;
using Kora.Core.Presentation;
using Kora.Core.Configuration;

using Microsoft.Extensions.Logging;

namespace Kora;

public sealed partial class ResponseWindowController : IDisposable, ICurrentResponsePresentation
{
    private readonly MainViewModel viewModel;
    private readonly ILogger<ResponseWindowController> logger;
    private ResponseWindow? window;
    private bool disposed;
    private readonly DetailWindowController? details;
    private readonly IWorkbenchPlacementPreferences? placementPreferences;
    private Task retiredPlacementOperations = Task.CompletedTask;
#pragma warning disable VSTHRD003 // Joins only placement writes retained by this controller across its owned window closure.
    internal Task PlacementOperation => Task.WhenAll(retiredPlacementOperations, window?.PlacementOperation ?? Task.CompletedTask);
#pragma warning restore VSTHRD003
    internal Action? PrepareDetailsHost { get; set; }
    private Action? openPermissionGrants;
    private Action? openEvidence;
    private Action? openCustomizations;
    private Action? openWork;
    internal Action? OpenWork
    {
        get => openWork;
        set { openWork = value; window?.BindWorkNavigation(value); }
    }
    internal Action? OpenCustomizations
    {
        get => openCustomizations;
        set { openCustomizations = value; window?.BindCustomizationNavigation(value); }
    }
    internal Action? OpenEvidence
    {
        get => openEvidence;
        set { openEvidence = value; window?.BindEvidenceNavigation(value); }
    }
    internal Action? OpenPermissionGrants
    {
        get => openPermissionGrants;
        set
        {
            openPermissionGrants = value;
            window?.BindPermissionNavigation(value);
        }
    }
    internal ResponseWindow PrepareWorkbenchOwner() => EnsureWindow();
    internal void ShowHostedView(WorkbenchActivity activity, Avalonia.Controls.Control view) =>
        EnsureWindow().ShowHostedView(activity, view);

    internal ResponseWindow PrepareSessions(SessionsViewModel model, SessionsPresentationOwner owner,
        Action management, Action newConversation)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var current = EnsureWindow();
        if (current.SessionView.DataContext is null)
        { current.AttachSessions(model, owner, management, newConversation); }
        else if (!ReferenceEquals(current.SessionView.DataContext, model))
        { throw new InvalidOperationException("The response window belongs to another session presentation."); }
        model.BindAutomaticDetails(this);
        return current;
    }

    private ResponseWindow EnsureWindow()
    {
        if (window is not null) { return window; }
        var opened = new ResponseWindow(viewModel, details, null, null, placementPreferences, logger);
        opened.PrepareDocumentHost = () => PrepareDetailsHost?.Invoke();
        opened.BindPermissionNavigation(OpenPermissionGrants);
        opened.BindEvidenceNavigation(OpenEvidence);
        opened.BindCustomizationNavigation(OpenCustomizations);
        opened.BindWorkNavigation(OpenWork);
        ObserveAutomaticDetailsWindow(opened);
        opened.Closed += (_, _) =>
        {
            retiredPlacementOperations = Task.WhenAll(retiredPlacementOperations, opened.PlacementOperation);
            if (ReferenceEquals(window, opened)) { window = null; }
        };
        window = opened;
        return opened;
    }

    public ResponseWindowController(
        MainViewModel viewModel,
        ILogger<ResponseWindowController> logger,
        DetailWindowController? detailController = null,
        IWorkbenchPlacementPreferences? placementPreferences = null)
    {
        this.viewModel = viewModel;
        this.logger = logger;
        details = detailController;
        this.placementPreferences = placementPreferences;
        viewModel.WindowActionRequested += OnWindowActionRequested;
        viewModel.ResponseRestoreRequested += OnResponseRestoreRequested;
        viewModel.CurrentResponseChanged += OnCurrentResponseChanged;
        viewModel.PrivacyClosureRequested += OnPrivacyClosureRequested;
        viewModel.BindCurrentResponsePresentation(this);
        viewModel.BindModelActionReviewPresentation(PresentModelActionReview);
    }

    private bool PresentModelActionReview(ModelActionReviewTarget target)
    {
        if (disposed || !viewModel.IsModelActionReviewCurrent(target)) { return false; }
        return EnsureWindow().ShowModelActionReview(target);
    }

    public Func<bool> CaptureLifetime()
    {
        var captured = window;
        return () => !disposed && captured is { IsVisible: true, WindowState: not Avalonia.Controls.WindowState.Minimized }
            && ReferenceEquals(captured, window);
    }

    public bool Dismiss(Func<bool> admitted)
    {
        if (disposed || window is not { IsVisible: true } current || !admitted()) { return false; }
        current.HideResponse();
        return !current.IsVisible;
    }

    public async Task<bool> OpenDetailsAsync(AdmittedDetailContent content, Func<bool> admitted)
    {
        if (disposed || window is not { IsVisible: true } current || !admitted()) { return false; }
        PrepareDetailsHost?.Invoke();
        bool Current() => !disposed && ReferenceEquals(window, current) && current.IsVisible && admitted();
        return Current() && details is not null
            && await details.OpenCurrentResponseAsync(content, current, Current) && Current();
    }

    private void OnCurrentResponseChanged(object? sender, EventArgs eventArgs) =>
        details?.RetireUnavailableCurrentResponses();

    private void OnPrivacyClosureRequested(object? sender, EventArgs eventArgs)
    {
        window?.Close();
        details?.ClearForPrivacy();
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        viewModel.WindowActionRequested -= OnWindowActionRequested;
        viewModel.ResponseRestoreRequested -= OnResponseRestoreRequested;
        viewModel.CurrentResponseChanged -= OnCurrentResponseChanged;
        viewModel.PrivacyClosureRequested -= OnPrivacyClosureRequested;
        if (window is not null)
        {
            window.Close();
            window = null;
        }
        details?.RetireUnavailableCurrentResponses();
    }

    private void OnResponseRestoreRequested(object? sender, EventArgs args)
    {
        if (!disposed && viewModel.CanRevealPrivatePresentation)
        { EnsureWindow().ShowResponse(restoreMinimized: true); }
    }

    private void OnWindowActionRequested(object? sender, WindowAction action)
    {
        if (action is WindowAction.Show or WindowAction.ShowPresence && !viewModel.CanRevealPrivatePresentation)
        {
            return;
        }
        switch (action)
        {
            case WindowAction.Show when viewModel.IsVisualResponseVisible:
                DesktopLog.Debug(logger, "Showing the visual response window");
                EnsureWindow().ShowResponse();

                break;
            case WindowAction.Hide:
                window?.HideResponse();
                break;
            case WindowAction.ShowPresence:
            case WindowAction.Show:
            case WindowAction.Close:
                break;
            default:
                throw new InvalidOperationException($"Unknown window action: {action}.");
        }
    }
}
