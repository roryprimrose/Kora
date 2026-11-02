namespace Kora.Windows.Audio;

internal sealed class WindowsRecognitionCompletedEventArgs(Exception? error) : EventArgs
{
    public Exception? Error { get; } = error;
}
