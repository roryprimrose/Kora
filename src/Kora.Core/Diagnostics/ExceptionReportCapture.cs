namespace Kora.Core.Diagnostics;

public sealed record ExceptionReportCapture(
    ExceptionReportSignature? Signature, ExceptionReportUnavailableReason UnavailableReason);
