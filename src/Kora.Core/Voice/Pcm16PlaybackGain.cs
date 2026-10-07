using System.Buffers.Binary;
using Kora.Core.Configuration;

namespace Kora.Core.Voice;

public static class Pcm16PlaybackGain
{
    public static void Attenuate(Span<byte> samples, PlaybackVolume volume)
    {
        if (samples.Length == 0 || samples.Length % sizeof(short) != 0)
        {
            throw new InvalidDataException("Speech PCM must contain complete signed 16-bit little-endian samples.");
        }
        if (volume == PlaybackVolume.Default) { return; }
        for (var offset = 0; offset < samples.Length; offset += sizeof(short))
        {
            var sample = BinaryPrimitives.ReadInt16LittleEndian(samples[offset..]);
            BinaryPrimitives.WriteInt16LittleEndian(samples[offset..],
                checked((short)(sample * volume.Percent / PlaybackVolume.MaximumPercent)));
        }
    }
}
