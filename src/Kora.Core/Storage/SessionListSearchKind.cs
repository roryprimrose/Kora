namespace Kora.Core.Storage;

/// <summary>The explicitly selected metadata navigation query, never an authority resolver.</summary>
public enum SessionListSearchKind
{
    /// <summary>Literal ordinal, case-sensitive substring of a validated name.</summary>
    NameSubstring,
    /// <summary>One nonempty immutable session ID in canonical lowercase D format.</summary>
    ExactId
}
