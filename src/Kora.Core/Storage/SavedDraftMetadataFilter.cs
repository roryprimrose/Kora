using System.Security.Cryptography;
using System.Text;

using Kora.Core.Conversation;
using Kora.Core.Hosting;
using Kora.Core.Memory;

namespace Kora.Core.Storage;

/// <summary>Content-free saved-draft predicates; empty native fields mean Any, never repaired input.</summary>
public sealed record SavedDraftMetadataFilter
{
    public static SavedDraftMetadataFilter Any { get; } = new(null, null, null);

    public SavedDraftMetadataFilter(long? draftRevision, long? minimumUtf8Bytes, long? maximumUtf8Bytes,
        long? minimumUnicodeScalars = null, long? maximumUnicodeScalars = null,
        Guid? source = null, HostId<DeviceProfileIdentity>? profile = null,
        long? minimumSessionGeneration = null, long? maximumSessionGeneration = null,
        long? minimumNameMetadataRevision = null, long? maximumNameMetadataRevision = null,
        long? minimumControlRevision = null, long? maximumControlRevision = null,
        long? minimumAuthorityCeiling = null, long? maximumAuthorityCeiling = null)
    {
        if (source == Guid.Empty) { throw new InvalidDataException("An observation source predicate cannot be an empty GUID."); }
        profile?.Validate();
        if (draftRevision is <= 0
            || minimumUtf8Bytes is < 0 or > ConversationText.MaximumUtf8Bytes
            || maximumUtf8Bytes is < 0 or > ConversationText.MaximumUtf8Bytes
            || minimumUtf8Bytes > maximumUtf8Bytes
            || minimumUnicodeScalars is < 0 or > ConversationText.MaximumUtf16Characters
            || maximumUnicodeScalars is < 0 or > ConversationText.MaximumUtf16Characters
            || minimumUnicodeScalars > maximumUnicodeScalars)
        { throw new InvalidDataException("Saved draft revision must be positive; UTF-8 byte and Unicode scalar bounds must be within their conversation maxima and minimum <= maximum."); }
        ValidateRange(minimumSessionGeneration, maximumSessionGeneration, 1);
        ValidateRange(minimumNameMetadataRevision, maximumNameMetadataRevision, 1);
        ValidateRange(minimumControlRevision, maximumControlRevision, 0);
        ValidateRange(minimumAuthorityCeiling, maximumAuthorityCeiling, 1);
        DraftRevision = draftRevision;
        MinimumUtf8Bytes = minimumUtf8Bytes;
        MaximumUtf8Bytes = maximumUtf8Bytes;
        MinimumUnicodeScalars = minimumUnicodeScalars;
        MaximumUnicodeScalars = maximumUnicodeScalars;
        Source = source;
        Profile = profile;
        MinimumSessionGeneration = minimumSessionGeneration;
        MaximumSessionGeneration = maximumSessionGeneration;
        MinimumNameMetadataRevision = minimumNameMetadataRevision;
        MaximumNameMetadataRevision = maximumNameMetadataRevision;
        MinimumControlRevision = minimumControlRevision;
        MaximumControlRevision = maximumControlRevision;
        MinimumAuthorityCeiling = minimumAuthorityCeiling;
        MaximumAuthorityCeiling = maximumAuthorityCeiling;
        Digest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            FormattableString.Invariant($"{draftRevision}\n{minimumUtf8Bytes}\n{maximumUtf8Bytes}\n{minimumUnicodeScalars}\n{maximumUnicodeScalars}\n{source:D}\n{profile?.Value:D}\n{minimumSessionGeneration}\n{maximumSessionGeneration}\n{minimumNameMetadataRevision}\n{maximumNameMetadataRevision}\n{minimumControlRevision}\n{maximumControlRevision}\n{minimumAuthorityCeiling}\n{maximumAuthorityCeiling}"))));
    }

    public long? DraftRevision { get; }
    public long? MinimumUtf8Bytes { get; }
    public long? MaximumUtf8Bytes { get; }
    public long? MinimumUnicodeScalars { get; }
    public long? MaximumUnicodeScalars { get; }
    public Guid? Source { get; }
    public HostId<DeviceProfileIdentity>? Profile { get; }
    public long? MinimumSessionGeneration { get; }
    public long? MaximumSessionGeneration { get; }
    public long? MinimumNameMetadataRevision { get; }
    public long? MaximumNameMetadataRevision { get; }
    public long? MinimumControlRevision { get; }
    public long? MaximumControlRevision { get; }
    public long? MinimumAuthorityCeiling { get; }
    public long? MaximumAuthorityCeiling { get; }
    public string Digest { get; }

    public bool Matches(SavedDraftSummary row)
    {
        row.Validate();
        return (DraftRevision is null || row.DraftRevision == DraftRevision)
        && (MinimumUtf8Bytes is null || row.Utf8Bytes >= MinimumUtf8Bytes)
        && (MaximumUtf8Bytes is null || row.Utf8Bytes <= MaximumUtf8Bytes)
        && (MinimumUnicodeScalars is null || row.UnicodeScalars >= MinimumUnicodeScalars)
        && (MaximumUnicodeScalars is null || row.UnicodeScalars <= MaximumUnicodeScalars)
        && (Source is not { } source || row.Source == source)
        && (Profile is not { } profile || row.Profile == profile)
        && InRange(row.Session.Generation.Value, MinimumSessionGeneration, MaximumSessionGeneration)
        && (MinimumNameMetadataRevision is null && MaximumNameMetadataRevision is null
            || row.Metadata is { } metadata && InRange(metadata.Revision.Value, MinimumNameMetadataRevision, MaximumNameMetadataRevision))
        && InRange(row.ControlRevision, MinimumControlRevision, MaximumControlRevision)
        && InRange(row.AuthorityCeiling, MinimumAuthorityCeiling, MaximumAuthorityCeiling);
    }

    private static void ValidateRange(long? minimum, long? maximum, long floor)
    {
        if (minimum < floor || maximum < floor || minimum > maximum)
        { throw new InvalidDataException("Saved draft provenance bounds must meet their positive/nonnegative field floor and minimum <= maximum."); }
    }

    private static bool InRange(long value, long? minimum, long? maximum) =>
        (minimum is null || value >= minimum) && (maximum is null || value <= maximum);

    public static SavedDraftMetadataFilter Parse(string revision, string minimum, string maximum,
        string minimumScalars = "", string maximumScalars = "", string source = "", string profile = "",
        string minimumSessionGeneration = "", string maximumSessionGeneration = "",
        string minimumNameMetadataRevision = "", string maximumNameMetadataRevision = "",
        string minimumControlRevision = "", string maximumControlRevision = "",
        string minimumAuthorityCeiling = "", string maximumAuthorityCeiling = "") =>
        new(OptionalUnsignedDecimal.Parse(revision), OptionalUnsignedDecimal.Parse(minimum),
            OptionalUnsignedDecimal.Parse(maximum), OptionalUnsignedDecimal.Parse(minimumScalars),
            OptionalUnsignedDecimal.Parse(maximumScalars), ParseOptionalGuid(source),
            ParseOptionalGuid(profile) is { } id ? new HostId<DeviceProfileIdentity>(id) : null,
            OptionalUnsignedDecimal.Parse(minimumSessionGeneration), OptionalUnsignedDecimal.Parse(maximumSessionGeneration),
            OptionalUnsignedDecimal.Parse(minimumNameMetadataRevision), OptionalUnsignedDecimal.Parse(maximumNameMetadataRevision),
            OptionalUnsignedDecimal.Parse(minimumControlRevision), OptionalUnsignedDecimal.Parse(maximumControlRevision),
            OptionalUnsignedDecimal.Parse(minimumAuthorityCeiling), OptionalUnsignedDecimal.Parse(maximumAuthorityCeiling));

    public static bool IsValid(string revision, string minimum, string maximum,
        string minimumScalars = "", string maximumScalars = "", string source = "", string profile = "",
        string minimumSessionGeneration = "", string maximumSessionGeneration = "",
        string minimumNameMetadataRevision = "", string maximumNameMetadataRevision = "",
        string minimumControlRevision = "", string maximumControlRevision = "",
        string minimumAuthorityCeiling = "", string maximumAuthorityCeiling = "")
    {
        try
        {
            _ = Parse(revision, minimum, maximum, minimumScalars, maximumScalars, source, profile,
                minimumSessionGeneration, maximumSessionGeneration, minimumNameMetadataRevision, maximumNameMetadataRevision,
                minimumControlRevision, maximumControlRevision, minimumAuthorityCeiling, maximumAuthorityCeiling);
            return true;
        }
        catch (InvalidDataException) { return false; }
    }

    private static Guid? ParseOptionalGuid(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length == 0) { return null; }
        if (!Guid.TryParseExact(text, "D", out var value) || value == Guid.Empty
            || !string.Equals(text, value.ToString("D"), StringComparison.Ordinal))
        { throw new InvalidDataException("Enter a nonempty canonical lowercase D-format GUID; empty means Any. No whitespace or repair."); }
        return value;
    }
}
