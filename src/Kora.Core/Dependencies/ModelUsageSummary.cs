using System.Globalization;
using System.Text.Json.Serialization;

namespace Kora.Core.Dependencies;

public sealed record ModelUsageSummary(
    ModelProviderIdentity Provider, string Model, long Executions, long InputReportedExecutions,
    long OutputReportedExecutions, long InputTokens, long OutputTokens,
    long? LatestContextTokens, long? ContextCapacityTokens, TimeSpan TotalElapsed)
{
    [JsonIgnore]
    public double ContextPercent => LatestContextTokens is { } used && ContextCapacityTokens is { } capacity
        ? (double)used / capacity * 100 : 0;
    [JsonIgnore]
    public bool HasContextPercent => LatestContextTokens is not null && ContextCapacityTokens is not null;
    [JsonIgnore]
    public string Label => Provider + " / " + Model;
    [JsonIgnore]
    public string InputLabel => "Input: " + SessionModelMetrics.FormatTokens(InputTokens, InputReportedExecutions, Executions);
    [JsonIgnore]
    public string OutputLabel => "Output: " + SessionModelMetrics.FormatTokens(OutputTokens, OutputReportedExecutions, Executions);
    [JsonIgnore]
    public string ContextLabel => LatestContextTokens is not { } used ? "Latest request context: not reported"
        : ContextCapacityTokens is { } capacity
            ? string.Create(CultureInfo.InvariantCulture, $"Latest request context: {used:N0} / {capacity:N0} tokens ({ContextPercent:0.0}%)")
            : string.Create(CultureInfo.InvariantCulture, $"Latest request context: {used:N0} tokens; capacity not reported");

    public void Validate()
    {
        if (!Enum.IsDefined(Provider) || string.IsNullOrWhiteSpace(Model) || Model.Length > 256 || Model.Any(char.IsControl)
            || Executions <= 0 || InputReportedExecutions < 0 || InputReportedExecutions > Executions
            || OutputReportedExecutions < 0 || OutputReportedExecutions > Executions
            || InputTokens < 0 || OutputTokens < 0 || TotalElapsed < TimeSpan.Zero)
        { throw new InvalidDataException("Per-model usage observation is invalid."); }
        new ModelTokenUsage(ContextTokens: LatestContextTokens, ContextCapacityTokens: ContextCapacityTokens).Validate();
    }

    public string Format() => string.Create(CultureInfo.InvariantCulture,
        $"{Label}: {Executions} executions; {InputLabel}; {OutputLabel}; {ContextLabel}; elapsed {TotalElapsed.TotalSeconds:0.###} seconds.");
}
