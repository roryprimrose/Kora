using System.Globalization;
using System.Text.Json;

using Kora.Application.Infrastructure;
using Kora.Core.Context;
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
    private SessionAttachmentFilter filter;
    private AttachmentCatalogueMode mode;
    private string queryText = string.Empty;
    private string metadataText = string.Empty;
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
    public bool CanEditId => CanEdit && mode == AttachmentCatalogueMode.ExactId;
    public bool CanRead => CanEdit && operation is null;
    public bool CanSearch => CanRead && ValidMetadataText && Enum.IsDefined(filter) && (mode == AttachmentCatalogueMode.Browse
        || SessionListSearch.IsValid(SessionListSearchKind.ExactId, SessionListFilter.All, queryText));
    public bool CanNext => CanSearch && page?.Next is not null;
    public bool CanCancel => !disposed && operation is not null;
    public bool CanNavigate => CanRead && selected?.CopyStatus == AttachmentCopyStatus.Confirmed;
    public IReadOnlyList<AttachmentCatalogueMode> Modes { get; } = Enum.GetValues<AttachmentCatalogueMode>();
    public IReadOnlyList<SessionAttachmentFilter> Filters { get; } = Enum.GetValues<SessionAttachmentFilter>();
    public SessionAttachmentFilter Filter
    {
        get => filter;
        set
        {
            if (!CanEdit || filter == value) { return; }
            InvalidateQuery();
            filter = value;
            Notify();
        }
    }
    public AttachmentCatalogueMode Mode
    {
        get => mode;
        set
        {
            if (!CanEdit || !Enum.IsDefined(value) || mode == value) { return; }
            InvalidateQuery();
            mode = value;
            Notify();
        }
    }
    public string QueryText
    {
        get => queryText;
        set
        {
            if (!CanEdit || string.Equals(queryText, value, StringComparison.Ordinal)) { return; }
            InvalidateQuery();
            queryText = value;
            Notify();
        }
    }
    public string MetadataText
    {
        get => metadataText;
        set
        {
            if (!CanEdit || string.Equals(metadataText, value, StringComparison.Ordinal)) { return; }
            InvalidateQuery();
            metadataText = value;
            Notify();
        }
    }
    private bool ValidMetadataText
    {
        get
        {
            try { LiteralMetadataQuery.Validate(metadataText); return true; }
            catch (InvalidDataException) { return false; }
        }
    }
    public string MetadataHint => ValidMetadataText
        ? "Optional case-sensitive literal in exposed session/source/revision/item IDs, digest or media type only. Spaces stay exact; no wildcard, name, path or body search. Removed slots never match. Empty disables literal filtering; typing performs no read."
        : "Invalid literal metadata query: at most 256 UTF-16 units / 512 UTF-8 bytes, valid single-line Unicode without control or format characters. No read or Browse fallback.";
    public string QueryHint => !Enum.IsDefined(filter)
        ? "Invalid stored session lifecycle filter. Choose All, Active, Done or Removed; no fallback or read."
        : mode == AttachmentCatalogueMode.Browse
        ? "Browse retained slots. Clear returns to Browse; typing performs no read."
        : !SessionListSearch.IsValid(SessionListSearchKind.ExactId, SessionListFilter.All, queryText)
            ? "Invalid exact session ID. Enter a nonempty canonical lowercase D-format UUID; no Browse fallback."
            : "Exact session ID: nonempty canonical lowercase D-format UUID; independent of optional literal metadata, never permission.";
    public bool IncludeRemoved
    {
        get => includeRemoved;
        set
        {
            if (!CanEdit || includeRemoved == value) { return; }
            InvalidateQuery();
            includeRemoved = value;
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
        var query = new SessionAttachmentQuery(includeRemoved,
            mode == AttachmentCatalogueMode.Browse ? null : queryText, filter,
            metadataText.Length == 0 ? null : metadataText);
        var cursor = next ? page?.Next ?? throw new InvalidOperationException("Refresh before Next.") : null;
        Clear();
        var result = await read(query, cursor, current, token);
        result.Validate();
        if (result.Query != query) { throw new InvalidDataException("The attachment page does not match the original native query."); }
        if (!current()) { throw new OperationCanceledException("Private metadata observation was revoked."); }
        page = result;
        status = "This page: " + result.Records.Length.ToString(CultureInfo.InvariantCulture) + " rows; "
            + result.ScannedRows.ToString(CultureInfo.InvariantCulture) + " slots scanned; "
            + result.AuditScannedRows.ToString(CultureInfo.InvariantCulture) + " authority commits validated. "
            + (result.AuthorityValidationPending ? "Authority validation pending; Next continues before any attachment row is disclosed. "
                : result.Next is not null ? "More slots remain. Next may follow an empty nonmatching page. "
                    : result.Records.IsEmpty && query.MetadataText is not null
                        ? "No matching retained metadata under these filters at this observation; no fallback. "
                    : result.Records.IsEmpty && query.ExactId is not null
                        ? "Exact attachment slot not found under this filter at this observation; no fallback to Browse. "
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
            if (!disposed && epoch == originalEpoch) { status = "Cancelled or revoked. No late page or navigation remains current. Refresh."; }
            activity.Complete(HostOperationOutcome.Cancelled);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Clear();
            if (!disposed && epoch == originalEpoch) { status = "Attachment catalogue unavailable: " + exception.GetType().Name + ". No empty success; correct the canonical ID/literal query or refresh under private admission."; }
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

    public void ClearQuery()
    {
        if (!CanEdit) { return; }
        InvalidateQuery();
        mode = AttachmentCatalogueMode.Browse;
        queryText = string.Empty;
        metadataText = string.Empty;
        Notify();
    }

    private void InvalidateQuery()
    {
        epoch++;
        operation?.Cancel();
        Clear();
        status = "Query/mode/session lifecycle/removal filter edited. Page, continuation and selection cleared; explicit Search/Refresh required.";
    }

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
        mode = AttachmentCatalogueMode.Browse;
        queryText = string.Empty;
        metadataText = string.Empty;
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
        filter = SessionAttachmentFilter.All;
        mode = AttachmentCatalogueMode.Browse;
        queryText = string.Empty;
        metadataText = string.Empty;
        Notify();
    }

    private void Notify()
    {
        OnPropertyChanged(nameof(Records));
        OnPropertyChanged(nameof(SelectedRecord));
        OnPropertyChanged(nameof(SelectedDetails));
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(CanEditId));
        OnPropertyChanged(nameof(CanRead));
        OnPropertyChanged(nameof(CanSearch));
        OnPropertyChanged(nameof(CanNext));
        OnPropertyChanged(nameof(CanCancel));
        OnPropertyChanged(nameof(CanNavigate));
        OnPropertyChanged(nameof(IncludeRemoved));
        OnPropertyChanged(nameof(Filter));
        OnPropertyChanged(nameof(Mode));
        OnPropertyChanged(nameof(QueryText));
        OnPropertyChanged(nameof(MetadataText));
        OnPropertyChanged(nameof(MetadataHint));
        OnPropertyChanged(nameof(QueryHint));
    }
}
