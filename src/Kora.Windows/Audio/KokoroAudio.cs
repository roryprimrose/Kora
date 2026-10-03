namespace Kora.Windows.Audio;

public sealed record KokoroAudio(
    byte[] Samples,
    int SampleRate,
    int BitsPerSample,
    int Channels);
