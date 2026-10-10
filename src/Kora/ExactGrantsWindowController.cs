using System.ComponentModel;

using Kora.Application.Interaction;
using Kora.Application.ViewModels;
using Kora.Core.Storage;

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
    private ExactGrantsWindow? window;
    private ExactGrantsViewModel? state;
    private bool disposed;

    internal ExactGrantsWindowController(MainViewModel main, IExactGrantStore store, ExactGrantControlAdmission control,
        ISessionWorkspaceAccess access, Func<bool> ownsDesktop, ILogger<ExactGrantsViewModel> logger)
    {
        this.main = main;
        this.store = store;
        this.control = control;
        this.access = access;
        this.ownsDesktop = ownsDesktop;
        this.logger = logger;
        main.PrivacyClosureRequested += OnPrivacyClosure;
        main.PropertyChanged += OnMainChanged;
    }

    private bool Admitted() => !disposed && ownsDesktop() && main.CanRevealPrivatePresentation;

    internal void Open()
    {
        if (!Admitted() || !access.CanInspect)
        {
            main.ReportHostInteractionFailure("Exact operation grants unavailable under current ownership/privacy. No record or effect was read.");
            return;
        }
        if (window is null)
        {
            state = new(store, control, access, () => window is { IsVisible: true } && Admitted(), logger);
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
