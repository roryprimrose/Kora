using NAudio.Wave;

namespace Kora.Windows.Audio;

internal sealed class SpeechOutputEnvelope(double[] levels, int framesPerWindow, int sampleRate)
{
    public static SpeechOutputEnvelope Create(WaveStream reader, CancellationToken cancellationToken)
    {
        var source = reader.ToSampleProvider();
        var framesPerWindow = Math.Max(1, source.WaveFormat.SampleRate / 50);
        var samples = new float[framesPerWindow * source.WaveFormat.Channels];
        var levels = new List<double>();
        var originalPosition = reader.Position;
        try
        {
            var count = 0;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var read = source.Read(samples.AsSpan(count));
                count += read;
                if (count == samples.Length || (read == 0 && count > 0))
                {
                    var sum = 0d;
                    for (var index = 0; index < count; index++)
                    {
                        if (!float.IsFinite(samples[index]))
                        {
                            throw new InvalidDataException("The synthesized speech contains an invalid audio sample.");
                        }
                        sum += (double)samples[index] * samples[index];
                    }

                    levels.Add(Math.Sqrt(sum / count));
                    count = 0;
                }

                if (read == 0)
                {
                    break;
                }
            }

            var maximum = levels.Count == 0 ? 0 : levels.Max();
            var normalized = levels.Select(level => maximum > 0 ? level / maximum : 0).ToArray();
            return new SpeechOutputEnvelope(normalized, framesPerWindow, source.WaveFormat.SampleRate);
        }
        finally
        {
            Array.Clear(samples);
            reader.Position = originalPosition;
        }
    }

    public double GetLevel(TimeSpan playbackPosition)
    {
        if (playbackPosition < TimeSpan.Zero)
        {
            return 0;
        }
        var index = (long)(playbackPosition.TotalSeconds * sampleRate / framesPerWindow);
        return index >= 0 && index < levels.Length ? levels[index] : 0;
    }

    public void Clear() => Array.Clear(levels);
}
