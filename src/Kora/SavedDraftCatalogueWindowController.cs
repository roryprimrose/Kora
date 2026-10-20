using System.ComponentModel;

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
    internal void Open(Func<bool> admitted)
    {
        if (!Admitted() || !admitted())
        {
            main.ReportHostInteractionFailure("Saved conversation draft catalogue unavailable under private ownership/privacy. No draft read.");
            return;
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
            view.Show();
        }
        if (!Admitted() || !admitted())
        {
            Close();
            main.ReportHostInteractionFailure("Saved conversation draft catalogue unavailable: opening admission changed. No draft read.");
            return;
        }
        window?.Activate();
    }
    private void OnSourceChanged(HostId<SessionIdentity> _)
    {
        if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Invoke(() => OnSourceChanged(_)); return; }
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
    private void OnClosed(object? sender, EventArgs args)
    {
        if (window is not null) { window.Closed -= OnClosed; }
        window = null;
    }
    private void Close()
    {
        if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Invoke(Close); return; }
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
