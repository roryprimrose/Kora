using AwesomeAssertions;

using Kora.Windows.Audio;

namespace Kora.Windows.IntegrationTests.Audio;

public sealed class BlockingAudioStreamTests
{
    [Fact]
    public void Read_consumes_multiple_buffers_without_losing_bytes()
    {
        using var stream = new BlockingAudioStream();
        stream.Add([1, 2]);
        stream.Add([3, 4, 5]);
        stream.Complete();
        var output = new byte[5];

        var firstRead = stream.Read(output, 0, 3);
        var secondRead = stream.Read(output, firstRead, output.Length - firstRead);
        var completedRead = stream.Read(output, output.Length, 0);

        firstRead.Should().Be(2);
        secondRead.Should().Be(3);
        completedRead.Should().Be(0);
        output.Should().Equal(1, 2, 3, 4, 5);
    }

    [Fact]
    public void Read_returns_zero_after_completed_buffers_are_exhausted()
    {
        using var stream = new BlockingAudioStream();
        stream.Complete();

        var bytesRead = stream.Read(new byte[4], 0, 4);

        bytesRead.Should().Be(0);
    }

    [Fact]
    public void Disposed_stream_rejects_add_and_read()
    {
        var stream = new BlockingAudioStream();
        stream.Dispose();

        var add = () => stream.Add([1]);
        var read = () => stream.Read(new byte[1], 0, 1);

        add.Should().Throw<ObjectDisposedException>();
        read.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public void Stream_exposes_read_only_non_seekable_contract()
    {
        using var stream = new BlockingAudioStream();

        stream.CanRead.Should().BeTrue();
        stream.CanWrite.Should().BeFalse();
        stream.CanSeek.Should().BeFalse();
        ((Action)(() => stream.Seek(0, SeekOrigin.Begin))).Should().Throw<NotSupportedException>();
        ((Action)(() => stream.SetLength(0))).Should().Throw<NotSupportedException>();
        ((Action)(() => stream.Write([], 0, 0))).Should().Throw<NotSupportedException>();
    }
}