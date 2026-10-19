using Kora.Windows.Coordination;

namespace Kora;

internal sealed class EvidenceCutoverRecoveryAdmissionException(
    EvidenceCutoverRecoveryAdmissionFailure failure, string? message) : InvalidOperationException(message)
{
    internal EvidenceCutoverRecoveryAdmissionFailure Failure { get; } = failure;
}
