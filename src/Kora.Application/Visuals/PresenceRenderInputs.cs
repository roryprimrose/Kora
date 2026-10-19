using Kora.Core;
using Kora.Core.Configuration;

namespace Kora.Application.Visuals;

public sealed record PresenceRenderInputs(
    AssistantState State, bool IsSpeaking, double SpeechOutputLevel, bool SpeechScalingEnabled,
    int SpeechScaleAmount, int Density, int DotSize, int MovementSpeed, bool IsLightTheme,
    bool ReducedMotion, bool IsPresenceVisible, double Width, double Height, double Scaling)
{
    public void Validate()
    {
        if (!Enum.IsDefined(State))
        {
            throw new InvalidDataException("The presence state is unknown.");
        }
        if (!double.IsFinite(SpeechOutputLevel) || SpeechOutputLevel is < 0 or > 1)
        {
            throw new InvalidDataException("The playback level must be finite and normalized.");
        }
        PresenceSettings.ValidateSpeechScaleAmountPercent(SpeechScaleAmount);
        PresenceSettings.ValidateDotDensityPercent(Density);
        PresenceSettings.ValidateDotSizePercent(DotSize);
        PresenceSettings.ValidateMovementSpeedPercent(MovementSpeed);
        if (!double.IsFinite(Width) || !double.IsFinite(Height) || Width <= 0 || Height <= 0
            || !double.IsFinite(Scaling) || Scaling <= 0
            || Math.Ceiling(Width * Scaling) > 4096 || Math.Ceiling(Height * Scaling) > 4096)
        {
            throw new InvalidDataException("The presence requires finite dimensions within its 4096-pixel buffer bound.");
        }
    }
}