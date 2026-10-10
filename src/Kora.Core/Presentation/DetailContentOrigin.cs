namespace Kora.Core.Presentation;

public enum DetailContentOrigin
{
    EmbeddedDocument,
    FinalizedResponse,
    SessionHistory,
    /// <summary>A volatile host-captured, untrusted external observation, not durable session authority.</summary>
    RetrievedWebResult,
}
