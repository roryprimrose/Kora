using System.ComponentModel;

using Avalonia.Threading;

using Kora.Application.Interaction;
using Kora.Application.ViewModels;
using Kora.Core.Storage;
using Kora.Core.Hosting;

using Microsoft.Extensions.Logging;

namespace Kora;

internal sealed class ExactGrantsWindowController : IDisposable
{
    private readonly MainViewModel main;
    private readonly IExactGrantStore store;
    private readonly ExactGrantControlAdmission control;
    private readonly ISessionWorkspaceAccess access;
    private readonly Func<bool> ownsDesktop;
    private readonly ILogger<ExactGrantsViewModel> logger;
    private readonly PermissionGrantManagementService permissions;
    private ExactGrantsWindow? window;
    private ExactGrantsViewModel? state;
    private bool disposed;
    private HostId<SessionIdentity>? session;
    private Func<bool> sessionAdmission = () => true;

    internal ExactGrantsWindowController(MainViewModel main, IExactGrantStore store, ExactGrantControlAdmission control,
        ISessionWorkspaceAccess access, Func<bool> ownsDesktop, ILogger<ExactGrantsViewModel> logger,
        PermissionGrantManagementService permissions)
    {
        this.main = main;
        this.store = store;
        this.control = control;
        this.access = access;
        this.ownsDesktop = ownsDesktop;
        this.logger = logger;
        this.permissions = permissions;
        main.PrivacyClosureRequested += OnPrivacyClosure;
        main.PropertyChanged += OnMainChanged;
    }

    private bool Admitted()
    {
        if (!Dispatcher.UIThread.CheckAccess()) { return Dispatcher.UIThread.Invoke(Admitted); }
        return !disposed && ownsDesktop() && main.CanRevealPrivatePresentation && sessionAdmission();
    }

    private bool WindowAdmitted()
    {
        if (!Dispatcher.UIThread.CheckAccess()) { return Dispatcher.UIThread.Invoke(WindowAdmitted); }
        return window is { IsVisible: true } && Admitted();
    }

    internal void Open(HostId<SessionIdentity>? target = null, Func<bool>? admitted = null)
    {
        var eligible = admitted ?? (() => true);
        if (!eligible())
        {
            main.ReportHostInteractionFailure("Permission grants navigation retired with its original session selection.");
            return;
        }
        if (window is not null && session != target) { Close(); }
        session = target;
        sessionAdmission = eligible;
        if (!Admitted() || !access.CanInspect)
        {
            main.ReportHostInteractionFailure("Exact operation grants unavailable under current ownership/privacy. No record or effect was read.");
            return;
        }
        if (window is null)
        {
            state = new(store, control, access, WindowAdmitted, logger, permissions, () => main.CanChangePreapprovedUris,
                managementView: true, managementSession: target);
            window = new(state);
            window.Closed += OnClosed;
            window.Show();
            state.RefreshCommand.Execute(null);
        }
        window.Activate();
    }

    private void OnMainChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (!Admitted()) { Close(); }
        else { state?.SynchronizeAuthority(); }
    }
    internal void SynchronizeSessionAdmission()
    {
        if (session is not null && !Admitted()) { Close(); }
    }
    private void OnPrivacyClosure(object? sender, EventArgs args) => Close();
    private void OnClosed(object? sender, EventArgs args)
    {
        state?.Dispose();
        state = null;
        if (window is not null) { window.Closed -= OnClosed; }
        window = null;
    }
    private void Close()
    {
        state?.Dispose();
        window?.Close();
        state = null;
        window = null;
    }
    public void Dispose()
    {
        if (disposed) { return; }
        disposed = true;
        main.PrivacyClosureRequested -= OnPrivacyClosure;
        main.PropertyChanged -= OnMainChanged;
        Close();
    }
}