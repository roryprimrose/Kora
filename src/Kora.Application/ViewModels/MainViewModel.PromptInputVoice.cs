using System.ComponentModel;

using Kora.Application.Definitions;
using Kora.Application.Voice;
using Kora.Core.Voice;

using Microsoft.Extensions.Logging;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    private PromptInputVoiceController? promptInputVoice;

    public PromptInputVoiceController? BindPromptInputVoice(ILogger<PromptInputVoiceController> inputLogger)
    {
        if (promptInputVoice is not null) { throw new InvalidOperationException("The host already has a prompt input voice owner."); }
        if (voiceRecognition is not IQuestionVoiceRecognitionService capture) { return null; }
        promptInputVoice = new(capture, uiDispatcher, ObservePromptInputVoice, IsPromptSourceCurrent, inputLogger, TimeProvider.System);
        PropertyChanged += PromptInputVoiceReadinessChanged;
        return promptInputVoice;
    }

    public void UnbindPromptInputVoice(PromptInputVoiceController? controller)
    {
        if (!ReferenceEquals(promptInputVoice, controller)) { return; }
        RetirePromptInputVoice();
        PropertyChanged -= PromptInputVoiceReadinessChanged;
        promptInputVoice = null;
    }

    private QuestionVoiceAdmission? ObservePromptInputVoice() =>
        questionVoiceReplies?.IsActive != true && !questionSpeech
        && Volatile.Read(ref pushToTalkHeld) == 0 && Volatile.Read(ref listeningStopRequests) == 0
        && !listeningStop.IsStopping ? ObserveActivatedInputVoiceAdmission() : null;

    private void PromptInputVoiceReadinessChanged(object? sender, PropertyChangedEventArgs args) => promptInputVoice?.Revalidate();

    private void RetirePromptInputVoice() =>
        promptInputVoice?.Retire("Prompt input admission changed. Microphone closed; late text discarded.");
}
