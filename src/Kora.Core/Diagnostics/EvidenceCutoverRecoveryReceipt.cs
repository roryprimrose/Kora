namespace Kora.Core.Diagnostics;

public sealed record EvidenceCutoverRecoveryReceipt(Guid StoreIdentity, long Generation, int RegisteredSegments);
