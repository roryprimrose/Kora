namespace Kora.Core.Voice;

public sealed class PlaybackVolumeUnavailableException(string message) : InvalidOperationException(message);
