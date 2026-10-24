using Kora.Core.Voice;

namespace Kora.Application.Voice;

public sealed partial class HandsFreeVoiceRecognitionService
{
    public SpeechInputGeneration ProfileGeneration => ProfileRuntime.ProfileGeneration;
    public bool IsProfileAvailable => !Volatile.Read(ref disposed)
        && activated is ISpeechInputProfileRuntime runtime && runtime.IsProfileAvailable;

    public void InitializeProfile(SpeechInputSelection? selection)
    {
        EnsureNotDisposed();
        if (!IsCaptureQuiescent) { throw new InvalidOperationException("Profile initialization requires quiescent input."); }
        ProfileRuntime.InitializeProfile(selection);
    }

    public async Task<ISpeechInputProfileSwitch> RetireProfileAsync(CancellationToken cancellationToken)
    {
        EnsureNotDisposed();
        await StopAsync(cancellationToken);
        EnsureNotDisposed();
        return await ProfileRuntime.RetireProfileAsync(cancellationToken);
    }

    private ISpeechInputProfileRuntime ProfileRuntime => activated as ISpeechInputProfileRuntime
        ?? throw new InvalidOperationException("The activated adapter does not provide speech-input profile switching.");
}
