using Kora.Application.Presentation;
using Kora.Core.Context;
using Kora.Core.Storage;

namespace Kora;

internal sealed partial class SavedDraftCatalogueViewModel
{
    private AdmittedMetadataFindState? metadataFind;
    private string selectedDetails = string.Empty;
    private SavedDraftSummary? metadataFindSelection;
    private SavedDraftPage? metadataFindPage;
    private long metadataFindEpoch;
    private long metadataFindControl;
    private long metadataFindRevision;

    private bool IsMetadataFindCurrent => metadataFind is not null && CanRead
        && epoch == metadataFindEpoch && access.ControlRevision == metadataFindControl && controlRevision == metadataFindControl
        && selected is not null && ReferenceEquals(selected, metadataFindSelection)
        && ReferenceEquals(page, metadataFindPage);
    private AdmittedMetadataFindSnapshot FindView => IsMetadataFindCurrent
        ? metadataFind!.ReadCurrent() : AdmittedMetadataFindSnapshot.Unavailable;

    public string MetadataFindQuery
    {
        get => FindView.Query;
        set
        {
            SynchronizeMetadataFind();
            if (metadataFind is null || string.Equals(FindView.Query, value, StringComparison.Ordinal)) { return; }
            metadataFindRevision++;
            metadataFind.Edit(value);
            NotifyMetadataFind();
        }
    }
    public string MetadataFindStatus => FindView.Status;
    public string MetadataFindMatch => FindView.ExactMatch;
    public int MetadataFindCount => FindView.Count;
    public int MetadataFindOrdinal => FindView.Ordinal;
    public bool CanFindMetadata => FindView.Available;
    public bool CanNavigateMetadataFind => FindView.CanNavigate;
    public bool HasMetadataFindMatch => FindView.Match is not null;
    public static string MetadataFindHint => $"Private one-line case-sensitive literal Find in this selected admitted metadata string only: maximum {LocalFileRetrievalPolicy.MaximumQueryCharacters} UTF-16 characters / {LocalFileRetrievalPolicy.MaximumQueryUtf8Bytes} UTF-8 bytes. No body, row search, reads or clipboard.";
    internal long MetadataFindRevision => metadataFindRevision;
    public string MetadataFindPosition
    {
        get
        {
            var view = FindView;
            return !view.Available ? string.Empty : view.HasCount
                ? $"Current selected metadata string only: {view.Ordinal} of {view.Count} nonoverlapping literal matches; no page or global total."
                : "No current counted metadata Find result. Find first explicitly; this is not a zero-match claim.";
        }
    }

    private void AdmitMetadataFind()
    {
        if (selected is not { } row) { return; }
        metadataFindSelection = row;
        metadataFindPage = page;
        metadataFindEpoch = epoch;
        metadataFindControl = access.ControlRevision;
        metadataFind = new(selectedDetails, () => IsMetadataFindCurrent && ReferenceEquals(selected, row));
    }

    internal long FindMetadata(SnapshotFindDirection direction)
    {
        SynchronizeMetadataFind();
        var revision = ++metadataFindRevision;
        metadataFind?.Find(direction);
        NotifyMetadataFind();
        return revision;
    }

    public void ClearMetadataFind()
    {
        SynchronizeMetadataFind();
        metadataFindRevision++;
        metadataFind?.Clear();
        NotifyMetadataFind();
    }

    internal bool IsMetadataFindRevisionCurrent(long revision) =>
        revision == metadataFindRevision && IsMetadataFindCurrent;

    internal void MetadataFindFocusUnavailable()
    {
        SynchronizeMetadataFind();
        metadataFindRevision++;
        metadataFind?.ReportFocusUnavailable();
        NotifyMetadataFind();
    }

    private void SynchronizeMetadataFind()
    {
        if (metadataFind is not null && !IsMetadataFindCurrent) { RetireMetadataFind(); }
    }

    private void RetireMetadataFind()
    {
        metadataFindRevision++;
        metadataFind?.Dispose();
        metadataFind = null;
        metadataFindSelection = null;
        metadataFindPage = null;
    }

    private void NotifyMetadataFind()
    {
        foreach (var name in new[] { nameof(MetadataFindQuery), nameof(MetadataFindStatus), nameof(MetadataFindMatch),
            nameof(MetadataFindCount), nameof(MetadataFindOrdinal), nameof(MetadataFindPosition), nameof(CanFindMetadata),
            nameof(CanNavigateMetadataFind), nameof(HasMetadataFindMatch) })
        { OnPropertyChanged(name); }
    }
}
