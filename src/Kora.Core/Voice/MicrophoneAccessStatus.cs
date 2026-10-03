namespace Kora.Core.Voice;

public sealed record MicrophoneAccessStatus(
    MicrophoneAccessState State,
    string Detail);
