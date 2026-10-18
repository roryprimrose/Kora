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
    private SessionAttachmentSummary? selected;
    private string selectedDetails = string.Empty;
    private AdmittedMetadataFindState? metadataFind;
    private CancellationTokenSource? operation;
    private bool includeRemoved;
    private SessionAttachmentFilter filter;
    private string mediaType = "All";
    private AttachmentCopyStatusChoice copyStatus = new(null);
    private AttachmentGenerationRelationshipChoice? generationRelationship = new(AttachmentGenerationRelationship.Any);
    private string minimumBytesText = string.Empty;
    private string maximumBytesText = string.Empty;
    private AttachmentCatalogueMode mode;
    private string queryText = string.Empty;
    private string metadataText = string.Empty;
    private string digestText = string.Empty;
    private string observationSourceText = string.Empty;
    private string deviceProfileText = string.Empty;
    private string capturedFromText = string.Empty;
    private string capturedUntilText = string.Empty;
    private string minimumSessionGenerationText = string.Empty;
    private string maximumSessionGenerationText = string.Empty;
    private string minimumCapturedGenerationText = string.Empty;
    private string maximumCapturedGenerationText = string.Empty;
    private string minimumStorageRevisionText = string.Empty;
    private string maximumStorageRevisionText = string.Empty;
    private string fileSourceIdText = string.Empty;
    private string fileRevisionIdText = string.Empty;
    private string fileItemIdText = string.Empty;
    private bool disposed;
    private bool synchronizingAuthority;
    private long epoch;
    private long controlRevision = access.ControlRevision;
    private string status = "Refresh explicitly. Opening never captures a file. " + SessionAttachmentPage.Scope;
    internal event Action<SessionAttachmentSummary>? SelectionRevoked;

    public IReadOnlyList<SessionAttachmentSummary> Records => page?.Records ?? [];
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
    public bool CanSearch => CanRead && ValidFileReference && ValidVersions && ValidCorrelation && ValidDigest && ValidCopyAndBytes && ValidMetadataText && ValidMediaType && Enum.IsDefined(filter) && (mode == AttachmentCatalogueMode.Browse
        || SessionListSearch.IsValid(SessionListSearchKind.ExactId, SessionListFilter.All, queryText));
    public bool CanNext => CanSearch && page?.Next is not null;
    public bool CanCancel => !disposed && operation is not null;
    public bool CanNavigate => CanRead && selected?.CopyStatus == AttachmentCopyStatus.Confirmed;
    public IReadOnlyList<AttachmentCatalogueMode> Modes { get; } = Enum.GetValues<AttachmentCatalogueMode>();
    public IReadOnlyList<SessionAttachmentFilter> Filters { get; } = Enum.GetValues<SessionAttachmentFilter>();
    public IReadOnlyList<string> MediaTypes { get; } = ["All", LocalFileMediaType.PlainText, LocalFileMediaType.Markdown];
    public IReadOnlyList<AttachmentCopyStatusChoice> CopyStatuses { get; } =
        [new(null), .. Enum.GetValues<AttachmentCopyStatus>().Select(value => new AttachmentCopyStatusChoice(value))];
    public IReadOnlyList<AttachmentGenerationRelationshipChoice> GenerationRelationships { get; } =
        [.. Enum.GetValues<AttachmentGenerationRelationship>().Select(value => new AttachmentGenerationRelationshipChoice(value))];
    public AttachmentGenerationRelationshipChoice? GenerationRelationship
    {
        get => generationRelationship;
        set
        {
            if (!CanEdit || generationRelationship == value) { return; }
            InvalidateQuery();
            generationRelationship = value;
            Notify();
        }
    }
    public string GenerationRelationshipHint => ValidVersions
        ? "Any preserves all filters. Same observed generation: retained CapturedGeneration = observed SessionGeneration; Earlier observed generation: CapturedGeneration < SessionGeneration. Constrained choices match only Confirmed copies. Passive historical metadata only, not current session/device identity, freshness, completeness, consent, authority, source integrity or proof a copy exists. Changing performs no read."
        : "Invalid generation relationship or version range. Choose Any, Same observed generation or Earlier observed generation and correct the positive inclusive ranges. No fallback or read.";
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
    public string DigestText
    {
        get => digestText;
        set
        {
            if (!CanEdit || string.Equals(digestText, value, StringComparison.Ordinal)) { return; }
            InvalidateQuery();
            digestText = value;
            Notify();
        }
    }
    private bool ValidDigest => digestText.Length == 0 || LocalFileDigest.IsCanonical(digestText);
    public string DigestHint => ValidDigest
        ? "Optional exact SHA-256: 64 lowercase hexadecimal characters, not all zero. Empty means Any. Only Confirmed retained copies match; Held and removed slots never match. Typing performs no read."
        : "Invalid exact SHA-256. Use exactly 64 lowercase hexadecimal characters, not all zero; no trimming, normalization, fallback or read.";
    public string ObservationSourceText
    {
        get => observationSourceText;
        set
        {
            if (!CanEdit || string.Equals(observationSourceText, value, StringComparison.Ordinal)) { return; }
            InvalidateQuery();
            observationSourceText = value;
            Notify();
        }
    }
    public string DeviceProfileText
    {
        get => deviceProfileText;
        set
        {
            if (!CanEdit || string.Equals(deviceProfileText, value, StringComparison.Ordinal)) { return; }
            InvalidateQuery();
            deviceProfileText = value;
            Notify();
        }
    }
    public string CapturedFromText
    {
        get => capturedFromText;
        set
        {
            if (!CanEdit || string.Equals(capturedFromText, value, StringComparison.Ordinal)) { return; }
            InvalidateQuery();
            capturedFromText = value;
            Notify();
        }
    }
    public string CapturedUntilText
    {
        get => capturedUntilText;
        set
        {
            if (!CanEdit || string.Equals(capturedUntilText, value, StringComparison.Ordinal)) { return; }
            InvalidateQuery();
            capturedUntilText = value;
            Notify();
        }
    }
    private SessionAttachmentQuery CorrelationQuery() => new(
        exactObservationSource: observationSourceText.Length == 0 ? null : observationSourceText,
        exactDeviceProfile: deviceProfileText.Length == 0 ? null : deviceProfileText,
        capturedFromUtc: OptionalCanonicalUtc.Parse(capturedFromText),
        capturedUntilUtc: OptionalCanonicalUtc.Parse(capturedUntilText));
    private bool ValidCorrelation
    {
        get
        {
            try { _ = CorrelationQuery(); return true; }
            catch (InvalidDataException) { return false; }
        }
    }
    public string CorrelationHint => ValidCorrelation
        ? "Optional exact observation-source/device-profile: nonempty lowercase D GUIDs. Inclusive Captured From/Until: yyyy-MM-ddTHH:mm:ss.fffffff+00:00, From <= Until. Empty means Any. Only Confirmed retained copies match. Correlation is not consent, ownership, session/file-read authority or current availability. Typing performs no read."
        : "Invalid correlation filter. Use complete nonempty lowercase D GUIDs and canonical UTC yyyy-MM-ddTHH:mm:ss.fffffff+00:00 with From <= Until. Empty means Any; no repair, fallback or read.";
    public string MinimumSessionGenerationText
    {
        get => minimumSessionGenerationText;
        set => ChangePredicateText(ref minimumSessionGenerationText, value);
    }
    public string MaximumSessionGenerationText
    {
        get => maximumSessionGenerationText;
        set => ChangePredicateText(ref maximumSessionGenerationText, value);
    }
    public string MinimumCapturedGenerationText
    {
        get => minimumCapturedGenerationText;
        set => ChangePredicateText(ref minimumCapturedGenerationText, value);
    }
    public string MaximumCapturedGenerationText
    {
        get => maximumCapturedGenerationText;
        set => ChangePredicateText(ref maximumCapturedGenerationText, value);
    }
    public string MinimumStorageRevisionText
    {
        get => minimumStorageRevisionText;
        set => ChangePredicateText(ref minimumStorageRevisionText, value);
    }
    public string MaximumStorageRevisionText
    {
        get => maximumStorageRevisionText;
        set => ChangePredicateText(ref maximumStorageRevisionText, value);
    }
    private void ChangePredicateText(ref string field, string value)
    {
        if (!CanEdit || string.Equals(field, value, StringComparison.Ordinal)) { return; }
        InvalidateQuery();
        field = value;
        Notify();
    }
    private SessionAttachmentQuery VersionQuery() => new(
        minimumSessionGeneration: OptionalUnsignedDecimal.Parse(minimumSessionGenerationText),
        maximumSessionGeneration: OptionalUnsignedDecimal.Parse(maximumSessionGenerationText),
        minimumCapturedGeneration: OptionalUnsignedDecimal.Parse(minimumCapturedGenerationText),
        maximumCapturedGeneration: OptionalUnsignedDecimal.Parse(maximumCapturedGenerationText),
        minimumStorageRevision: OptionalUnsignedDecimal.Parse(minimumStorageRevisionText),
        maximumStorageRevision: OptionalUnsignedDecimal.Parse(maximumStorageRevisionText),
        generationRelationship: generationRelationship?.Relationship
            ?? throw new InvalidDataException("Choose Any, Same observed generation or Earlier observed generation."));
    private bool ValidVersions
    {
        get
        {
            try { _ = VersionQuery(); return true; }
            catch (InvalidDataException) { return false; }
        }
    }
    public string VersionHint => ValidVersions
        ? "Optional inclusive session-generation, captured-generation and storage-revision ranges. Empty means Any; positive invariant decimal digits only, 1 through 9223372036854775807, minimum <= maximum. Only Confirmed retained copies match. Versions are correlation metadata, not consent, session/file-read authority, currentness or proof a copy still exists. Typing performs no read."
        : "Invalid generation/revision range. Use positive invariant decimal digits only, 1 through 9223372036854775807, minimum <= maximum. Empty means Any; no repair, fallback or read.";
    public string FileSourceIdText
    {
        get => fileSourceIdText;
        set => ChangePredicateText(ref fileSourceIdText, value);
    }
    public string FileRevisionIdText
    {
        get => fileRevisionIdText;
        set => ChangePredicateText(ref fileRevisionIdText, value);
    }
    public string FileItemIdText
    {
        get => fileItemIdText;
        set => ChangePredicateText(ref fileItemIdText, value);
    }
    private SessionAttachmentQuery FileReferenceQuery() => new(
        exactFileSourceId: fileSourceIdText.Length == 0 ? null : fileSourceIdText,
        exactFileRevisionId: fileRevisionIdText.Length == 0 ? null : fileRevisionIdText,
        exactFileItemId: fileItemIdText.Length == 0 ? null : fileItemIdText);
    private bool ValidFileReference
    {
        get
        {
            try { _ = FileReferenceQuery(); return true; }
            catch (InvalidDataException) { return false; }
        }
    }
    public string FileReferenceHint => ValidFileReference
        ? "Optional exact local-file source/revision/item IDs: nonempty canonical lowercase D-format GUIDs; empty means Any. Only Confirmed retained copies match. Retained correlation only, not source consent, current file identity, path disclosure, body integrity, file-read authority or proof a copy exists. Typing performs no read."
        : "Invalid exact file-reference ID. Use complete nonempty canonical lowercase D-format GUIDs. Empty means Any; no trimming, normalization, fallback or read.";
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
        if (!ValidMediaType) { throw new InvalidDataException("Unknown or noncanonical exact media-type filter."); }
        var bounds = CopyAndBytesQuery();
        var correlation = CorrelationQuery();
        var versions = VersionQuery();
        var fileReference = FileReferenceQuery();
        var query = new SessionAttachmentQuery(includeRemoved,
            mode == AttachmentCatalogueMode.Browse ? null : queryText, filter,
            metadataText.Length == 0 ? null : metadataText, string.Equals(mediaType, "All", StringComparison.Ordinal) ? null : mediaType,
            bounds.CopyStatus, bounds.MinimumStoredBytes, bounds.MaximumStoredBytes,
            digestText.Length == 0 ? null : digestText,
            correlation.ExactObservationSource?.ToString("D"), correlation.ExactDeviceProfile?.ToString("D"),
            correlation.CapturedFromUtc, correlation.CapturedUntilUtc,
            versions.MinimumSessionGeneration, versions.MaximumSessionGeneration,
            versions.MinimumCapturedGeneration, versions.MaximumCapturedGeneration,
            versions.MinimumStorageRevision, versions.MaximumStorageRevision,
            fileReference.ExactFileSourceId?.ToString("D"), fileReference.ExactFileRevisionId?.ToString("D"),
            fileReference.ExactFileItemId?.ToString("D"), versions.GenerationRelationship);
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
                    : result.Records.IsEmpty && (query.MetadataText is not null || query.ExactMediaType is not null
                        || query.CopyStatus is not null || query.MinimumStoredBytes is not null || query.MaximumStoredBytes is not null
                        || query.ExactDigest is not null || query.HasCorrelationPredicates || query.HasVersionPredicates || query.HasFileReferencePredicates)
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
        digestText = string.Empty;
        ClearCorrelation();
        Notify();
    }

    private void InvalidateQuery()
    {
        epoch++;
        operation?.Cancel();
        Clear();
        status = "Query/mode/session lifecycle/media type/copy status/byte bounds/digest/correlation/version ranges/generation relationship/file-reference IDs/removal filter edited. Page, continuation and selection cleared; explicit Search/Refresh required.";
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
        mode = AttachmentCatalogueMode.Browse;
        queryText = string.Empty;
        metadataText = string.Empty;
        digestText = string.Empty;
        ClearCorrelation();
        copyStatus = CopyStatuses[0];
        minimumBytesText = string.Empty;
        maximumBytesText = string.Empty;
        status = "Private source, lifecycle, retention or attachment changed. Page and selection cleared; refresh.";
        Notify();
    }

    private void Clear()
    {
        RetireFind();
        selectedDetails = string.Empty;
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
        mediaType = "All";
        copyStatus = CopyStatuses[0];
        minimumBytesText = string.Empty;
        maximumBytesText = string.Empty;
        mode = AttachmentCatalogueMode.Browse;
        queryText = string.Empty;
        metadataText = string.Empty;
        digestText = string.Empty;
        ClearCorrelation();
        Notify();
    }

    private void ClearCorrelation()
    {
        generationRelationship = GenerationRelationships[0];
        observationSourceText = string.Empty;
        deviceProfileText = string.Empty;
        capturedFromText = string.Empty;
        capturedUntilText = string.Empty;
        minimumSessionGenerationText = string.Empty;
        maximumSessionGenerationText = string.Empty;
        minimumCapturedGenerationText = string.Empty;
        maximumCapturedGenerationText = string.Empty;
        minimumStorageRevisionText = string.Empty;
        maximumStorageRevisionText = string.Empty;
        fileSourceIdText = string.Empty;
        fileRevisionIdText = string.Empty;
        fileItemIdText = string.Empty;
    }

    private void Notify()
    {
        NotifyFind();
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
        OnPropertyChanged(nameof(MetadataText));
        OnPropertyChanged(nameof(MetadataHint));
        OnPropertyChanged(nameof(DigestText));
        OnPropertyChanged(nameof(DigestHint));
        OnPropertyChanged(nameof(ObservationSourceText));
        OnPropertyChanged(nameof(DeviceProfileText));
        OnPropertyChanged(nameof(CapturedFromText));
        OnPropertyChanged(nameof(CapturedUntilText));
        OnPropertyChanged(nameof(CorrelationHint));
        OnPropertyChanged(nameof(MinimumSessionGenerationText));
        OnPropertyChanged(nameof(MaximumSessionGenerationText));
        OnPropertyChanged(nameof(MinimumCapturedGenerationText));
        OnPropertyChanged(nameof(MaximumCapturedGenerationText));
        OnPropertyChanged(nameof(MinimumStorageRevisionText));
        OnPropertyChanged(nameof(MaximumStorageRevisionText));
        OnPropertyChanged(nameof(VersionHint));
        OnPropertyChanged(nameof(GenerationRelationship));
        OnPropertyChanged(nameof(GenerationRelationshipHint));
        OnPropertyChanged(nameof(FileSourceIdText));
        OnPropertyChanged(nameof(FileRevisionIdText));
        OnPropertyChanged(nameof(FileItemIdText));
        OnPropertyChanged(nameof(FileReferenceHint));
        OnPropertyChanged(nameof(QueryHint));
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
