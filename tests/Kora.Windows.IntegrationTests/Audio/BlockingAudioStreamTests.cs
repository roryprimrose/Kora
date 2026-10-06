using AwesomeAssertions;

using Kora.Windows.Audio;

using System.Speech.AudioFormat;
using System.Speech.Recognition;

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

        firstRead.Should().Be(3);
        secondRead.Should().Be(2);
        completedRead.Should().Be(0);
        stream.Position.Should().Be(5);
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
    public void Stream_accepts_only_the_no_op_seeks_required_by_SAPI()
    {
        using var stream = new BlockingAudioStream();

        stream.CanRead.Should().BeTrue();
        stream.CanWrite.Should().BeFalse();
        stream.CanSeek.Should().BeFalse();
        stream.Length.Should().Be(long.MaxValue);
        stream.Position.Should().Be(0);
        stream.Position = 0;
        stream.Seek(0, SeekOrigin.Begin).Should().Be(0);
        stream.Seek(0, SeekOrigin.Current).Should().Be(0);
        ((Action)(() => stream.Position = 1)).Should().Throw<NotSupportedException>();
        ((Action)(() => stream.Seek(1, SeekOrigin.Begin))).Should().Throw<NotSupportedException>();
        ((Action)(() => stream.Seek(0, SeekOrigin.End))).Should().Throw<NotSupportedException>();
        ((Action)(() => stream.SetLength(0))).Should().Throw<NotSupportedException>();
        ((Action)(() => stream.Write([], 0, 0))).Should().Throw<NotSupportedException>();
    }

    [Fact]
    public async Task Installed_SAPI_recognizer_can_activate_against_the_live_stream()
    {
        var recognizerInfo = SpeechRecognitionEngine.InstalledRecognizers().FirstOrDefault(candidate =>
            string.Equals(candidate.Culture.TwoLetterISOLanguageName, "en", StringComparison.Ordinal));
        if (recognizerInfo is null)
        {
            Assert.Skip("No English Windows speech recognizer is installed.");
        }

        using var stream = new BlockingAudioStream();
        using var recognizer = new SpeechRecognitionEngine(recognizerInfo.Id);
        var completed = new TaskCompletionSource<RecognizeCompletedEventArgs>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        recognizer.RecognizeCompleted += (_, eventArgs) => completed.TrySetResult(eventArgs);
        var grammar = new GrammarBuilder(new Choices("help")) { Culture = recognizerInfo.Culture };
        recognizer.LoadGrammar(new Grammar(grammar));
        recognizer.SetInputToAudioStream(stream,
            new SpeechAudioFormatInfo(EncodingFormat.Pcm, 16000, 16, 1, 32000, 2, null));

        recognizer.RecognizeAsync(RecognizeMode.Single);
        stream.Add(new byte[3200]);
        stream.Complete();
        var result = await completed.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        result.Error.Should().BeNull();
    }
}