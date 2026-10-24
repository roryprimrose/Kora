namespace Kora.Core.Agents;

public sealed class AgentProfilePublicationUnconfirmedException(Exception innerException)
    : IOException("Owned agent publication occurred; durable completion is unconfirmed. Inspect the exact destination before any new operation.", innerException);
