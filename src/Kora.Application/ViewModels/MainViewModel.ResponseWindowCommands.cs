using Kora.Application.Configuration;
using Kora.Application.Diagnostics;
using Kora.Core.Auditing;
using Kora.Core.Configuration;
using Kora.Core.Diagnostics;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    private bool responseWindowPreferencesUnconfirmed = true;

    private ResponseWindowSettings? ReadConfirmedResponseWindowSettings()
    {
        if (responseWindowPreferencesUnconfirmed)
        {
            throw new InvalidOperationException("Response-window preferences are unconfirmed. Inspect saved state and audit receipts before restarting.");
        }
        var saved = appearancePreferences.LoadResponseWindowSettings();
        if ((saved ?? ResponseWindowSettings.Default) != responseWindowSettings)
        {
            throw new InvalidDataException("Saved response-window preferences changed or a previous write is uncertain. Inspect saved state and audit receipts before restarting.");
        }
        return saved;
    }

    internal async Task ExecuteResponseWindowCommandAsync(ResponseWindowCommand command,
        SecurityAuditInitiator initiator, CancellationToken cancellationToken = default)
    {
        if (!IsCallMutationHostEligible)
        {
            Transcript = "Response-window control requires the current owning unlocked host. Initiate a fresh request.";
            return;
        }
        if (command.Operation == AppearanceCommandOperation.Clarify)
        {
            Transcript = "Clarify the response-window setting. " + ResponseWindowCommand.Syntax;
            return;
        }
        if (command.Operation is AppearanceCommandOperation.Set or AppearanceCommandOperation.Reset
            && (IsResponseInteractionPending || preferenceWriteActive))
        {
            Transcript = "Response-window change unavailable. Resolve pending questions/approvals or wait for the current preference result, then explicitly retry.";
            return;
        }
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var saved = ReadConfirmedResponseWindowSettings();
            var source = saved is null ? "declared defaults; no saved preference" : "saved device-local preference";
            if (command.Operation == AppearanceCommandOperation.List)
            {
                Transcript = $"{ResponseWindowCommand.AlwaysShowId} = {FormatResponseWindowBoolean(IsResponseAlwaysVisible)} (default false); "
                    + $"{ResponseWindowCommand.TopmostId} = {FormatResponseWindowBoolean(IsResponseWindowTopmost)} (default true); {source}. "
                    + ResponseWindowCommand.Syntax;
                return;
            }
            var alwaysShow = command.Option == ResponseWindowOption.AlwaysShow;
            var id = alwaysShow ? ResponseWindowCommand.AlwaysShowId : ResponseWindowCommand.TopmostId;
            var current = alwaysShow ? IsResponseAlwaysVisible : IsResponseWindowTopmost;
            if (command.Operation == AppearanceCommandOperation.Get)
            {
                Transcript = $"{id} = {FormatResponseWindowBoolean(current)}; {source}. "
                    + "Preference only: existing private/source/visibility and dismissal rules still apply; no response is opened.";
                return;
            }
            var value = command.Operation == AppearanceCommandOperation.Reset
                ? alwaysShow ? ResponseWindowSettings.Default.AlwaysShow : ResponseWindowSettings.Default.Topmost
                : command.Value!.Value;
            if (current == value)
            {
                Transcript = $"{id} = {FormatResponseWindowBoolean(current)}; unchanged; no write or effect.";
                return;
            }
            using var activity = HostActivity.BeginOperation(HostActivityLayer.Application, HostOperation.Storage,
                OriginalOrigin(initiator));
            var confirmed = alwaysShow
                ? await SetResponseAlwaysVisibleAsync(value, initiator, ReportResponseWindowCommandFailure)
                : await SetResponseWindowTopmostAsync(value, initiator, ReportResponseWindowCommandFailure);
            activity.Complete(confirmed ? HostOperationOutcome.Completed : HostOperationOutcome.Failed);
            if (!confirmed)
            {
                Transcript = "Response-window preference not confirmed. " + Transcript
                    + " Inspect saved state/audit receipts before a fresh change.";
                return;
            }
            Transcript = $"{id} = {FormatResponseWindowBoolean(value)}; saved device-local preference only. No response was opened or restored.";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException
            or InvalidOperationException or OperationCanceledException)
        {
            ApplicationLog.Error(logger, exception, "Inspecting or changing response-window preferences");
            Transcript = "Response-window preference not confirmed. " + exception.Message;
        }
    }

    private static string FormatResponseWindowBoolean(bool value) => value ? "true" : "false";

    private void ReportResponseWindowCommandFailure(string title, string body) => Transcript = title + " " + body;
}
