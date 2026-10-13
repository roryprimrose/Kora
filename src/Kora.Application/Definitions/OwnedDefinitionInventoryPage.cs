using System.Collections.ObjectModel;

namespace Kora.Application.Definitions;

public sealed record OwnedDefinitionInventoryPage(
    ReadOnlyCollection<OwnedDefinitionInventoryRow> Rows, int Offset, int TotalMatches, int DisplayUtf8Bytes)
{
    public int? NextOffset => Offset + Rows.Count < TotalMatches ? Offset + Rows.Count : null;
}
