using Avalonia.Controls;
using Avalonia.Threading;
using Kora.Application.Hosting;
using Kora.Application.ViewModels;
using Kora.Core.Context;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora;

internal sealed partial class SessionAttachmentWindowController : IUserFilePicker, IRetainedAttachmentViewer, IDisposable
{
    private readonly Action<string> reportFailure;
    private readonly SessionFileAttachmentService service;
    private readonly SessionWorkspaceService sessions;
    private readonly ISessionWorkspaceAccess access;
    private readonly IUserFilePicker picker;
    private readonly MainViewModel? typographyHost;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private LocalFilePreviewWindow? window;
    private CancellationTokenSource? lifetime;
    private CancellationTokenRegistration inspectionCancellation;
    private Func<bool>? eligible;
    private HostId<SessionIdentity>? session;
    private SessionAttachmentSummary? catalogueSelection;
    private Guid reviewing;
    private long viewRevision;
    private bool disposed;

    internal SessionAttachmentWindowController(Action<string> reportFailure, SessionFileAttachmentService service,
        SessionWorkspaceService sessions, ISessionWorkspaceAccess access, IUserFilePicker picker, MainViewModel? typographyHost = null)
    {
        this.reportFailure = reportFailure;
        this.service = service;
        this.sessions = sessions;
        this.access = access;
        this.picker = picker;
        this.typographyHost = typographyHost;
        sessions.SessionRetired += OnRevoked;
        sessions.SessionLifecycleChanged += OnRevoked;
        service.Revoked += OnAttachmentRevoked;
        service.InspectionRevoked += CloseInspection;
        timer.Tick += OnTick;
        timer.Start();
    }

    public async Task<string?> SelectAsync(CancellationToken cancellationToken) =>
        await Dispatcher.UIThread.InvokeAsync(() => picker.SelectAsync(cancellationToken));

