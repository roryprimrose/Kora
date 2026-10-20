using System.ComponentModel;

using Avalonia;
using Avalonia.Threading;

using Kora.Application.Hosting;
using Kora.Application.ViewModels;
using Kora.Core.Hosting;
using Kora.Core.Storage;

using Microsoft.Extensions.Logging;

namespace Kora;

internal sealed class SavedDraftCatalogueWindowController : IDisposable
{
    private readonly MainViewModel main;
    private readonly SessionWorkspaceService service;
    private readonly SessionsWindowController sessions;
    private readonly ISessionWorkspaceAccess access;
    private readonly ILogger<SavedDraftCatalogueViewModel> logger;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private SavedDraftCatalogueWindow? window;
    private bool disposed;
    private long preparationRevision;

    internal Func<bool> CapturePreparationLifetime()
    {
        var revision = Interlocked.Read(ref preparationRevision);
        return () => !disposed && revision == Interlocked.Read(ref preparationRevision);
    }

    internal Func<bool> CapturePreparationSource()
    {
        if (!Dispatcher.UIThread.CheckAccess()) { return Dispatcher.UIThread.Invoke(CapturePreparationSource); }
        return window is null ? static () => true
            : window.DataContext is SavedDraftCatalogueViewModel model ? model.CaptureQueryLifetime() : static () => false;
    }

    internal bool PrepareExactId(Guid sessionId, Func<bool> admitted, Func<bool> sourceAdmitted)
    {
        if (!Admitted() || !admitted() || !sourceAdmitted() || sessionId == Guid.Empty) { return false; }
        if (!TryOpen(admitted, reportFailure: false)) { return false; }
        return window is { } view && view.PrepareExactId(sessionId,
            () => ReferenceEquals(window, view) && Admitted() && admitted(), sourceAdmitted);
    }

    internal SavedDraftCatalogueWindowController(MainViewModel main, SessionWorkspaceService service,
        SessionsWindowController sessions, ISessionWorkspaceAccess access, ILogger<SavedDraftCatalogueViewModel> logger)
    {
        this.main = main;
        this.service = service;
        this.sessions = sessions;
        this.access = access;
        this.logger = logger;
        main.PrivacyClosureRequested += OnPrivacyClosure;
        main.PropertyChanged += OnMainChanged;
        service.WorkOverviewInvalidated += OnSourceChanged;
        service.SessionLifecycleChanged += OnSourceChanged;
        service.SessionRetired += OnSourceChanged;
        timer.Tick += OnTick;
        timer.Start();
    }

    private bool Admitted()
    {
        if (!Dispatcher.UIThread.CheckAccess()) { return Dispatcher.UIThread.Invoke(Admitted); }
        return !disposed && access.CanInspect && main.CanRevealPrivatePresentation;
    }
    internal void Open() => Open(static () => true);
    internal void Open(Func<bool> admitted) => TryOpen(admitted, reportFailure: true);
    private bool TryOpen(Func<bool> admitted, bool reportFailure)
    {
        if (!Admitted() || !admitted())
        {
            if (reportFailure) { main.ReportHostInteractionFailure("Saved conversation draft catalogue unavailable under private ownership/privacy. No draft read."); }
            return false;
        }
        if (window is null)
        {
            SavedDraftCatalogueWindow? owned = null;
            bool WindowAdmitted()
            {
                if (!Dispatcher.UIThread.CheckAccess()) { return Dispatcher.UIThread.Invoke(WindowAdmitted); }
                return owned is { IsVisible: true } && ReferenceEquals(window, owned) && Admitted();
            }
            var model = new SavedDraftCatalogueViewModel(service, sessions.NavigateSavedDraftAsync,
                access, WindowAdmitted, logger);
            var view = new SavedDraftCatalogueWindow(model);
            owned = view;
            window = view;
            view.Closed += OnClosed;
            view.PropertyChanged += OnWindowChanged;
            view.Show();
        }
        if (!Admitted() || !admitted())
        {
            Close();
            if (reportFailure) { main.ReportHostInteractionFailure("Saved conversation draft catalogue unavailable: opening admission changed. No draft read."); }
            return false;
        }
        window?.Activate();
        // Initial bindings observed the not-yet-visible window and must re-evaluate admission after Show.
        if (window?.DataContext is SavedDraftCatalogueViewModel visibleModel) { visibleModel.NotifyPresentationAdmission(); }
        return window is { IsVisible: true };
    }
    private void OnSourceChanged(HostId<SessionIdentity> _)
    {
        if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Invoke(() => OnSourceChanged(_)); return; }
        preparationRevision++;
        if (window?.DataContext is SavedDraftCatalogueViewModel model) { model.Revoke(); }
    }
    private void OnTick(object? sender, EventArgs args) => Synchronize();
    private void OnMainChanged(object? sender, PropertyChangedEventArgs args) => Synchronize();
    private void Synchronize()
    {
        if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Invoke(Synchronize); return; }
        if (!Admitted()) { Close(); }
        else if (window?.DataContext is SavedDraftCatalogueViewModel model) { model.SynchronizeAuthority(); }
    }
    private void OnPrivacyClosure(object? sender, EventArgs args) => Close();
    private void OnWindowChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.Property != Visual.IsVisibleProperty || sender is not SavedDraftCatalogueWindow { IsVisible: false } view) { return; }
        preparationRevision++;
        if (view.DataContext is SavedDraftCatalogueViewModel model) { model.Revoke(); }
    }
    private void OnClosed(object? sender, EventArgs args)
    {
        preparationRevision++;
        if (window is not null)
        {
            window.Closed -= OnClosed;
            window.PropertyChanged -= OnWindowChanged;
        }
        window = null;
    }
    private void Close()
    {
        if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Invoke(Close); return; }
        preparationRevision++;
        if (window?.DataContext is SavedDraftCatalogueViewModel model) { model.Dispose(); }
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
        service.WorkOverviewInvalidated -= OnSourceChanged;
        service.SessionLifecycleChanged -= OnSourceChanged;
        service.SessionRetired -= OnSourceChanged;
        Close();
    }
}
