using Avalonia.Platform.Storage;

using Kora.Application.Skills;
using Kora.Application.ViewModels;
using Kora.Core.Skills;

using Microsoft.Extensions.Logging;

namespace Kora;

internal sealed partial class SharedSkillSourcesWindowController(
    MainViewModel main, SharedSkillDiscoveryService discovery, Func<bool> isCurrentHost,
    ILogger<SharedSkillSourcesWindowController> logger) : IDisposable
{
    private SharedSkillSourcesWindow? window;
    private CancellationTokenSource? lifetime;
    private bool busy;
    private bool disposed;

    internal void Open()
    {
        if (!Eligible()) { main.ReportHostInteractionFailure("Shared skill inspection requires the owning unlocked host."); return; }
        if (window is not null) { window.Activate(); return; }
        lifetime = new();
        window = new(() => RunAsync(RegisterAsync), () => RunAsync(RefreshAsync),
            () => RunAsync(DiscoverAsync), () => RunAsync(VerifyAsync));
        window.Closed += OnClosed;
        main.PrivacyClosureRequested += OnPrivacyClosed;
        window.Show();
        window.Activate();
        _ = RunAsync(RefreshAsync);
    }

    private bool Eligible() => !disposed && isCurrentHost() && main.CanRevealPrivatePresentation;

    private async Task RunAsync(Func<CancellationToken, Task> operation)
    {
        if (busy || window is null || lifetime is null || !Eligible()) { return; }
        var opened = window;
        var token = lifetime.Token;
        busy = true;
        opened.SetBusy(true);
        try { await operation(token); }
        catch (OperationCanceledException) { opened.Report("Inspection cancelled. No successful receipt or revision claimed."); }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Exception messages can include profile paths or package content.
            InspectionFailed(logger, exception.GetType().Name);
            opened.ClearCatalogue();
            var reason = exception is SharedSkillUnavailableException unavailable ? unavailable.ReasonCode
                : exception is InvalidDataException ? "invalid-registration-or-format" : "host-or-storage-unavailable";
            opened.Report($"Shared source unavailable: {reason}. "
                + "Nothing is enabled or sent to a model. Corrupt preferences fail closed: restore a verified registration file; "
                + "for removed/replaced roots, restore the original source. No reset or silent fallback is performed.");
        }
        finally
        {
            busy = false;
            if (ReferenceEquals(window, opened)) { opened.SetBusy(false); }
        }
    }

    private async Task RefreshAsync(CancellationToken token)
    {
        var sources = await discovery.LoadSourcesAsync(Eligible, token);
        if (Eligible() && !token.IsCancellationRequested) { window?.SetSources(sources); }
    }

    private async Task RegisterAsync(CancellationToken token)
    {
        if (window is not { } opened) { return; }
        var selected = await opened.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Register a bounded profile skill root for local read only (no enablement or execution)",
            AllowMultiple = false,
        });
        try
        {
            if (selected.Count != 1 || !Eligible()) { return; }
            var path = selected[0].TryGetLocalPath()
                ?? throw new InvalidDataException("An exact local profile directory is required.");
            await discovery.RegisterAsync(path, Eligible, token);
            await RefreshAsync(token);
            window?.Report("Read-only root registration saved with its directory identity. Select it to list bounded snapshots. No other authority granted.");
        }
        finally { foreach (var folder in selected) { folder.Dispose(); } }
    }

    private async Task DiscoverAsync(CancellationToken token)
    {
        if (window?.SelectedSource is not { } source) { window?.Report("Select an explicitly registered source first."); return; }
        var catalogue = await discovery.DiscoverAsync(source, Eligible, token);
        if (Eligible() && !token.IsCancellationRequested) { window?.SetCatalogue(catalogue); }
    }

    private async Task VerifyAsync(CancellationToken token)
    {
        if (window?.SelectedSource is not { } source || window.SelectedPackage is not { } snapshot)
        { window?.Report("List and select an immutable package revision first."); return; }
        var current = await discovery.IsCurrentAsync(snapshot, source, Eligible, token);
        if (Eligible() && !token.IsCancellationRequested)
        {
            window?.Report(current
                ? "Exact source/revision still matches the bounded recheck. This is not enablement, approval, execution or future freshness."
                : "STALE: live source/revision no longer matches. Displayed text remains the exact old immutable snapshot, not current content. List again to review a new revision.");
        }
    }

    private void OnPrivacyClosed(object? sender, EventArgs args) => window?.Close();

    private void OnClosed(object? sender, EventArgs args)
    {
        main.PrivacyClosureRequested -= OnPrivacyClosed;
        lifetime?.Cancel();
        lifetime?.Dispose();
        lifetime = null;
        window?.ClearPrivateContent();
        window = null;
    }

    public void Dispose()
    {
        disposed = true;
        window?.Close();
    }
}
