using System.Collections.Immutable;
using Kora.Core.Configuration.RegisteredSettings;
using Kora.Core.Configuration.SettingInspection;

namespace Kora.Core.Tools;

/// <summary>Read-only registered metadata and reviewed scalars, never an arbitrary configuration reader.</summary>
public static class SettingsDiscoveryTools
{
    public const string List = "settings.list";
    public const string Get = "settings.get";
    public const string ResultReason = "reviewed-settings-lower-trust";
    public static IReadOnlyList<CapabilityDescriptor> Descriptors { get; } = Array.AsReadOnly<CapabilityDescriptor>(
    [
        new(List, 1, CapabilityInputShape.Page, CapabilityOutputShape.RegisteredSettings,
            CapabilityEffect.ReadOnlyObservation, CapabilityAvailability.Available,
            Array.AsReadOnly<CapabilityLane>([CapabilityLane.Execution]), ReadOnlyCapabilityCatalog.Limits),
        new(Get, 1, CapabilityInputShape.RegisteredSettingId, CapabilityOutputShape.RegisteredSettings,
            CapabilityEffect.ReadOnlyObservation, CapabilityAvailability.Available,
            Array.AsReadOnly<CapabilityLane>([CapabilityLane.Execution]), new(1024, 1, 4096)),
    ]);

    public static CapabilityDescriptor? FindExact(string id) =>
        Descriptors.SingleOrDefault(item => string.Equals(item.Id, id, StringComparison.Ordinal));

    public static RegisteredSettingDescriptor? FindSetting(string id) =>
        RegisteredSettingRegistry.Settings.SingleOrDefault(item => string.Equals(item.Id, id, StringComparison.Ordinal));

    public static bool HasConfirmedReader(RegisteredSetting setting) =>
        SettingInspectionReview.InspectedSettings.Contains(setting);

    public static CapabilityReply Read(ImmutableArray<SettingObservation> observations, string id, string json)
    {
        var descriptor = FindExact(id);
        if (descriptor is null) { return new(CapabilityOutcome.Denied, "unknown-capability"); }
        var input = CapabilityInputPolicy.Read(descriptor, json);
        if (input.DenialReason is { } denial) { return new(CapabilityOutcome.Denied, denial); }
        RegisteredSettingsResult result;
        if (string.Equals(id, Get, StringComparison.Ordinal))
        {
            var setting = FindSetting(input.Id!.Id)!;
            var observation = observations.SingleOrDefault(item => item.Setting == setting.Setting);
            if (observation is null) { return new(CapabilityOutcome.Unavailable, "setting-value-unavailable"); }
            result = new([new(setting, HasConfirmedReader(setting.Setting))], 1, null, observation);
        }
        else
        {
            var settings = RegisteredSettingRegistry.Settings;
            if (CapabilityPagePolicy.Resolve(input.Page!, settings.Count) is not { } page)
            { return new(CapabilityOutcome.Denied, "page-out-of-range"); }
            result = new(settings.Skip(input.Page!.Offset).Take(page.Count)
                .Select(item => new RegisteredSettingMetadata(item, HasConfirmedReader(item.Setting))).ToImmutableArray(),
                settings.Count, page.NextOffset);
        }
        return new(CapabilityOutcome.Succeeded, ResultReason, Settings: result);
    }

    public static bool Matches(CapabilityReply reply, string id, CapabilityInputResult input)
    {
        if (!string.Equals(reply.Reason, ResultReason, StringComparison.Ordinal)
            || reply.Settings is not { } result || result.Records.IsDefaultOrEmpty
            || reply.Capabilities is not null || reply.Descriptor is not null || reply.Version is not null
            || reply.Readiness is not null || reply.Runtimes is not null || reply.Runtime is not null
            || reply.WebPage is not null || reply.Mcp is not null || reply.Sessions is not null || reply.Customizations is not null
            || reply.ProviderSettings is not null)
        { return false; }
        if (string.Equals(id, Get, StringComparison.Ordinal))
        {
            return result.TotalRecords == 1 && result.NextOffset is null && result.Records.Length == 1
                && result.Records[0] == new RegisteredSettingMetadata(FindSetting(input.Id!.Id)!, true)
                && result.Observation is { } observation
                && HasConfirmedReader(observation.Setting)
                && observation.Setting == result.Records[0].Descriptor.Setting;
        }
        var settings = RegisteredSettingRegistry.Settings;
        return result.Observation is null && result.TotalRecords == settings.Count
            && CapabilityPagePolicy.Resolve(input.Page!, settings.Count) is { } page
            && result.NextOffset == page.NextOffset
            && result.Records.SequenceEqual(settings.Skip(input.Page!.Offset).Take(page.Count)
                .Select(item => new RegisteredSettingMetadata(item, HasConfirmedReader(item.Setting))));
    }
}
