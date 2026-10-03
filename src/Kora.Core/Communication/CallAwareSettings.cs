namespace Kora.Core.Communication;

public sealed record CallAwareSettings(
    bool ShowVisualTextDuringCalls,
    bool AllowVoiceActivationDuringCalls)
{
    public static CallAwareSettings Default { get; } = new(
        ShowVisualTextDuringCalls: true,
        AllowVoiceActivationDuringCalls: true);
}
