using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Tools;

using Microsoft.Extensions.Logging;

namespace Kora.Application.Tools;

public sealed class ReadOnlyCapabilityRegistry(
    ICapabilityHostAccess host,
    IApplicationInfo application,
    DependencyBootstrapper dependencies,
    ILogger<ReadOnlyCapabilityRegistry> logger)
{
    private readonly Guid registryId = Guid.NewGuid();
    private static readonly JsonSerializerOptions wireOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };
    private static readonly string[] DependencyIds =
        ["kora.storage", "kora.sqlite", "powershell.runtime", "local.inference", "windows.voice", "windows.tts"];

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
            var version = application.Version;
            return string.IsNullOrWhiteSpace(version) || version.Length > 128
                ? new(CapabilityOutcome.Failed, "invalid-version-observation")
                : new(CapabilityOutcome.Succeeded, "observed",
                    Version: new(version, "Not observed by the current version provider."));
        }
        if (descriptor.Input == CapabilityInputShape.Id)
        {
            if (fields.Length != 1 || !string.Equals(fields[0].Name, "id", StringComparison.Ordinal)
                || fields[0].Value.ValueKind != JsonValueKind.String)
            {
                return new(CapabilityOutcome.Denied, "invalid-id-input");
            }
            return Get(descriptor.Id, new CapabilityIdInput(fields[0].Value.GetString()!));
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
        return List(descriptor.Id, page);
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

    private CapabilityReply Get(string id, CapabilityIdInput input)
    {
        if (string.Equals(id, ReadOnlyCapabilityCatalog.Get, StringComparison.Ordinal))
        {
            var target = ReadOnlyCapabilityCatalog.Descriptors.FirstOrDefault(item => string.Equals(item.Id, input.Id, StringComparison.Ordinal));
            return target is null
                ? new(CapabilityOutcome.Denied, "unknown-capability")
                : new(CapabilityOutcome.Succeeded, "admitted-read-only", Descriptor: target);
        }
        return string.Equals(input.Id, "local.inference", StringComparison.Ordinal)
            ? new(CapabilityOutcome.Succeeded, "recorded-observation", Runtime: Runtime())
            : new(CapabilityOutcome.Denied, "unknown-runtime");
    }

    private CapabilityReply List(string id, CapabilityPageInput input)
    {
        var total = id switch
        {
            ReadOnlyCapabilityCatalog.List => ReadOnlyCapabilityCatalog.Descriptors.Count,
            ReadOnlyCapabilityCatalog.Readiness => DependencyIds.Length,
            _ => 1,
        };
        if (input.Offset >= total)
        {
            return new(CapabilityOutcome.Denied, "page-out-of-range");
        }
        var count = Math.Min(input.Count, total - input.Offset);
        int? next = input.Offset + count < total ? input.Offset + count : null;
        var observations = dependencies.Observations;
        return id switch
        {
            ReadOnlyCapabilityCatalog.List => new(CapabilityOutcome.Succeeded, "admitted-read-only",
                Capabilities: new(ReadOnlyCapabilityCatalog.Descriptors.Skip(input.Offset).Take(count).ToArray(), total, next)),
            ReadOnlyCapabilityCatalog.Readiness => new(CapabilityOutcome.Succeeded, "recorded-observations",
                Readiness: new(DependencyIds.Skip(input.Offset).Take(count)
                    .Select(dependencyId => Observe(dependencyId, observations)).ToArray(), total, next)),
            _ => new(CapabilityOutcome.Succeeded, "recorded-observation", Runtimes: new([Runtime()], total, next)),
        };
    }

    private static ReadinessObservation Observe(string id, IReadOnlyList<DependencyObservation> observations)
    {
        var observation = observations.FirstOrDefault(item => string.Equals(item.Status.Id, id, StringComparison.Ordinal));
        if (observation is null)
        {
            return new(id, null, CapabilityAvailability.NotObserved, "No completed observation is recorded.", null);
        }
        var readiness = observation.Status.Readiness;
        return new(id, readiness, readiness == DependencyReadiness.Ready
                ? CapabilityAvailability.Available : CapabilityAvailability.Unavailable,
            readiness switch
            {
                DependencyReadiness.Ready => "The existing probe completed successfully; this is not a fresh check.",
                DependencyReadiness.Missing => "A required dependency was not found by the existing probe.",
                DependencyReadiness.NeedsConfiguration => "The existing probe requires configuration or a selected prerequisite.",
                DependencyReadiness.Incompatible => "The existing probe found an incompatible dependency.",
                DependencyReadiness.Blocked => "The existing probe reported a blocked dependency.",
                _ => "The existing probe failed; open setup for local recovery details.",
            }, observation.ObservedAt);
    }

    private RuntimeObservation Runtime()
    {
        var observation = Observe("local.inference", dependencies.Observations);
        return new(observation.Id, RuntimeLocality.Local, observation.Availability,
            observation.Readiness, observation.Reason, observation.ObservedAt, ToolLoopQualified: false);
    }

    private CapabilityReply Finish(string id, CapabilityReply reply)
    {
        CapabilityLog.Result(logger, id, reply.Outcome, reply.Reason);
        return reply;
    }
}