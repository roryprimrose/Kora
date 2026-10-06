using AwesomeAssertions;

using Kora.Windows.Audio;

using NAudio.Wave;

namespace Kora.Windows.IntegrationTests.Audio;

public sealed class SpeechOutputEnvelopeTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Envelope_tracks_silent_quiet_and_loud_pcm_windows_without_consuming_playback(int channels)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            foreach (var sample in new short[] { 0, 8192, 16384 })
            {
                for (var index = 0; index < 20 * channels; index++)
                {
                    writer.Write(sample);
                }
            }
        }
        stream.Position = 0;
        using var reader = new RawSourceWaveStream(stream, new WaveFormat(1000, 16, channels));

        var envelope = SpeechOutputEnvelope.Create(reader, TestContext.Current.CancellationToken);

        reader.Position.Should().Be(0);
        envelope.GetLevel(TimeSpan.Zero).Should().Be(0);
        envelope.GetLevel(TimeSpan.FromMilliseconds(19)).Should().Be(0);
        envelope.GetLevel(TimeSpan.FromMilliseconds(20)).Should().Be(0.5);
        envelope.GetLevel(TimeSpan.FromMilliseconds(40)).Should().Be(1);
        envelope.GetLevel(TimeSpan.FromMilliseconds(60)).Should().Be(0);
        envelope.GetLevel(TimeSpan.FromMilliseconds(-20)).Should().Be(0);

        envelope.Clear();
        envelope.GetLevel(TimeSpan.FromMilliseconds(40)).Should().Be(0);
    }

    [Fact]
    public void Envelope_handles_a_partial_final_window_and_restores_reader_position()
    {
        using var stream = new MemoryStream(new byte[] { 0, 64, 0, 64, 0, 64 });
        using var reader = new RawSourceWaveStream(stream, new WaveFormat(1000, 16, 1));
        reader.Position = 2;

        var envelope = SpeechOutputEnvelope.Create(reader, TestContext.Current.CancellationToken);

        reader.Position.Should().Be(2);
        envelope.GetLevel(TimeSpan.Zero).Should().Be(1);
        envelope.GetLevel(TimeSpan.FromMilliseconds(20)).Should().Be(0);
    }

    [Fact]
    public void Envelope_silence_does_not_produce_a_non_finite_level()
    {
        using var stream = new MemoryStream(new byte[80]);
        using var reader = new RawSourceWaveStream(stream, new WaveFormat(1000, 16, 1));

        var envelope = SpeechOutputEnvelope.Create(reader, TestContext.Current.CancellationToken);

        envelope.GetLevel(TimeSpan.Zero).Should().Be(0);
        envelope.GetLevel(TimeSpan.FromMilliseconds(20)).Should().Be(0);
    }

    [Fact]
    public void Envelope_cancellation_restores_reader_position()
    {
        using var stream = new MemoryStream(new byte[80]);
        using var reader = new RawSourceWaveStream(stream, new WaveFormat(1000, 16, 1));
        reader.Position = 2;
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var action = () => SpeechOutputEnvelope.Create(reader, cancellation.Token);

        action.Should().Throw<OperationCanceledException>();
        reader.Position.Should().Be(2);
    }

    [Fact]
    public void Empty_audio_has_no_output_level()
    {
        using var stream = new MemoryStream();
        using var reader = new RawSourceWaveStream(stream, new WaveFormat(1000, 16, 1));

        var envelope = SpeechOutputEnvelope.Create(reader, TestContext.Current.CancellationToken);

        envelope.GetLevel(TimeSpan.Zero).Should().Be(0);
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    public void Envelope_rejects_invalid_float_samples(float sample)
    {
        using var stream = new MemoryStream(BitConverter.GetBytes(sample));
        using var reader = new RawSourceWaveStream(stream, WaveFormat.CreateIeeeFloatWaveFormat(1000, 1));

        var action = () => SpeechOutputEnvelope.Create(reader, TestContext.Current.CancellationToken);

        action.Should().Throw<InvalidDataException>();
        reader.Position.Should().Be(0);
    }
}
