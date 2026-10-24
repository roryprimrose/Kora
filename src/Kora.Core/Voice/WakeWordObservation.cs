namespace Kora.Core.Voice;

public sealed record WakeWordObservation(long Generation, long CommandStartSample, bool PlaybackExcluded);
