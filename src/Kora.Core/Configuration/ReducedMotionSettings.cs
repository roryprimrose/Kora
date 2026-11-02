using Kora.Core.Presentation;

namespace Kora.Core.Configuration;

/// <summary>Device-local reduction may restrict motion, never override the system accessibility preference.</summary>
public static class ReducedMotionSettings
{
    public const bool DefaultEnabled = false;

    public static bool ParseSaved(string value) => bool.TryParse(value, out var enabled)
        ? enabled : throw new InvalidDataException("The saved reduced motion preference is invalid.");

    public static string FormatSaved(bool enabled) => enabled ? "True" : "False";

    public static bool IsMotionEnabled(bool reducedMotion, MotionPreference system) => system switch
    {
        MotionPreference.AnimationsEnabled => !reducedMotion,
        MotionPreference.ReducedMotion => false,
        _ => throw new InvalidOperationException("The current motion preference is invalid."),
    };
}
