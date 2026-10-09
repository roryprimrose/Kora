using System.Text.Json;
using System.Text.Json.Nodes;
using Kora.Windows.Dependencies;

namespace R02Proof;

internal static class NativeObserverControl
{
    public static readonly Fixture Fixture = new("native-observer-control",
        "What is 2 plus 2? Return exactly {\"answer\":\"4\"}.", "", "4", [@"^\s*4\s*$"], [], false);

    public static async Task<int> RunAsync(JsonObject report, QualificationOptions qualification,
        ProcessTreeObserver? observer = null, LocalTransport? transportOverride = null,
        Func<Task>? checkpoint = null, CancellationToken cancellationToken = default)
    {
        qualification.Validate();
        using var transport = transportOverride ?? LocalTransport.Real(observer, new(0));
        using var client = new HttpClient(transport) { Timeout = TimeSpan.FromMinutes(2) };
        if (transport.Observer is null) throw new InvalidOperationException("Observer control requires an admitted process root.");
        var identity = await Program.VerifyProfileAsync(client, qualification.RuntimeVersion, cancellationToken);
        report["runtimeMetadata"] = identity.Version;
        report["modelCatalogue"] = identity.Catalogue;
        report["modelMetadata"] = await Measurements.ShowAsync(client, cancellationToken);
        var initial = await Measurements.GetAsync(client, "/api/ps", cancellationToken);
        report["initialLoadedModels"] = initial;
        if (initial["models"] is not JsonArray models || models.Count != 0)
            throw new InvalidOperationException("Observer control requires an empty runtime; existing residency is not owned and will not be disturbed.");
        report["testedProfile"] = JsonSerializer.SerializeToNode(new
        {
            runtimeVersion = qualification.RuntimeVersion,
            qualification.ComparisonRuntime,
            productionPinsChanged = false,
            temperature = 0,
            seed = SamplingSettings.Seed,
            context = SamplingSettings.Context,
            requests = "Exactly two: model-unloaded cold buffered, then warm streamed; same synthetic prompt.",
            limitation = "Observer positive control only, not repeated trials, answer quality qualification, server cessation or resource-budget acceptance.",
        });
        var rows = new JsonArray();
        report["observerControlTrials"] = rows;
        try
        {
            var buffered = await Program.ReasonAsync(client, Fixture, cancellationToken, transport.Observer);
            await RecordAsync("buffered", "cold", buffered);
            cancellationToken.ThrowIfCancellationRequested();
            var payload = Measurements.StreamingPayload(await Program.CapturePayloadAsync(Fixture));
            var streamed = await Measurements.StreamAsync(client, payload, cancellationToken, transport.Observer);
            var (answer, shape) = Measurements.ParseAnswer(streamed.Response);
            await RecordAsync("streamed", "warm", new JsonObject
            {
                ["fixtureId"] = Fixture.Id,
                ["state"] = streamed.State,
                ["completionMs"] = streamed.CompletionMs,
                ["firstResponseTokenMs"] = streamed.FirstResponseTokenMs,
                ["answer"] = answer,
                ["quality"] = JsonSerializer.SerializeToNode(Quality.Score(Fixture, answer, shape)),
                ["generation"] = JsonSerializer.SerializeToNode(streamed),
                ["resources"] = JsonSerializer.SerializeToNode(streamed.Resources),
            });
            var observationPassed = rows.All(r => r!["result"]!["state"]!.GetValue<string>() == "Completed"
                && r["result"]!["quality"]!["Passed"]!.GetValue<bool>()
                && r["nativeRunnerObserved"]!.GetValue<bool>()
                && r["sampledCpuActivity"]!.GetValue<bool>()
                && r["result"]!["resources"]!["ObservationStatus"]!.GetValue<string>() != "Incomplete");
            report["observerControlPassed"] = observationPassed;
            report["state"] = observationPassed
                ? "Bounded native-observer positive control passed; qualification remains incomplete."
                : "Bounded native-observer positive control failed; retain evidence, do not infer resource qualification.";
            return observationPassed ? 0 : 3;
        }
        finally
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try
            {
                await Program.VerifyProfileAsync(client, qualification.RuntimeVersion, cleanup.Token);
                report["finalLoadedModels"] = await Measurements.UnloadAsync(client, cleanup.Token);
                report["cleanup"] = "Selected test model confirmed unloaded; pre-existing server and assets retained.";
            }
            catch (Exception exception) when (exception is HttpRequestException or JsonException or IOException
                or InvalidOperationException or OperationCanceledException)
            {
                report["cleanup"] = "Unknown: " + exception.GetType().Name + ": " + exception.Message;
                throw new InvalidOperationException("Could not confirm owned-model cleanup; inspect before any replay.", exception);
            }
            finally
            {
                report["calls"] = JsonSerializer.SerializeToNode(transport.Calls);
                report["experimentRequests"] = JsonSerializer.SerializeToNode(transport.GenerationPayloads);
                if (checkpoint is not null) await checkpoint();
            }
        }

