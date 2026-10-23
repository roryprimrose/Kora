using System.Text;

using Kora.Application.Hosting;
using Kora.Application.Diagnostics;
using Kora.Core;
using Kora.Core.Auditing;
using Kora.Core.Commands;
using Kora.Core.Hosting;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    private SessionWorkspaceService? sessionCommands;
    private SessionRequestCommandService? sessionRequests;
    private ConversationSearchCommandService? conversationSearchCommands;
    private bool conversationSearchPresentation;
    private SessionAttachmentCommandService? attachmentCommands;
    public void BindSessionAttachmentCommands(SessionAttachmentCommandService service) => attachmentCommands = service;
    private readonly HashSet<Guid> sessionPresentationSources = [];

    public void RevokeSessionPresentation(HostId<SessionIdentity> session)
    {
        if (!sessionPresentationSources.Contains(session.Value)) { return; }
        RetireSessionCommandReviews();
        sessionPresentationSources.Clear();
        ResponseBody = "Session presentation revoked by inactivity retention. Refresh the exact retained session before inspecting it again.";
    }

    public void BindSessionCommands(SessionWorkspaceService service, SessionRequestCommandService? requests = null,
        ConversationSearchCommandService? searches = null)
    {
        conversationSearchCommands?.Retire();
        sessionCommands = service;
        sessionRequests = requests;
        conversationSearchCommands = searches;
        PrivacyClosureRequested -= OnSessionRequestPrivacyClosure;
        PrivacyClosureRequested += OnSessionRequestPrivacyClosure;
    }

    private void OnSessionRequestPrivacyClosure(object? sender, EventArgs args)
    {
        RetireSessionCommandReviews();
        if (SessionCommand.Parse(CommandText, AssistantName) is { } command
            && (command.Operation == SessionCommandOperation.RequestReview || command.IsRetentionCommand || command.IsConversationSearch))
        { CommandText = string.Empty; }
    }

    internal async Task ExecuteSessionCommandAsync(SessionCommand command, SecurityAuditInitiator initiator)
    {
        if (disposed) { return; }
        if (command.Operation == SessionCommandOperation.Invalid)
        {
            RetireSessionCommandReviews();
            ShowInformation("Session command not accepted.", command.Error!);
            return;
        }
        if (pendingModelQuestion is not null || IsGrantChangePending || IsModelActionApprovalPending)
        {
            RetireSessionCommandReviews();
            ShowInformation("Session command blocked.", "Resolve the current question or approval explicitly first. No target or decision changed.");
            return;
        }
        var origin = initiator switch
        {
            SecurityAuditInitiator.VoiceCommand => RequestOrigin.ActivatedVoice,
            SecurityAuditInitiator.LocalUser or SecurityAuditInitiator.TypedCommand => RequestOrigin.LocalUi,
            _ => RequestOrigin.HostSystem,
        };
        var callRevision = CallPolicyRevision;
        var recoveryRevision = Volatile.Read(ref voiceRecoveryRevision);
        var originalName = AssistantName;
        var topologyRevision = privacyObservation.Current.TopologyRevision;
        var requestCommand = command.Operation is SessionCommandOperation.RequestReview
            or SessionCommandOperation.RequestConfirm or SessionCommandOperation.RequestCancel;
        if (requestCommand) { RetireSpeechSummarySuggestions(); }
        var attachmentCommand = command.Operation == SessionCommandOperation.AttachmentInspect;
        var sourceBoundCommand = requestCommand || command.IsRetentionCommand || attachmentCommand;
        bool Eligible() => !disposed && IsHostInputEligible && CallPolicyRevision == callRevision
            && (!command.IsRetentionCommand || privacyObservation.Current.TopologyRevision == topologyRevision
                && Volatile.Read(ref voiceRecoveryRevision) == recoveryRevision
                && string.Equals(originalName, AssistantName, StringComparison.Ordinal))
            && pendingModelQuestion is null && !IsGrantChangePending && !IsModelActionApprovalPending
            && (origin != RequestOrigin.ActivatedVoice || (IsVoiceEnabled && HasVoiceConsent
                && CallObservation.AllowActivation && Volatile.Read(ref voiceRecoveryRevision) == recoveryRevision
                && (!sourceBoundCommand || !IsProtectedCall && string.Equals(originalName, AssistantName, StringComparison.Ordinal))
                && (!command.IsConversationSearch
                    || isAssistantNameAvailable && string.Equals(originalName, AssistantName, StringComparison.Ordinal))));
        if (sessionCommands is null)
        {
            ShowInformation("Session commands unavailable.", "The trusted durable workspace service is not bound. Use the native Sessions workspace when available.");
            return;
        }
        if (!Eligible())
        {
            ShowInformation("Session command denied.", "The originating channel, call observation or private host changed. Initiate a fresh command.");
            return;
        }
        if (origin == RequestOrigin.ActivatedVoice && IsProtectedCall
            && command.Operation is SessionCommandOperation.Create or SessionCommandOperation.Rename
                or SessionCommandOperation.Done or SessionCommandOperation.Resume)
        {
            ShowInformation("Voice session mutation unavailable during a protected call.",
                "The workspace mutation gate requires a clear/unavailable call observation. No delayed mutation or later confirmation is queued.");
            return;
        }
        try
        {
            if (command.IsConversationSearch)
            {
                var page = await (conversationSearchCommands
                    ?? throw new InvalidOperationException("The canonical conversation search command service is unavailable."))
                    .ExecuteAsync(command, origin, Eligible, CancellationToken.None);
                if (!Eligible()) { return; }
                PresentResponse(AssistantState.Information, "Exact conversation search.",
                    page is null ? "Host-held search cancelled. No content, submission or session change."
                        : Encoding.UTF8.GetString(Kora.Core.Conversation.ConversationSearchPage.Serialize(page)),
                    refreshOutput: false, sourceAssigned: () =>
                    {
                        conversationSearchPresentation = true;
                        forceVisualResponse = true;
                    });
                sessionPresentationSources.Add(command.SessionId!.Value);
                return;
            }
            if (attachmentCommand)
            {
                await (attachmentCommands ?? throw new InvalidOperationException("The native retained attachment viewer is unavailable."))
                    .Execute(command, origin, Eligible, CancellationToken.None);
                if (Eligible())
                {
                    ShowInformation("Retained attachment inspection opened.",
                        "Exact historical source opened only in the private native inert viewer. No capture, session renewal, model use or speech.");
                }
                return;
            }
            var result = command.IsRetentionCommand
                ? await ExecuteSessionRetentionCommandAsync(command, origin, Eligible, CancellationToken.None)
                : requestCommand
                ? await (sessionRequests ?? throw new InvalidOperationException("The canonical retained-request command service is unavailable."))
                    .ExecuteAsync(command, origin, Eligible, CancellationToken.None)
                : await sessionCommands.ExecuteCommandAsync(command, origin, Eligible, CancellationToken.None);
            if (!Eligible()) { return; }
            if (result.RequestSubmission is { } submitted)
            { await PinSpeechSummarySuggestionAsync(submitted, Eligible); }
            ShowInformation("Session command " + result.Outcome + ".", Encoding.UTF8.GetString(SessionCommandResult.Serialize(result)));
            if (command.SessionId is { } id) { sessionPresentationSources.Add(id); }
            foreach (var row in result.Sessions) { sessionPresentationSources.Add(row.Id); }
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException
            or UnauthorizedAccessException or OperationCanceledException or ArgumentException)
        {
            if (attachmentCommand)
            {
                RetainedAttachmentInspectionFailed(logger, exception.GetType().Name);
                if (!disposed && IsHostInputEligible)
                {
                    ShowFailure("Retained attachment unavailable.",
                        "The exact historical source or original private admission is unavailable. Finish any pending native review, then inspect native storage/copy recovery. No alternate source was opened.");
                }
                return;
            }
            ApplicationLog.Error(logger, exception, "Running a bounded session command");
            if (disposed || !IsHostInputEligible) { return; }
            var result = new SessionCommandResult("not-confirmed",
                exception.Message + " Resolve current questions/work and privacy/ownership/storage gates, "
                + "then inspect the exact ID and revisions before a fresh request. "
                + "A mutation may already have committed before a receipt failure; no rollback or automatic retry is claimed.");
            ShowFailure("Session command not confirmed.", Encoding.UTF8.GetString(SessionCommandResult.Serialize(result)));
        }
    }
}
