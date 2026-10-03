namespace Kora.Windows.Audio;

internal static class VoiceRecognitionShutdown
{
    public static async Task WaitForCompletionAsync(
        ValueTask recorderDisposal,
        Task recognitionCompletion)
    {
        ArgumentNullException.ThrowIfNull(recognitionCompletion);

#pragma warning disable VSTHRD003 // Both operations are event-driven and awaited without blocking a context thread.
        await Task.WhenAll(
            recorderDisposal.AsTask(),
            recognitionCompletion);
#pragma warning restore VSTHRD003
    }
}
