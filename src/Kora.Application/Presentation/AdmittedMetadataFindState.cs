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
    private int count;
    private int ordinal;

    public bool Available => EnsureCurrent();
    public string Query => EnsureCurrent() ? query : string.Empty;
    public string Status => EnsureCurrent() ? status : string.Empty;
    public SnapshotLiteralSpan? Match => EnsureCurrent() ? match : null;
    public bool CanNavigate => EnsureCurrent() && cursor is not null;
    public int Count => EnsureCurrent() ? count : 0;
    public int Ordinal => EnsureCurrent() ? ordinal : 0;
    public string ExactMatch => EnsureCurrent() && match is { } span ? snapshot.Substring(span.Start, span.Length) : string.Empty;

    public void Edit(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (!EnsureCurrent()) { return; }
        query = value;
        cursor = match = null;
        count = ordinal = 0;
        status = string.Empty;
    }

    public void Clear()
    {
        if (!EnsureCurrent()) { return; }
        query = status = string.Empty;
        cursor = match = null;
        count = ordinal = 0;
    }

    public void ReportFocusUnavailable()
    {
        if (!EnsureCurrent()) { return; }
        cursor = match = null;
        count = ordinal = 0;
        status = "Exact metadata match focus is unavailable; no match or focus claimed. Find first to retry.";
    }

    public void Find(SnapshotFindDirection direction)
    {
        if (!EnsureCurrent()) { return; }
        match = null;
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
        SnapshotLiteralSpan? found;
        if (counted)
        {
            var result = SnapshotLiteralFind.FindCounted(snapshot, query, direction, cursor);
            found = result.Match;
            count = result.Count;
            ordinal = result.Ordinal;
        }
        else { found = SnapshotLiteralFind.Find(snapshot, query, direction, cursor); }
        if (!EnsureCurrent()) { return; }
        match = found;
        cursor = found ?? (direction == SnapshotFindDirection.First ? null : cursor);
        status = found is not null
            ? $"One exact literal match: UTF-16 [{found.Start}, {found.Start + found.Length}); end exclusive."
            : direction == SnapshotFindDirection.First ? "No literal match in the selected admitted metadata details."
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
    }
}
