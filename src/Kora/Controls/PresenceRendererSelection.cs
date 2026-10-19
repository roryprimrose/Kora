namespace Kora.Controls;

internal static class PresenceRendererSelection
{
    internal const string EnvironmentVariable = "KORA_PRESENCE_RENDERER";

    public static bool UseNative(string? value) => value switch
    {
        null or "avalonia" => false,
        "native" => true,
        _ => throw new InvalidDataException("KORA_PRESENCE_RENDERER must be unset, 'avalonia', or 'native'."),
    };
}