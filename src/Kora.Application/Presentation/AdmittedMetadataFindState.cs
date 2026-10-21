using Kora.Core.Context;

namespace Kora.Application.Presentation;

// Getters are cached reads. Owners must check admission before disclosure and at each native focus boundary.
public sealed class AdmittedMetadataFindState(string admittedDetails, Func<bool> current, bool counted = false) : IDisposable
{
    private string snapshot = admittedDetails;
    private Func<bool>? admission = current;
    private string query = string.Empty;
    private string status = string.Empty;
    private string exactMatch = string.Empty;
    private SnapshotLiteralSpan? cursor;
    private SnapshotLiteralSpan? match;
    private int count;
    private int ordinal;

    public int Count => count;
    public int Ordinal => ordinal;
    public bool Available => admission is not null;
    public string Query => query;
    public string Status => status;
    public SnapshotLiteralSpan? Match => match;
    public bool CanNavigate => cursor is not null;
    public string ExactMatch => exactMatch;

    public void Edit(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (!EnsureCurrent()) { return; }
        query = value;
        cursor = match = null;
        exactMatch = string.Empty;
        count = ordinal = 0;
        status = string.Empty;
    }

    public void Clear()
    {
        if (!EnsureCurrent()) { return; }
        query = status = string.Empty;
        cursor = match = null;
        exactMatch = string.Empty;
        count = ordinal = 0;
    }

    public void ReportFocusUnavailable()
    {
        if (!EnsureCurrent()) { return; }
        cursor = match = null;
        exactMatch = string.Empty;
        count = ordinal = 0;
        status = "Exact metadata match focus is unavailable; no match or focus claimed. Find first to retry.";
    }

    public void Find(SnapshotFindDirection direction)
    {
        if (!EnsureCurrent()) { return; }
        match = null;
        exactMatch = string.Empty;
        if (!SnapshotLiteralFind.IsValidQuery(query))
        {
            cursor = null;
            count = ordinal = 0;
            status = "Invalid literal Find query: enter nonempty, bounded, well-formed one-line text without unsafe controls. Nothing was truncated.";
            return;
        }
        if (direction != SnapshotFindDirection.First && cursor is null)
        {
            status = "Find first after editing or clearing; no current match to navigate.";
            return;
        }
        var result = counted ? SnapshotLiteralFind.FindCounted(snapshot, query, direction, cursor)
            : (SnapshotLiteralFind.Find(snapshot, query, direction, cursor), 0, 0);
        if (!EnsureCurrent()) { return; }
        var (found, total, position) = result;
        if (counted) { count = total; ordinal = position; }
        match = found;
        exactMatch = found is { } span ? snapshot.Substring(span.Start, span.Length) : string.Empty;
        cursor = found ?? (direction == SnapshotFindDirection.First ? null : cursor);
        status = found is not null
            ? $"One exact literal match: UTF-16 [{found.Start}, {found.Start + found.Length}); end exclusive."
            : direction == SnapshotFindDirection.First ? "No literal match in the selected admitted metadata details."
                : direction == SnapshotFindDirection.Next ? "End of forward direction; no wrap. Earlier matches may remain."
                    : "End of backward direction; no wrap. Later matches may remain.";
        if (counted)
        {
            status += $" Selected metadata only: {count} nonoverlapping matches; "
                + (found is null ? $"no displayed match; boundary ordinal {ordinal} of {count}." : $"selected {ordinal} of {count}.");
        }
    }

    public bool CheckAdmission() => EnsureCurrent();

    private bool EnsureCurrent()
    {
        var admitted = admission;
        if (admitted?.Invoke() == true && ReferenceEquals(admission, admitted)) { return true; }
        Dispose();
        return false;
    }

    public void Dispose()
    {
        admission = null;
        snapshot = query = status = exactMatch = string.Empty;
        cursor = match = null;
        count = ordinal = 0;
    }
}
