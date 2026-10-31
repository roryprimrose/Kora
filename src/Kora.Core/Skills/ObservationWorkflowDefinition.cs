using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using Kora.Core.Dependencies;
using Kora.Core.Tools;

namespace Kora.Core.Skills;

/// <summary>A closed ordered declaration of existing host observations, never action authority.</summary>
public sealed class ObservationWorkflowDefinition
{
    public const int MaximumSteps = 6;
    public const int MaximumSourceBytes = 8192;
    public const int MaximumInputBytes = 4096;
    public const int MaximumResultBytes = 8192;
    public static TimeSpan MaximumDuration { get; } = TimeSpan.FromSeconds(15);

    private ObservationWorkflowDefinition(ImmutableArray<ObservationWorkflowStep> steps) => Steps = steps;

    public ImmutableArray<ObservationWorkflowStep> Steps { get; }
    public string ToolDeclaration => string.Join(',', Steps.Select(step => step.Tool).Distinct(StringComparer.Ordinal));

    public static ObservationWorkflowDefinition Parse(string source)
    {
        byte[] bytes;
        try { bytes = new UTF8Encoding(false, true).GetBytes(source); }
        catch (EncoderFallbackException exception) { throw new InvalidDataException("Workflow source requires well-formed Unicode.", exception); }
        using var document = ModelToolProtocol.ReadJson(bytes, MaximumSourceBytes, out var duplicates, maximumDepth: 6);
        var root = document.RootElement;
        if (duplicates || root.ValueKind != JsonValueKind.Object
            || root.EnumerateObject().Count() != 1 || !root.TryGetProperty("steps", out var steps)
            || steps.ValueKind != JsonValueKind.Array || steps.GetArrayLength() is < 1 or > MaximumSteps)
        { throw new InvalidDataException("Declare only an ordered steps array containing one to six explicit observations."); }
        var result = ImmutableArray.CreateBuilder<ObservationWorkflowStep>();
        var total = 0;
        foreach (var step in steps.EnumerateArray())
        {
            if (step.ValueKind != JsonValueKind.Object || step.EnumerateObject().Count() != 2
                || !step.TryGetProperty("tool", out var tool) || tool.ValueKind != JsonValueKind.String
                || !step.TryGetProperty("input", out var input)
                || ReadOnlyCapabilityCatalog.FindExact(tool.GetString()!) is not { } descriptor
                || CapabilityInputPolicy.Read(descriptor, input.GetRawText()).DenialReason is not null)
            { throw new InvalidDataException("Every step requires an exact original six-ID tool and its complete admitted input; other directives are forbidden."); }
            var json = input.GetRawText();
            total += Encoding.UTF8.GetByteCount(json);
            if (total > MaximumInputBytes) { throw new InvalidDataException("The complete workflow input budget was exceeded; no steps were dropped."); }
            result.Add(new(result.Count + 1, descriptor.Id, json));
        }
        return new(result.ToImmutable());
    }
}
