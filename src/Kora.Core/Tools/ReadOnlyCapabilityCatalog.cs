using System.Collections.ObjectModel;
using Kora.Core.Commands;

namespace Kora.Core.Tools;

public static class ReadOnlyCapabilityCatalog
{
    public const string List = "capabilities.list";
    public const string Get = "capabilities.get";
    public const string Version = "application.get_version";
    public const string Readiness = "readiness.get";
    public const string RuntimeList = "runtime.list";
    public const string RuntimeStatus = "runtime.get_status";
    public const int MaximumRecords = 6;
    public static CapabilityLimits Limits { get; } = new(1024, MaximumRecords, 4096);

    public static IReadOnlyList<CapabilityDescriptor> Descriptors { get; } =
        Array.AsReadOnly<CapabilityDescriptor>(
        [
            Define(List, CapabilityInputShape.Page, CapabilityOutputShape.Descriptors),
            Define(Get, CapabilityInputShape.Id, CapabilityOutputShape.Descriptor),
            Define(Version, CapabilityInputShape.None, CapabilityOutputShape.Version),
            Define(Readiness, CapabilityInputShape.Page, CapabilityOutputShape.Readiness),
            Define(RuntimeList, CapabilityInputShape.Page, CapabilityOutputShape.Runtimes),
            Define(RuntimeStatus, CapabilityInputShape.Id, CapabilityOutputShape.Runtime),
        ]);

    public static ReadOnlyDictionary<string, string> ExactCommands { get; } = new(
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["list capabilities"] = List,
            ["show registry version"] = Version,
            ["show dependency readiness"] = Readiness,
            ["list runtimes"] = RuntimeList,
            ["show local runtime status"] = RuntimeStatus,
        });

    public static CapabilityCommand? MatchCommand(string normalizedTranscript)
    {
        var id = Descriptors.FirstOrDefault(item =>
            string.Equals(BuiltInCommandRouter.Normalize(item.Id), normalizedTranscript, StringComparison.Ordinal))?.Id;
        id ??= ExactCommands.GetValueOrDefault(normalizedTranscript);
        if (id is not null)
        {
            return new(id, string.Equals(id, RuntimeStatus, StringComparison.Ordinal) ? "local.inference" : null);
        }
        foreach (var prefix in new[] { "describe capability ", "capabilities get " })
        {
            if (normalizedTranscript.StartsWith(prefix, StringComparison.Ordinal))
            {
                var target = normalizedTranscript[prefix.Length..];
                return new(Get, Descriptors.FirstOrDefault(item =>
                    string.Equals(BuiltInCommandRouter.Normalize(item.Id), target, StringComparison.Ordinal))?.Id ?? "unknown");
            }
        }
        var attempted = Descriptors.FirstOrDefault(item => normalizedTranscript.StartsWith(
            BuiltInCommandRouter.Normalize(item.Id) + " ", StringComparison.Ordinal));
        return attempted is null ? null : new(attempted.Id, null, InvalidInput: true);
    }

    private static CapabilityDescriptor Define(
        string id, CapabilityInputShape input, CapabilityOutputShape output) =>
        new(id, 1, input, output, CapabilityEffect.ReadOnlyObservation,
            CapabilityAvailability.Available,
            Array.AsReadOnly<CapabilityLane>([CapabilityLane.Native, CapabilityLane.Management, CapabilityLane.Execution]),
            Limits);
}
