using System.Text.Json;

namespace Kora.ManualProofHarness;

internal static class Li03AutomatedDisposition
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly string[] ProductionPaths = ["productionPairedTrials", "productionWorkflowTrials"];

    internal static void Create(string measurementPath, string outputPath)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(measurementPath));
        var root = document.RootElement;
        if (!HasConclusiveAutomatedResult(root))
            throw new InvalidDataException(
                "The measurement ended before automated candidate gates completed; it cannot produce a rejection disposition.");
        var streamFailures = root.GetProperty("streamTrials").EnumerateArray()
            .Where(IsStreamFailure)
            .GroupBy(row => row.GetProperty("fixtureId").GetString() ?? "unknown", StringComparer.Ordinal)
            .Select(group => new { fixtureId = group.Key, occurrences = group.Count() })
            .ToArray();
        var productionRows = ProductionPaths
            .Where(name => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array)
            .SelectMany(name => root.GetProperty(name).EnumerateArray())
            .ToArray();
        var productionFailures = productionRows.Where(IsProductionFailure)
            .GroupBy(row => row.GetProperty("fixtureId").GetString() ?? "unknown", StringComparer.Ordinal)
            .Select(group => new { fixtureId = group.Key, occurrences = group.Count() })
            .ToArray();
        var automatedPassed = root.TryGetProperty("automatedQualityAndClientChecksPassed", out var passed)
            && passed.GetBoolean();
        var result = new
        {
            schema = "Kora.ManualProofHarness.LI03AutomatedDisposition.v1",
            recordedUtc = DateTimeOffset.UtcNow,
            measurementPath,
            runtimeVersion = root.GetProperty("testedProfile").GetProperty("runtimeVersion").GetString(),
            comparisonRuntime = root.GetProperty("testedProfile").GetProperty("comparisonRuntime").GetBoolean(),
            automatedChecksPassed = automatedPassed,
            disposition = automatedPassed ? "EligibleForHumanSemanticReview" : "RejectedByAutomatedGates",
            noHumanReviewRequiredToReject = !automatedPassed,
            humanReviewStillRequiredToQualify = true,
            streamTrialCount = root.GetProperty("streamTrials").GetArrayLength(),
            streamFailureCount = streamFailures.Sum(item => item.occurrences),
            streamFailuresByFixture = streamFailures,
            productionTrialCount = productionRows.Length,
            productionFailureCount = productionFailures.Sum(item => item.occurrences),
            productionFailuresByFixture = productionFailures,
            cleanup = root.TryGetProperty("cleanup", out var cleanup) ? cleanup.GetString() : null,
            limitation = "Automated rejection is conclusive for this run. Passing automated gates would still require the independent human rubric and remaining LI03 qualification evidence.",
        };
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)
            ?? throw new InvalidOperationException("Disposition output requires a parent directory."));
        File.WriteAllText(outputPath, JsonSerializer.Serialize(result, JsonOptions));
    }

    internal static bool HasConclusiveAutomatedResult(string measurementPath)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(measurementPath));
        return HasConclusiveAutomatedResult(document.RootElement);
    }

    private static bool HasConclusiveAutomatedResult(JsonElement root) =>
        root.TryGetProperty("automatedQualityAndClientChecksPassed", out var result)
        && result.ValueKind is JsonValueKind.True or JsonValueKind.False;

    private static bool IsStreamFailure(JsonElement row) =>
        !string.Equals(row.GetProperty("generation").GetProperty("State").GetString(), "Completed", StringComparison.Ordinal)
        || !row.GetProperty("quality").GetProperty("Passed").GetBoolean()
        || !row.GetProperty("cpuOnlyVerified").GetBoolean();

    private static bool IsProductionFailure(JsonElement row)
    {
        var result = row.TryGetProperty("result", out var nested) ? nested : row;
        return !string.Equals(result.GetProperty("state").GetString(), "Completed", StringComparison.Ordinal)
            || !result.GetProperty("quality").GetProperty("Passed").GetBoolean();
    }
}
