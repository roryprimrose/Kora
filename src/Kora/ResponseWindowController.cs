using Kora.Application.ViewModels;
using Kora.Application.Presentation;
using Kora.Core.Presentation;

using Microsoft.Extensions.Logging;

namespace Kora;

public sealed class ResponseWindowController : IDisposable, ICurrentResponsePresentation
{
    private readonly MainViewModel viewModel;
    private readonly ILogger<ResponseWindowController> logger;
    private ResponseWindow? window;
    private bool disposed;
    private readonly DetailWindowController? details;
    internal Action? PrepareDetailsHost { get; set; }

    internal ResponseWindow PrepareSessions(SessionsViewModel model, SessionsPresentationOwner owner,
        Action management, Action newConversation)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var current = EnsureWindow();
        if (current.SessionView.DataContext is null)
        { current.AttachSessions(model, owner, management, newConversation); }
        else if (!ReferenceEquals(current.SessionView.DataContext, model))
        { throw new InvalidOperationException("The response window belongs to another session presentation."); }
        return current;
    }

    private ResponseWindow EnsureWindow()
    {
        if (window is not null) { return window; }
        var opened = new ResponseWindow(viewModel, details);
        opened.Closed += (_, _) =>
        {
            if (ReferenceEquals(window, opened)) { window = null; }
        };
        window = opened;
        return opened;
    }

    public ResponseWindowController(
        MainViewModel viewModel,
        ILogger<ResponseWindowController> logger,
        DetailWindowController? detailController = null)
    {
        this.viewModel = viewModel;
        this.logger = logger;
        details = detailController;
        viewModel.WindowActionRequested += OnWindowActionRequested;
        viewModel.CurrentResponseChanged += OnCurrentResponseChanged;
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
        return () => !disposed && captured is { IsVisible: true } && ReferenceEquals(captured, window);
    }

    public bool Dismiss(Func<bool> admitted)
    {
        if (disposed || window is not { IsVisible: true } current || !admitted()) { return false; }
        current.HideResponse();
        return !current.IsVisible;
    }

    public bool OpenDetails(AdmittedDetailContent content, Func<bool> admitted)
    {
        if (disposed || window is not { IsVisible: true } current || !admitted()) { return false; }
        PrepareDetailsHost?.Invoke();
        return !disposed && ReferenceEquals(window, current) && admitted()
            && details?.OpenCurrentResponse(content, current, admitted) == true;
    }

    private void OnCurrentResponseChanged(object? sender, EventArgs eventArgs) =>
        details?.RetireUnavailableCurrentResponses();

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        viewModel.WindowActionRequested -= OnWindowActionRequested;
        viewModel.CurrentResponseChanged -= OnCurrentResponseChanged;
        if (window is not null)
        {
            window.Close();
            window = null;
        }
        details?.RetireUnavailableCurrentResponses();
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
