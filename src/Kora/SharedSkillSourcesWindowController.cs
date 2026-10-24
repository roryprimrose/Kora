using Avalonia.Platform.Storage;
using System.ComponentModel;

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
    private CancellationTokenSource? activeOperation;
    private bool busy;
    private bool disposed;
    private bool opening;
    private bool closeAfterOpening;

    internal void Open() => TryOpen(static () => true);

    internal bool TryOpen(Func<bool> eligible)
    {
        if (opening || !eligible() || !Eligible()) { main.ReportHostInteractionFailure("Shared skill inspection requires the owning unlocked host and a known call policy."); return false; }
        if (window is not null) { window.Activate(); return true; }
        lifetime = new();
        var revision = main.CallPolicyRevision;
        window = new(() => RunAsync(RegisterAsync), () => RunAsync(RefreshAsync),
            () => RunAsync(DiscoverAsync, fenceCatalogue: true), () => RunAsync(VerifyAsync, fenceCatalogue: true),
            () => RunAsync(UnregisterAsync, supersede: true),
            () => Eligible() && main.CallPolicyRevision == revision);
        window.BindInvocation(() => RunAsync(ReviewInvocationAsync));
        window.Closed += OnClosed;
        main.PrivacyClosureRequested += OnPrivacyClosed;
        main.PropertyChanged += OnMainChanged;
        var opened = window;
        opening = true;
        try { opened.Show(); }
        catch (InvalidOperationException) { Close(); throw; }
        finally
        {
            opening = false;
            if (closeAfterOpening) { closeAfterOpening = false; Close(); }
        }
        if (!ReferenceEquals(window, opened) || !eligible() || !Eligible())
        {
            opened.Close();
            return false;
        }
        opened.Activate();
        _ = RunAsync(RefreshAsync);
        return true;
    }

    private bool Eligible() => !closeAfterOpening && !disposed && isCurrentHost()
        && main.CanRevealPrivatePresentation && main.IsSkillReviewCallEligible;

    private bool CurrentWindow(SharedSkillSourcesWindow opened, CancellationToken token) =>
        Eligible() && ReferenceEquals(window, opened) && !token.IsCancellationRequested;

    private async Task RunAsync(Func<CancellationToken, Task> operation, bool supersede = false, bool fenceCatalogue = false)
    {
        if (busy && !supersede || window is null || lifetime is null || !Eligible()) { return; }
        var opened = window;
        using var currentOperation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        var previous = activeOperation;
        activeOperation = currentOperation;
        var token = currentOperation.Token;
        busy = true;
        opened.SetBusy(true);
        opened.SetWithdrawalBusy(supersede);
        long? inspectionGeneration = null;
        try
        {
            if (supersede)
            {
                opened.ClearCatalogue();
                if (previous is not null) { await previous.CancelAsync(); }
            }
            token.ThrowIfCancellationRequested();
            var pending = operation(token);
            // Discovery first retires its old catalogue synchronously; fence the ensuing read.
            if (fenceCatalogue) { inspectionGeneration = opened.CatalogueGeneration; }
            await pending;
        }
        catch (OperationCanceledException)
        {
            if (ReferenceEquals(window, opened) && ReferenceEquals(activeOperation, currentOperation)
                && (inspectionGeneration is null || inspectionGeneration == opened.CatalogueGeneration))
            {
                opened.ClearCatalogue();
                opened.Report("Operation cancelled. A source mutation may already be durable; refresh registrations. No successful receipt claimed.");
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Exception messages can include profile paths or package content.
            InspectionFailed(logger, exception.GetType().Name);
            if (!ReferenceEquals(window, opened) || !ReferenceEquals(activeOperation, currentOperation)
                || inspectionGeneration is { } generation && generation != opened.CatalogueGeneration) { return; }
            opened.ClearCatalogue();
            var reason = exception is SharedSkillUnavailableException unavailable ? unavailable.ReasonCode
                : exception is InvalidDataException ? "invalid-registration-or-format" : "host-or-storage-unavailable";
            opened.Report($"Shared source unavailable: {reason}. "
                + "Nothing is enabled or sent to a model. Corrupt preferences fail closed: restore a verified registration file; "
                + "A mutation may already be durable: refresh registrations before any further read. "
                + "An attempted withdrawal keeps that old source identity read-closed: retry unregistering saved metadata, "
                + "then use a fresh folder selection for new read consent. A missing root can still be unregistered. "
                + "No rollback, reset or silent fallback is claimed.");
        }
        finally
        {
            if (ReferenceEquals(activeOperation, currentOperation))
            {
                activeOperation = null;
                busy = false;
                if (ReferenceEquals(window, opened)) { opened.SetBusy(false); opened.SetWithdrawalBusy(false); }
            }
        }
    }

    private async Task RefreshAsync(CancellationToken token)
    {
        if (window is not { } opened) { return; }
        opened.ClearPrivateContent();
        var sources = await discovery.LoadSourcesAsync(() => CurrentWindow(opened, token), token);
        if (CurrentWindow(opened, token)) { opened.SetSources(sources); }
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
            if (selected.Count != 1 || !CurrentWindow(opened, token)) { return; }
            var path = selected[0].TryGetLocalPath()
                ?? throw new InvalidDataException("An exact local profile directory is required.");
            await discovery.RegisterAsync(path, () => CurrentWindow(opened, token), token);
            await RefreshAsync(token);
            window?.Report("Read-only root registration saved with its directory identity. Select it to list bounded snapshots. No other authority granted.");
        }
        finally { foreach (var folder in selected) { folder.Dispose(); } }
    }

    private async Task DiscoverAsync(CancellationToken token)
    {
        if (window is not { } opened || opened.SelectedSource is not { } source)
        { window?.Report("Select an explicitly registered source first."); return; }
        opened.ClearCatalogue();
        var generation = opened.CatalogueGeneration;
        var catalogue = await discovery.DiscoverAsync(source, () => CurrentWindow(opened, token), token);
        if (CurrentWindow(opened, token) && opened.SelectedSource == source && opened.CatalogueGeneration == generation)
        { opened.SetCatalogue(catalogue); }
    }

    private async Task VerifyAsync(CancellationToken token)
    {
        if (window is not { } opened || opened.SelectedSource is not { } source || opened.SelectedPackage is not { } snapshot)
        { window?.Report("List and select an immutable package revision first."); return; }
        var generation = opened.CatalogueGeneration;
        var current = await discovery.IsCurrentAsync(snapshot, source, () => CurrentWindow(opened, token), token);
        if (CurrentWindow(opened, token) && opened.SelectedSource == source && opened.SelectedPackage == snapshot
            && opened.CatalogueGeneration == generation)
        {
            window?.Report(current
                ? "Exact source/revision still matches the bounded recheck. This is not enablement, approval, execution or future freshness."
                : "STALE: live source/revision no longer matches. Displayed text remains the exact old immutable snapshot, not current content. List again to review a new revision.");
        }
    }

    private async Task UnregisterAsync(CancellationToken token)
    {
        if (window is not { } opened || opened.SelectedSource is not { } source)
        { window?.Report("Select an explicitly registered source to withdraw its local read consent."); return; }
        var confirmedSources = opened.RegisteredSources;
        if (!await opened.ConfirmWithdrawalAsync(source, token)) { return; }
        if (!CurrentWindow(opened, token) || opened.SelectedSource != source)
        { throw new InvalidOperationException("The confirmed native source selection changed."); }
        var remaining = await discovery.UnregisterAsync(source, confirmedSources, () => CurrentWindow(opened, token), token);
        if (CurrentWindow(opened, token))
        {
            opened.SetSources(remaining);
            opened.Report("Local read consent withdrawn. Only Kora's registration and local snapshots were removed; "
                + "your shared skill files, enablement, execution and grants were not changed.");
        }
    }

    private void OnPrivacyClosed(object? sender, EventArgs args) => Close();

    private void OnMainChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (string.Equals(args.PropertyName, nameof(MainViewModel.CallPolicyRevision), StringComparison.Ordinal)) { Close(); }
    }

    private void OnClosed(object? sender, EventArgs args)
    {
        main.PrivacyClosureRequested -= OnPrivacyClosed;
        main.PropertyChanged -= OnMainChanged;
        lifetime?.Cancel();
        lifetime?.Dispose();
        lifetime = null;
        activeOperation = null;
        busy = false;
        window?.ClearPrivateContent();
        window = null;
    }

    public void Dispose()
    {
        disposed = true;
        Close();
    }

    private void Close()
    {
        // Clear private state now, but do not dispose native controls inside their Show callback.
        if (opening)
        {
            closeAfterOpening = true;
            window?.ClearPrivateContent();
            return;
        }
        window?.Close();
    }
}
