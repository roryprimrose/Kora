using System.Globalization;
using System.Text.Json;

using Kora.Application.Infrastructure;
using Kora.Application.Presentation;
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
    private IReadOnlyList<SessionAttachmentSummary> visibleRecords = [];
    private AttachmentPageOrderChoice? pageOrder = new(SessionAttachmentPageOrder.Source, "Source order (default)");
    private bool notifying;
    private SessionAttachmentSummary? selected;
    private string selectedDetails = string.Empty;
    private AdmittedMetadataFindState? metadataFind;
    private CancellationTokenSource? operation;
    private bool includeRemoved;
    private SessionAttachmentFilter filter;
    private string mediaType = "All";
    private AttachmentCopyStatusChoice copyStatus = new(null);
    private string minimumBytesText = string.Empty;
    private string maximumBytesText = string.Empty;
    private AttachmentCatalogueMode mode;
    private string queryText = string.Empty;
    private bool disposed;
    private bool synchronizingAuthority;
    private long epoch;
    private long controlRevision = access.ControlRevision;
    private string status = "Refresh explicitly. Opening never captures a file. " + SessionAttachmentPage.Scope;
    internal event Action<SessionAttachmentSummary>? SelectionRevoked;

    public IReadOnlyList<SessionAttachmentSummary> Records => visibleRecords;
    public IReadOnlyList<AttachmentPageOrderChoice> PageOrders { get; } =
        [new(SessionAttachmentPageOrder.Source, "Source order (default)"),
            new(SessionAttachmentPageOrder.RetainedBytesAscending, "Retained byte count ascending (unknown last)"),
            new(SessionAttachmentPageOrder.RetainedBytesDescending, "Retained byte count descending (unknown last)")];
    public bool CanOrderPage => CanEdit && page is not null;
    public string PageOrderHint => "Order this loaded page only; metadata only. No read, query/scan/storage/global order or count change. "
        + "Display names are not available in this catalogue; no display-name ordering is offered. Equal byte counts use canonical session ID ties. "
        + "Unknown retained byte counts stay last in either direction. Next keeps the original semantic continuation. "
        + "Changing or clearing order retires selection, details, literal Find and native open readiness; select again explicitly.";
    public AttachmentPageOrderChoice? PageOrder
    {
        get => pageOrder;
        set
        {
            SynchronizeAuthority();
            if (notifying || !CanOrderPage || pageOrder == value || page is not { } source) { return; }
            epoch++;
            operation?.Cancel();
            RetireSelection();
            SynchronizeAuthority();
            if (!ReferenceEquals(page, source)) { return; }
            pageOrder = value;
            try
            {
                // New presentation identities reject delayed callbacks from the retired native list.
                visibleRecords = [.. SessionAttachmentPageOrdering.Apply(source, RequirePageOrder()).Select(row => row with { })];
                status = "Loaded-page order changed without a read. Selection retired; select again explicitly. " + SessionAttachmentPage.Scope;
            }
            catch (InvalidDataException exception)
            {
                visibleRecords = [];
                status = "Invalid loaded-page order or metadata. No results; Clear order restores Source order after full validation.";
                Failure(logger, exception.GetType().Name);
            }
            Notify();
        }
    }
    public void ClearPageOrder() => PageOrder = PageOrders[0];
    private SessionAttachmentPageOrder RequirePageOrder()
    {
        if (pageOrder is not { Value: var order } || !Enum.IsDefined(order))
        { throw new InvalidDataException("Choose a defined loaded-page order; no fallback or read."); }
        return order;
    }
    public SessionAttachmentSummary? SelectedRecord => selected;
    public string SelectedDetails
    {
        get
        {
            SynchronizeAuthority();
            return selectedDetails;
        }
    }
    public string MetadataFindQuery
    {
        get => CurrentFind()?.Query ?? string.Empty;
        set
        {
            var find = CurrentFind();
            if (find is not null && string.Equals(find.Query, value, StringComparison.Ordinal)) { return; }
            find?.Edit(value);
            NotifyFind();
        }
    }
    public string MetadataFindStatus => CurrentFind()?.Status ?? string.Empty;
    public string MetadataFindMatch => CurrentFind()?.ExactMatch ?? string.Empty;
    public bool CanFindMetadata => CurrentFind()?.Available == true;
    public bool CanNavigateMetadataFind => CurrentFind()?.CanNavigate == true;
    public bool HasMetadataFindMatch => CurrentFind()?.Match is not null;

    private AdmittedMetadataFindState? CurrentFind()
    {
        SynchronizeAuthority();
        return metadataFind;
    }

    public void FindMetadata(SnapshotFindDirection direction)
    {
        CurrentFind()?.Find(direction);
        NotifyFind();
    }

    public void ClearMetadataFind()
    {
        CurrentFind()?.Clear();
        NotifyFind();
    }

    internal void MetadataFindFocusUnavailable()
    {
        CurrentFind()?.ReportFocusUnavailable();
        NotifyFind();
    }
    public string Status => status;
    public bool CanEdit => !disposed && access.CanInspect && admitted();
    public bool CanEditId => CanEdit && mode == AttachmentCatalogueMode.ExactId;
    public bool CanRead => CanEdit && operation is null;
    public bool CanSearch => CanRead && pageOrder is not null && Enum.IsDefined(pageOrder.Value) && ValidCopyAndBytes && ValidMediaType && Enum.IsDefined(filter) && (mode == AttachmentCatalogueMode.Browse
        || SessionListSearch.IsValid(SessionListSearchKind.ExactId, SessionListFilter.All, queryText));
    public bool CanNext => CanSearch && page?.Next is not null;
    public bool CanCancel => !disposed && operation is not null;
    public bool CanNavigate => CanRead && selected?.CopyStatus == AttachmentCopyStatus.Confirmed;
    public IReadOnlyList<AttachmentCatalogueMode> Modes { get; } = Enum.GetValues<AttachmentCatalogueMode>();
    public IReadOnlyList<SessionAttachmentFilter> Filters { get; } = Enum.GetValues<SessionAttachmentFilter>();
    public IReadOnlyList<string> MediaTypes { get; } = ["All", LocalFileMediaType.PlainText, LocalFileMediaType.Markdown];
    public IReadOnlyList<AttachmentCopyStatusChoice> CopyStatuses { get; } =
        [new(null), .. Enum.GetValues<AttachmentCopyStatus>().Select(value => new AttachmentCopyStatusChoice(value))];
    public AttachmentCopyStatusChoice CopyStatus
    {
        get => copyStatus;
        set
        {
            if (!CanEdit || copyStatus == value) { return; }
            InvalidateQuery();
            copyStatus = value;
            Notify();
        }
    }
    public string MinimumBytesText
    {
        get => minimumBytesText;
        set
        {
            if (!CanEdit || string.Equals(minimumBytesText, value, StringComparison.Ordinal)) { return; }
            InvalidateQuery();
            minimumBytesText = value;
            Notify();
        }
    }
    public string MaximumBytesText
    {
        get => maximumBytesText;
        set
        {
            if (!CanEdit || string.Equals(maximumBytesText, value, StringComparison.Ordinal)) { return; }
            InvalidateQuery();
            maximumBytesText = value;
            Notify();
        }
    }
    private SessionAttachmentQuery CopyAndBytesQuery()
    {
        if (copyStatus is null) { throw new InvalidDataException("Choose Any or a defined copy status."); }
        return new(copyStatus: copyStatus.Status, minimumStoredBytes: OptionalUnsignedDecimal.Parse(minimumBytesText),
            maximumStoredBytes: OptionalUnsignedDecimal.Parse(maximumBytesText));
    }
    private bool ValidCopyAndBytes
    {
        get
        {
            try { _ = CopyAndBytesQuery(); return true; }
            catch (InvalidDataException) { return false; }
        }
    }
    public string CopyAndBytesHint => ValidCopyAndBytes
        ? "Copy status is exact; Any disables it. Inclusive retained stored-byte bounds: empty means Any; invariant digits only, 0 through "
            + LocalFilePolicy.MaximumBytes.ToString(CultureInfo.InvariantCulture)
            + ", minimum <= maximum. Unavailable tombstones never match byte bounds; Held metadata grants no navigation. Editing performs no read."
        : "Invalid copy status or stored-byte bounds. Choose Any or a defined status; use only unsigned invariant digits, 0 through "
            + LocalFilePolicy.MaximumBytes.ToString(CultureInfo.InvariantCulture) + ", minimum <= maximum. No repair, fallback or read.";
    public string MediaType
    {
        get => mediaType;
        set
        {
            if (!CanEdit || string.Equals(mediaType, value, StringComparison.Ordinal)) { return; }
            InvalidateQuery();
            mediaType = value;
            Notify();
        }
    }
    private bool ValidMediaType => string.Equals(mediaType, "All", StringComparison.Ordinal) || LocalFileMediaType.IsCanonical(mediaType);
    public string MediaTypeHint => ValidMediaType
        ? "Exact retained media type; All includes removed provenance, but removed tombstones never match a selected type. Independent of ID, lifecycle and literal query; changing performs no read."
        : "Invalid exact media type. Choose All, text/plain or text/markdown; no normalization, fallback or read.";
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
        SynchronizeAuthority();
        if (notifying || !CanRead || summary is not null && !Records.Any(row => ReferenceEquals(row, summary))) { return; }
        var previous = selected;
        RetireFind();
        selected = summary;
        selectedDetails = string.Empty;
        if (summary is not null)
        {
            var details = JsonSerializer.SerializeToNode(summary)!.AsObject();
            if (summary.CopyStatus == AttachmentCopyStatus.Unavailable)
            { details.Remove(nameof(SessionAttachmentSummary.StoredBytes)); }
            selectedDetails = details.ToJsonString();
            var originalEpoch = epoch;
            var originalControl = access.ControlRevision;
            metadataFind = new(selectedDetails, () => CanRead && epoch == originalEpoch
                && access.ControlRevision == originalControl && ReferenceEquals(selected, summary));
        }
        if (previous is not null && !ReferenceEquals(previous, summary)) { SelectionRevoked?.Invoke(previous); }
        Notify();
    }

    public Task Refresh(bool next = false) => Run(async (current, token) =>
    {
        var order = RequirePageOrder();
        if (!ValidMediaType) { throw new InvalidDataException("Unknown or noncanonical exact media-type filter."); }
        var bounds = CopyAndBytesQuery();
        var query = new SessionAttachmentQuery(includeRemoved,
            mode == AttachmentCatalogueMode.Browse ? null : queryText, filter,
            exactMediaType: string.Equals(mediaType, "All", StringComparison.Ordinal) ? null : mediaType,
            copyStatus: bounds.CopyStatus, minimumStoredBytes: bounds.MinimumStoredBytes,
            maximumStoredBytes: bounds.MaximumStoredBytes);
        var cursor = next ? page?.Next ?? throw new InvalidOperationException("Refresh before Next.") : null;
        Clear();
        var result = await read(query, cursor, current, token);
        result.Validate();
        if (result.Query != query) { throw new InvalidDataException("The attachment page does not match the original native query."); }
        if (!current()) { throw new OperationCanceledException("Private metadata observation was revoked."); }
        visibleRecords = SessionAttachmentPageOrdering.Apply(result, order);
        page = result;
        status = "This page: " + result.Records.Length.ToString(CultureInfo.InvariantCulture) + " rows; "
            + result.ScannedRows.ToString(CultureInfo.InvariantCulture) + " slots scanned; "
            + result.AuditScannedRows.ToString(CultureInfo.InvariantCulture) + " authority commits validated. "
            + (result.AuthorityValidationPending ? "Authority validation pending; Next continues before any attachment row is disclosed. "
                : result.Next is not null ? "More slots remain. Next may follow an empty nonmatching page. "
                    : result.Records.IsEmpty && (query.ExactMediaType is not null
                        || query.CopyStatus is not null || query.MinimumStoredBytes is not null || query.MaximumStoredBytes is not null)
                        ? "No matching retained metadata on this bounded page under these filters; end of this observation, not a global total; no fallback. "
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
        RetireFind();
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
            if (epoch == originalEpoch) { Clear(); }
            if (!disposed && epoch == originalEpoch) { status = "Cancelled or revoked. No late page or navigation remains current. Refresh."; }
            activity.Complete(HostOperationOutcome.Cancelled);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            if (epoch == originalEpoch) { Clear(); }
            if (!disposed && epoch == originalEpoch) { status = "Attachment catalogue unavailable: " + exception.GetType().Name + ". No empty success; correct the canonical ID/literal query or refresh under private admission."; }
            Failure(logger, exception.GetType().Name);
            activity.Complete(HostOperationOutcome.Failed);
        }
        finally
        {
            operation = null;
            SynchronizeAuthority();
            Notify();
        }
    }

    public void Cancel() => Revoke();

    public void ClearQuery()
    {
        if (!CanEdit) { return; }
        InvalidateQuery();
        pageOrder = PageOrders[0];
        mode = AttachmentCatalogueMode.Browse;
        queryText = string.Empty;
        Notify();
    }

    private void InvalidateQuery()
    {
        epoch++;
        operation?.Cancel();
        Clear();
        status = "Query/mode/session lifecycle/media type/copy status/byte bounds/removal filter edited. Page, continuation and selection cleared; explicit Search/Refresh required.";
    }

    internal void PrepareExactSession(Guid sessionId)
    {
        var text = sessionId.ToString("D");
        if (!CanEdit || !SessionListSearch.IsValid(SessionListSearchKind.ExactId, SessionListFilter.All, text))
        { throw new InvalidOperationException("Exact metadata query preparation is unavailable."); }
        InvalidateQuery();
        mode = AttachmentCatalogueMode.ExactId;
        queryText = text;
        status = "PREPARED / REVIEW NEEDED. No I/O, query, count or read ran. Other filter drafts remain AND-composed. "
            + "Review all visible filters, then explicitly Search. This ID is future metadata query data, not session selection, "
            + "current ownership, read/capture authority or proof a retained copy exists.";
        Notify();
    }

    internal void PreparationUnavailable()
    {
        if (disposed) { return; }
        InvalidateQuery();
        status = "Native catalogue preparation unavailable: exact-session query focus was not acknowledged. "
            + "No search or read ran. Filter drafts remain AND-composed; review them before explicit Search.";
        Notify();
    }

    internal void SynchronizeAuthority()
    {
        if (disposed || synchronizingAuthority) { return; }
        if (controlRevision == access.ControlRevision && CanEdit) { return; }
        synchronizingAuthority = true;
        try
        {
            controlRevision = access.ControlRevision;
            Revoke();
        }
        finally { synchronizingAuthority = false; }
    }

    internal void Revoke()
    {
        if (disposed) { return; }
        epoch++;
        operation?.Cancel();
        Clear();
        pageOrder = PageOrders[0];
        mode = AttachmentCatalogueMode.Browse;
        queryText = string.Empty;
        copyStatus = CopyStatuses[0];
        minimumBytesText = string.Empty;
        maximumBytesText = string.Empty;
        status = "Private source, lifecycle, retention or attachment changed. Page and selection cleared; refresh.";
        Notify();
    }

    private void Clear()
    {
        page = null;
        visibleRecords = [];
        RetireSelection();
    }

    private void RetireSelection()
    {
        RetireFind();
        selectedDetails = string.Empty;
        var previous = selected;
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
        pageOrder = PageOrders[0];
        status = string.Empty;
        includeRemoved = false;
        filter = SessionAttachmentFilter.All;
        mediaType = "All";
        copyStatus = CopyStatuses[0];
        minimumBytesText = string.Empty;
        maximumBytesText = string.Empty;
        mode = AttachmentCatalogueMode.Browse;
        queryText = string.Empty;
        Notify();
    }

    private void Notify()
    {
        notifying = true;
        try
        {
            NotifyFind();
            OnPropertyChanged(nameof(PageOrder));
            OnPropertyChanged(nameof(CanOrderPage));
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
            OnPropertyChanged(nameof(MediaType));
            OnPropertyChanged(nameof(MediaTypeHint));
            OnPropertyChanged(nameof(CopyStatus));
            OnPropertyChanged(nameof(MinimumBytesText));
            OnPropertyChanged(nameof(MaximumBytesText));
            OnPropertyChanged(nameof(CopyAndBytesHint));
            OnPropertyChanged(nameof(Mode));
            OnPropertyChanged(nameof(QueryText));
            OnPropertyChanged(nameof(QueryHint));
        }
        finally { notifying = false; }
    }

    private void RetireFind()
    {
        metadataFind?.Dispose();
        metadataFind = null;
    }

    private void NotifyFind()
    {
        OnPropertyChanged(nameof(MetadataFindQuery));
        OnPropertyChanged(nameof(MetadataFindStatus));
        OnPropertyChanged(nameof(MetadataFindMatch));
        OnPropertyChanged(nameof(CanFindMetadata));
        OnPropertyChanged(nameof(CanNavigateMetadataFind));
        OnPropertyChanged(nameof(HasMetadataFindMatch));
    }
}
