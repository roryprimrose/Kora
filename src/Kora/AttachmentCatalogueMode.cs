namespace Kora;

/// <summary>Volatile native attachment metadata observation mode, never session authority.</summary>
internal enum AttachmentCatalogueMode
{
    /// <summary>Browse retained slots under the removal filter.</summary>
    Browse,
    /// <summary>Filter validated slots by a canonical immutable session ID.</summary>
    ExactId,
}
