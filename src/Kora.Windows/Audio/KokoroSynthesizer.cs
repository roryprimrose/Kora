using KokoroSharp;
using KokoroSharp.Core;

namespace Kora.Windows.Audio;

internal sealed class KokoroSynthesizer(string modelPath) : IKokoroSynthesizer
{
    private readonly KokoroWavSynthesizer synthesizer = new(modelPath);

    public Task<byte[]> SynthesizeAsync(string text, KokoroVoice voice) =>
        synthesizer.SynthesizeAsync(text, voice);

    public void Dispose() => synthesizer.Dispose();
}
