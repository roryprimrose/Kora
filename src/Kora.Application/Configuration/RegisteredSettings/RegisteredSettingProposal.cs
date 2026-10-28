using Kora.Core.Configuration;
using Kora.Core.Configuration.RegisteredSettings;
using Kora.Core.Voice;

namespace Kora.Application.Configuration.RegisteredSettings;

/// <summary>Validated suggestion data only. Source review and fresh confirmation still own admission.</summary>
public abstract record RegisteredSettingProposal(RegisteredSetting Setting)
{
    public RegisteredSettingDescriptor Descriptor => RegisteredSettingRegistry.Get(Setting);

    public sealed record Appearance(RegisteredSetting Key, AppearanceOption Option, AppearanceValue Value)
        : RegisteredSettingProposal(Key);
    public sealed record Summary(RegisteredSetting Key, SpeechOption Option, string Value)
        : RegisteredSettingProposal(Key);
    public sealed record Volume(PlaybackVolume Value) : RegisteredSettingProposal(RegisteredSetting.PlaybackVolume);
    public sealed record ResponseMode(ResponseOutputMode Value) : RegisteredSettingProposal(RegisteredSetting.ResponseMode);
}
