namespace Kora.Core.Voice;

public sealed class WindowsSpeechRateUnavailableException(string message, Exception? innerException = null)
    : InvalidOperationException(message, innerException);
