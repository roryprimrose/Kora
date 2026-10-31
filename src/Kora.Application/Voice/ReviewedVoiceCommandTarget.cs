using Kora.Core.Hosting;
using Kora.Core.Voice;

namespace Kora.Application.Voice;

/// <summary>Host-captured original command destination and admission, never supplied by recognition text.</summary>
public sealed record ReviewedVoiceCommandTarget(
    MicrophoneDevice Microphone,
    string AssistantName,
    Func<bool> IsCurrent,
    Func<VoiceTranscriptEventArgs, HostRequest, Func<bool>, Task> Dispatch);
