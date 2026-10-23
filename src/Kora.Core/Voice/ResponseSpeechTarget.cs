using System.Runtime.InteropServices;

namespace Kora.Core.Voice;

/// <summary>Identifies one current response or scoped retained-answer playback and its call policy revision.</summary>
/// <param name="ResponseId">The host-allocated response identity, never text or a historical row ID.</param>
/// <param name="Revision">The exact presentation revision.</param>
/// <param name="PolicyRevision">The observed communication policy revision.</param>
[StructLayout(LayoutKind.Auto)]
public readonly record struct ResponseSpeechTarget(Guid ResponseId, long Revision, long PolicyRevision);
