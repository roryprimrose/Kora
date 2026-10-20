using Microsoft.Extensions.Logging;

namespace Kora.Application.Calendar;

public sealed partial class DateArithmeticRegistry
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Date arithmetic {DateOperation} outcome {DateOutcome}")]
    private static partial void Result(ILogger logger, DateArithmeticOperation dateOperation, string dateOutcome);
}
