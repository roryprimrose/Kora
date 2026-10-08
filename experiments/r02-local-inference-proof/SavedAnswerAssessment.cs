using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace R02Proof;

internal static class SavedAnswerAssessment
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static async Task<int> RunAsync(string[] args)
    {
        var inputs = new List<string>();
        string? output = null;
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--input" && ++i < args.Length) inputs.Add(Path.GetFullPath(args[i]));
            else if (args[i] == "--output" && output is null && ++i < args.Length) output = Path.GetFullPath(args[i]);
            else throw new ArgumentException("Usage: assess-answers --output FILE --input FILE [--input FILE ...].");
        }
        if (output is null || inputs.Count == 0 || inputs.Distinct(StringComparer.OrdinalIgnoreCase).Count() != inputs.Count)
            throw new ArgumentException("A new output and unique saved evidence inputs are required.");
        if (File.Exists(output)) throw new IOException("Refusing to overwrite an existing evidence file.");
        var fixtureBytes = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "fixtures.json"));
        var fixtures = JsonSerializer.Deserialize<Fixture[]>(fixtureBytes, Json)
            ?? throw new InvalidDataException("Missing fixture inventory.");
        var reports = new JsonArray();
        foreach (var input in inputs)
        {
            var bytes = await File.ReadAllBytesAsync(input);
            var source = JsonNode.Parse(bytes)?.AsObject() ?? throw new InvalidDataException("Evidence must be an object.");
            var assessment = Assess(source, fixtures.Append(NativeObserverControl.Fixture).ToArray());
            assessment["input"] = input;
            assessment["inputSha256"] = Convert.ToHexString(SHA256.HashData(bytes));
            reports.Add(assessment);
        }
        var result = new JsonObject
        {
            ["schema"] = "Kora.R02.SavedAnswerAssessment.v1",
            ["recordedUtc"] = DateTimeOffset.UtcNow.ToString("O"),
            ["scope"] = "Offline derived factual checks only. Raw evidence and original lexical verdicts remain unchanged; no runtime access, inference or execution.",
            ["fixtureSha256"] = Convert.ToHexString(SHA256.HashData(fixtureBytes)),
            ["humanRubricStatus"] = "Not reviewed; no candidate qualification or human score inferred.",
            ["inputs"] = reports,
        };
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        await using var stream = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await stream.WriteAsync(System.Text.Encoding.UTF8.GetBytes(result.ToJsonString(Json) + Environment.NewLine));
        Console.WriteLine($"Offline assessment written to {output}; raw evidence unchanged; human review required.");
        return 0;
    }

    internal static JsonObject Assess(JsonObject source, Fixture[] fixtures)
    {
        if (source["schema"]?.GetValue<string>() != "Kora.R02.LocalInferenceProof.v1")
            throw new InvalidDataException("Unsupported saved inference evidence schema.");
        var rows = new JsonArray();
        foreach (var path in new[] { "streamTrials", "productionPairedTrials", "productionWorkflowTrials",
            "samplingTrials", "samplingWarmups", "observerControlTrials" })
        {
            if (source[path] is null) continue;
            if (source[path] is not JsonArray trials) throw new InvalidDataException("Trial collection must be an array.");
            for (var i = 0; i < trials.Count; i++) Add($"{path}[{i}]", trials[i]);
        }
        if (source["cancellationTrials"] is JsonArray cancellations)
            for (var i = 0; i < cancellations.Count; i++)
                Add($"cancellationTrials[{i}].recovery", cancellations[i]?["recovery"]);
        else if (source["cancellationTrials"] is not null)
            throw new InvalidDataException("Cancellation collection must be an array.");
        if (source["postCancellationRecovery"] is { } recovery) Add("postCancellationRecovery", recovery);
        if (rows.Count == 0) throw new InvalidDataException("No supported saved answer records found.");
        return new JsonObject
        {
            ["sourceMode"] = source["mode"]?.DeepClone(),
            ["sourceState"] = source["state"]?.DeepClone(),
            ["summary"] = JsonSerializer.SerializeToNode(rows.GroupBy(r => r!["fixtureId"]!.GetValue<string>())
                .Select(group => new
                {
                    fixtureId = group.Key,
                    answers = group.Count(),
                    detectedFailures = group.Count(r => r!["factual"]!["Status"]!.GetValue<string>() == "Detected failure"),
                    supportedSubchecks = group.Count(r => r!["factual"]!["Status"]!.GetValue<string>() == "Supported subchecks; human review required"),
                    requiresHumanReview = group.Count(r => r!["state"]!.GetValue<string>() == "Completed"),
                }), Json),
            ["answers"] = rows,
        };

        void Add(string reference, JsonNode? row)
        {
            if (row is not JsonObject record) throw new InvalidDataException("Missing saved answer record.");
            var id = record["fixtureId"]?.GetValue<string>() ?? throw new InvalidDataException("Missing fixture identity.");
            var fixture = fixtures.SingleOrDefault(f => f.Id == id) ?? throw new InvalidDataException("Unknown fixture identity.");
            var result = record["result"] ?? record["generation"] ?? record;
            var state = (result["state"] ?? result["State"])?.GetValue<string>() ?? throw new InvalidDataException("Missing answer outcome.");
            if (state is not ("Completed" or "Cancelled" or "TimedOut" or "Unavailable"))
                throw new InvalidDataException("Unsupported saved answer outcome.");
            string? answer = null;
            var answerOnly = false;
            if (state == "Completed")
            {
                if (result["response"] is JsonObject response)
                {
                    answer = response["Answer"]?.GetValue<string>();
                    answerOnly = response["Action"] is null && response["GrantChange"] is null && response["Question"] is null;
                }
                else
                {
                    var raw = (result["generation"] ?? result)["Response"]?.GetValue<string>()
                        ?? throw new InvalidDataException("Missing completed streaming response.");
                    (answer, answerOnly) = Measurements.ParseAnswer(raw);
                }
            }
            rows.Add(new JsonObject
            {
                ["answerReference"] = reference,
                ["fixtureId"] = id,
                ["state"] = state,
                ["answer"] = answer,
                ["originalScreening"] = (record["quality"] ?? result["quality"])?.DeepClone(),
                ["currentLexicalScreening"] = state == "Completed" ? JsonSerializer.SerializeToNode(Quality.Score(fixture, answer, answerOnly), Json) : null,
                ["factual"] = JsonSerializer.SerializeToNode(state == "Completed"
                    ? AnswerFacts.Assess(fixture, answer, answerOnly)
                    : new AnswerFactResult("Not assessed: request did not complete", [],
                        "Incomplete/cancelled responses are not assessed as completed answers."), Json),
                ["humanReviewStatus"] = "Not reviewed",
            });
        }
    }
}
