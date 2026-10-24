namespace Kora.Core.Voice;

/// <summary>Mono PCM pre-roll. Sample positions are generation-local; no audio is persisted.</summary>
public sealed class WakeAudioBuffer
{
    private readonly short[] samples;
    private readonly Lock gate = new();
    private long end;
    private int count;

    public WakeAudioBuffer(int samplesPerSecond)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(samplesPerSecond, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(samplesPerSecond, 192000);
        samples = new short[checked(samplesPerSecond * 2)];
    }

    public int Capacity => samples.Length;
    public int Count { get { lock (gate) { return count; } } }
    public long EndSample { get { lock (gate) { return end; } } }

    public void Append(ReadOnlySpan<short> audio, bool knownPlayback = false)
    {
        lock (gate)
        {
            if (knownPlayback)
            {
                ClearCore();
                end = checked(end + audio.Length);
                return;
            }
            foreach (var sample in audio)
            {
                samples[(int)(end % samples.Length)] = sample;
                end = checked(end + 1);
                count = Math.Min(count + 1, samples.Length);
            }
        }
    }

    /// <summary>Consumes once from the exact exclusive wake end; unavailable boundaries fail closed.</summary>
    public short[] TakeCommand(long commandStartSample)
    {
        lock (gate)
        {
            if (count == 0 || commandStartSample < end - count || commandStartSample > end)
            {
                ClearCore();
                throw new InvalidOperationException("The wake boundary is not in the current bounded audio interval.");
            }
            var result = new short[checked((int)(end - commandStartSample))];
            for (var index = 0; index < result.Length; index++)
            {
                result[index] = samples[(int)((commandStartSample + index) % samples.Length)];
            }
            ClearCore();
            return result;
        }
    }

    public void Clear()
    {
        lock (gate) { ClearCore(); }
    }

    private void ClearCore()
    {
        Array.Clear(samples);
        count = 0;
    }
}
