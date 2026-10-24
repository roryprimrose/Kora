using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;

namespace Kora.Application.Dependencies;

/// <summary>A volatile, exact final request. Only its issuing host review service can complete it.</summary>
public sealed class CopilotTransmissionReview
{
    internal CopilotTransmissionReview(CopilotTransmission transmission, Func<HostActivity> presentation)
    { Transmission = transmission; Presentation = presentation; }

    public CopilotTransmission Transmission { get; }
    public bool IsPending => !Completion.Task.IsCompleted;
    internal Func<HostActivity> Presentation { get; }
    internal TaskCompletionSource<bool> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}
