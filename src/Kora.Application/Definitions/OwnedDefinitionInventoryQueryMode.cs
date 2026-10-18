namespace Kora.Application.Definitions;

/// <summary>Selects the metadata identity or literal navigation rule, without granting use authority.</summary>
public enum OwnedDefinitionInventoryQueryMode
{
    /// <summary>Finds an ordinal-ignore-case substring in filename, display name or artifact ID.</summary>
    Literal,
    /// <summary>Matches an authoritative canonical owned name ID, without an extension or path.</summary>
    ExactOwnedId,
    /// <summary>Matches an admitted observed artifact identifier using ordinal case-sensitive equality.</summary>
    ExactArtifactId,
}
