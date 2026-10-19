namespace Kora.Core.Dependencies;

/// <summary>Provenance is echoed by the host transport, never read from provider JSON.</summary>
public sealed record ModelToolTransportReply(
    ModelTurnOutcome Outcome, ModelTurnProvenance Provenance, ModelAssistantMessage? Message = null,
    ModelToolUsage? Usage = null);
