using Kora.Core.Diagnostics;

using Microsoft.Extensions.Logging;

namespace Kora.Application.Diagnostics;

public sealed partial class DurableEvidenceQuery
{
    [LoggerMessage(180, LogLevel.Information, "Evidence query returned {RecordCount} records, {SerializedBytes} serialized bytes, status {QueryStatus}.")]
    private static partial void Returned(ILogger logger, int recordCount, int serializedBytes, EvidencePageStatus queryStatus);

    [LoggerMessage(181, LogLevel.Error, "Evidence query failed; exception type {ExceptionType}.")]
    private static partial void Failed(ILogger logger, string exceptionType);
}
