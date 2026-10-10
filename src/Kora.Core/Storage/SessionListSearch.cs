using System.Security.Cryptography;
using System.Text;

using Kora.Core.Hosting;

namespace Kora.Core.Storage;

/// <summary>Volatile metadata navigation policy. Names and query digests grant no session authority.</summary>
public sealed class SessionListSearch
{
    private readonly Guid exactId;

    public SessionListSearch(SessionListSearchKind kind, SessionListFilter filter, string value)
    {
        if (!Enum.IsDefined(kind) || !Enum.IsDefined(filter))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), "Select a known query kind and lifecycle filter.");
        }
        ArgumentNullException.ThrowIfNull(value);
        if (kind is SessionListSearchKind.NameSubstring)
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
        Digest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            kind.ToString() + "\n" + filter.ToString() + "\n" + Value)));
    }

    public SessionListSearchKind Kind { get; }
    public SessionListFilter Filter { get; }
    public string Value { get; }
    public Guid? ExactId => Kind is SessionListSearchKind.ExactId ? exactId : null;
    public string Digest { get; }

    public bool Matches(SessionWorkspaceEntry entry) =>
        (Filter is SessionListFilter.All || entry.Authority.IsActive == (Filter is SessionListFilter.Active))
        && (Kind is SessionListSearchKind.ExactId ? entry.Authority.SessionId.Value == exactId
            : entry.Metadata?.Name.Value.Contains(Value, StringComparison.Ordinal) == true);

    public static bool IsValid(SessionListSearchKind kind, SessionListFilter filter, string value)
    {
        try { _ = new SessionListSearch(kind, filter, value); return true; }
        catch (ArgumentException) { return false; }
        catch (InvalidDataException) { return false; }
    }
}
