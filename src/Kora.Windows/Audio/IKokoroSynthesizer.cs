using KokoroSharp.Core;

namespace Kora.Windows.Audio;

internal interface IKokoroSynthesizer : IDisposable
{
    Task<byte[]> SynthesizeAsync(string text, KokoroVoice voice);
}
