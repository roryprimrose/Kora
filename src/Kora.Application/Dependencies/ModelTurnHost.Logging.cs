using Kora.Core.Dependencies;
using Microsoft.Extensions.Logging;

namespace Kora.Application.Dependencies;

public sealed partial class ModelTurnHost
{
    [LoggerMessage(1780, LogLevel.Information,
        "Model turn {ModelTurnId}: {ModelTurnOutcome}; reason {ModelTurnReason}; provider {ModelProvider}; model {ModelId}; revision {ModelRevision}.")]
    private static partial void Result(ILogger logger, ModelTurnOutcome modelTurnOutcome,
        ModelTurnReason modelTurnReason, Guid? modelTurnId, ModelProviderIdentity? modelProvider,
        Guid? modelId, long? modelRevision);

    [LoggerMessage(1781, LogLevel.Warning, "Model turn failed with {FailureType}; outcome unknown.")]
    private static partial void Fault(ILogger logger, string failureType);

    [LoggerMessage(1782, LogLevel.Warning, "Model tool request denied by current turn authority.")]
    private static partial void ToolDenied(ILogger logger);

    [LoggerMessage(1783, LogLevel.Information, "Late model response suppressed for {ModelTurnId}; adapter completion observed.")]
    private static partial void LateResponse(ILogger logger, Guid modelTurnId);
}
