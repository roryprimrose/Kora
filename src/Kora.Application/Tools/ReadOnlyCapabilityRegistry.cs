using Kora.Core.Diagnostics;
using Kora.Core.Tools;
using Kora.Tools.Application;
using Kora.Tools.Capabilities;
using Kora.Tools.Readiness;
using Kora.Tools.Runtime;

using Microsoft.Extensions.Logging;

namespace Kora.Application.Tools;

public sealed class ReadOnlyCapabilityRegistry(
    ICapabilityHostAccess host,
    CapabilitiesList listCapabilities,
    CapabilitiesGet getCapability,
    ApplicationGetVersion getVersion,
    ReadinessGet getReadiness,
    RuntimeList listRuntimes,
    RuntimeGetStatus getRuntimeStatus,
    ILogger<ReadOnlyCapabilityRegistry> logger)
{
    private readonly Guid registryId = Guid.NewGuid();

    public CapabilityCaller Admit(CapabilityLane lane)
    {
        if (!Enum.IsDefined(lane) || !host.IsCurrentHost)
        {
            throw new InvalidOperationException("The caller lane or current host is not admitted.");
        }
        return new CapabilityCaller(registryId, HostActivity.RequireCurrent(), lane);
    }

    public CapabilityReply Invoke(
        CapabilityCaller caller, string id, string input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(input);
        cancellationToken.ThrowIfCancellationRequested();
        var descriptor = ReadOnlyCapabilityCatalog.FindExact(id);
        if (!IsAdmitted(caller) || !ReferenceEquals(HostActivity.Current, caller.Activity))
        {
            return Finish("unadmitted", new(CapabilityOutcome.Denied, "current-host-context-required"));
        }
        using var activity = HostActivity.BeginChild(HostActivityLayer.Application, HostOperation.Tool);
        try
        {
            var reply = descriptor is null
                ? new CapabilityReply(CapabilityOutcome.Denied, "unknown-capability")
                : Dispatch(descriptor, input);
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsAdmitted(caller) || !ReferenceEquals(HostActivity.Current, activity))
            {
                reply = new(CapabilityOutcome.Denied, "host-admission-changed");
            }
            reply = BoundReply(reply);
            activity.Complete(reply.Outcome == CapabilityOutcome.Succeeded
                ? HostOperationOutcome.Completed : HostOperationOutcome.Failed);
            return Finish(descriptor?.Id ?? "unknown", reply);
        }
        catch (OperationCanceledException)
        {
            activity.Complete(HostOperationOutcome.Cancelled);
            throw;
        }
        catch
        {
            activity.Complete(HostOperationOutcome.Failed);
            throw;
        }
    }

    public static byte[] Serialize(CapabilityReply reply) =>
        CapabilityReplyWire.Serialize(BoundReply(reply));

    internal static CapabilityReply BoundReply(CapabilityReply reply)
    {
        if (!CapabilityReplyWire.IsWithinLimit(reply))
        {
            return new(CapabilityOutcome.Failed, "output-limit-exceeded");
        }
        if (reply.Outcome == CapabilityOutcome.Succeeded
            && (reply.Descriptor is { } descriptor
                && (ReadOnlyCapabilityCatalog.FindExact(descriptor.Id) is not { } expected
                    || !CapabilityReplyWire.IsSingleDescriptor(reply, expected))
                || reply.Runtime is not null && !CapabilityReplyWire.IsSingleRecordedRuntime(reply)))
        {
            return new(CapabilityOutcome.Failed, "invalid-recorded-result");
        }
        return reply;
    }

    private bool IsAdmitted(CapabilityCaller caller) =>
        caller.RegistryId == registryId && Enum.IsDefined(caller.Lane) && host.IsCurrentHost
        && HostActivity.Current is { } current
        && ReferenceEquals(current.Request, caller.Activity.Request)
        && caller.Activity.Outcome == HostOperationOutcome.Unknown;

    private CapabilityReply Dispatch(CapabilityDescriptor descriptor, string input)
    {
        var parsed = CapabilityInputPolicy.Read(descriptor, input);
        if (parsed.DenialReason is { } denial)
        {
            return new(CapabilityOutcome.Denied, denial);
        }
        if (descriptor.Input == CapabilityInputShape.None)
        {
            return getVersion.Execute();
        }
        if (descriptor.Input == CapabilityInputShape.Id)
        {
            return string.Equals(descriptor.Id, ReadOnlyCapabilityCatalog.Get, StringComparison.Ordinal)
                ? getCapability.Execute(parsed.Id!) : getRuntimeStatus.Execute(parsed.Id!);
        }
        return descriptor.Id switch
        {
            ReadOnlyCapabilityCatalog.List => listCapabilities.Execute(parsed.Page!),
            ReadOnlyCapabilityCatalog.Readiness => getReadiness.Execute(parsed.Page!),
            _ => listRuntimes.Execute(parsed.Page!),
        };
    }

    private CapabilityReply Finish(string id, CapabilityReply reply)
    {
        CapabilityLog.Result(logger, id, reply.Outcome, reply.Reason);
        return reply;
    }
}