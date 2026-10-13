using System.Globalization;

using Kora.Application.Hosting;
using Kora.Application.Infrastructure;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;

using Microsoft.Extensions.Logging;

namespace Kora;

internal sealed partial class SavedDraftCatalogueViewModel(
    SessionWorkspaceService service,
    Func<SavedDraftSummary, Func<bool>, CancellationToken, Task<bool>> navigate,
    ISessionWorkspaceAccess access, Func<bool> admitted, ILogger<SavedDraftCatalogueViewModel> logger) : ObservableObject, IDisposable
{
    private readonly HostRequest viewer = HostRequest.Create(RequestOrigin.LocalUi);
    private SavedDraftPage? page;
    private SavedDraftSummary? selected;
    private CancellationTokenSource? operation;
    private SessionListFilter filter;
    private bool disposed;
    private long epoch;
    private long controlRevision = access.ControlRevision;
    private string status = "Refresh explicitly; opening performs no metadata read. " + SavedDraftPage.Scope;
    public IReadOnlyList<SavedDraftSummary> Records => page?.Records ?? [];
    public SavedDraftSummary? SelectedRecord => selected;
    public string Status => status;
    public IReadOnlyList<SessionListFilter> Filters { get; } = Enum.GetValues<SessionListFilter>();
    public SessionListFilter Filter
    {
        get => filter;
        set
        {
            if (!CanRead || !Enum.IsDefined(value) || filter == value) { return; }
            Revoke();
            filter = value;
            Notify();
        }
    }
    public string SelectedDetails => selected is null ? string.Empty
        : "Exact session " + selected.Session.SessionId.Value.ToString("D")
            + " | stored " + (selected.Session.IsActive ? "Active" : "Done")
            + " | generation " + selected.Session.Generation.Value.ToString(CultureInfo.InvariantCulture)
            + " | metadata revision " + (selected.Metadata?.Revision.Value ?? 0).ToString(CultureInfo.InvariantCulture)
            + " | private name (inert): " + (selected.Metadata?.Name.Value ?? "Unnamed")
            + " | saved draft revision " + selected.DraftRevision.ToString(CultureInfo.InvariantCulture)
            + " | UTF-8 bytes " + selected.Utf8Bytes.ToString(CultureInfo.InvariantCulture)
            + " | SQL Unicode scalar length " + selected.UnicodeScalars.ToString(CultureInfo.InvariantCulture);
    public bool CanRead => !disposed && access.CanInspect && admitted() && operation is null;
    public bool CanNext => CanRead && page?.Next is not null;
    public bool CanNavigate => CanRead && selected is not null;
    public bool CanCancel => !disposed && operation is not null;

    public void Select(SavedDraftSummary? row)
    {
        if (!CanRead || row is not null && !Records.Contains(row)) { return; }
        selected = row;
        Notify();
    }

    public Task Refresh(bool next = false) => Run(async (current, token) =>
    {
        var cursor = next ? page?.Next ?? throw new InvalidOperationException("Refresh before Next.") : null;
        Clear();
        var result = await service.ReadSavedDraftCatalogue(filter, cursor, current, token);
        if (!current()) { throw new OperationCanceledException("Private catalogue observation revoked."); }
        page = result;
        status = "This page: " + result.Records.Length.ToString(CultureInfo.InvariantCulture) + " saved drafts; "
            + result.Scanned.ToString(CultureInfo.InvariantCulture) + " candidate slots consumed; "
            + result.AuditScanned.ToString(CultureInfo.InvariantCulture) + " authority commits validated. "
            + (result.AuthorityPending ? "Authority validation pending; Next continues before disclosing rows. "
                : result.Next is not null ? "More candidates remain, even if this page is empty. "
                    : "End at this observation, not a global inventory or total. ") + SavedDraftPage.Scope;
    });

    public Task Navigate() => Run(async (current, token) =>
    {
        var exact = selected ?? throw new InvalidOperationException("Select an observed saved draft.");
        bool Original() => current() && ReferenceEquals(selected, exact);
        var opened = await navigate(exact, Original, token);
        if (!Original()) { throw new OperationCanceledException("Original catalogue selection revoked."); }
        status = opened
            ? "Opened the exact session metadata. Body was not imported. Use its explicit Reload saved draft, then a separate Submit if currently admitted. Done remains Done."
            : "Navigation cancelled; the Sessions target and memory-only composer remain unchanged.";
    });

    private async Task Run(Func<Func<bool>, CancellationToken, Task> action)
    {
        if (!CanRead) { return; }
        var originalEpoch = epoch;
        var control = access.ControlRevision;
        using var cancellation = new CancellationTokenSource();
        operation = cancellation;
        bool Current() => !disposed && epoch == originalEpoch && !cancellation.IsCancellationRequested
            && access.CanInspect && access.ControlRevision == control && admitted();
        using var activity = HostActivity.BeginRoot(viewer, HostActivityLayer.Desktop, HostOperation.Request);
        Notify();
        try
        {
            await action(Current, cancellation.Token);
            if (!Current()) { throw new OperationCanceledException("Private saved draft source revoked."); }
            activity.Complete(HostOperationOutcome.Completed);
        }
        catch (OperationCanceledException)
        {
            Clear();
            if (!disposed) { status = "Cancelled/revoked; no late catalogue page or selection remains current. Refresh."; }
            activity.Complete(HostOperationOutcome.Cancelled);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Clear();
            if (!disposed) { status = "Saved draft catalogue unavailable: " + exception.GetType().Name + ". Refresh; no empty success or newer source followed."; }
            Failure(logger, exception.GetType().Name);
            activity.Complete(HostOperationOutcome.Failed);
        }
        finally { operation = null; Notify(); }
    }

    public void Cancel() => Revoke();
    internal void SynchronizeAuthority()
    {
        if (controlRevision == access.ControlRevision && access.CanInspect && admitted()) { return; }
        controlRevision = access.ControlRevision;
        Revoke();
    }
    internal void Revoke()
    {
        if (disposed) { return; }
        epoch++;
        operation?.Cancel();
        Clear();
        status = "Private source/lifecycle/draft changed. Page, continuation and selection cleared; Refresh.";
        Notify();
    }
    private void Clear() { page = null; selected = null; }
    public void Dispose()
    {
        if (disposed) { return; }
        disposed = true;
        epoch++;
        operation?.Cancel();
        Clear();
        status = string.Empty;
        Notify();
    }
    private void Notify()
    {
        foreach (var name in new[] { nameof(Records), nameof(SelectedRecord), nameof(SelectedDetails), nameof(Status),
            nameof(Filter), nameof(CanRead), nameof(CanNext), nameof(CanNavigate), nameof(CanCancel) })
        { OnPropertyChanged(name); }
    }
}
