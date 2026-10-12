using System.Collections.ObjectModel;
using System.Globalization;
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

    public static void ValidateQuery(string query)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.Length > LocalFileRetrievalPolicy.MaximumQueryCharacters)
        { throw new InvalidDataException("Use at most 256 UTF-16 units / 512 UTF-8 bytes for the literal metadata query."); }
        try
        {
            if (Utf8.GetByteCount(query) > LocalFileRetrievalPolicy.MaximumQueryUtf8Bytes
                || query.EnumerateRunes().Any(rune => Rune.GetUnicodeCategory(rune) is UnicodeCategory.Control
                    or UnicodeCategory.Format or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator))
            { throw new InvalidDataException("Use a bounded single-line literal metadata query without control or format characters."); }
        }
        catch (EncoderFallbackException exception)
        { throw new InvalidDataException("The metadata query must contain valid Unicode.", exception); }
    }

    public OwnedDefinitionInventoryPage Page(string query, OwnedDefinitionUseFilter filter, int offset = 0)
    {
        ValidateQuery(query);
        if (!Enum.IsDefined(filter)) { throw new ArgumentOutOfRangeException(nameof(filter)); }
        var matching = Rows.Where(row => (filter switch
        {
            OwnedDefinitionUseFilter.All => true,
            OwnedDefinitionUseFilter.UseEnabled => row.ObservedUseEnabled is true,
            OwnedDefinitionUseFilter.ReviewRequired => row.ObservedUseEnabled is false,
            _ => row.ObservedUseEnabled is null,
        }) && (row.CanonicalFileName.Contains(query, StringComparison.OrdinalIgnoreCase)
            || row.DisplayName?.Contains(query, StringComparison.OrdinalIgnoreCase) == true
            || row.ArtifactId?.Contains(query, StringComparison.OrdinalIgnoreCase) == true)).ToArray();
        if (offset < 0 || offset > matching.Length) { throw new ArgumentOutOfRangeException(nameof(offset)); }
        var visible = new List<OwnedDefinitionInventoryRow>();
        var bytes = 0;
        foreach (var row in matching.Skip(offset))
        {
            var size = Utf8.GetByteCount(row.Details) + Utf8.GetByteCount(row.ToString());
            if (size > MaximumDisplayUtf8Bytes)
            { throw new InvalidDataException("One complete metadata row exceeds the native display bound."); }
            if (bytes + size > MaximumDisplayUtf8Bytes) { break; }
            visible.Add(row);
            bytes += size;
        }
        return new(Array.AsReadOnly(visible.ToArray()), offset, matching.Length, bytes);
    }
}
