using System.Collections.ObjectModel;
using System.Text;

using Kora.Core.Artifacts;
using Kora.Core.Context;
using Kora.Core.Storage;

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

    public static void ValidateQuery(string query) => LiteralMetadataQuery.Validate(query);

    public OwnedDefinitionInventoryPage Page(string query, OwnedDefinitionUseFilter filter, int offset = 0, string digest = "",
        string revision = "", string observedFromUtc = "", string observedUntilUtc = "")
    {
        ValidateQuery(query);
        ArgumentNullException.ThrowIfNull(digest);
        if (digest.Length != 0) { ArtifactDigest.Validate(digest); }
        ArgumentNullException.ThrowIfNull(revision);
        if (revision.Length != 0) { OwnedInstructionRevision.Validate(revision); }
        var from = OptionalCanonicalUtc.Parse(observedFromUtc);
        var until = OptionalCanonicalUtc.Parse(observedUntilUtc);
        if (from > until)
        { throw new InvalidDataException("Observed UTC From must not be after Until."); }
        if (!Enum.IsDefined(filter)) { throw new ArgumentOutOfRangeException(nameof(filter)); }
        var validated = Rows.Select(row =>
        {
            var size = Utf8.GetByteCount(row.Details) + Utf8.GetByteCount(row.ToString());
            if (size > MaximumDisplayUtf8Bytes)
            { throw new InvalidDataException("One complete metadata row exceeds the native display bound."); }
            if (row.NativeRevision is { } nativeRevision) { OwnedInstructionRevision.Validate(nativeRevision); }
            return (Row: row, Size: size);
        }).ToArray();
        var matching = validated.Where(item => (filter switch
        {
            OwnedDefinitionUseFilter.All => true,
            OwnedDefinitionUseFilter.UseEnabled => item.Row.ObservedUseEnabled is true,
            OwnedDefinitionUseFilter.ReviewRequired => item.Row.ObservedUseEnabled is false,
            _ => item.Row.ObservedUseEnabled is null,
        }) && (digest.Length == 0 || string.Equals(item.Row.Digest, digest, StringComparison.Ordinal))
            && (revision.Length == 0 || string.Equals(item.Row.NativeRevision, revision, StringComparison.Ordinal))
            && (from is null || item.Row.ObservedUtc >= from)
            && (until is null || item.Row.ObservedUtc <= until)
            && (item.Row.CanonicalFileName.Contains(query, StringComparison.OrdinalIgnoreCase)
                || item.Row.DisplayName?.Contains(query, StringComparison.OrdinalIgnoreCase) == true
                || item.Row.ArtifactId?.Contains(query, StringComparison.OrdinalIgnoreCase) == true)).ToArray();
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
        { Inventory = this, Query = query, Filter = filter, Digest = digest, Revision = revision,
            ObservedFromUtc = observedFromUtc, ObservedUntilUtc = observedUntilUtc };
    }

    public OwnedDefinitionInventoryPage Continue(OwnedDefinitionInventoryPage page, string query,
        OwnedDefinitionUseFilter filter, string digest = "", string revision = "", string observedFromUtc = "", string observedUntilUtc = "")
    {
        ArgumentNullException.ThrowIfNull(page);
        if (!ReferenceEquals(page.Inventory, this) || !string.Equals(page.Query, query, StringComparison.Ordinal)
            || page.Filter != filter || !string.Equals(page.Digest, digest, StringComparison.Ordinal)
            || !string.Equals(page.Revision, revision, StringComparison.Ordinal)
            || !string.Equals(page.ObservedFromUtc, observedFromUtc, StringComparison.Ordinal)
            || !string.Equals(page.ObservedUntilUtc, observedUntilUtc, StringComparison.Ordinal)
            || page.NextOffset is not { } next)
        { throw new InvalidOperationException("The inventory or complete filter changed, or no next page exists. Restart from the first page."); }
        return Page(query, filter, next, digest, revision, observedFromUtc, observedUntilUtc);
    }
}
