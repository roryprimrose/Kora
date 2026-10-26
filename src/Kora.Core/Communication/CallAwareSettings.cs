namespace Kora.Core.Communication;

public sealed record CallAwareSettings(
    bool ShowVisualTextDuringCalls,
    bool AllowVoiceActivationDuringCalls,
    bool AllowSpokenApprovalsDuringCalls = false)
{
    public static CallAwareSettings Default { get; } = new(
        ShowVisualTextDuringCalls: true,
        AllowVoiceActivationDuringCalls: true);
}
