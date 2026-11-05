using System.Collections.Immutable;

using Kora.Core.Mcp;

namespace Kora;

/// <summary>Filters an already admitted metadata snapshot without consulting a producer or mutating user choices.</summary>
internal sealed class McpCapabilityRows
{
    private ImmutableArray<McpCapabilityRow> all = [];
    internal string Filter { get; private set; } = string.Empty;
    internal McpCapabilityCategory? Category { get; private set; }
    internal McpCapabilityRow? Selected { get; private set; }
    internal ImmutableArray<McpCapabilityRow> Visible { get; private set; } = [];
    internal int Total { get; private set; }
    internal string CountText => Visible.Length + " of " + Total + " capabilities";

    internal void SetSnapshot(IEnumerable<McpCapabilityRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var snapshot = rows.ToImmutableArray();
        if (snapshot.Any(row => row is null)
            || snapshot.Select(row => row.Identity).Distinct(StringComparer.Ordinal).Count() != snapshot.Length)
        { throw new ArgumentException("Displayed MCP metadata requires unique exact source-qualified identities.", nameof(rows)); }
        all = snapshot;
        Selected = Selected is { } selected ? all.SingleOrDefault(row =>
            string.Equals(row.Identity, selected.Identity, StringComparison.Ordinal)) : null;
        Refresh();
    }

    internal void ApplyFilter(string? filter, McpCapabilityCategory? category)
    {
        if (category is not (null or McpCapabilityCategory.Tools or McpCapabilityCategory.Resources or McpCapabilityCategory.Prompts))
        { throw new ArgumentOutOfRangeException(nameof(category)); }
        Filter = filter ?? string.Empty;
        Category = category;
        Refresh();
    }

    internal void Select(McpCapabilityRow row)
    {
        if (!Visible.Any(item => ReferenceEquals(item, row)))
        { throw new InvalidOperationException("Select an exact currently displayed MCP metadata row."); }
        Selected = row;
    }

    internal void ClearFilter() => ApplyFilter(string.Empty, Category);

    internal void ClearPrivate()
    {
        all = [];
        Visible = [];
        Selected = null;
        Filter = string.Empty;
        Category = null;
        Total = 0;
    }

    private void Refresh()
    {
        var category = all.Where(row => Category is null || row.Capability.Category == Category).ToArray();
        Total = category.Length;
        Visible = category.Where(row => row.Matches(Filter)).ToImmutableArray();
    }
}