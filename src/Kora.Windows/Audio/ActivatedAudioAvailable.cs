namespace Kora.Windows.Audio;

internal delegate void ActivatedAudioAvailable(IActivatedCapture sender, ReadOnlySpan<byte> buffer);
