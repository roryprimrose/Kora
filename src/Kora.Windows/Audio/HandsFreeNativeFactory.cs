using Kora.Core.Voice;

namespace Kora.Windows.Audio;

internal sealed class HandsFreeNativeFactory : IHandsFreeNativeFactory
{
    public IHandsFreeKeywordDetector CreateKeyword(HandsFreeNativeProfile profile, PocketSphinxKeywordConfiguration configuration) =>
        new PocketSphinxKeywordDetector(profile, configuration);
    public IHandsFreeCommandEndpoint CreateEndpoint(HandsFreeNativeProfile profile) => new SileroCommandEndpoint(profile.VadPath);
    public async Task<IHandsFreeMicrophone> OpenMicrophoneAsync(HandsFreeAudioBinding binding, Func<bool> admitted)
    {
        var opened = await WindowsActivatedCaptureFactory.OpenMicrophoneAsync(binding.Microphone, admitted).ConfigureAwait(false);
        return new HandsFreeMicrophone(opened.Recorder, opened.Endpoint);
    }
    public Task<IActivatedCapture> OpenCommandAsync(HandsFreeAudioBinding binding, BlockingAudioStream stream, Func<bool> admitted) =>
        Task.Run(() => WindowsActivatedCaptureFactory.OpenCommand(binding.Microphone, binding.CommandPhrases, stream, admitted), CancellationToken.None);
}
