using System.Collections.Immutable;
using System.Globalization;

using Kora.Core.Artifacts;
using Kora.Core.Dependencies;
using Kora.Core.Tools;

namespace Kora.Core.Agents;

/// <summary>Compatibility data only; parsing never enables a profile or admits a run.</summary>
public sealed class BoundedAgentProfile
{
    public const string FormatVersion = "1";
    private static readonly ImmutableHashSet<string> Fields = ImmutableHashSet.Create(StringComparer.Ordinal,
        "name", "description", "kora-agent-format", "runtime", "input", "output", "references", "tools",
        "maximum-calls", "maximum-rounds", "maximum-input-bytes", "maximum-output-bytes", "deadline-ms");
    private static readonly ImmutableHashSet<string> SupportedTools = ImmutableHashSet.Create(StringComparer.Ordinal,
        ReadOnlyCapabilityCatalog.List, ReadOnlyCapabilityCatalog.Get, ReadOnlyCapabilityCatalog.Version,
        ReadOnlyCapabilityCatalog.Readiness, ReadOnlyCapabilityCatalog.RuntimeList, ReadOnlyCapabilityCatalog.RuntimeStatus);

    private BoundedAgentProfile(ArtifactFileDocument document, ImmutableArray<string> tools)
    {
        Document = document;
        Tools = tools;
        var metadata = document.Metadata;
        RequestedCalls = ReadLimit(metadata, "maximum-calls", allowZero: true);
        RequestedRounds = ReadLimit(metadata, "maximum-rounds");
        RequestedInputUtf8Bytes = ReadLimit(metadata, "maximum-input-bytes");
        RequestedOutputUtf8Bytes = ReadLimit(metadata, "maximum-output-bytes");
        RequestedDeadlineMilliseconds = ReadLimit(metadata, "deadline-ms");
        if (tools.IsEmpty != (RequestedCalls == 0))
        { throw new InvalidDataException("A tool-free profile must request zero calls; a tool-capable profile requires a positive call budget."); }
    }

    public ArtifactFileDocument Document { get; }
    public ImmutableArray<string> Tools { get; }
    public int RequestedCalls { get; }
    public int RequestedRounds { get; }
    public int RequestedInputUtf8Bytes { get; }
    public int RequestedOutputUtf8Bytes { get; }
    public int RequestedDeadlineMilliseconds { get; }
    public int MaximumCalls => Math.Min(RequestedCalls, ModelToolProtocol.MaximumCalls);
    public int MaximumRounds => Math.Min(RequestedRounds, Math.Min(ModelToolProtocol.MaximumRounds, MaximumCalls + 1));
    public int MaximumInputUtf8Bytes => Math.Min(RequestedInputUtf8Bytes, ModelToolProtocol.MaximumInputUtf8Bytes);
    public int MaximumOutputUtf8Bytes => Math.Min(RequestedOutputUtf8Bytes, ModelToolProtocol.MaximumOutputUtf8Bytes);
    public TimeSpan Deadline => TimeSpan.FromMilliseconds(Math.Min(RequestedDeadlineMilliseconds, ModelToolProtocol.Deadline.TotalMilliseconds));
    public ModelTurnConstraints Constraints => new(1, Tools, MaximumCalls, MaximumRounds,
        MaximumInputUtf8Bytes, MaximumOutputUtf8Bytes, (int)Deadline.TotalMilliseconds);

    public static BoundedAgentProfile Parse(ArtifactFileDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var metadata = document.Metadata;
        if (document.Definition.Kind != ArtifactKind.Agent || metadata.Count != Fields.Count
            || metadata.Keys.Any(key => !Fields.Contains(key)))
        { throw new InvalidDataException("An executable agent profile requires exactly the supported v1 metadata fields."); }
        Require(metadata, "kora-agent-format", FormatVersion);
        Require(metadata, "runtime", "local");
        Require(metadata, "input", "text");
        Require(metadata, "output", "text");
        Require(metadata, "references", "none");
        var value = metadata["tools"];
        var tools = string.Equals(value, "none", StringComparison.Ordinal)
            ? ImmutableArray<string>.Empty : value.Split(',').Select(item => item.Trim()).ToImmutableArray();
        if (tools.Length > SupportedTools.Count
            || tools.Distinct(StringComparer.Ordinal).Count() != tools.Length
            || tools.Any(tool => !SupportedTools.Contains(tool)))
        { throw new InvalidDataException("Agent tools must be distinct exact host read-only capability IDs, or 'none'."); }
        return new(document, tools);
    }

    private static int ReadLimit(IReadOnlyDictionary<string, string> metadata, string field, bool allowZero = false)
    {
        if (!int.TryParse(metadata[field], NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            || value < (allowZero ? 0 : 1))
        { throw new InvalidDataException("An agent budget must be an explicit bounded decimal integer."); }
        return value;
    }

    private static void Require(IReadOnlyDictionary<string, string> metadata, string field, string expected)
    {
        if (!string.Equals(metadata[field], expected, StringComparison.Ordinal))
        { throw new InvalidDataException("The agent requires an unsupported format, runtime, input, output or reference contract."); }
    }
}
