using System.Runtime.InteropServices;

namespace Kora.Core.Voice;

/// <summary>Identifies one host-admitted playback and its native output generation.</summary>
/// <param name="Id">The host-issued playback identity, never an engine or model identity.</param>
/// <param name="Generation">The output generation admitted by the playback owner.</param>
[StructLayout(LayoutKind.Auto)]
public readonly record struct SpeechPlaybackToken(Guid Id, long Generation);
