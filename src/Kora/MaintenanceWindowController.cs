using Kora.Application.Maintenance;
using Kora.Application.ViewModels;

namespace Kora;

internal sealed class MaintenanceWindowController : IDisposable
{
    private readonly MainViewModel main;
    private readonly MaintenanceViewModel state;
    private readonly Func<bool> ownsDesktop;
    private MaintenanceWindow? window;
    private bool disposed;

    internal MaintenanceWindowController(MainViewModel main, MaintenanceViewModel state, Func<bool> ownsDesktop)
    {
        this.main = main;
        this.state = state;
        this.ownsDesktop = ownsDesktop;
        state.BindGate(() => !disposed && ownsDesktop() && main.CanRevealPrivatePresentation && !main.IsProtectedCall);
        main.MaintenanceRequested += OnRequested;
        main.PrivacyClosureRequested += OnPrivacyClosure;
        main.PropertyChanged += OnMainChanged;
        state.PropertyChanged += OnStateChanged;
        UpdateStatus();
    }

    internal void Open()
    {
        if (disposed || !ownsDesktop() || !main.CanRevealPrivatePresentation || main.IsProtectedCall)
        {
            main.ReportHostInteractionFailure("Maintenance review denied by current host ownership, privacy or call protection. No check or navigation was dispatched.");
            return;
        }
        if (window is null)
        {
            var opened = new MaintenanceWindow(state);
            opened.Closed += (_, _) => { if (ReferenceEquals(window, opened)) { window = null; } };
            window = opened;
            opened.Show();
        }
        window.Activate();
    }

    private void OnRequested(object? sender, EventArgs args) => Open();
    private void OnStateChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args) => UpdateStatus();
    private void UpdateStatus() => main.MaintenanceStatus = state.Status + " " + state.VerificationStatus;

    private void OnMainChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (string.Equals(args.PropertyName, nameof(MainViewModel.IsProtectedCall), StringComparison.Ordinal) && main.IsProtectedCall) { OnPrivacyClosure(sender, EventArgs.Empty); }
    }

    private void OnPrivacyClosure(object? sender, EventArgs args)
    {
        state.PrivacyClosed();
        window?.Close();
        window = null;
    }

    public void Dispose()
    {
        if (disposed) { return; }
        disposed = true;
        main.MaintenanceRequested -= OnRequested;
        main.PrivacyClosureRequested -= OnPrivacyClosure;
        main.PropertyChanged -= OnMainChanged;
        state.PropertyChanged -= OnStateChanged;
        state.Dispose();
        window?.Close();
        window = null;
    }
}
