namespace Kora.Core.Voice;

/// <summary>Revalidates host authority after output queueing and invokes the exact synthesis start.</summary>
/// <param name="start">The owned synchronous synthesis start, invoked at most once.</param>
/// <param name="cancellationToken">Cancellation of this output admission.</param>
public delegate Task SpeechStartAdmission(Action start, CancellationToken cancellationToken);