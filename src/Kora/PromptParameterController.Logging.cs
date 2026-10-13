using Microsoft.Extensions.Logging;

namespace Kora;

internal sealed partial class PromptParameterController
{
    [LoggerMessage(7310, LogLevel.Warning, "Owned prompt preparation failed with {OutcomeType}.")]
    private static partial void PreparationFailed(ILogger logger, string outcomeType);
}
