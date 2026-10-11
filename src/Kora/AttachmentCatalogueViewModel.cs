using System.Globalization;
using System.Text.Json;

using Kora.Application.Infrastructure;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;

using Microsoft.Extensions.Logging;

namespace Kora;

internal sealed partial class AttachmentCatalogueViewModel(
    Func<SessionAttachmentQuery, string?, Func<bool>, CancellationToken, Task<SessionAttachmentPage>> read,
    Func<SessionAttachmentSummary, Func<bool>, CancellationToken, Task> navigate,
    ISessionWorkspaceAccess access, Func<bool> admitted, ILogger<AttachmentCatalogueViewModel> logger) : ObservableObject, IDisposable
{
    private readonly HostRequest viewer = HostRequest.Create(RequestOrigin.LocalUi);
    private SessionAttachmentPage? page;
    private SessionAttachmentSummary? selected;
    private CancellationTokenSource? operation;
    private bool includeRemoved;
    private bool disposed;
    private long epoch;
    private long controlRevision = access.ControlRevision;
    private string status = "Refresh explicitly. Opening never captures a file. " + SessionAttachmentPage.Scope;
    internal event Action<SessionAttachmentSummary>? SelectionRevoked;

    public IReadOnlyList<SessionAttachmentSummary> Records => page?.Records ?? [];
    public SessionAttachmentSummary? SelectedRecord => selected;
    public string SelectedDetails => selected is null ? string.Empty : JsonSerializer.Serialize(selected);
    public string Status => status;
    public bool CanEdit => !disposed && access.CanInspect && admitted();
    public bool CanRead => CanEdit && operation is null;
    public bool CanNext => CanRead && page?.Next is not null;
    public bool CanCancel => !disposed && operation is not null;
    public bool CanNavigate => CanRead && selected?.CopyStatus == AttachmentCopyStatus.Confirmed;
    public bool IncludeRemoved
    {
        get => includeRemoved;
        set
        {
            if (!CanEdit || includeRemoved == value) { return; }
            Revoke();
            includeRemoved = value;
            status = "Query changed. Refresh starts a fresh bounded observation.";
            Notify();
        }
    }

    public void Select(SessionAttachmentSummary? summary)
    {
        if (!CanRead || summary is not null && !Records.Contains(summary)) { return; }
        var previous = selected;
        selected = summary;
        if (previous is not null && !ReferenceEquals(previous, summary)) { SelectionRevoked?.Invoke(previous); }
        Notify();
    }

    public Task Refresh(bool next = false) => Run(async (current, token) =>
    {
        var cursor = next ? page?.Next ?? throw new InvalidOperationException("Refresh before Next.") : null;
        Clear();
        var result = await read(new(includeRemoved), cursor, current, token);
        result.Validate();
        if (!current()) { throw new OperationCanceledException("Private metadata observation was revoked."); }
        page = result;
        status = "This page: " + result.Records.Length.ToString(CultureInfo.InvariantCulture) + " rows; "
            + result.ScannedRows.ToString(CultureInfo.InvariantCulture) + " slots scanned; "
            + result.AuditScannedRows.ToString(CultureInfo.InvariantCulture) + " authority commits validated. "
            + (result.AuthorityValidationPending ? "Authority validation pending; Next continues before any attachment row is disclosed. "
                : result.Next is not null ? "More slots remain. Next may follow an empty retained-only page. "
                    : "End of this observation, not a global total. ")
            + SessionAttachmentPage.Scope;
    });

    public Task Navigate() => Run(async (current, token) =>
    {
        var exact = selected ?? throw new InvalidOperationException("Select a confirmed exact retained slot.");
        bool Original() => current() && selected == exact;
        await navigate(exact, Original, token);
        if (!Original()) { throw new OperationCanceledException("The original exact navigation selection was revoked."); }
        status = "Opened the exact retained historical revision with full byte/hash/provenance validation. No resume or question retarget.";
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
            if (!Current()) { throw new OperationCanceledException("Private attachment catalogue admission changed."); }
            activity.Complete(HostOperationOutcome.Completed);
        }
        catch (OperationCanceledException)
        {
            Clear();
            if (!disposed) { status = "Cancelled or revoked. No late page or navigation remains current. Refresh."; }
            activity.Complete(HostOperationOutcome.Cancelled);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Clear();
            if (!disposed) { status = "Attachment catalogue unavailable: " + exception.GetType().Name + ". No empty success; refresh under private admission."; }
            Failure(logger, exception.GetType().Name);
            activity.Complete(HostOperationOutcome.Failed);
        }
        finally
        {
            operation = null;
            Notify();
        }
    }

    public void Cancel() => Revoke();

    internal void SynchronizeAuthority()
    {
        if (controlRevision == access.ControlRevision && CanEdit) { return; }
        controlRevision = access.ControlRevision;
        Revoke();
    }

    internal void Revoke()
    {
        if (disposed) { return; }
        epoch++;
        operation?.Cancel();
        Clear();
        status = "Private source, lifecycle, retention or attachment changed. Page and selection cleared; refresh.";
        Notify();
    }

    private void Clear()
    {
        var previous = selected;
        page = null;
        selected = null;
        if (previous is not null) { SelectionRevoked?.Invoke(previous); }
    }

    public void Dispose()
    {
        if (disposed) { return; }
        disposed = true;
        epoch++;
        operation?.Cancel();
        Clear();
        status = string.Empty;
        includeRemoved = false;
        Notify();
    }

    private void Notify()
    {
        OnPropertyChanged(nameof(Records));
        OnPropertyChanged(nameof(SelectedRecord));
        OnPropertyChanged(nameof(SelectedDetails));
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(CanRead));
        OnPropertyChanged(nameof(CanNext));
        OnPropertyChanged(nameof(CanCancel));
        OnPropertyChanged(nameof(CanNavigate));
        OnPropertyChanged(nameof(IncludeRemoved));
    }
}
