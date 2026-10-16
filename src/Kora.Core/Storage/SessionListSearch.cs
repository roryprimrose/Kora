using System.Security.Cryptography;
using System.Text;
using System.Globalization;

using Kora.Core.Hosting;

namespace Kora.Core.Storage;

/// <summary>Volatile metadata navigation policy. Names and query digests grant no session authority.</summary>
public sealed class SessionListSearch
{
    private readonly Guid exactId;

    public SessionListSearch(SessionListSearchKind kind, SessionListFilter filter, string value,
        long? minimumGeneration = null, long? maximumGeneration = null,
        long? minimumMetadataRevision = null, long? maximumMetadataRevision = null,
        SessionNameAvailability nameAvailability = SessionNameAvailability.Any)
    {
        if (!Enum.IsDefined(kind) || !Enum.IsDefined(filter))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), "Select a known query kind and lifecycle filter.");
        }
        if (!Enum.IsDefined(nameAvailability))
        {
            throw new ArgumentOutOfRangeException(nameof(nameAvailability), "Select a known name-metadata availability.");
        }
        ArgumentNullException.ThrowIfNull(value);
        if (kind is SessionListSearchKind.Browse)
        {
            if (value.Length != 0)
            {
                throw new InvalidDataException("Browse requires empty query text. Select NameSubstring, ExactName or ExactId to search text.");
            }
            Value = value;
        }
        else if (kind is SessionListSearchKind.NameSubstring or SessionListSearchKind.ExactName)
        {
            // A valid scalar occupies at most two UTF-16 units; bound validation work before parsing.
            if (value.Length > SessionName.MaximumScalars * 2)
            {
                throw new InvalidDataException("The query exceeds the authoritative session-name bounds.");
            }
            Value = new SessionName(value).Value;
        }
        else
        {
            if (!Guid.TryParseExact(value, "D", out var id) || id == Guid.Empty
                || !string.Equals(value, id.ToString("D"), StringComparison.Ordinal))
            {
                throw new InvalidDataException("Enter a nonempty immutable session ID in canonical lowercase D format.");
            }
            exactId = id;
            Value = value;
        }
        Kind = kind;
        Filter = filter;
        NameAvailability = nameAvailability;
        MinimumGeneration = ValidateBound(minimumGeneration);
        MaximumGeneration = ValidateBound(maximumGeneration);
        MinimumMetadataRevision = ValidateBound(minimumMetadataRevision);
        MaximumMetadataRevision = ValidateBound(maximumMetadataRevision);
        if (minimumGeneration > maximumGeneration || minimumMetadataRevision > maximumMetadataRevision)
        {
            throw new InvalidDataException("Session metadata range minimum must be <= maximum. Empty means Any.");
        }
        Digest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            kind.ToString() + "\n" + filter.ToString() + "\n" + Value + "\n"
            + MinimumGeneration?.ToString(CultureInfo.InvariantCulture) + "\n"
            + MaximumGeneration?.ToString(CultureInfo.InvariantCulture) + "\n"
            + MinimumMetadataRevision?.ToString(CultureInfo.InvariantCulture) + "\n"
            + MaximumMetadataRevision?.ToString(CultureInfo.InvariantCulture) + "\n" + NameAvailability.ToString())));
    }

    public SessionListSearchKind Kind { get; }
    public SessionListFilter Filter { get; }
    public SessionNameAvailability NameAvailability { get; }
    public string Value { get; }
    public long? MinimumGeneration { get; }
    public long? MaximumGeneration { get; }
    public long? MinimumMetadataRevision { get; }
    public long? MaximumMetadataRevision { get; }
    public Guid? ExactId => Kind is SessionListSearchKind.ExactId ? exactId : null;
    public string Digest { get; }

    public bool Matches(SessionWorkspaceEntry entry)
    {
        ValidateEntry(entry);
        return (Filter is SessionListFilter.All || entry.Authority.IsActive == (Filter is SessionListFilter.Active))
            && (NameAvailability is SessionNameAvailability.Any
                || (entry.Metadata is not null) == (NameAvailability is SessionNameAvailability.Named))
            && (MinimumGeneration is not { } minimum || entry.Authority.Generation.Value >= minimum)
            && (MaximumGeneration is not { } maximum || entry.Authority.Generation.Value <= maximum)
            && (MinimumMetadataRevision is not { } minimumRevision
                || entry.Metadata is { } lowerMetadata && lowerMetadata.Revision.Value >= minimumRevision)
            && (MaximumMetadataRevision is not { } maximumRevision
                || entry.Metadata is { } upperMetadata && upperMetadata.Revision.Value <= maximumRevision)
            && (Kind is SessionListSearchKind.Browse || (Kind is SessionListSearchKind.ExactId ? entry.Authority.SessionId.Value == exactId
                : Kind is SessionListSearchKind.ExactName
                    ? string.Equals(entry.Metadata?.Name.Value, Value, StringComparison.Ordinal)
                    : entry.Metadata?.Name.Value.Contains(Value, StringComparison.Ordinal) == true));
    }

    public static void ValidateEntry(SessionWorkspaceEntry entry)
    {
        if (entry is null || entry.Authority is null)
        {
            throw new InvalidDataException("Session metadata requires a validated exact session authority.");
        }
        entry.Authority.SessionId.Validate();
        _ = new HostRevision(entry.Authority.Generation.Value);
        if (entry.Metadata is { } metadata)
        {
            if (metadata.SessionId != entry.Authority.SessionId || metadata.Name is null)
            {
                throw new InvalidDataException("Name metadata must belong to its exact session and carry a valid name.");
            }
            _ = new HostRevision(metadata.Revision.Value);
            _ = new SessionName(metadata.Name.Value);
        }
    }

    public static SessionListSearch Parse(SessionListSearchKind kind, SessionListFilter filter, string value,
        string minimumGeneration = "", string maximumGeneration = "",
        string minimumMetadataRevision = "", string maximumMetadataRevision = "",
        SessionNameAvailability nameAvailability = SessionNameAvailability.Any) =>
        new(kind, filter, value, OptionalUnsignedDecimal.Parse(minimumGeneration), OptionalUnsignedDecimal.Parse(maximumGeneration),
            OptionalUnsignedDecimal.Parse(minimumMetadataRevision), OptionalUnsignedDecimal.Parse(maximumMetadataRevision), nameAvailability);

    private static long? ValidateBound(long? value)
    {
        if (value is null) { return null; }
        try { return new HostRevision(value.Value).Value; }
        catch (ArgumentOutOfRangeException exception)
        {
            throw new InvalidDataException("Session metadata bounds must be positive decimal integers. Empty means Any.", exception);
        }
    }

    public static bool IsValid(SessionListSearchKind kind, SessionListFilter filter, string value,
        string minimumGeneration = "", string maximumGeneration = "",
        string minimumMetadataRevision = "", string maximumMetadataRevision = "",
        SessionNameAvailability nameAvailability = SessionNameAvailability.Any)
    {
        try { _ = Parse(kind, filter, value, minimumGeneration, maximumGeneration, minimumMetadataRevision, maximumMetadataRevision, nameAvailability); return true; }
        catch (ArgumentException) { return false; }
        catch (InvalidDataException) { return false; }
    }
}
