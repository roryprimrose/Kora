using Avalonia.Controls;
using Avalonia.Threading;
using Kora.Application.Hosting;
using Kora.Core.Context;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora;

internal sealed class SessionAttachmentWindowController : IUserFilePicker, IDisposable
{
    private readonly Action<string> reportFailure;
    private readonly SessionFileAttachmentService service;
    private readonly SessionWorkspaceService sessions;
    private readonly ISessionWorkspaceAccess access;
    private readonly IUserFilePicker picker;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private LocalFilePreviewWindow? window;
    private CancellationTokenSource? lifetime;
    private Func<bool>? eligible;
    private HostId<SessionIdentity>? session;
    private Guid reviewing;
    private bool disposed;

    internal SessionAttachmentWindowController(Action<string> reportFailure, SessionFileAttachmentService service,
        SessionWorkspaceService sessions, ISessionWorkspaceAccess access, IUserFilePicker picker)
    {
        this.reportFailure = reportFailure;
        this.service = service;
        this.sessions = sessions;
        this.access = access;
        this.picker = picker;
        sessions.SessionRetired += OnRevoked;
        sessions.SessionLifecycleChanged += OnRevoked;
        service.Revoked += OnAttachmentRevoked;
        service.InspectionRevoked += CloseInspection;
        timer.Tick += OnTick;
        timer.Start();
    }

    public async Task<string?> SelectAsync(CancellationToken cancellationToken) =>
        await Dispatcher.UIThread.InvokeAsync(() => picker.SelectAsync(cancellationToken));

    internal async Task Open(SessionWorkspaceEntry target, bool attach, Func<bool> admission,
        CancellationToken token)
    {
        Close();
        var control = access.ControlRevision;
        lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        var cancellation = lifetime.Token;
        session = target.Authority.SessionId;
        bool Current() => !disposed && !cancellation.IsCancellationRequested && access.CanInspect
            && access.ControlRevision == control && admission() && session == target.Authority.SessionId;
        eligible = Current;
        if (!Current()) { throw new InvalidOperationException("The exact selected session is stale."); }
        var view = new LocalFilePreviewWindow(reportFailure);
        window = view;
        view.Closed += OnClosed;
        if (attach)
        {
            var result = await service.Select(target, this, Current, cancellation);
            if (result != LocalFileOutcome.Reviewed || !Current() || service.Review is not { } review)
            {
                Close();
                throw new InvalidOperationException("The exact native file was not reviewed. No attachment admitted.");
            }
            view.ShowAttachmentReview(review, target.Authority.Generation, async () =>
            {
                if (!Current()) { throw new InvalidOperationException("The original persistence review is stale."); }
                reviewing = Guid.Empty;
                var outcome = await service.Confirm(review.ReviewId, cancellation);
                if (outcome != LocalFileOutcome.Admitted)
                {
                    throw new InvalidOperationException("Attachment was not admitted; inspect durable status before retrying.");
                }
                await ShowRetained(view, target.Authority.SessionId, Current, cancellation);
            });
            reviewing = review.ReviewId;
        }
        else { await ShowRetained(view, target.Authority.SessionId, Current, cancellation); }
        if (!Current()) { Close(); return; }
        view.Show();
        view.Activate();
    }

    private async Task ShowRetained(LocalFilePreviewWindow view, HostId<SessionIdentity> exactSession,
        Func<bool> current, CancellationToken token)
    {
        var retained = await service.Read(exactSession, token);
        if (!current()) { throw new OperationCanceledException(token); }
        if (retained is null)
        {
            await ShowRemoval(view, exactSession, current, token);
            return;
        }
        view.ShowAttachment(retained, query => service.Search(retained, query, token),
            () => current() && ReferenceEquals(window, view), async () =>
        {
            await ShowRemoval(view, exactSession, current, token);
        });
    }

    private async Task ShowRemoval(LocalFilePreviewWindow view, HostId<SessionIdentity> exactSession,
        Func<bool> current, CancellationToken token)
    {
        var review = await service.PreviewRemoval(exactSession, token);
        if (!current()) { throw new OperationCanceledException(token); }
        view.ShowAttachmentRemoval(review, async () =>
        {
            if (!current()) { throw new InvalidOperationException("The exact removal preview is stale."); }
            view.Content = null;
            await service.Remove(review, current, token);
            reportFailure("Exact attachment revoked and owned database/journal copies removed. Original files unchanged.");
            Close();
        });
    }

    private void OnTick(object? sender, EventArgs args)
    {
        if (eligible is not null && (!eligible() || service.Review is { } review
            && review.Request.SessionId != session || reviewing != Guid.Empty && service.Review?.ReviewId != reviewing)) { Close(); }
    }

    private void OnRevoked(HostId<SessionIdentity> revoked)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Invoke(() => OnRevoked(revoked));
            return;
        }

        if (session == revoked) { Close(); }
    }

    internal void RevokeSession(HostId<SessionIdentity> revoked) => OnRevoked(revoked);

    private void OnAttachmentRevoked(HostId<SessionIdentity> revoked)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Invoke(() => OnAttachmentRevoked(revoked));
            return;
        }
        if (session != revoked) { return; }
        RevokeView();
    }

    private void RevokeView()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Invoke(RevokeView);
            return;
        }
        if (window is not { } view) { return; }
        window = null;
        view.Closed -= OnClosed;
        view.ClearAndClose();
    }

    private void CloseInspection()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Invoke(CloseInspection);
            return;
        }
        RevokeView();
        eligible = null;
        session = null;
        reviewing = Guid.Empty;
        lifetime?.Cancel();
        lifetime?.Dispose();
        lifetime = null;
    }

    private void OnClosed(object? sender, EventArgs args) => Close();

    internal void Close()
    {
        var view = window;
        window = null;
        eligible = null;
        session = null;
        reviewing = Guid.Empty;
        if (view is not null)
        {
            view.Closed -= OnClosed;
            view.ClearAndClose();
        }
        lifetime?.Cancel();
        lifetime?.Dispose();
        lifetime = null;
        service.Revoke();
    }

    public void Dispose()
    {
        disposed = true;
        timer.Stop();
        timer.Tick -= OnTick;
        sessions.SessionRetired -= OnRevoked;
        sessions.SessionLifecycleChanged -= OnRevoked;
        service.Revoked -= OnAttachmentRevoked;
        service.InspectionRevoked -= CloseInspection;
        Close();
    }
}
