using System.Text.Json.Serialization;

namespace Kora.Core.Configuration.SettingInspection;

[JsonConverter(typeof(JsonStringEnumConverter<SettingInspectionProfile>))]
public enum SettingInspectionProfile
{
    ConfirmedSettings,
    WindowsRate,
}
