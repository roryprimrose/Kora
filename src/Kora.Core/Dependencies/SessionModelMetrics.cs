using System.Collections.Immutable;
using System.Globalization;
using System.Text;

namespace Kora.Core.Dependencies;

/// <summary>Content-free observations since this Kora process started, not billing or execution authority.</summary>
public sealed record SessionModelMetrics(
    DateTimeOffset StartedAt, DateTimeOffset ObservedAt, long Executions,
    long InputReportedExecutions, long OutputReportedExecutions, long InputTokens, long OutputTokens,
    ImmutableArray<ModelUsageSummary> Models, int OmittedModels = 0, bool Retained = false)
{
    public void Validate()
    {
        if (StartedAt.Offset != TimeSpan.Zero || ObservedAt.Offset != TimeSpan.Zero || ObservedAt < StartedAt
            || Executions < 0 || InputReportedExecutions < 0 || InputReportedExecutions > Executions
            || OutputReportedExecutions < 0 || OutputReportedExecutions > Executions
            || InputTokens < 0 || OutputTokens < 0 || Models.IsDefault || OmittedModels < 0)
        { throw new InvalidDataException("Session metrics observation is invalid."); }
        foreach (var model in Models) { model.Validate(); }
        if (Models.Sum(model => model.Executions) > Executions
            || Models.Sum(model => model.InputTokens) > InputTokens
            || Models.Sum(model => model.OutputTokens) > OutputTokens)
        { throw new InvalidDataException("Model metrics exceed their session observation."); }
    }

    public string Format()
    {
        var text = new StringBuilder(Retained
            ? "Recorded session model usage (retained with the conversation).\n"
            : "Session model usage since Kora started (not lifetime usage).\n");
        text.Append(CultureInfo.InvariantCulture, $"Observed UTC {ObservedAt:O}. Finished model executions: {Executions}.\n");
        text.Append("Input tokens: ").Append(FormatTokens(InputTokens, InputReportedExecutions, Executions));
        text.Append("\nOutput tokens: ").Append(FormatTokens(OutputTokens, OutputReportedExecutions, Executions));
        text.Append("\nCost / credits: not reported; no price estimate.\n");
        if (Executions == 0) { text.Append("No finished model execution recorded in this observation.\n"); }
        foreach (var model in Models) { text.Append('\n').Append(model.Format()).Append('\n'); }
        if (OmittedModels > 0) { text.Append(CultureInfo.InvariantCulture, $"\n{OmittedModels} model rows omitted by the tool bound; session totals include them. Open Session metrics for all rows.\n"); }
        return text.Append("Token totals sum reported consumption across models, including repeated context in tool rounds. ")
            .Append("Missing reports are unknown, not zero. Context is the latest finished request's input, not a live or cumulative conversation window. ")
            .Append("Context percentages and capacities are never added across models. In-flight, interrupted or earlier uninstrumented calls may not have reported usage.").ToString();
    }

    public string FormatSpeech() => Executions == 0
        ? "No finished model execution recorded. Token usage and cost are not reported."
        : string.Create(CultureInfo.InvariantCulture,
            $"Recorded session usage: input {SpeechTokens(InputTokens, InputReportedExecutions)}, output {SpeechTokens(OutputTokens, OutputReportedExecutions)}. {Models.Length + OmittedModels} models. Reporting may be partial. Context is per model; cost is not reported.");

    private static string SpeechTokens(long tokens, long reports) =>
        reports == 0 ? "not reported" : tokens.ToString("N0", CultureInfo.InvariantCulture) + " tokens";

    internal static string FormatTokens(long tokens, long reported, long executions) => reported == 0
        ? "not reported"
        : tokens.ToString("N0", CultureInfo.InvariantCulture)
            + (reported < executions ? $" (partial; {reported.ToString(CultureInfo.InvariantCulture)}/{executions.ToString(CultureInfo.InvariantCulture)} executions reported)" : " (reported)");
}
