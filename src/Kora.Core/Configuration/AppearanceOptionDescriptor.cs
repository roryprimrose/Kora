namespace Kora.Core.Configuration;

public sealed record AppearanceOptionDescriptor(
    AppearanceOption Option,
    string Id,
    string Description,
    AppearanceValueType Type,
    string Units,
    AppearanceValue Default,
    int? Minimum,
    int? Maximum,
    string AuditAction)
{
    public string Scope => "device-local";
    public string Effect => "appearance-only";
    public string Availability => "local-host";
    public string ApplicationTiming => "immediate-after-save";
    public bool IsCallSensitive => false;
    public bool CanReset => true;
    public string SpokenName => Id.Replace('.', ' ').Replace('-', ' ');
}
