using Microsoft.Extensions.Logging;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Standalone UUID utility outcome: {Outcome}")]
    private static partial void UuidResult(ILogger logger, string outcome);
}
