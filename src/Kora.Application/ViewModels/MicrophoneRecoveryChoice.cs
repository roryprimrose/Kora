using Kora.Core.Voice;

namespace Kora.Application.ViewModels;

public sealed record MicrophoneRecoveryChoice(
    MicrophoneDevice Device, long Revision, long CallRevision, bool IsAvailable, string Label);
