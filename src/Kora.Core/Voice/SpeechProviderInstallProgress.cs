namespace Kora.Core.Voice;

public sealed record SpeechProviderInstallProgress(
    SpeechProviderInstallStage Stage,
    long BytesReceived,
    long TotalBytes)
{
    public int Percentage =>
        TotalBytes <= 0
            ? 0
            : (int)Math.Clamp(BytesReceived * 100 / TotalBytes, 0, 100);
}
