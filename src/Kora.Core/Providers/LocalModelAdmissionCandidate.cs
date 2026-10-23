namespace Kora.Core.Providers;

/// <summary>Opaque platform observation handle; fields alone cannot authorize a process or produce a receipt.</summary>
public sealed record LocalModelAdmissionCandidate(Guid ObservationId, string EvidenceDigest, ProviderAccountId Account);