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
    private SavedDraftCatalogueMode mode;
    private string queryText = string.Empty;
    private string revisionText = string.Empty;
    private string minimumBytesText = string.Empty;
    private string maximumBytesText = string.Empty;
    private string minimumScalarsText = string.Empty;
    private string maximumScalarsText = string.Empty;
    private string sourceText = string.Empty;
    private string profileText = string.Empty;
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
            if (!CanEditQuery || !Enum.IsDefined(value) || filter == value) { return; }
            InvalidateQuery();
            filter = value;
            Notify();
        }
    }
    public IReadOnlyList<SavedDraftCatalogueMode> Modes { get; } = Enum.GetValues<SavedDraftCatalogueMode>();
    public SavedDraftCatalogueMode Mode
    {
        get => mode;
        set
        {
            if (!CanEditQuery || !Enum.IsDefined(value) || mode == value) { return; }
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
            if (!CanEditQuery || string.Equals(queryText, value, StringComparison.Ordinal)) { return; }
            InvalidateQuery();
            queryText = value;
            Notify();
        }
    }
    private SessionListSearchKind QueryKind => mode == SavedDraftCatalogueMode.NameSubstring
        ? SessionListSearchKind.NameSubstring : SessionListSearchKind.ExactId;
    public string RevisionText
    {
        get => revisionText;
        set => EditMetadata(ref revisionText, value);
    }
    public string MinimumBytesText
    {
        get => minimumBytesText;
        set => EditMetadata(ref minimumBytesText, value);
    }
    public string MaximumBytesText
    {
        get => maximumBytesText;
        set => EditMetadata(ref maximumBytesText, value);
    }
    public string MinimumScalarsText
    {
        get => minimumScalarsText;
        set => EditMetadata(ref minimumScalarsText, value);
    }
    public string MaximumScalarsText
    {
        get => maximumScalarsText;
        set => EditMetadata(ref maximumScalarsText, value);
    }
    public string SourceText
    {
        get => sourceText;
        set => EditMetadata(ref sourceText, value);
    }
    public string ProfileText
    {
        get => profileText;
        set => EditMetadata(ref profileText, value);
    }
    private void EditMetadata(ref string field, string value)
    {
        if (!CanEditQuery || string.Equals(field, value, StringComparison.Ordinal)) { return; }
        InvalidateQuery();
        field = value;
        Notify();
    }
    private bool ValidMetadata => SavedDraftMetadataFilter.IsValid(revisionText, minimumBytesText, maximumBytesText,
        minimumScalarsText, maximumScalarsText, sourceText, profileText);
    public string MetadataHint => ValidMetadata
        ? "Optional exact saved revision and inclusive UTF-8 byte / Unicode scalar ranges; empty means Any. Decimal digits only; byte bounds 0-"
            + Kora.Core.Conversation.ConversationText.MaximumUtf8Bytes.ToString(CultureInfo.InvariantCulture)
            + "; scalar bounds 0-" + Kora.Core.Conversation.ConversationText.MaximumUtf16Characters.ToString(CultureInfo.InvariantCulture)
            + ". Optional exact observation source/device profile: nonempty canonical lowercase D-format GUIDs. Metadata/correlation only, not ownership, identity or authority. Empty means Any; no trimming or repair. Not code units or a body read."
        : "Invalid metadata filter: Search/Refresh refused. Use positive revision and byte/scalar bounds within their conversation maxima, minimum <= maximum; no signs, whitespace, separators or overflow. Source/profile must be nonempty canonical lowercase D-format GUIDs or empty for Any; no braces, uppercase, whitespace or repair.";
    public bool CanEditQuery => !disposed && access.CanInspect && admitted();
    public bool CanSearch => CanRead && ValidMetadata
        && (mode == SavedDraftCatalogueMode.Browse || SessionListSearch.IsValid(QueryKind, filter, queryText));
    public string QueryHint => mode == SavedDraftCatalogueMode.Browse
        ? "Browse retained saved drafts. Clear explicitly returns to Browse; typing never reads."
        : mode == SavedDraftCatalogueMode.NameSubstring
            ? "Name substring: nonempty NFC, no surrounding whitespace, controls or formats; 120 scalars / 480 UTF-8 bytes. Ordinal case-sensitive; not body search."
            : "Exact ID: nonempty canonical lowercase D-format session UUID; never interpreted as a name or permission.";
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
    public bool CanNext => CanSearch && page?.Next is not null;
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
        var query = mode == SavedDraftCatalogueMode.Browse ? null : new SessionListSearch(QueryKind, filter, queryText);
        var metadataFilter = SavedDraftMetadataFilter.Parse(revisionText, minimumBytesText, maximumBytesText,
            minimumScalarsText, maximumScalarsText, sourceText, profileText);
        var cursor = next ? page?.Next ?? throw new InvalidOperationException("Refresh before Next.") : null;
        Clear();
        var result = await service.ReadSavedDraftCatalogue(filter, cursor, current, token, query, metadataFilter);
        if (!current()) { throw new OperationCanceledException("Private catalogue observation revoked."); }
        page = result;
        status = "This page: " + result.Records.Length.ToString(CultureInfo.InvariantCulture) + " saved drafts; "
            + result.Scanned.ToString(CultureInfo.InvariantCulture) + " candidate slots consumed; "
            + result.AuditScanned.ToString(CultureInfo.InvariantCulture) + " authority commits validated. "
            + (result.AuthorityPending ? "Authority validation pending; Next continues before disclosing rows. "
                : result.Next is not null ? "More candidates remain, even if this page is empty. "
                    : result.Records.IsEmpty && query?.Kind == SessionListSearchKind.ExactId
                        ? "Exact saved draft not found under this filter at this observation; no fallback to Browse. "
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
            if (!disposed && epoch == originalEpoch) { status = "Cancelled/revoked; no late catalogue page or selection remains current. Refresh."; }
            activity.Complete(HostOperationOutcome.Cancelled);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Clear();
            if (!disposed && epoch == originalEpoch) { status = "Saved draft catalogue unavailable: " + exception.GetType().Name + ". Refresh; no empty success or newer source followed."; }
            Failure(logger, exception.GetType().Name);
            activity.Complete(HostOperationOutcome.Failed);
        }
        finally { operation = null; Notify(); }
    }

    public void Cancel() => Revoke();
    public void ClearQuery()
    {
        if (!CanEditQuery) { return; }
        InvalidateQuery();
        mode = SavedDraftCatalogueMode.Browse;
        queryText = string.Empty;
        ClearMetadata();
        Notify();
    }
    private void InvalidateQuery()
    {
        epoch++;
        operation?.Cancel();
        Clear();
        status = "Query/mode/filter edited. Page, continuation and selection invalidated; explicit Search/Refresh required.";
    }
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
        mode = SavedDraftCatalogueMode.Browse;
        queryText = string.Empty;
        status = "Private source/lifecycle/draft changed. Page, continuation and selection cleared; Refresh.";
        ClearMetadata();
        Notify();
    }
    private void Clear() { page = null; selected = null; }
    private void ClearMetadata()
    {
        revisionText = string.Empty;
        minimumBytesText = string.Empty;
        maximumBytesText = string.Empty;
        minimumScalarsText = string.Empty;
        maximumScalarsText = string.Empty;
        sourceText = string.Empty;
        profileText = string.Empty;
    }
    public void Dispose()
    {
        if (disposed) { return; }
        disposed = true;
        epoch++;
        operation?.Cancel();
        Clear();
        mode = SavedDraftCatalogueMode.Browse;
        queryText = string.Empty;
        ClearMetadata();
        status = string.Empty;
        Notify();
    }
    private void Notify()
    {
        foreach (var name in new[] { nameof(Records), nameof(SelectedRecord), nameof(SelectedDetails), nameof(Status),
            nameof(Filter), nameof(Mode), nameof(QueryText), nameof(QueryHint), nameof(CanEditQuery),
            nameof(RevisionText), nameof(MinimumBytesText), nameof(MaximumBytesText), nameof(MetadataHint),
            nameof(MinimumScalarsText), nameof(MaximumScalarsText),
            nameof(SourceText), nameof(ProfileText),
            nameof(CanSearch), nameof(CanRead), nameof(CanNext), nameof(CanNavigate), nameof(CanCancel) })
        { OnPropertyChanged(name); }
    }
}
