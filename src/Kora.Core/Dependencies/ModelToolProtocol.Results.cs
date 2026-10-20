using Kora.Core.Tools;

namespace Kora.Core.Dependencies;

public static partial class ModelToolProtocol
{
    private static void ValidateResult(ModelToolCall call, CapabilityReply reply)
    {
        ValidateText(reply.Reason);
        var shapeCount = (reply.Capabilities is null ? 0 : 1) + (reply.Descriptor is null ? 0 : 1)
            + (reply.Version is null ? 0 : 1) + (reply.Readiness is null ? 0 : 1)
            + (reply.Runtimes is null ? 0 : 1) + (reply.Runtime is null ? 0 : 1);
        var descriptor = ReadOnlyCapabilityCatalog.FindExact(call.CapabilityId)!;
        var input = CapabilityInputPolicy.Read(descriptor, call.InputJson);
        var valid = shapeCount == 1 && (descriptor.Output switch
        {
            CapabilityOutputShape.Descriptors => reply.Capabilities is { Records: not null } capabilities
                && ValidPage(capabilities.Records, capabilities.TotalRecords, capabilities.NextOffset, input.Page!)
                && capabilities.TotalRecords == ReadOnlyCapabilityCatalog.Descriptors.Count
                && capabilities.Records.Select((item, index) => item is not null && CapabilityReplyWire.IsExactDescriptor(
                    item, ReadOnlyCapabilityCatalog.Descriptors[input.Page!.Offset + index])).All(match => match),
            CapabilityOutputShape.Descriptor => reply.Descriptor is not null
                && CapabilityReplyWire.IsSingleDescriptor(reply, ReadOnlyCapabilityCatalog.FindExact(input.Id!.Id)!),
            CapabilityOutputShape.Version => reply.Version is { } version && ValidVersion(version),
            CapabilityOutputShape.Readiness => reply.Readiness is { Records: not null } readiness
                && ValidPage(readiness.Records, readiness.TotalRecords, readiness.NextOffset, input.Page!)
                && readiness.Records.All(ValidReadiness),
            CapabilityOutputShape.Runtimes => reply.Runtimes is { Records: not null } runtimes
                && ValidPage(runtimes.Records, runtimes.TotalRecords, runtimes.NextOffset, input.Page!)
                && runtimes.TotalRecords == 1 && runtimes.Records.All(ValidRuntime),
            _ => reply.Runtime is { } runtime && ValidRuntime(runtime),
        });
        if (!valid) { throw new InvalidDataException("The host tool result does not match its exact requested output shape."); }
    }

    private static bool ValidPage<T>(IReadOnlyList<T> records, int total, int? next, CapabilityPageInput input)
    {
        return total is >= 1 and <= ReadOnlyCapabilityCatalog.MaximumRecords
            && CapabilityPagePolicy.Resolve(input, total) is { } page
            && records.Count == page.Count && next == page.NextOffset;
    }

    private static bool ValidVersion(ApplicationVersionObservation version)
    {
        if (string.IsNullOrWhiteSpace(version.Version) || string.IsNullOrWhiteSpace(version.DeploymentObservation))
        { return false; }
        ValidateText(version.Version);
        ValidateText(version.DeploymentObservation);
        return true;
    }

    private static bool ValidReadiness(ReadinessObservation observation)
    {
        if (observation is null || string.IsNullOrWhiteSpace(observation.Id) || string.IsNullOrWhiteSpace(observation.Reason)
            || !Enum.IsDefined(observation.Availability) || observation.Readiness is { } state && !Enum.IsDefined(state))
        { return false; }
        ValidateText(observation.Id);
        ValidateText(observation.Reason);
        return true;
    }

    private static bool ValidRuntime(RuntimeObservation runtime)
    {
        if (runtime is null || !CapabilityReplyWire.IsRecordedLocalRuntime(runtime)) { return false; }
        ValidateText(runtime.Reason);
        return true;
    }
}
