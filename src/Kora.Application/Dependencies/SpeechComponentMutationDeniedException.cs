using Kora.Core.Dependencies;

namespace Kora.Application.Dependencies;

public sealed class SpeechComponentMutationDeniedException(SpeechComponentMutationAdmission admission)
    : InvalidOperationException(admission.Detail)
{
    public SpeechComponentMutationState State { get; } = admission.State;
    public string ReasonCode { get; } = admission.ReasonCode;
}
