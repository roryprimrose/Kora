using Kora.Application.Diagnostics;
using Kora.Application.Hosting;
using Kora.Core.Auditing;
using Kora.Core.Commands;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    public event Func<SavedDraftCataloguePreparation, Task<bool>>? SavedDraftCataloguePreparationRequested;

    private async Task ExecuteSavedDraftCatalogueCommand(SavedDraftCatalogueCommand command,
        SecurityAuditInitiator initiator, ManualCallInput? originalInput)
    {
        var origin = OriginalOrigin(initiator);
        if (origin is not (RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice)
            || initiator is not (SecurityAuditInitiator.TypedCommand or SecurityAuditInitiator.VoiceCommand)
            || !isAssistantNameAvailable
            || origin == RequestOrigin.ActivatedVoice && !command.IsActivated)
        {
            Transcript = "Saved draft catalogue unavailable: use original typed input or the current available assistant-name voice prefix.";
            return;
        }
        var admitted = CaptureWorkspaceSurfaceAdmission(initiator, originalInput);
        if (!admitted())
        {
            Transcript = "Saved draft catalogue unavailable: original private host admission changed.";
            return;
        }
        if (command.IsHelp)
        {
            Transcript = SavedDraftCatalogueCommand.Help;
            return;
        }
        if (command.SessionId is not { } sessionId)
        {
            Transcript = "Saved draft catalogue command not accepted. " + SavedDraftCatalogueCommand.Help;
            return;
        }
        var handler = SavedDraftCataloguePreparationRequested;
        if (handler is null)
        {
            Transcript = "Saved draft catalogue unavailable: the native preparation route is not bound. No query or session changed.";
            return;
        }
        bool Current() => admitted() && ReferenceEquals(handler, SavedDraftCataloguePreparationRequested);
        using var activity = HostActivity.BeginRoot(HostRequest.Create(origin), HostActivityLayer.Application, HostOperation.Presentation);
        try
        {
            if (!await handler(new(sessionId, Current)) || !Current())
            {
                Transcript = "Saved draft catalogue unavailable: native preparation was not acknowledged for the original host and window lifetime.";
                activity.Complete(HostOperationOutcome.Cancelled);
                return;
            }
            Transcript = "Saved draft catalogue prepared; no I/O yet. Other visible filters are retained and AND-composed with the exact ID. Review them and explicitly Search. No session selected or body loaded.";
            activity.Complete(HostOperationOutcome.Completed);
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException
            or UnauthorizedAccessException or OperationCanceledException or ArgumentException)
        {
            ApplicationLog.Error(logger, exception, "Preparing the native saved draft catalogue");
            Transcript = "Saved draft catalogue unavailable: native preparation failed. No search or navigation was requested.";
            activity.Complete(HostOperationOutcome.Failed);
        }
    }
}
