using Kora.Application.Hosting;
using Kora.Core.Auditing;
using Kora.Core.Conversation;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Voice;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel : ISessionAnswerPlayback
{
    private sealed class RetainedAnswerSpeech(ConversationMessage source, ResponseSpeechTarget target, HostRequest request,
        Func<bool> admitted, Func<CancellationToken, Task> validate)
    {
        internal ConversationMessage Source { get; } = source;
        internal ResponseSpeechTarget Target { get; } = target;
        internal HostRequest Request { get; } = request;
        internal Func<bool> Admitted { get; } = admitted;
        internal Func<CancellationToken, Task> Validate { get; } = validate;
        internal bool Interrupted { get; set; }
        internal bool InterruptionConfirmed { get; set; }
        internal string Result { get; set; } = "Retained answer was not played completely. The full visual source is unchanged; inspect speech length, privacy and speech readiness before a fresh deliberate request. No successful read or queued replay is claimed.";
    }

    private RetainedAnswerSpeech? retainedAnswerSpeech;
    private Func<Func<bool>, Func<CancellationToken, Task>?>? captureSelectedAnswerRead;
    private Func<bool> retainedAnswerDecisionsEligible = static () => true;
    private int retainedAnswerDisplacedDecisionInput;

    public void RefuseSelectedAnswerReadForRetiredDecisionInput() =>
        Interlocked.Exchange(ref retainedAnswerDisplacedDecisionInput, 1);

    public void BindRetainedAnswerReadDecisionGate(Func<bool> eligible)
    {
        if (retainedAnswerSpeech is not null) { RetireResponseReadPlayback(); }
        retainedAnswerDecisionsEligible = eligible;
    }

    private bool CanEnterRetainedAnswerRead => composerDictation?.IsActive != true
        && !speechInterruptionActive
        && questionVoiceReplies?.IsActive != true && !IsResponseInteractionPending && !IsGrantEditorVisible
        && responseReadDecisionsEligible() && retainedAnswerDecisionsEligible();

    public void BindSelectedAnswerRead(Func<Func<bool>, Func<CancellationToken, Task>?> capture) =>
        captureSelectedAnswerRead = capture;

    public async Task<string> ReadAnswerOnceAsync(ConversationMessage source, Func<bool> admitted,
        Func<CancellationToken, Task> validateSource, CancellationToken token)
    {
        source.Validate();
        var request = HostActivity.RequireCurrent().Request;
        if (source.AssistantResponse is null || source.Request.SessionId != request.SessionId
            || request.Origin is not (RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice)
            || request.InvocationId is not null || !admitted() || !CanEnterRetainedAnswerRead || retainedAnswerSpeech is not null
            || IsSpeaking || responseReadCancellation is not null)
        { return "Retained answer read refused: select an exact ASSISTANT answer with unchanged private input admission. No replay was queued."; }
        var binding = new RetainedAnswerSpeech(source, new(Guid.NewGuid(), source.Revision, CallPolicyRevision), request,
            () => admitted() && CanEnterRetainedAnswerRead, validateSource);
        retainedAnswerSpeech = binding;
        try
        {
            var input = CaptureResponseReadInput();
            if (!input.Eligible())
            { return "Retained answer speech unavailable: normal speech mode, call, privacy, owner and channel restrictions still apply. No replay was queued."; }
            await speechReadOnce.ExecuteAsync(binding.Target,
                request.Origin == RequestOrigin.ActivatedVoice ? SecurityAuditInitiator.VoiceCommand : SecurityAuditInitiator.LocalUser,
                token);
            return binding.Result;
        }
        finally
        {
            retainedAnswerSpeech = null;
        }
    }
}
