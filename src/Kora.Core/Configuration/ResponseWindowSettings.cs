namespace Kora.Core.Configuration;

public sealed record ResponseWindowSettings(
    bool AlwaysShow,
    bool Topmost,
    ResponseWindowPosition? Position)
{
    public static ResponseWindowSettings Default { get; } = new(
        AlwaysShow: false,
        Topmost: true,
        Position: null);
}
