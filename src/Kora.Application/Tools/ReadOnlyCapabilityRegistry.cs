using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

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
    private static readonly JsonSerializerOptions wireOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

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
        var descriptor = ReadOnlyCapabilityCatalog.Descriptors.FirstOrDefault(item =>
            string.Equals(item.Id, id, StringComparison.Ordinal));
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
        JsonSerializer.SerializeToUtf8Bytes(BoundReply(reply), wireOptions);

    internal static CapabilityReply BoundReply(CapabilityReply reply)
    {
        if (JsonSerializer.SerializeToUtf8Bytes(reply, wireOptions).Length
            > ReadOnlyCapabilityCatalog.Limits.MaximumOutputUtf8Bytes)
        {
            return new(CapabilityOutcome.Failed, "output-limit-exceeded");
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
        if (Encoding.UTF8.GetByteCount(input) > descriptor.Limits.MaximumInputUtf8Bytes)
        {
            return new(CapabilityOutcome.Denied, "input-limit-exceeded");
        }
        using var document = Parse(input);
        if (document is null || document.RootElement.ValueKind != JsonValueKind.Object)
        {
            return new(CapabilityOutcome.Denied, "invalid-input");
        }
        var fields = document.RootElement.EnumerateObject().ToArray();
        if (fields.Select(field => field.Name).Distinct(StringComparer.Ordinal).Count() != fields.Length)
        {
            return new(CapabilityOutcome.Denied, "duplicate-input-field");
        }
        if (descriptor.Input == CapabilityInputShape.None)
        {
            if (fields.Length != 0)
            {
                return new(CapabilityOutcome.Denied, "unknown-input-field");
            }
            return getVersion.Execute();
        }
        if (descriptor.Input == CapabilityInputShape.Id)
        {
            if (fields.Length != 1 || !string.Equals(fields[0].Name, "id", StringComparison.Ordinal)
                || fields[0].Value.ValueKind != JsonValueKind.String)
            {
                return new(CapabilityOutcome.Denied, "invalid-id-input");
            }
            var id = new CapabilityIdInput(fields[0].Value.GetString()!);
            return string.Equals(descriptor.Id, ReadOnlyCapabilityCatalog.Get, StringComparison.Ordinal)
                ? getCapability.Execute(id) : getRuntimeStatus.Execute(id);
        }
        if (fields.Any(field => field.Name is not ("offset" or "count")
            || field.Value.ValueKind != JsonValueKind.Number
            || !field.Value.TryGetInt32(out _)))
        {
            return new(CapabilityOutcome.Denied, "invalid-page-input");
        }
        var page = new CapabilityPageInput(
            fields.FirstOrDefault(field => string.Equals(field.Name, "offset", StringComparison.Ordinal)).Value is { ValueKind: JsonValueKind.Number } offset
                ? offset.GetInt32() : 0,
            fields.FirstOrDefault(field => string.Equals(field.Name, "count", StringComparison.Ordinal)).Value is { ValueKind: JsonValueKind.Number } count
                ? count.GetInt32() : descriptor.Limits.MaximumRecords);
        if (page.Offset < 0 || page.Count < 1 || page.Count > descriptor.Limits.MaximumRecords)
        {
            return new(CapabilityOutcome.Denied, "page-out-of-range");
        }
        return descriptor.Id switch
        {
            ReadOnlyCapabilityCatalog.List => listCapabilities.Execute(page),
            ReadOnlyCapabilityCatalog.Readiness => getReadiness.Execute(page),
            _ => listRuntimes.Execute(page),
        };
    }

    private static JsonDocument? Parse(string input)
    {
        try
        {
            return JsonDocument.Parse(input, new JsonDocumentOptions { MaxDepth = 4 });
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private CapabilityReply Finish(string id, CapabilityReply reply)
    {
        CapabilityLog.Result(logger, id, reply.Outcome, reply.Reason);
        return reply;
    }
}