        async Task RecordAsync(string path, string phase, JsonObject result)
        {
            var row = new JsonObject
            {
                ["trial"] = rows.Count + 1,
                ["fixtureId"] = Fixture.Id,
                ["path"] = path,
                ["phase"] = phase,
                ["request"] = transport.GenerationPayloads.LastOrDefault() is { } request ? JsonNode.Parse(request) : null,
                ["result"] = result,
            };
            rows.Add(row);
            if (checkpoint is not null) await checkpoint();
            var ps = await Measurements.GetAsync(client, "/api/ps", cancellationToken);
            var cpuOnly = Measurements.CpuOnly(ps);
            var contextVerified = cpuOnly
                && ps["models"]![0]!["context_length"]?.GetValue<int>() == SamplingSettings.Context;
            var resources = result["resources"] ?? throw new InvalidDataException("Missing control resource receipt.");
            var nativeSeen = resources["ObservedProcesses"]!.AsArray().Any(p =>
                p!["Name"]!.GetValue<string>().Equals("llama-server", StringComparison.OrdinalIgnoreCase)
                && p["WorkingSetBytes"]!.GetValue<long>() > 0 && p["CpuSeconds"]!.GetValue<double>() > 0);
            row["loadedModels"] = ps;
            row["cpuOnlyVerified"] = cpuOnly;
            row["contextVerified"] = contextVerified;
            row["nativeRunnerObserved"] = nativeSeen;
            row["sampledCpuActivity"] = resources["SampledCpuSeconds"]!.GetValue<double>() > 0;
            if (transportOverride is null)
                Console.WriteLine($"observer control {path} {phase}: {result["state"]}; nativeRunner={nativeSeen}, completion={result["completionMs"]!.GetValue<double>():F0} ms");
            if (checkpoint is not null) await checkpoint();
            if (!contextVerified || result["state"]!.GetValue<string>() != "Completed")
                throw new InvalidOperationException("Control generation or CPU/context evidence failed; no further generation is admitted.");
        }
    }

    public static JsonObject Evaluate(JsonObject report, TimingBudgets budgets)
    {
        if (report["observerControlTrials"] is not JsonArray rows)
            return Qualification.Assess(budgets, [], false);
        var checks = rows.Select(r => Qualification.Check(r!["path"]!.GetValue<string>(),
            r["trial"]!.GetValue<int>(), r["phase"]!.GetValue<string>(), "completion",
            r["result"]!["state"]!.GetValue<string>(), r["result"]!["completionMs"]?.GetValue<double>(),
            r["phase"]!.GetValue<string>() == "cold" ? budgets.ColdCompletionMs : budgets.WarmCompletionMs)).ToList();
        foreach (var row in rows.Where(r => r!["path"]!.GetValue<string>() == "streamed"))
            checks.Add(Qualification.Check("streamed", row!["trial"]!.GetValue<int>(), "warm", "first-response-token",
                row["result"]!["state"]!.GetValue<string>(), row["result"]!["firstResponseTokenMs"]?.GetValue<double>(),
                budgets.WarmFirstResponseMs));
        return Qualification.Assess(budgets, checks, rows.Count == 2
            && rows[0]!["path"]!.GetValue<string>() == "buffered"
            && rows[1]!["path"]!.GetValue<string>() == "streamed");
    }
}