    public async Task Open(SessionWorkspaceEntry target, SessionAttachmentSummary exact,
        Func<bool> admitted, CancellationToken token) =>
        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var previousLifetime = lifetime;
            try { await Open(target, attach: false, admitted, token, exact, inspectionOnly: true); }
            catch
            {
                if (!ReferenceEquals(lifetime, previousLifetime) && ReferenceEquals(catalogueSelection, exact))
                { Close(revokeService: false); }
                throw;
            }
        });

    internal async Task Open(SessionWorkspaceEntry target, bool attach, Func<bool> admission,
        CancellationToken token, SessionAttachmentSummary? expected = null, bool inspectionOnly = false)
    {
        if (expected is not null && !service.IsQuiescent)
        {
            throw new InvalidOperationException("Finish or discard the current native file review/control before exact catalogue navigation.");
        }
        // Exact catalogue navigation owns and cancels this controller's old lifetime, not
        // the independent catalogue publisher. Other existing routes retain global revocation.
        Close(revokeService: expected is null);
        catalogueSelection = expected;
        var control = access.ControlRevision;
        lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        var openingLifetime = lifetime;
        var cancellation = lifetime.Token;
        if (inspectionOnly) { inspectionCancellation = cancellation.Register(() => PostForCurrentInspection(CloseInspection)); }
        session = target.Authority.SessionId;
        bool Current() => !disposed && !cancellation.IsCancellationRequested && access.CanInspect
            && access.ControlRevision == control && admission() && session == target.Authority.SessionId
            && ReferenceEquals(lifetime, openingLifetime);
        eligible = Current;
        if (!Current()) { throw new InvalidOperationException("The exact selected session is stale."); }
        var view = new LocalFilePreviewWindow(reportFailure, typographyHost);
        window = view;
        view.Closed += OnClosed;
        if (attach)
        {
            var result = await service.Select(target, this, Current, cancellation);
            if (result != LocalFileOutcome.Reviewed || !Current() || service.Review is not { } review)
            {
                if (ReferenceEquals(lifetime, openingLifetime)) { Close(); }
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
                await ShowRetained(view, target, Current, cancellation);
            });
            reviewing = review.ReviewId;
        }
        else { await ShowRetained(view, target, Current, cancellation, expected, inspectionOnly); }
        if (inspectionOnly)
        {
            var resolved = await service.ResolveInspection(expected!, Current, cancellation);
            if (resolved.Authority != target.Authority)
            { throw new InvalidOperationException("The exact session generation changed before native disclosure."); }
        }
        if (!Current())
        {
            if (ReferenceEquals(lifetime, openingLifetime)) { Close(); }
            return;
        }
        window?.Show();
        window?.Activate();
    }

    private async Task ShowRetained(LocalFilePreviewWindow view, SessionWorkspaceEntry target,
        Func<bool> current, CancellationToken token, SessionAttachmentSummary? expected = null, bool inspectionOnly = false)
    {
        var exactSession = target.Authority.SessionId;
        SessionFileAttachment? retained;
        try { retained = await service.Read(exactSession, token); }
        catch (SessionFileReplacementHeldException exception)
        {
            if (expected is not null) { throw new InvalidOperationException("The selected exact attachment is now held. Refresh the catalogue; no alternate body was opened.", exception); }
            // Failed certification never falls back to either body; only reviewed removal is available.
            var recovery = NewView();
            await ShowRemoval(recovery, exactSession, current, token);
            if (current()) { recovery.Show(); recovery.Activate(); }
            return;
        }
        if (!current()) { throw new OperationCanceledException(token); }
        if (retained is null)
        {
            if (expected is not null) { throw new InvalidOperationException("The selected exact attachment is unavailable. Refresh the catalogue."); }
            await ShowRemoval(view, exactSession, current, token);
            return;
        }
        expected?.RequireAttachment(retained);
        view.ShowAttachment(retained, async (query, mode, searchToken) =>
        {
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token, searchToken);
            return await service.Search(retained, query, mode, cancellation.Token);
        },
            () => current() && ReferenceEquals(window, view), inspectionOnly ? null : async () =>
        {
            await ShowRemoval(view, exactSession, current, token);
        }, !inspectionOnly && target.Authority.IsActive && access.CanControl ? async () =>
        {
            if (!current()) { throw new InvalidOperationException("The selected active session changed. Inspect again."); }
            var outcome = await service.SelectReplacement(target, retained, this, current, token);
            if (outcome != LocalFileOutcome.Reviewed || !current() || service.ReplacementReview is not { } replacement)
            {
                throw new InvalidOperationException("Replacement not reviewed; the old durable snapshot is unchanged. Inspect again.");
            }
            var reviewView = NewView();
            reviewView.ShowAttachmentReplacement(replacement, async () =>
            {
                if (!current()) { throw new InvalidOperationException("The exact old/new replacement review is stale."); }
                reviewing = Guid.Empty;
                var result = await service.Confirm(replacement.Next.ReviewId, token);
                if (result != LocalFileOutcome.Admitted)
                {
                    throw new InvalidOperationException("No replacement completion claimed. Inspect exact durable status and copy recovery.");
                }
                var completedView = NewView();
                await ShowRetained(completedView, target, current, token);
                if (current()) { completedView.Show(); completedView.Activate(); }
            });
            reviewing = replacement.Next.ReviewId;
            reviewView.Show();
            reviewView.Activate();
        } : null, async navigationToken =>
        {
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token, navigationToken);
            if (!current() || !ReferenceEquals(window, view))
            {
                throw new InvalidOperationException("The exact retained inspection is no longer current.");
            }
            var resolved = await service.Read(exactSession, cancellation.Token);
            if (!current() || !ReferenceEquals(window, view)
                || resolved?.StorageRevision != retained.StorageRevision || resolved.File.Reference != retained.File.Reference)
            {
                if (ReferenceEquals(window, view))
                {
                    reportFailure("The exact retained revision is unavailable. The old source view was cleared; inspect again.");
                    RevokeView();
                }
                throw new InvalidOperationException("The exact retained revision is unavailable. Inspect again.");
            }
            return resolved.File;
        });
    }

    private LocalFilePreviewWindow NewView()
    {
        RevokeView();
        var view = new LocalFilePreviewWindow(reportFailure, typographyHost);
        window = view;
        view.Closed += OnClosed;
        return view;
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

    private void PostForCurrentInspection(Action notification, bool exactView = false)
    {
        var expectedLifetime = Volatile.Read(ref lifetime);
        var expected = Volatile.Read(ref viewRevision);
        // Global revocation follows the inspection lifetime; body invalidation also binds its exact view.
        Dispatcher.UIThread.Post(() =>
        {
            if (ReferenceEquals(expectedLifetime, lifetime)
                && (!exactView || expected == Volatile.Read(ref viewRevision))) { notification(); }
        });
    }

    private void OnRevoked(HostId<SessionIdentity> revoked)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            PostForCurrentInspection(() => OnRevoked(revoked));
            return;
        }

        if (session == revoked) { Close(); }
    }

    internal void RevokeSession(HostId<SessionIdentity> revoked) => OnRevoked(revoked);

    private void OnAttachmentRevoked(HostId<SessionIdentity> revoked)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            PostForCurrentInspection(() => OnAttachmentRevoked(revoked), exactView: true);
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
        Interlocked.Increment(ref viewRevision);
        if (window is not { } view) { return; }
        window = null;
        view.Closed -= OnClosed;
        view.ClearAndClose();
    }

    private void CloseInspection()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            PostForCurrentInspection(CloseInspection);
            return;
        }
        RevokeView();
        eligible = null;
        session = null;
        reviewing = Guid.Empty;
        lifetime?.Cancel();
        inspectionCancellation.Dispose();
        lifetime?.Dispose();
        lifetime = null;
    }

    private void OnClosed(object? sender, EventArgs args) => Close();

    internal void CloseExact(SessionAttachmentSummary original)
    {
        if (ReferenceEquals(catalogueSelection, original)) { Close(revokeService: false); }
    }

    internal void Close(bool revokeService = true)
    {
        Interlocked.Increment(ref viewRevision);
        var view = window;
        window = null;
        eligible = null;
        session = null;
        catalogueSelection = null;
        reviewing = Guid.Empty;
        if (view is not null)
        {
            view.Closed -= OnClosed;
            view.ClearAndClose();
        }
        lifetime?.Cancel();
        inspectionCancellation.Dispose();
        lifetime?.Dispose();
        lifetime = null;
        if (revokeService) { service.Revoke(); }
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
