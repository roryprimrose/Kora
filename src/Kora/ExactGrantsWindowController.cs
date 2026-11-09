using System.ComponentModel;

using Avalonia.Threading;
using Avalonia.Controls;

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
    private ExactGrantsView? view;
    private Window? viewOwner;
    private Func<Window>? getWorkbench;
    private Action<ExactGrantsView>? presentWorkbench;
    private ExactGrantsViewModel? state;
    private bool disposed;
    private HostId<SessionIdentity>? session;
    private Func<bool> sessionAdmission = () => true;
    internal Task InventoryOperation { get; private set; } = Task.CompletedTask;

    internal void BindWorkbench(Func<Window> owner, Action<ExactGrantsView> present)
    {
        getWorkbench = owner;
        presentWorkbench = present;
    }

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
        return (view?.IsNativePresented == true || window is { IsVisible: true, WindowState: not WindowState.Minimized })
            && Admitted();
    }

    internal void Open(HostId<SessionIdentity>? target = null, Func<bool>? admitted = null)
    {
        var eligible = admitted ?? (() => true);
        if (!eligible())
        {
            main.ReportHostInteractionFailure("Permission grants navigation retired with its original session selection.");
            return;
        }
        if ((window is not null || view is not null) && session != target) { Close(); }
        session = target;
        sessionAdmission = eligible;
        if (!Admitted() || !access.CanInspect)
        {
            main.ReportHostInteractionFailure("Exact operation grants unavailable under current ownership/privacy. No record or effect was read.");
            return;
        }
        if (state is null)
        {
            var created = new ExactGrantsViewModel(store, control, access, WindowAdmitted, logger, permissions, () => main.CanChangePreapprovedUris,
                managementView: true, managementSession: target);
            state = created;
            if (getWorkbench is not null && presentWorkbench is not null)
            {
                viewOwner = getWorkbench();
                view = new ExactGrantsView(viewOwner, Admitted, created);
                viewOwner.Closed += OnClosed;
                presentWorkbench(view);
            }
            else
            {
                window = new(created);
                window.Closed += OnClosed;
                window.Show();
            }
            if (!ReferenceEquals(state, created) || !eligible() || !WindowAdmitted())
            {
                if (ReferenceEquals(state, created)) { Close(); }
                main.ReportHostInteractionFailure("Permission grants opening retired before the admitted inventory read.");
                return;
            }
            InventoryOperation = ReadInventoryAsync(created);
        }
        else if (view is not null && presentWorkbench is not null) { presentWorkbench(view); }
        window?.Activate();
    }

    private static async Task ReadInventoryAsync(ExactGrantsViewModel created)
    {
        try { await created.RefreshCommand.ExecuteAsync(); }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException
            or InvalidOperationException or ArgumentException or OperationCanceledException)
        { created.ReportFailure(exception); }
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
        => Close();
    private void Close()
    {
        var oldState = state;
        var oldWindow = window;
        var oldView = view;
        var oldOwner = viewOwner;
        state = null;
        window = null;
        view = null;
        viewOwner = null;
        if (oldWindow is not null) { oldWindow.Closed -= OnClosed; }
        if (oldOwner is not null) { oldOwner.Closed -= OnClosed; }
        oldView?.Dispose();
        if (oldOwner is ResponseWindow workbench && oldView is not null) { workbench.RemoveHostedView(oldView); }
        oldState?.Dispose();
        if (oldWindow is { IsVisible: true }) { oldWindow.Close(); }
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