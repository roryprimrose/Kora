using Kora.Core.Voice;

namespace Kora.Windows.Audio;

/// <summary>Creates generation-owned native resources; never supplies host admission or profile qualification.</summary>
internal interface IHandsFreeNativeFactory
{
    /// <summary>Creates the pinned keyword C ABI consumer.</summary>
    IHandsFreeKeywordDetector CreateKeyword(HandsFreeNativeProfile profile, PocketSphinxKeywordConfiguration configuration);
    /// <summary>Creates command-only endpointing.</summary>
    IHandsFreeCommandEndpoint CreateEndpoint(HandsFreeNativeProfile profile);
    /// <summary>Opens the one admitted microphone.</summary>
    Task<IHandsFreeMicrophone> OpenMicrophoneAsync(HandsFreeAudioBinding binding, Func<bool> admitted);
    /// <summary>Opens existing local ASR over the admitted command-only PCM stream.</summary>
    Task<IActivatedCapture> OpenCommandAsync(HandsFreeAudioBinding binding, BlockingAudioStream stream, Func<bool> admitted);
}
