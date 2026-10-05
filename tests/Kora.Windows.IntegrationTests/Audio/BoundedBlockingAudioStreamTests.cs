using AwesomeAssertions;

using Kora.Windows.Audio;

namespace Kora.Windows.IntegrationTests.Audio;

public sealed class BoundedBlockingAudioStreamTests
{
    [Fact]
    public void Overflow_clears_the_queue_including_a_partial_buffer()
    {
        using var stream = new BlockingAudioStream(8);
        stream.TryAdd([1, 2, 3, 4, 5, 6]).Should().BeTrue();
        var output = new byte[2];
        stream.Read(output, 0, output.Length).Should().Be(2);
        stream.BufferedBytes.Should().Be(4);

        stream.TryAdd([7, 8, 9, 10, 11]).Should().BeFalse();

        stream.BufferedBytes.Should().Be(0);
        stream.Read(output, 0, output.Length).Should().Be(0);
        stream.TryAdd([1]).Should().BeFalse();
    }

    [Fact]
    public void Queue_is_bounded_to_two_seconds_of_16k_mono_PCM()
    {
        using var stream = new BlockingAudioStream();

        stream.TryAdd(new byte[64000]).Should().BeTrue();
        stream.TryAdd([1]).Should().BeFalse();

        stream.BufferedBytes.Should().Be(0);
    }

    [Fact]
    public async Task Clear_wakes_a_blocked_reader_without_delivering_audio()
    {
        using var stream = new BlockingAudioStream();
        var read = Task.Run(() => stream.Read(new byte[8], 0, 8), TestContext.Current.CancellationToken);

        stream.ClearAndComplete();

        (await read.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken)).Should().Be(0);
    }

    [Fact]
    public void Complete_drains_activated_audio_and_dispose_clears_it()
    {
        using var stream = new BlockingAudioStream();
        stream.TryAdd([1, 2, 3]).Should().BeTrue();
        stream.Complete();
        var bytes = new byte[3];

        stream.Read(bytes, 0, 3).Should().Be(3);
        bytes.Should().Equal(1, 2, 3);
        stream.Read(bytes, 0, 3).Should().Be(0);

        using var abandoned = new BlockingAudioStream();
        abandoned.TryAdd([4, 5]).Should().BeTrue();
        abandoned.Dispose();
        abandoned.BufferedBytes.Should().Be(0);
        abandoned.TryAdd([6]).Should().BeFalse();
    }
}
