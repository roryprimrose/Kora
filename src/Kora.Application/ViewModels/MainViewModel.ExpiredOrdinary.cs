namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    public Func<bool> CaptureFreshOrdinaryRequestEligibility()
    {
        var original = CaptureSessionInstructionScopeEligibility();
        var privacy = CaptureCustomizationVoiceEligibility();
        var input = Interlocked.Read(ref workspaceSurfaceInputRevision);
        var capture = voiceRecognition.CaptureGeneration;
        return () => original() && privacy() && input == Interlocked.Read(ref workspaceSurfaceInputRevision)
            && capture == voiceRecognition.CaptureGeneration && voiceRecognition.IsCaptureQuiescent
            && composerDictation?.IsActive != true;
    }
}
