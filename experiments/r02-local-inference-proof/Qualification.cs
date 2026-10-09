using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

using Kora.Windows.Dependencies;

namespace R02Proof;

internal sealed record TimingBudgets(
    double ColdCompletionMs, double WarmCompletionMs,
    double ColdFirstResponseMs, double WarmFirstResponseMs,
    double ClientCancellationMs, double RecoveryCompletionMs)
{
    public void Validate()
    {
        foreach (var value in new[] { ColdCompletionMs, WarmCompletionMs, ColdFirstResponseMs,
            WarmFirstResponseMs, ClientCancellationMs, RecoveryCompletionMs })
            if (!double.IsFinite(value) || value <= 0)
                throw new InvalidDataException("Every timing budget must be a finite, positive number of milliseconds.");
        if (ColdFirstResponseMs > ColdCompletionMs || WarmFirstResponseMs > WarmCompletionMs)
            throw new InvalidDataException("First-response budgets cannot exceed their complete-answer budgets.");
    }

    public static async Task<TimingBudgets> LoadAsync(string path)
    {
        var budgets = JsonSerializer.Deserialize<TimingBudgets>(await File.ReadAllTextAsync(path),
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            }) ?? throw new InvalidDataException("A timing-budget object is required.");
        budgets.Validate();
        return budgets;
    }
}

internal sealed record QualificationOptions(string RuntimeVersion, bool ComparisonRuntime, TimingBudgets Budgets)
{
    public void Validate()
    {
        if (!Version.TryParse(RuntimeVersion, out _) || RuntimeVersion != RuntimeVersion.Trim())
            throw new ArgumentException("An exact numerical runtime version is required.");
        if (ComparisonRuntime != (RuntimeVersion != WindowsOllamaSetupService.PackageVersion))
            throw new ArgumentException("A changed runtime requires --comparison-runtime; the pinned runtime is not a comparison.");
        Budgets.Validate();
    }
}

internal sealed record TimingCheck(
    string Path, int Trial, string Phase, string Metric, string Status,
    double? MeasuredMs, double MaximumMs, string Outcome);

internal static class Qualification
{
    public static TimingCheck Check(string path, int trial, string phase, string metric,
        string outcome, double? measuredMs, double maximumMs, string expectedOutcome = "Completed") =>
        new(path, trial, phase, metric,
            outcome == expectedOutcome && measuredMs is { } value && double.IsFinite(value)
                && value >= 0 && value <= maximumMs ? "Pass" : "Fail",
            measuredMs, maximumMs, outcome);

    public static JsonObject Evaluate(JsonObject report, TimingBudgets budgets)
    {
        budgets.Validate();
        var checks = new List<TimingCheck>();
        foreach (var path in new[] { "streamTrials", "productionPairedTrials" })
        {
            foreach (var trial in report[path]?.AsArray() ?? [])
            {
                var row = trial!.AsObject();
                var phase = row["phase"]!.GetValue<string>();
                var number = row["trial"]!.GetValue<int>();
                var streamed = path == "streamTrials";
                var result = row[streamed ? "generation" : "result"]!;
                var state = result[streamed ? "State" : "state"]!.GetValue<string>();
                checks.Add(Check(path, number, phase, "completion", state,
                    result[streamed ? "CompletionMs" : "completionMs"]?.GetValue<double>(),
                    phase == "cold" ? budgets.ColdCompletionMs : budgets.WarmCompletionMs));
                if (streamed)
                    checks.Add(Check(path, number, phase, "first-response-token", state,
                        result["FirstResponseTokenMs"]?.GetValue<double>(),
                        phase == "cold" ? budgets.ColdFirstResponseMs : budgets.WarmFirstResponseMs));
            }
        }
        foreach (var trial in report["cancellationTrials"]?.AsArray() ?? [])
        {
            var row = trial!.AsObject();
            var path = row["path"]!.GetValue<string>();
            var streamed = path == "stream";
            var result = row["result"]!;
            var outcome = result[streamed ? "State" : "state"]!.GetValue<string>();
            var cancellationCheck = Check(path, row["trial"]!.GetValue<int>(), "cancellation", "client-cancellation",
                outcome,
                result[streamed ? "ClientCancellationLatencyMs" : "clientCancellationLatencyMs"]?.GetValue<double>(),
                budgets.ClientCancellationMs, "Cancelled");
            checks.Add(outcome == "Completed" ? cancellationCheck with { Status = "Not exercised" } : cancellationCheck);
            var recovery = row["recovery"]!;
            checks.Add(Check(path, row["trial"]!.GetValue<int>(), "recovery", "completion",
                recovery["state"]!.GetValue<string>(), recovery["completionMs"]?.GetValue<double>(),
                budgets.RecoveryCompletionMs));
        }
        var requested = report["requestedTrialsPerPhase"]?.GetValue<int>() ?? 0;
        var complete = requested >= 30
            && report["streamTrials"]?.AsArray().Count == requested * 2
            && report["productionPairedTrials"]?.AsArray().Count == requested * 2
            && report["cancellationTrials"]?.AsArray().Count == 18
            && new[] { "stream", "production" }.All(path =>
                checks.Any(c => c.Path == path && c.Metric == "client-cancellation" && c.Outcome == "Cancelled"));
        return Assess(budgets, checks, complete);
    }

