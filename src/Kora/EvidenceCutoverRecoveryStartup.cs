using System.Text.Json;
using Kora.Core.Diagnostics;

namespace Kora;

internal static class EvidenceCutoverRecoveryStartup
{
    internal const string Argument = "--recover-evidence-cutover";

    internal static bool TryRun(string[] args, Func<EvidenceCutoverRecoveryReceipt> recover,
        TextWriter output, TextWriter error, Action<string> notify, out int exitCode)
    {
        exitCode = 0;
        if (!args.Any(value => value.StartsWith("--recover-evidence", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        var committed = false;
        try
        {
            if (args.Length != 1 || !string.Equals(args[0], Argument, StringComparison.Ordinal))
            {
                throw new ArgumentException($"Evidence recovery accepts only the exact argument {Argument}; no assistant was started.", nameof(args));
            }
            var receipt = recover();
            committed = true;
            output.WriteLine(JsonSerializer.Serialize(new
            {
                Version = 1,
                Operation = "evidence.first-cutover-recovery",
                Outcome = "Completed",
                BootstrapDiagnostic = true,
                receipt.StoreIdentity,
                receipt.Generation,
                receipt.RegisteredSegments,
                AssistantStarted = false,
                OwnershipMarkerReleased = false,
            }));
            output.Flush();
            notify("Evidence cutover completed. No assistant was started and no ownership marker was released. " +
                "Reconcile instance ownership separately before an explicitly approved launch.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or InvalidDataException or InvalidOperationException or ArgumentException or OperationCanceledException
            or System.ComponentModel.Win32Exception or BadImageFormatException or JsonException)
        {
            exitCode = 1;
            var notification = committed
                ? $"Evidence cutover committed, but its completion notification failed: {exception.Message}. Do not repeat recovery or remove files; inspect the committed state explicitly."
                : $"Evidence cutover recovery did not complete: {exception.Message}. Existing state was preserved; no assistant was started and no ownership marker was released.";
            try
            {
                error.WriteLine(JsonSerializer.Serialize(new
                {
                    Version = 1,
                    Operation = "evidence.first-cutover-recovery",
                    Outcome = committed ? "CommittedNotificationFailed"
                        : exception is EvidenceCutoverRecoveryAdmissionException ? "NotAdmitted" : "Failed",
                    BootstrapDiagnostic = true,
                    CommitState = committed ? "Committed"
                        : exception is EvidenceCutoverRecoveryAdmissionException ? "NotStarted" : "Unconfirmed",
                    ReasonCode = FailureCode(exception),
                    AssistantStarted = false,
                    OwnershipMarkerReleased = false,
                    RetryAllowed = false,
                }));
                error.Flush();
            }
            catch (Exception receiptFailure) when (receiptFailure is IOException or ObjectDisposedException)
            {
                notification += $" The failure receipt could not be written: {receiptFailure.Message}.";
            }
            notify(notification);
        }
        return true;
    }

    private static string FailureCode(Exception exception) => exception switch
    {
        EvidenceCutoverRecoveryAdmissionException admission => admission.Failure.ToString(),
        UnauthorizedAccessException => "AccessDenied",
        InvalidDataException => "InvalidStorageState",
        OperationCanceledException => "Cancelled",
        ArgumentException => "InvalidArguments",
        IOException => "StorageOrReceiptIoFailure",
        InvalidOperationException => "InvalidOperation",
        System.ComponentModel.Win32Exception => "WindowsIdentityUnavailable",
        BadImageFormatException => "InvalidBuildImage",
        JsonException => "InvalidJsonState",
        _ => throw new ArgumentOutOfRangeException(nameof(exception)),
    };
}
