namespace Kora.Core.Voice;

public sealed class AudioOutputDeviceUnavailableException : InvalidOperationException
{
    public AudioOutputDeviceUnavailableException(string message)
        : this(AudioOutputFailureReason.Unavailable, message)
    {
    }

    public AudioOutputDeviceUnavailableException(string message, Exception innerException)
        : this(AudioOutputFailureReason.Unavailable, message, innerException)
    {
    }

    public AudioOutputDeviceUnavailableException(
        AudioOutputFailureReason reason,
        string message)
        : base(message)
    {
        Reason = reason;
    }

    public AudioOutputDeviceUnavailableException(
        AudioOutputFailureReason reason,
        string message,
        Exception innerException)
        : base(message, innerException)
    {
        Reason = reason;
    }

    public AudioOutputFailureReason Reason { get; }
}