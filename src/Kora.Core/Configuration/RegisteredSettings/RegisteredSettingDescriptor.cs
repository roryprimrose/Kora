namespace Kora.Core.Configuration.RegisteredSettings;

public sealed record RegisteredSettingDescriptor(
    RegisteredSetting Setting, string Id, string Description, string Units,
    int? Minimum, int? Maximum, bool RequiresNativeReview)
{
    public string Scope => "shared device-local";
}
