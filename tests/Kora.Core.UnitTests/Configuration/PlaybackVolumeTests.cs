using System.Buffers.Binary;
using AwesomeAssertions;
using Kora.Core.Configuration;
using Kora.Core.Voice;

namespace Kora.Core.UnitTests.Configuration;

public sealed class PlaybackVolumeTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(100)]
    public void Valid_percent_is_strongly_typed_and_zero_alone_blocks_speech(int percent)
    {
        var volume = new PlaybackVolume(percent);
        volume.Percent.Should().Be(percent);
        volume.AllowsSpeech.Should().Be(percent > 0);
        PlaybackVolume.Parse(percent.ToString(System.Globalization.CultureInfo.InvariantCulture)).Should().Be(volume);
        PlaybackVolume.Default.Percent.Should().Be(100);
        default(PlaybackVolume).AllowsSpeech.Should().BeFalse();
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("101")]
    [InlineData("2147483648")]
    [InlineData("01")]
    [InlineData("1.0")]
    [InlineData("1%")]
    [InlineData("+1")]
    [InlineData(" 1")]
    [InlineData("1 ")]
    [InlineData("one")]
    [InlineData("")]
    [InlineData(null)]
    public void Invalid_complete_scalar_is_not_rounded_clamped_or_defaulted(string? value)
    {
        var action = () => PlaybackVolume.Parse(value!);
        action.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void Strong_scalar_rejects_out_of_bounds_construction(int percent)
    {
        var action = () => new PlaybackVolume(percent);
        action.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(30)]
    [InlineData(100)]
    public void Pcm_gain_is_byte_identical_at_unity_and_never_amplifies_any_signed_sample(int percent)
    {
        var samples = new byte[(ushort.MaxValue + 1) * sizeof(short)];
        for (var index = 0; index <= ushort.MaxValue; index++)
        {
            BinaryPrimitives.WriteInt16LittleEndian(samples.AsSpan(index * sizeof(short)), (short)(index + short.MinValue));
        }
        var original = samples.ToArray();
        Pcm16PlaybackGain.Attenuate(samples, new(percent));
        for (var index = 0; index <= ushort.MaxValue; index++)
        {
            var before = BinaryPrimitives.ReadInt16LittleEndian(original.AsSpan(index * sizeof(short)));
            var after = BinaryPrimitives.ReadInt16LittleEndian(samples.AsSpan(index * sizeof(short)));
            after.Should().Be((short)(before * percent / 100));
            Math.Abs((int)after).Should().BeLessThanOrEqualTo(Math.Abs((int)before));
        }
        if (percent == 100) { samples.Should().Equal(original); }
        if (percent == 0) { samples.Should().OnlyContain(sample => sample == 0); }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    public void Incomplete_or_empty_pcm_is_failure_even_at_unity(int length)
    {
        var samples = new byte[length];
        var action = () => Pcm16PlaybackGain.Attenuate(samples, PlaybackVolume.Default);
        action.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void Unavailable_volume_is_explicit_not_a_device_failure() =>
        new PlaybackVolumeUnavailableException("owned gain unavailable").Message.Should().Be("owned gain unavailable");
}
