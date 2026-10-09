using System.Text.Json;
using System.Text.Json.Nodes;

namespace R02Proof;

internal static class InferenceComparison
{
    public static async Task<int> RunAsync(JsonObject report, Fixture[] fixtures, int trials,
        QualificationOptions qualification, LocalTransport? defaultOverride = null,
        LocalTransport? zeroOverride = null, Func<Task>? checkpoint = null,
        CancellationToken cancellationToken = default, bool compareStreaming = false, ProcessTreeObserver? observer = null)
    {
        qualification.Validate();
        using var defaultTransport = defaultOverride ?? LocalTransport.Real(observer, new(compareStreaming ? 0 : null));
        using var zeroTransport = zeroOverride ?? LocalTransport.Real(observer, new(0, compareStreaming ? true : null));
        using var defaultClient = new HttpClient(defaultTransport) { Timeout = TimeSpan.FromMinutes(2) };
        using var zeroClient = new HttpClient(zeroTransport) { Timeout = TimeSpan.FromMinutes(2) };
        var identity = await Program.VerifyProfileAsync(defaultClient, qualification.RuntimeVersion, cancellationToken);
        report["runtimeMetadata"] = identity.Version;
        report["modelCatalogue"] = identity.Catalogue;
        report["modelMetadata"] = await Measurements.ShowAsync(defaultClient, cancellationToken);
        var initial = await Measurements.GetAsync(defaultClient, "/api/ps", cancellationToken);
        report["initialLoadedModels"] = initial;
        SelfTests.Require(initial["models"]!.AsArray().All(m =>
            m!["name"]!.GetValue<string>() == Kora.Windows.Dependencies.WindowsOllamaSetupService.Model),
            "Another model is loaded; do not disturb shared residency.");
        report["requestedTrialsPerProfile"] = trials;
        report["comparisonFactor"] = compareStreaming ? "streaming" : "temperature";
        report["testedProfile"] = JsonSerializer.SerializeToNode(new
        {
            runtimeVersion = qualification.RuntimeVersion,
            qualification.ComparisonRuntime,
            productionPinsChanged = false,
            context = SamplingSettings.Context,
            seed = SamplingSettings.Seed,
            transport = compareStreaming
                ? "Buffered source-linked reasoner versus streamed captured production request; CPU-only; think=false; keep_alive=5m."
                : "Both buffered source-linked production reasoner; CPU-only; think=false; keep_alive=5m.",
            factor = compareStreaming ? "Only stream=false versus stream=true; temperature zero for both."
                : "Temperature omitted/runtime default versus explicitly zero.",
            limitation = "Fixed-seed/context comparison profiles are not unchanged production defaults or candidate qualification.",
        });
        var rows = new JsonArray();
        var warmups = new JsonArray();
        report["samplingTrials"] = rows;
        report["samplingWarmups"] = warmups;
        var allPassed = true;
        var profilesByFactor = Profiles(compareStreaming);
        try
        {
            await Measurements.UnloadAsync(defaultClient, cancellationToken);
            async Task MeasureAsync(string profile, int trial, Fixture fixture, JsonArray destination)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var client = profile == profilesByFactor[0] ? defaultClient : zeroClient;
                var transport = profile == profilesByFactor[0] ? defaultTransport : zeroTransport;
                var before = transport.GenerationPayloads.Count;
                var result = profile == "streamed"
                    ? await StreamReasonAsync(client, fixture, cancellationToken, transport.Observer)
                    : await Program.ReasonAsync(client, fixture, cancellationToken, transport.Observer);
                result["path"] = profile == "streamed"
                    ? "Streamed replay of captured production request with experimental matched overrides."
                    : "Source-linked buffered production reasoner with experimental matched sampling/context/keep-alive overrides.";
                var ps = await Measurements.GetAsync(client, "/api/ps", cancellationToken);
                var cpuOnly = Measurements.CpuOnly(ps);
                var contextVerified = cpuOnly
                    && ps["models"]![0]!["context_length"]?.GetValue<int>() == SamplingSettings.Context;
                allPassed &= result["state"]!.GetValue<string>() == "Completed"
                    && result["quality"]!["Passed"]!.GetValue<bool>() && contextVerified;
                destination.Add(new JsonObject
                {
                    ["profile"] = profile,
                    ["trial"] = trial,
                    ["fixtureId"] = fixture.Id,
                    ["request"] = transport.GenerationPayloads.Count == before + 1
                        ? JsonNode.Parse(transport.GenerationPayloads[before]) : null,
                    ["result"] = result,
                    ["loadedModels"] = ps,
                    ["cpuOnlyVerified"] = cpuOnly,
                    ["contextVerified"] = contextVerified,
                });
                if (defaultOverride is null)
                    Console.WriteLine($"comparison {profile} {trial}/{trials} {fixture.Id}: {Program.GenerationProgress(result)}");
                if (checkpoint is not null) await checkpoint();
                SelfTests.Require(contextVerified, "CPU-only residency or matched effective context was not confirmed; stop comparison.");
            }
            foreach (var profile in profilesByFactor)
                await MeasureAsync(profile, 0, fixtures[0], warmups);
            for (var i = 0; i < trials; i++)
            {
                await Program.VerifyProfileAsync(defaultClient, qualification.RuntimeVersion, cancellationToken);
                var fixture = fixtures[i % fixtures.Length];
                var profiles = i % 2 == 0
                    ? profilesByFactor
                    : profilesByFactor.Reverse().ToArray();
                foreach (var profile in profiles)
                    await MeasureAsync(profile, i + 1, fixture, rows);
            }
            report["samplingSummary"] = JsonSerializer.SerializeToNode(rows.GroupBy(r => r!["profile"]!.GetValue<string>())
                .Select(group => new
                {
                    profile = group.Key,
                    trials = group.Count(),
                    screenedPasses = group.Count(r => r!["result"]!["quality"]?["Passed"]?.GetValue<bool>() == true),
                    shapeFailures = group.Count(r => r!["result"]!["quality"]?["ShapePassed"]?.GetValue<bool>() != true),
                    lexicalSafetyFailures = group.Count(r => r!["result"]!["quality"]?["SafetyPassed"]?.GetValue<bool>() != true),
                    completionMs = Measurements.Distribution(group.Select(r => r!["result"]!["completionMs"]!.GetValue<double>())),
                }));
            report["automatedQualityAndClientChecksPassed"] = allPassed;
            report["state"] = "Matched comparison evidence collected; fixed lexical checks and human rubric are not interchangeable. No production change or candidate acceptance.";
            return allPassed ? 0 : 3;
        }
        finally
        {
            using var cleanupTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try
            {
                await Program.VerifyProfileAsync(defaultClient, qualification.RuntimeVersion, cleanupTimeout.Token);
                report["finalLoadedModels"] = await Measurements.UnloadAsync(defaultClient, cleanupTimeout.Token);
                report["cleanup"] = "Selected test model confirmed unloaded; pre-existing server and assets retained.";
            }
            catch (Exception exception) when (exception is HttpRequestException or JsonException
                or InvalidOperationException or OperationCanceledException)
            {
                report["cleanup"] = "Unknown: " + exception.GetType().Name + ": " + exception.Message;
                throw new InvalidOperationException("Comparison cleanup could not be confirmed; inspect before replay.", exception);
            }
            finally
            {
                report["calls"] = JsonSerializer.SerializeToNode(new Dictionary<string, List<string>>
                {
                    [profilesByFactor[0]] = defaultTransport.Calls,
                    [profilesByFactor[1]] = zeroTransport.Calls,
                });
                if (checkpoint is not null) await checkpoint();
            }
        }
    }

    public static JsonObject Evaluate(JsonObject report, TimingBudgets budgets)
    {
        var rows = report["samplingTrials"]?.AsArray() ?? [];
        var checks = rows.Select(row => Qualification.Check(
            row!["profile"]!.GetValue<string>(), row["trial"]!.GetValue<int>(), "warm", "completion",
            row["result"]!["state"]!.GetValue<string>(), row["result"]!["completionMs"]?.GetValue<double>(),
            budgets.WarmCompletionMs)).ToList();
        foreach (var row in rows.Where(r => r!["profile"]!.GetValue<string>() == "streamed"))
            checks.Add(Qualification.Check("streamed", row!["trial"]!.GetValue<int>(), "warm", "first-response-token",
                row["result"]!["state"]!.GetValue<string>(),
                row["result"]!["firstResponseTokenMs"]?.GetValue<double>(), budgets.WarmFirstResponseMs));
        var requested = report["requestedTrialsPerProfile"]?.GetValue<int>() ?? 0;
        var complete = requested >= 30 && rows.Count == requested * 2
            && Profiles(report["comparisonFactor"]?.GetValue<string>() == "streaming").All(profile =>
                rows.Count(r => r!["profile"]!.GetValue<string>() == profile) == requested);
        return Qualification.Assess(budgets, checks, complete);
    }

    private static string[] Profiles(bool compareStreaming) =>
        compareStreaming ? ["buffered", "streamed"] : ["runtime-default", "temperature-zero"];

    private static async Task<JsonObject> StreamReasonAsync(HttpClient client, Fixture fixture,
        CancellationToken cancellationToken, ProcessTreeObserver? observer)
    {
        var payload = JsonNode.Parse(await Program.CapturePayloadAsync(fixture))!.AsObject();
        var generation = await Measurements.StreamAsync(client, payload, cancellationToken, observer);
        var (answer, answerOnly) = Measurements.ParseAnswer(generation.Response);
        return new JsonObject
        {
            ["fixtureId"] = fixture.Id,
            ["state"] = generation.State,
            ["response"] = JsonSerializer.SerializeToNode(new Kora.Core.Dependencies.LocalModelResponse(answer, null)),
            ["quality"] = JsonSerializer.SerializeToNode(Quality.Score(fixture, answer, answerOnly)),
            ["completionMs"] = generation.CompletionMs,
            ["firstResponseTokenMs"] = generation.FirstResponseTokenMs,
            ["generation"] = JsonSerializer.SerializeToNode(generation),
            ["resources"] = JsonSerializer.SerializeToNode(generation.Resources),
        };
    }
}