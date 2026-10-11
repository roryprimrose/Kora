using System.ComponentModel;

using Avalonia.Threading;

using Kora.Application.Hosting;
using Kora.Application.ViewModels;
using Kora.Core.Hosting;
using Kora.Core.Storage;

using Microsoft.Extensions.Logging;

namespace Kora;

internal sealed class AttachmentCatalogueWindowController : IDisposable
{
    private readonly MainViewModel main;
    private readonly SessionWorkspaceService sessions;
    private readonly SessionFileAttachmentService files;
    private readonly SessionAttachmentCatalogueService service;
    private readonly SessionAttachmentWindowController attachments;
    private readonly ISessionWorkspaceAccess access;
    private readonly ILogger<AttachmentCatalogueViewModel> logger;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private AttachmentCatalogueWindow? window;
    private bool disposed;

    internal AttachmentCatalogueWindowController(MainViewModel main, SessionWorkspaceService sessions,
        SessionFileAttachmentService files, SessionAttachmentWindowController attachments,
        ISessionWorkspaceAccess access, ILogger<AttachmentCatalogueViewModel> logger, SessionAttachmentCatalogueService service)
    {
        this.main = main;
        this.sessions = sessions;
        this.files = files;
        this.attachments = attachments;
        this.access = access;
        this.logger = logger;
        this.service = service;
        main.PrivacyClosureRequested += OnPrivacyClosure;
        main.PropertyChanged += OnMainChanged;
        sessions.WorkOverviewInvalidated += OnSourceChanged;
        sessions.SessionLifecycleChanged += OnSourceChanged;
        sessions.SessionRetired += OnSourceChanged;
        files.Revoked += OnSourceChanged;
        files.InspectionRevoked += OnInspectionRevoked;
        timer.Tick += OnTick;
        timer.Start();
    }

    private bool Admitted()
    {
        if (!Dispatcher.UIThread.CheckAccess()) { return Dispatcher.UIThread.Invoke(Admitted); }
        return !disposed && access.CanInspect && main.CanRevealPrivatePresentation;
    }

    internal void Open()
    {
        if (!Admitted())
        {
            main.ReportHostInteractionFailure("Retained attachment catalogue unavailable under current private ownership/privacy. No file was read.");
            return;
        }
        if (window is null)
        {
            AttachmentCatalogueWindow? owned = null;
            bool WindowAdmitted()
            {
                if (!Dispatcher.UIThread.CheckAccess()) { return Dispatcher.UIThread.Invoke(WindowAdmitted); }
                return owned is { IsVisible: true } && ReferenceEquals(window, owned) && Admitted();
            }
            var model = new AttachmentCatalogueViewModel(service.Read, Navigate, access, WindowAdmitted, logger);
            model.SelectionRevoked += attachments.CloseExact;
            var view = new AttachmentCatalogueWindow(model);
            // Admission is bound to this exact owned window; opening itself performs no metadata read.
            owned = view;
            window = view;
            view.Closed += OnClosed;
            view.Show();
        }
        window.Activate();
    }

    private async Task Navigate(SessionAttachmentSummary exact, Func<bool> admitted, CancellationToken token)
    {
        if (!admitted()) { throw new InvalidOperationException("The original exact catalogue selection was revoked."); }
        var target = await service.ResolveExact(exact, admitted, token);
        if (!admitted()) { throw new OperationCanceledException(token); }
        try
        {
            await attachments.Open(target, attach: false, admitted, token, exact);
            // Native asynchronous presentation is bracketed by the same exact original reference,
            // storage revision and current host-resolved session generation.
            _ = await service.ResolveExact(exact, admitted, token);
            if (!admitted()) { throw new OperationCanceledException(token); }
        }
        catch
        {
            attachments.CloseExact(exact);
            throw;
        }
    }

    private void OnSourceChanged(HostId<SessionIdentity> _) => OnInspectionRevoked();

    private void OnInspectionRevoked()
    {
        if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Invoke(OnInspectionRevoked); return; }
        if (window?.DataContext is AttachmentCatalogueViewModel model) { model.Revoke(); }
    }

    private void OnTick(object? sender, EventArgs args) => Synchronize();
    private void OnMainChanged(object? sender, PropertyChangedEventArgs args) => Synchronize();

    private void Synchronize()
    {
        if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Invoke(Synchronize); return; }
        if (!Admitted()) { Close(); }
        else if (window?.DataContext is AttachmentCatalogueViewModel model) { model.SynchronizeAuthority(); }
    }

    private void OnPrivacyClosure(object? sender, EventArgs args) => Close();
    private void OnClosed(object? sender, EventArgs args)
    {
        if (window?.DataContext is AttachmentCatalogueViewModel model) { model.SelectionRevoked -= attachments.CloseExact; }
        if (window is not null) { window.Closed -= OnClosed; }
        window = null;
    }

    private void Close()
    {
        if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Invoke(Close); return; }
        if (window?.DataContext is AttachmentCatalogueViewModel model) { model.Dispose(); }
        window?.Close();
        window = null;
    }

    public void Dispose()
    {
        if (disposed) { return; }
        disposed = true;
        timer.Stop();
        timer.Tick -= OnTick;
        main.PrivacyClosureRequested -= OnPrivacyClosure;
        main.PropertyChanged -= OnMainChanged;
        sessions.WorkOverviewInvalidated -= OnSourceChanged;
        sessions.SessionLifecycleChanged -= OnSourceChanged;
        sessions.SessionRetired -= OnSourceChanged;
        files.Revoked -= OnSourceChanged;
        files.InspectionRevoked -= OnInspectionRevoked;
        Close();
    }
}
