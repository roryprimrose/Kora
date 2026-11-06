using Kora.Core.Hosting;
using Kora.Core.Voice;

namespace Kora.Application.Voice;

/// <summary>Host-admitted wake lineage, separate from the actual final ASR text and from action authority.</summary>
public sealed record HandsFreeCommandActivation(
    HandsFreeVoiceAdmission Admission, WakeWordObservation Wake, HostRequest Request);
