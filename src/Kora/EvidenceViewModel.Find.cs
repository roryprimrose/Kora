using Kora.Core.Context;
using Kora.Core.Diagnostics;

namespace Kora;

internal sealed partial class EvidenceViewModel
{
    private string findQuery = string.Empty;
    private string findStatus = string.Empty;
    private SnapshotLiteralSpan? findSelection;
    private SnapshotLiteralSpan? findAnchor;
    private long findRevision;

    public string FindQuery
    {
        get => findQuery;
        set
        {
            if (!EnsureFindAdmission()) { return; }
            if (!CanFind) { ClearFind(); return; }
            if (string.Equals(findQuery, value, StringComparison.Ordinal)) { return; }
            findQuery = value;
            RetireFind(clearQuery: false);
        }
    }

    public string FindStatus => findStatus;
    public static string FindQueryHint => $"Literal Find: max {LocalFileRetrievalPolicy.MaximumQueryCharacters} UTF-16 characters / "
        + $"{LocalFileRetrievalPolicy.MaximumQueryUtf8Bytes} UTF-8 bytes";
    public SnapshotLiteralSpan? FindSelection => findSelection;
    public string FindMatchText => findSelection is { } span ? resultText.Substring(span.Start, span.Length) : string.Empty;
    public bool HasFindSelection => findSelection is not null;
    public bool CanFind => CanQuery && page?.Status == EvidencePageStatus.Available
        && !page.Records.Any(record => record.ContentOmitted)
        && !page.UnavailableSources.Any(source => currentQuery?.Source != EvidenceSource.All
            || source is not (EvidenceSource.Session or EvidenceSource.Conversation));
    public bool CanMoveFind => CanFind && findAnchor is not null;
    internal long FindRevision => findRevision;

    public void ClearFind() => RetireFind(clearQuery: true);

    public bool EnsureFindAdmission()
    {
        if (!closed && canInspect()) { return true; }
        Clear("Evidence inspection retired by host ownership or privacy.");
        Notify();
        return false;
    }

    public void FindLiteral(SnapshotFindDirection direction)
    {
        if (!EnsureFindAdmission()) { return; }
        var text = resultText;
        var literal = findQuery;
        var anchor = findAnchor;
        var admitted = CanFind;
        RetireFind(clearQuery: false);
        var revision = findRevision;
        if (!EnsureFindAdmission()) { return; }
        if (!admitted || !CanFind)
        {
            findStatus = "Literal Find unavailable. Explicitly read a successful complete bounded Evidence page first.";
            NotifyFind();
            return;
        }
        try
        {
            var match = SnapshotLiteralFind.Find(text, literal, direction, anchor);
            if (!IsFindCurrent(revision) || !ReferenceEquals(text, resultText)
                || !string.Equals(literal, findQuery, StringComparison.Ordinal)) { return; }
            findAnchor = match ?? (direction == SnapshotFindDirection.First ? null : anchor);
            findSelection = match;
            findStatus = match is null
                ? direction == SnapshotFindDirection.First ? "No literal match in this exact serialized Evidence page; not a retained-source or all-clear result."
                    : direction == SnapshotFindDirection.Next ? "End of forward direction; no wrap. Earlier matches may remain."
                        : "End of backward direction; no wrap. Later matches may remain."
                : $"One exact literal match: UTF-16 [{match.Start}, {match.Start + match.Length}); end exclusive. This page only.";
            NotifyFind();
            if (!IsFindCurrent(revision)) { return; }
        }
        catch (Exception exception) when (exception is InvalidDataException or ArgumentOutOfRangeException)
        {
            if (!IsFindCurrent(revision)) { return; }
            findStatus = "Literal Find rejected. Enter nonempty, bounded, well-formed one-line text without controls or format characters; nothing was truncated.";
            Failure(logger, exception.GetType().Name);
            NotifyFind();
        }
    }

    internal bool IsFindCurrent(long revision) =>
        EnsureFindAdmission() && revision == findRevision && CanFind;

    internal void FindFocusFailed()
    {
        RetireFind(clearQuery: false);
        findStatus = "Literal Find focus unavailable; no selection or focus claimed.";
        Failure(logger, nameof(InvalidOperationException));
        NotifyFind();
    }

    private void RetireFind(bool clearQuery)
    {
        findRevision++;
        if (clearQuery) { findQuery = string.Empty; }
        findAnchor = null;
        findSelection = null;
        findStatus = string.Empty;
        NotifyFind();
    }

    private void NotifyFind()
    {
        OnPropertyChanged(nameof(FindQuery));
        OnPropertyChanged(nameof(FindStatus));
        OnPropertyChanged(nameof(FindSelection));
        OnPropertyChanged(nameof(FindMatchText));
        OnPropertyChanged(nameof(HasFindSelection));
        OnPropertyChanged(nameof(CanFind));
        OnPropertyChanged(nameof(CanMoveFind));
    }
}
