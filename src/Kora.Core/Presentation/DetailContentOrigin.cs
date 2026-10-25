namespace Kora.Core.Presentation;

public enum DetailContentOrigin
{
    EmbeddedDocument,
    FinalizedResponse,
    SessionHistory,
    /// <summary>A volatile host-captured, untrusted external observation, not durable session authority.</summary>
    RetrievedWebResult,
    /// <summary>An exact retained original USER record with a separately typed HOST observation, never a model answer.</summary>
    RetainedConversation,
    /// <summary>Complete separately retained assistant answer; not an approval, original USER or host receipt.</summary>
    RetainedAssistant,
}
