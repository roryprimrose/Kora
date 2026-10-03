using AwesomeAssertions;

using Kora.Windows.Audio;

namespace Kora.Windows.IntegrationTests.Audio;

public sealed class VoiceRecognitionShutdownTests
{
    [Fact]
    public async Task WaitForCompletionAsync_waits_for_capture_and_recognition()
    {
        var recorderDisposal = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var recognitionCompletion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var shutdown = VoiceRecognitionShutdown.WaitForCompletionAsync(
            new ValueTask(recorderDisposal.Task),
            recognitionCompletion.Task);

        shutdown.IsCompleted.Should().BeFalse();
        recorderDisposal.SetResult();
        await Task.Yield();
        shutdown.IsCompleted.Should().BeFalse();

        recognitionCompletion.SetResult();
        await shutdown;
    }

    [Fact]
    public async Task WaitForCompletionAsync_propagates_capture_failure()
    {
        var expected = new InvalidOperationException("Capture teardown failed.");

        var action = () => VoiceRecognitionShutdown.WaitForCompletionAsync(
            new ValueTask(Task.FromException(expected)),
            Task.CompletedTask);

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage(expected.Message);
    }
}
