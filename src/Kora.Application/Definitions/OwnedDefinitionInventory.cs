using System.Collections.ObjectModel;
using System.Text;

using Kora.Core.Artifacts;
using Kora.Core.Context;

namespace Kora.Application.Definitions;

public sealed class OwnedDefinitionInventory
{
    public const int MaximumDisplayUtf8Bytes = 64 * 1024;
    private static readonly UTF8Encoding Utf8 = new(false, true);
    internal readonly ReadOnlyCollection<ArtifactDefinition> CatalogueIdentity;
    internal readonly long FailureRevision;

    internal OwnedDefinitionInventory(ArtifactKind kind, IReadOnlyList<string> names,
        ReadOnlyCollection<ArtifactDefinition> candidates, long failureRevision, DateTimeOffset observedUtc)
    {
        if (observedUtc.Offset != TimeSpan.Zero)
        { throw new InvalidDataException("Inventory observation time must be UTC."); }
        if (names.Count > OwnedDefinitionKind.MaximumFiles(kind)
            || names.Distinct(StringComparer.Ordinal).Count() != names.Count)
        { throw new InvalidDataException("The owned filename inventory is oversized or ambiguous."); }
        Kind = kind;
        CatalogueIdentity = candidates;
        FailureRevision = failureRevision;
        Rows = Array.AsReadOnly(names.Order(StringComparer.Ordinal).Select(name =>
        {
            OwnedInstructionNames.ReadName(name);
            var candidate = candidates.SingleOrDefault(item => item.Kind == kind && item.Source is "kora-profile"
                && string.Equals(item.OwnedInstructionName, name, StringComparison.Ordinal));
            return new OwnedDefinitionInventoryRow(kind, name, candidate, observedUtc);
        }).ToArray());
    }

    public ArtifactKind Kind { get; }
    public ReadOnlyCollection<OwnedDefinitionInventoryRow> Rows { get; }

    public static void ValidateQuery(string query, OwnedDefinitionInventoryQueryMode mode = OwnedDefinitionInventoryQueryMode.Literal)
    {
        LiteralMetadataQuery.Validate(query);
        switch (mode)
        {
            case OwnedDefinitionInventoryQueryMode.Literal: break;
            case OwnedDefinitionInventoryQueryMode.ExactOwnedId: OwnedInstructionNames.WriteName(query); break;
            case OwnedDefinitionInventoryQueryMode.ExactArtifactId: ArtifactDefinition.ValidateId(query); break;
            default: throw new ArgumentOutOfRangeException(nameof(mode));
        }
    }

    public OwnedDefinitionInventoryPage Page(string query, OwnedDefinitionUseFilter filter, int offset = 0,
        OwnedDefinitionInventoryQueryMode mode = OwnedDefinitionInventoryQueryMode.Literal,
        OwnedDefinitionInventoryOrder order = OwnedDefinitionInventoryOrder.FileNameAscending)
    {
        ValidateQuery(query, mode);
        if (!Enum.IsDefined(filter)) { throw new ArgumentOutOfRangeException(nameof(filter)); }
        if (!Enum.IsDefined(order)) { throw new ArgumentOutOfRangeException(nameof(order)); }
        var validated = Rows.Select(row =>
        {
            var size = Utf8.GetByteCount(row.Details) + Utf8.GetByteCount(row.ToString());
            if (size > MaximumDisplayUtf8Bytes)
            { throw new InvalidDataException("One complete metadata row exceeds the native display bound."); }
            if (row.NativeRevision is { } nativeRevision) { OwnedInstructionRevision.Validate(nativeRevision); }
            return (Row: row, Size: size);
        }).ToArray();
        var filtered = validated.Where(item => (filter switch
        {
            OwnedDefinitionUseFilter.All => true,
            OwnedDefinitionUseFilter.UseEnabled => item.Row.ObservedUseEnabled is true,
            OwnedDefinitionUseFilter.ReviewRequired => item.Row.ObservedUseEnabled is false,
            _ => item.Row.ObservedUseEnabled is null,
        }) && (mode switch
            {
                OwnedDefinitionInventoryQueryMode.ExactOwnedId => string.Equals(item.Row.Name, query, StringComparison.Ordinal),
                OwnedDefinitionInventoryQueryMode.ExactArtifactId => item.Row.NativeRevision is not null
                    && string.Equals(item.Row.ArtifactId, query, StringComparison.Ordinal),
                _ => item.Row.CanonicalFileName.Contains(query, StringComparison.OrdinalIgnoreCase)
                || item.Row.DisplayName?.Contains(query, StringComparison.OrdinalIgnoreCase) == true
                || item.Row.ArtifactId?.Contains(query, StringComparison.OrdinalIgnoreCase) == true,
            }));
        // Sort the extensionless filename identity: appending the kind extension would reverse prefix pairs such as alpha/alpha-more.
        var matching = (order switch
        {
            OwnedDefinitionInventoryOrder.FileNameAscending => filtered.OrderBy(item => item.Row.Name, StringComparer.Ordinal),
            OwnedDefinitionInventoryOrder.FileNameDescending => filtered.OrderByDescending(item => item.Row.Name, StringComparer.Ordinal),
            OwnedDefinitionInventoryOrder.DisplayNameAscending => filtered.OrderBy(item => item.Row.DisplayName is null)
                .ThenBy(item => item.Row.DisplayName, StringComparer.Ordinal),
            _ => filtered.OrderBy(item => item.Row.DisplayName is null)
                .ThenByDescending(item => item.Row.DisplayName, StringComparer.Ordinal),
        }).ThenBy(item => item.Row.Name, StringComparer.Ordinal).ToArray();
        if (offset < 0 || offset > matching.Length) { throw new ArgumentOutOfRangeException(nameof(offset)); }
        var visible = new List<OwnedDefinitionInventoryRow>();
        var bytes = 0;
        foreach (var item in matching.Skip(offset))
        {
            if (bytes + item.Size > MaximumDisplayUtf8Bytes) { break; }
            visible.Add(item.Row);
            bytes += item.Size;
        }
        return new(Array.AsReadOnly(visible.ToArray()), offset, matching.Length, bytes)
        { Inventory = this, Query = query, Mode = mode, Order = order, Filter = filter };
    }

    public OwnedDefinitionInventoryPage Continue(OwnedDefinitionInventoryPage page, string query,
        OwnedDefinitionUseFilter filter,
        OwnedDefinitionInventoryQueryMode mode = OwnedDefinitionInventoryQueryMode.Literal,
        OwnedDefinitionInventoryOrder order = OwnedDefinitionInventoryOrder.FileNameAscending)
    {
        ArgumentNullException.ThrowIfNull(page);
        if (!ReferenceEquals(page.Inventory, this) || !string.Equals(page.Query, query, StringComparison.Ordinal)
            || page.Mode != mode || page.Order != order || page.Filter != filter
            || page.NextOffset is not { } next)
        { throw new InvalidOperationException("The inventory or complete filter changed, or no next page exists. Restart from the first page."); }
        return Page(query, filter, next, mode, order);
    }
}
