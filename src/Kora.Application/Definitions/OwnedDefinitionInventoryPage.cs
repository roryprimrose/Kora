using System.Collections.ObjectModel;

namespace Kora.Application.Definitions;

public sealed record OwnedDefinitionInventoryPage(
    ReadOnlyCollection<OwnedDefinitionInventoryRow> Rows, int Offset, int TotalMatches, int DisplayUtf8Bytes)
{
    internal OwnedDefinitionInventory? Inventory { get; init; }
    internal string Query { get; init; } = string.Empty;
    internal OwnedDefinitionInventoryQueryMode Mode { get; init; }
    internal OwnedDefinitionInventoryOrder Order { get; init; }
    internal OwnedDefinitionUseFilter Filter { get; init; }
    public int? NextOffset => Offset + Rows.Count < TotalMatches ? Offset + Rows.Count : null;
}
