namespace Kora.Core.Configuration;

public sealed record ResponseWindowSettings(
    bool AlwaysShow,
    bool Topmost,
    ResponseWindowPosition? Position)
{
    public const int DefaultTimeoutSeconds = 5;
    public const int MinimumTimeoutSeconds = VisibilityTimeoutSettings.MinimumSeconds;
    public const int MaximumTimeoutSeconds = VisibilityTimeoutSettings.MaximumSeconds;

    public static ResponseWindowSettings Default { get; } = new(
        AlwaysShow: false,
        Topmost: true,
        Position: null);

    public static void ValidateTimeoutSeconds(int seconds) =>
        VisibilityTimeoutSettings.ValidateSeconds(seconds);
}
