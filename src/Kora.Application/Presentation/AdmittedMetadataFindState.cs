using Kora.Core.Context;

namespace Kora.Application.Presentation;

public sealed class AdmittedMetadataFindState(string admittedDetails, Func<bool> current, bool counted = false) : IDisposable
{
    private string snapshot = admittedDetails;
    private Func<bool>? admission = current;
    private string query = string.Empty;
    private string status = string.Empty;
    private SnapshotLiteralSpan? cursor;
    private SnapshotLiteralSpan? match;
    private string exactMatch = string.Empty;
    private int count;
    private int ordinal;
    private bool hasCount;

    // Passive readers must not retire state or raise notifications from binding getters.
    public AdmittedMetadataFindSnapshot ReadCurrent() => admission?.Invoke() == true
        ? new(true, query, status, match, cursor is not null, exactMatch, count, ordinal, hasCount)
        : AdmittedMetadataFindSnapshot.Unavailable;

    public bool Available => EnsureCurrent();
    public string Query => EnsureCurrent() ? query : string.Empty;
    public string Status => EnsureCurrent() ? status : string.Empty;
    public SnapshotLiteralSpan? Match => EnsureCurrent() ? match : null;
    public bool CanNavigate => EnsureCurrent() && cursor is not null;
    public int Count => EnsureCurrent() ? count : 0;
    public int Ordinal => EnsureCurrent() ? ordinal : 0;
    public string ExactMatch => EnsureCurrent() ? exactMatch : string.Empty;

    public void Edit(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (!EnsureCurrent()) { return; }
        query = value;
        cursor = match = null;
        count = ordinal = 0;
        hasCount = false;
        exactMatch = string.Empty;
        status = string.Empty;
    }

    public void Clear()
    {
        if (!EnsureCurrent()) { return; }
        query = status = string.Empty;
        cursor = match = null;
        count = ordinal = 0;
        hasCount = false;
        exactMatch = string.Empty;
    }

    public void ReportFocusUnavailable()
    {
        if (!EnsureCurrent()) { return; }
        cursor = match = null;
        count = ordinal = 0;
        status = "Exact metadata match focus is unavailable; no match or focus claimed. Find first to retry.";
        hasCount = false;
        exactMatch = string.Empty;
    }

    public void Find(SnapshotFindDirection direction)
    {
        if (!EnsureCurrent()) { return; }
        if (!Enum.IsDefined(direction)) { throw new ArgumentOutOfRangeException(nameof(direction)); }
        match = null;
        exactMatch = string.Empty;
        if (!SnapshotLiteralFind.IsValidQuery(query))
        {
            cursor = null;
            count = ordinal = 0;
            hasCount = false;
            status = "Invalid literal Find query: enter nonempty, bounded, well-formed one-line text without unsafe controls. Nothing was truncated.";
            return;
        }
        if (direction != SnapshotFindDirection.First && cursor is null)
        {
            status = "Find first after editing or clearing; no current match to navigate.";
            return;
        }
        var result = SnapshotLiteralFind.FindCounted(snapshot, query, direction, cursor);
        if (!EnsureCurrent()) { return; }
        var found = result.Match;
        count = result.Count;
        ordinal = result.Ordinal;
        hasCount = true;
        match = found;
        exactMatch = found is null ? string.Empty : snapshot.Substring(found.Start, found.Length);
        cursor = found ?? (direction == SnapshotFindDirection.First ? null : cursor);
        status = found is not null
            ? $"One exact literal match: UTF-16 [{found.Start}, {found.Start + found.Length}); end exclusive. Match {ordinal} of {count} in this selected admitted metadata string only."
            : direction == SnapshotFindDirection.First ? "No literal match in the selected admitted metadata details; count 0 in this string only."
                : direction == SnapshotFindDirection.Next ? "End of forward direction; no wrap. Earlier matches may remain."
                    : "End of backward direction; no wrap. Later matches may remain.";
        if (counted)
        {
            status += $" Selected metadata only: {count} nonoverlapping matches; "
                + (found is null ? "no displayed match." : $"selected {ordinal} of {count}.");
        }
    }

    private bool EnsureCurrent()
    {
        if (admission?.Invoke() == true) { return true; }
        Dispose();
        return false;
    }

    public void Dispose()
    {
        admission = null;
        snapshot = query = status = string.Empty;
        cursor = match = null;
        count = ordinal = 0;
        hasCount = false;
        exactMatch = string.Empty;
    }
}
