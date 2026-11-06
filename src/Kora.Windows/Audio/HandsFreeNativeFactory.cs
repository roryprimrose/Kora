using Kora.Core.Voice;

namespace Kora.Windows.Audio;

internal sealed class HandsFreeNativeFactory : IHandsFreeNativeFactory
{
    public IHandsFreeKeywordDetector CreateKeyword(HandsFreeNativeProfile profile, PocketSphinxKeywordConfiguration configuration) =>
        new PocketSphinxKeywordDetector(profile, configuration);
    public IHandsFreeCommandEndpoint CreateEndpoint(HandsFreeNativeProfile profile) => CreateEndpoint(profile, VoiceCaptureTiming.Default);
    public IHandsFreeCommandEndpoint CreateEndpoint(HandsFreeNativeProfile profile, VoiceCaptureTiming timing) =>
        new SileroCommandEndpoint(profile.VadPath, timing);
    public async Task<IHandsFreeMicrophone> OpenMicrophoneAsync(HandsFreeAudioBinding binding, Func<bool> admitted)
    {
        var opened = await WindowsActivatedCaptureFactory.OpenMicrophoneAsync(binding.Microphone, admitted).ConfigureAwait(false);
        return new HandsFreeMicrophone(opened.Recorder, opened.Endpoint);
    }
    public Task<IActivatedCapture> OpenCommandAsync(HandsFreeAudioBinding binding, BlockingAudioStream stream, Func<bool> admitted) =>
        Task.Run(() => WindowsActivatedCaptureFactory.OpenCommandAsync(binding.Microphone, binding.CommandPhrases, stream, admitted, binding.Timing), CancellationToken.None);
}
