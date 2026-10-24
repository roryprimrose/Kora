using Kora.Core.Dependencies;

namespace Kora.Application.Dependencies;

/// <summary>Explicit original-request Copilot preparation. It never approves a physical transmission.</summary>
public interface ICopilotTurnAdmission
{
    Task<ModelTurnAdmission> AdmitCopilotAsync(ModelContextEnvelope context, CancellationToken token);
}