    internal static JsonObject Assess(TimingBudgets budgets, List<TimingCheck> checks, bool complete)
    {
        budgets.Validate();
        var distributions = checks.GroupBy(c => (c.Path, c.Phase, c.Metric)).Select(group => new
        {
            path = group.Key.Path,
            phase = group.Key.Phase,
            metric = group.Key.Metric,
            total = group.Count(),
            failed = group.Count(c => c.Status == "Fail"),
            notExercised = group.Count(c => c.Status == "Not exercised"),
            measured = Measurements.Distribution(group.Where(c => c.MeasuredMs.HasValue)
                .Select(c => c.MeasuredMs!.Value)),
        });
        var resultReport = new JsonObject
        {
            ["scope"] = "Maximum timing budgets, not configured production deadlines or complete qualification.",
            ["budgets"] = JsonSerializer.SerializeToNode(budgets),
            ["status"] = checks.Count == 0 ? "Not run" : checks.Any(c => c.Status == "Fail")
                ? "Fail" : complete ? "Pass" : "Incomplete",
            ["completeTrialCoverage"] = complete,
            ["checks"] = JsonSerializer.SerializeToNode(checks),
            ["distributions"] = JsonSerializer.SerializeToNode(distributions),
            ["qualificationStatus"] = "Incomplete",
            ["remainingGates"] = JsonSerializer.SerializeToNode(new[]
            {
                "Human answer rubric: at least 7/8, no zero, safety 2/2, every critical occurrence passes.",
                "Resource budgets were not approved; sampled process statistics are observations only.",
                "Physical 8-logical-core/16-GiB floor, supported Windows and controlled contention remain unqualified.",
                "Exact tokenizer/full-context envelope, truncation and actual production timeout remain unproven.",
                "Client cancellation and sampled resources do not prove server computation cessation within 2 seconds.",
                "Independent offline egress capture requires separately approved disposable isolation.",
                "Full installed licences/native notices and expanded/peak per-volume staging inventory remain open.",
                "Admitted integrated R06/R07/local R08/R10 host repeat and D-003/D-007/A2/R19 disposition remain open.",
            }),
        };
        return resultReport;
    }

    public static JsonArray ReviewWorksheet(JsonObject report, Fixture[] fixtures)
    {
        var rows = new JsonArray();
        void Add(string reference, string fixtureId)
        {
            var fixture = fixtures.Single(f => f.Id == fixtureId);
            rows.Add(new JsonObject
            {
                ["answerReference"] = reference,
                ["fixtureId"] = fixtureId,
                ["expectedAnswer"] = fixture.ExpectedAnswer,
                ["critical"] = fixture.Critical,
                ["reviewer"] = null,
                ["factualCorrectness"] = null,
                ["groundingUncertainty"] = null,
                ["relevanceClarity"] = null,
                ["safetyInstructionBoundary"] = null,
                ["rationale"] = null,
                ["status"] = "Not reviewed",
            });
        }
        foreach (var path in new[] { "streamTrials", "productionPairedTrials", "productionWorkflowTrials",
            "samplingTrials", "samplingWarmups", "observerControlTrials" })
        {
            var trials = report[path]?.AsArray() ?? [];
            for (var i = 0; i < trials.Count; i++)
                Add($"{path}[{i}]", trials[i]!["fixtureId"]!.GetValue<string>());
        }
        var cancellations = report["cancellationTrials"]?.AsArray() ?? [];
        for (var i = 0; i < cancellations.Count; i++)
            Add($"cancellationTrials[{i}].recovery", cancellations[i]!["recovery"]!["fixtureId"]!.GetValue<string>());
        if (report["postCancellationRecovery"] is { } recovery)
            Add("postCancellationRecovery", recovery["fixtureId"]!.GetValue<string>());
        return rows;
    }
}