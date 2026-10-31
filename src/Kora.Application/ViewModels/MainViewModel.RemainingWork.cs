using Kora.Application.Diagnostics;
using Kora.Application.Hosting;
using Kora.Core;
using Kora.Core.Auditing;
using Kora.Core.Commands;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    private SessionRemainingWorkCommandService? remainingWorkCommands;
    private SessionConversationService? remainingWorkConversation;
    private SessionWorkspaceService? remainingWorkWorkspace;
    private long remainingWorkBinding;
    private Guid remainingWorkPresentation;

    public void BindRemainingWork(SessionRemainingWorkCommandService commands, SessionConversationService conversation)
    {
        RetireRemainingWorkBinding();
        remainingWorkCommands = commands;
        remainingWorkConversation = conversation;
        if (sessionCommands is { } workspace)
        {
            remainingWorkWorkspace = workspace;
            workspace.SessionMetadataChanging += RetireRemainingWorkSource;
            workspace.SessionLifecycleChanged += RetireRemainingWorkSource;
            workspace.SessionRetired += RetireRemainingWorkSource;
        }
    }

    private void RetireRemainingWorkBinding()
    {
        remainingWorkCommands?.Retire();
        remainingWorkCommands = null;
        remainingWorkConversation = null;
        if (remainingWorkWorkspace is { } previous)
        {
            previous.SessionMetadataChanging -= RetireRemainingWorkSource;
            previous.SessionLifecycleChanged -= RetireRemainingWorkSource;
            previous.SessionRetired -= RetireRemainingWorkSource;
            remainingWorkWorkspace = null;
        }
        Interlocked.Increment(ref remainingWorkBinding);
    }

    private void RetireRemainingWorkSource(HostId<SessionIdentity> session)
    {
        remainingWorkCommands?.Retire();
        Interlocked.Increment(ref remainingWorkBinding);
        RevokeSessionPresentation(session);
    }

    public bool CanAnswerRemainingWork => IsHostInputEligible && !IsProtectedCall && CanEnterRetainedAnswerRead;

    private Func<bool> CaptureRemainingWorkEligibility(RequestOrigin origin, Func<bool> admitted)
    {
        var input = CaptureManualCallInput(origin, CallPolicyRevision);
        var source = CaptureNewRequestOpeningEligibility(input);
        var binding = Interlocked.Read(ref remainingWorkBinding);
        var revision = Interlocked.Read(ref workspaceSurfaceInputRevision);
        var composer = Interlocked.Read(ref attachmentCatalogueComposerRevision);
        var name = assistantNameConfiguration.Get().Revision;
        var privacy = privacyObservation.Current;
        var recovery = Interlocked.Read(ref voiceRecoveryRevision);
        var initiallyEligible = CanAnswerRemainingWork;
        return () => initiallyEligible && CanAnswerRemainingWork && source() && admitted()
            && binding == Interlocked.Read(ref remainingWorkBinding)
            && revision == Interlocked.Read(ref workspaceSurfaceInputRevision)
            && composer == Interlocked.Read(ref attachmentCatalogueComposerRevision)
            && name == assistantNameConfiguration.Get().Revision
            && privacy.TopologyRevision == privacyObservation.Current.TopologyRevision
            && privacy.MicrophoneAccess == privacyObservation.Current.MicrophoneAccess
            && privacy.SessionState == privacyObservation.Current.SessionState
            && recovery == Interlocked.Read(ref voiceRecoveryRevision);
    }

    private Func<bool> CaptureRemainingWorkChoiceEligibility(RequestOrigin origin)
    {
        var call = CallPolicyRevision;
        var name = assistantNameConfiguration.Get().Revision;
        var owner = auditImpactOwnerRevision();
        var topology = privacyObservation.Current.TopologyRevision;
        var binding = Interlocked.Read(ref remainingWorkBinding);
        // A later original-channel choice has its own capture generation; this retains only metadata-selection policy.
        return () => CanAnswerRemainingWork && call == CallPolicyRevision
            && name == assistantNameConfiguration.Get().Revision && owner == auditImpactOwnerRevision()
            && topology == privacyObservation.Current.TopologyRevision && binding == Interlocked.Read(ref remainingWorkBinding)
            && (origin != RequestOrigin.ActivatedVoice || IsVoiceEnabled && HasVoiceConsent && CallObservation.AllowActivation);
    }

    private async Task ExecuteRemainingWorkCommandAsync(SessionCommand command, SecurityAuditInitiator initiator,
        Func<bool>? original, bool activated)
    {
        var origin = OriginalOrigin(initiator);
        if (!CanAnswerRemainingWork || command.Operation == SessionCommandOperation.Invalid
            || origin is not (RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice)
            || origin == RequestOrigin.ActivatedVoice && (!activated || !isAssistantNameAvailable))
        {
            Transcript = "Remaining-work answer unavailable. Resolve the original question, approval, editor, dictation or private/channel admission first. "
                + SessionCommand.RemainingWorkSyntax;
            return;
        }
        var live = CaptureRemainingWorkEligibility(origin, original ?? (static () => true));
        var choiceSource = CaptureRemainingWorkChoiceEligibility(origin);
        await HostRequestRunner.RunAsync(origin, async () =>
        {
            try
            {
                var commands = remainingWorkCommands ?? throw new InvalidOperationException("The host remaining-work query is unavailable.");
                var result = await commands.ExecuteAsync(command, origin, live, CancellationToken.None, choiceSource);
                if (!live()) { return; }
                if (result.Answer is { } answer)
                { await PresentRemainingWorkAsync(answer, commands.Conversation, live, CancellationToken.None); }
                else
                {
                    PresentResponse(AssistantState.Information, "Remaining-work target choice.", result.Format(),
                        refreshOutput: false, sourceAssigned: () =>
                        {
                            forceVisualResponse = true;
                            foreach (var choice in result.Choices) { sessionPresentationSources.Add(choice.Authority.SessionId.Value); }
                        });
                }
            }
            catch (Exception exception) when (exception is InvalidOperationException or IOException
                or UnauthorizedAccessException or OperationCanceledException or ArgumentException)
            {
                ApplicationLog.Error(logger, exception, "Answering addressed remaining work");
                if (live()) { ShowFailure("Remaining-work answer unavailable/changed.", exception.Message); }
            }
        });
    }

    public async Task<string> AnswerRemainingWorkAsync(SessionWorkspaceEntry target, Func<bool> admitted, CancellationToken token)
    {
        var live = CaptureRemainingWorkEligibility(RequestOrigin.LocalUi, admitted);
        if (!live()) { throw new InvalidOperationException("Remaining-work answer requires current private, decision-free native input."); }
        var body = string.Empty;
        await HostRequestRunner.RunAsync(RequestOrigin.LocalUi, async () =>
        {
            var conversation = remainingWorkConversation ?? throw new InvalidOperationException("The host remaining-work query is unavailable.");
            var answer = await conversation.ReadRemainingWorkAsync(target, live, token);
            body = await PresentRemainingWorkAsync(answer, conversation, live, token);
        });
        return body;
    }

    private async Task<string> PresentRemainingWorkAsync(SessionRemainingWorkAnswer answer, SessionConversationService conversation,
        Func<bool> admitted, CancellationToken token)
    {
        await conversation.ValidateRemainingWorkAsync(answer, admitted, token);
        if (!conversation.IsRemainingWorkCurrent(answer, admitted)) { throw new InvalidOperationException("Remaining-work publication changed. Ask again."); }
        var body = answer.Format();
        var identity = Guid.NewGuid();
        var speech = Interlocked.Read(ref manualCallSpeechRevision);
        PresentResponse(AssistantState.Information, "Remaining work.", body, sourceAssigned: () =>
        {
            remainingWorkPresentation = identity;
            sessionPresentationSources.Add(answer.Session.Authority.SessionId.Value);
            forceVisualResponse = true;
        });
        var response = Interlocked.Read(ref webResponseRevision);
        bool Current() => remainingWorkPresentation == identity && response == Interlocked.Read(ref webResponseRevision)
            && conversation.IsRemainingWorkCurrent(answer, admitted);
        if (Current() && IsSpeechResponseEnabled)
        {
            await TrySpeakCurrentResponseAsync(answer.FormatSpeech(), expectedSpeechRevision: speech, additionalEligibility: Current,
                boundedExactReadback: true,
                validateReadSource: cancellation => conversation.ValidateRemainingWorkAsync(answer, admitted, cancellation),
                cancellationToken: token);
        }
        return Current() ? body : string.Empty;
    }
}