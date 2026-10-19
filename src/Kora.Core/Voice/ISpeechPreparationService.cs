namespace Kora.Core.Voice;

public interface ISpeechPreparationService
{
    // Preparation never acquires assets, synthesizes audio, or opens input/output devices.
    Task PrepareAsync(SpeechVoice voice, CancellationToken cancellationToken = default);
}
