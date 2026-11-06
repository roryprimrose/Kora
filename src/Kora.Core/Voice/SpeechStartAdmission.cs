namespace Kora.Core.Voice;

/// <summary>Revalidates host authority at each synthesis and playback boundary after queued or deferred work.</summary>
/// <param name="start">The owned synchronous boundary start, invoked at most once during this callback.</param>
/// <param name="cancellationToken">Cancellation of this output admission.</param>
public delegate Task SpeechStartAdmission(Action start, CancellationToken cancellationToken);