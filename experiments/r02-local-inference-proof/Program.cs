using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Kora.Core.Commands;
using Kora.Core.Dependencies;
using Kora.Windows.Dependencies;

namespace R02Proof;

internal static class Program
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    public static async Task<int> Main(string[] args)
    {
        try
        {
            if (args.Length < 3 || args[1] != "--output"
                || args[0] is not ("self-test" or "observe" or "measure"))
                throw new ArgumentException("Usage: self-test|observe|measure --output FILE [--exclusive-runtime --trials 30]");
            var mode = args[0];
            var output = Path.GetFullPath(args[2]);
            if (File.Exists(output)) throw new IOException("Refusing to overwrite an existing evidence file.");
            var trials = 30;
            var exclusive = false;
            for (var i = 3; i < args.Length; i++)
            {
                if (args[i] == "--exclusive-runtime") exclusive = true;
                else if (args[i] == "--trials" && ++i < args.Length
                    && int.TryParse(args[i], out var count) && count is >= 30 and <= 1000) trials = count;
                else throw new ArgumentException("Unknown option or invalid trial count (30-1000 required).");
            }
            if (mode == "measure" && !exclusive)
                throw new ArgumentException("Measurement unloads/reloads the model. Use --exclusive-runtime only with consent on a dedicated test runtime.");

            var fixtures = JsonSerializer.Deserialize<Fixture[]>(
                await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "fixtures.json")), Json)
                ?? throw new InvalidDataException("No fixtures.");
            SelfTests.Require(fixtures.Length >= 6 && fixtures.Select(f => f.Id).Distinct().Count() == fixtures.Length,
                "Invalid fixture inventory.");
            SelfTests.Require(fixtures.All(f => f.Prompt.Length <= 4096), "Fixture exceeds production request bound.");
            var report = new JsonObject
            {
                ["schema"] = "Kora.R02.LocalInferenceProof.v1",
                ["startedUtc"] = DateTimeOffset.UtcNow.ToString("O"),
                ["mode"] = mode,
                ["machineSummary"] = JsonSerializer.SerializeToNode(new
                {
                    os = RuntimeInformation.OSDescription,
                    architecture = RuntimeInformation.OSArchitecture.ToString(),
                    logicalProcessors = Environment.ProcessorCount,
                    dotnet = RuntimeInformation.FrameworkDescription,
                    cpuIdentifier = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER"),
                }),
                ["candidate"] = JsonSerializer.SerializeToNode(new
                {
                    runtime = "Ollama",
                    version = WindowsOllamaSetupService.PackageVersion,
                    model = WindowsOllamaSetupService.Model,
                    modelDigest = WindowsOllamaSetupService.ModelDigest,
                    bootstrapDownloadEstimate = WindowsOllamaSetupService.ModelDownloadSize,
                    endpoint = Measurements.Endpoint,
                }),
                ["networkEvidence"] = "NOT ESTABLISHED: transport confinement is not OS egress containment; attach approved isolated-environment control and independent capture evidence.",
                ["referenceHardwareEvidence"] = "NOT ESTABLISHED: a machine inventory alone is not a CPU-floor trial with UI and normal local services running.",
            };
            var code = 0;
            try
            {
                if (mode == "self-test")
                    report["tests"] = JsonSerializer.SerializeToNode(await SelfTests.RunAsync(fixtures), Json);
                else if (mode == "observe")
                    code = await ObserveAsync(report, fixtures);
                else
                    code = await MeasureAsync(report, fixtures, trials);
            }
            catch (Exception exception) when (exception is IOException or JsonException
                or InvalidOperationException or HttpRequestException or TimeoutException or OperationCanceledException)
            {
                report["state"] = "Failed; retain partial evidence, do not interpret as acceptance.";
                report["error"] = exception.GetType().Name + ": " + exception.Message;
                Console.Error.WriteLine(report["error"]!.GetValue<string>());
                code = 3;
            }
            report["finishedUtc"] = DateTimeOffset.UtcNow.ToString("O");
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            await File.WriteAllTextAsync(output, report.ToJsonString(Json) + Environment.NewLine);
            Console.WriteLine($"{mode}: evidence written to {output}; exit {code}.");
            return code;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or JsonException
            or InvalidOperationException or HttpRequestException or TimeoutException)
        {
            Console.Error.WriteLine($"{exception.GetType().Name}: {exception.Message}");
            return 1;
        }
    }

    private static async Task<int> ObserveAsync(JsonObject report, Fixture[] fixtures)
    {
        using var transport = LocalTransport.Real();
        using var client = new HttpClient(transport) { Timeout = Timeout.InfiniteTimeSpan };
        var watch = Stopwatch.StartNew();
        var status = await new LocalInferenceDependencyProbe(client).ProbeAsync(CancellationToken.None);
        report["probe"] = JsonSerializer.SerializeToNode(new
        {
            state = status.Readiness.ToString(), status.Detail, completionMs = watch.Elapsed.TotalMilliseconds,
        });
        report["workflow"] = await ReasonAsync(client, fixtures[0]);
        report["calls"] = JsonSerializer.SerializeToNode(transport.Calls);
        report["fallback"] = "No remote client/provider, no redirect/proxy, no retry or asset acquisition path invoked.";
        return status.Readiness == DependencyReadiness.Ready
            && report["workflow"]!["state"]!.GetValue<string>() == "Completed"
            && report["workflow"]!["quality"]!["Passed"]!.GetValue<bool>() ? 0 : 2;
    }

    private static async Task<JsonObject> ReasonAsync(HttpClient client, Fixture fixture,
        CancellationToken cancellationToken = default)
    {
        var watch = Stopwatch.StartNew();
        await using var meter = new ResourceMeter();
        var result = new JsonObject { ["fixtureId"] = fixture.Id, ["path"] = "source-linked production reasoner; CPU-only transport override; otherwise production defaults" };
        try
        {
            var answer = await new WindowsOllamaReasoner(client, new BuiltInCommandCatalog())
                .ReasonAsync(fixture.Prompt, SelfTests.Context, cancellationToken);
            result["state"] = "Completed";
            result["response"] = JsonSerializer.SerializeToNode(answer, Json);
            result["quality"] = JsonSerializer.SerializeToNode(Quality.Score(fixture, answer.Answer,
                answer.Action is null && answer.GrantChange is null && answer.Question is null), Json);
        }
        catch (OperationCanceledException exception)
        {
            result["state"] = cancellationToken.IsCancellationRequested ? "Cancelled" : "Unavailable";
            result["error"] = exception.GetType().Name;
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidDataException
            or InvalidOperationException or JsonException or TimeoutException)
        {
            result["state"] = "Unavailable";
            result["error"] = exception.GetType().Name + ": " + exception.Message;
        }
        watch.Stop();
        result["completionMs"] = watch.Elapsed.TotalMilliseconds;
        result["firstTokenMs"] = null;
        result["firstTokenLimitation"] = "Production uses stream=false; first-token latency is not observable on this path.";
        result["resources"] = JsonSerializer.SerializeToNode(await meter.FinishAsync(), Json);
        return result;
    }

    internal static async Task<int> MeasureAsync(JsonObject report, Fixture[] fixtures, int trials,
        LocalTransport? transportOverride = null)
    {
        using var transport = transportOverride ?? LocalTransport.Real();
        using var client = new HttpClient(transport) { Timeout = TimeSpan.FromMinutes(2) };
        try
        {
            var version = await Measurements.GetAsync(client, "/api/version");
            report["runtimeMetadata"] = version;
            SelfTests.Require(version["version"]?.GetValue<string>() == WindowsOllamaSetupService.PackageVersion,
                "Installed runtime is not the pinned version; refusing generation.");
            var tags = await Measurements.GetAsync(client, "/api/tags");
            report["modelCatalogue"] = tags;
            var selected = tags["models"]!.AsArray().SingleOrDefault(m => m!["name"]?.GetValue<string>() == WindowsOllamaSetupService.Model);
            SelfTests.Require(string.Equals(selected?["digest"]?.GetValue<string>(),
                WindowsOllamaSetupService.ModelDigest, StringComparison.OrdinalIgnoreCase),
                "Pinned model is missing or changed; refusing generation.");
            report["modelMetadata"] = await Measurements.ShowAsync(client);
            var initialPs = await Measurements.GetAsync(client, "/api/ps");
            report["initialLoadedModels"] = initialPs;
            SelfTests.Require(initialPs["models"]!.AsArray().All(m => m!["name"]?.GetValue<string>() == WindowsOllamaSetupService.Model),
                "Another model is loaded; do not disturb a shared runtime.");
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or InvalidOperationException or TaskCanceledException)
        {
            report["state"] = "Blocked";
            report["blocker"] = exception.GetType().Name + ": " + exception.Message;
            report["calls"] = JsonSerializer.SerializeToNode(transport.Calls);
            return 2;
        }

        var records = new JsonArray();
        report["streamTrials"] = records;
        report["trialDefinition"] = $"Exactly {trials} cold and {trials} immediately paired warm trials; six rotating fixtures; model-unloaded cold (OS cache not flushed).";
        var allPassed = true;
        var cold = new List<GenerationResult>();
        var warm = new List<GenerationResult>();
        for (var i = 0; i < trials; i++)
        {
            var fixture = fixtures[i % fixtures.Length];
            // Capture the exact current production request without calling a real model.
            var productionPayload = await CapturePayloadAsync(fixture);
            foreach (var phase in new[] { "cold", "warm" })
            {
                if (phase == "cold") await Measurements.UnloadAsync(client);
                var payload = Measurements.StreamingPayload(productionPayload);
                var generation = await Measurements.StreamAsync(client, payload);
                var (answer, answerOnly) = Measurements.ParseAnswer(generation.Response);
                var quality = Quality.Score(fixture, answer, answerOnly);
                var ps = await Measurements.GetAsync(client, "/api/ps");
                var cpuOnly = Measurements.CpuOnly(ps);
                allPassed &= generation.State == "Completed" && quality.Passed && cpuOnly;
                records.Add(JsonSerializer.SerializeToNode(new
                {
                    trial = i + 1, phase, fixtureId = fixture.Id, request = payload,
                    generation, quality, cpuOnlyVerified = cpuOnly, loadedModels = ps,
                }, Json));
                (phase == "cold" ? cold : warm).Add(generation);
                if (transportOverride is null)
                    Console.WriteLine($"{phase} {i + 1}/{trials} {fixture.Id}: {generation.State}, quality={quality.Passed}");
            }
        }
        report["streamSummary"] = JsonSerializer.SerializeToNode(new
        {
            coldFirstTokenMs = Measurements.Distribution(cold.Where(r => r.FirstTokenMs.HasValue).Select(r => r.FirstTokenMs!.Value)),
            warmFirstTokenMs = Measurements.Distribution(warm.Where(r => r.FirstTokenMs.HasValue).Select(r => r.FirstTokenMs!.Value)),
            coldFirstResponseTokenMs = Measurements.Distribution(cold.Where(r => r.FirstResponseTokenMs.HasValue).Select(r => r.FirstResponseTokenMs!.Value)),
            warmFirstResponseTokenMs = Measurements.Distribution(warm.Where(r => r.FirstResponseTokenMs.HasValue).Select(r => r.FirstResponseTokenMs!.Value)),
            coldCompletionMs = Measurements.Distribution(cold.Where(r => r.State == "Completed").Select(r => r.CompletionMs)),
            warmCompletionMs = Measurements.Distribution(warm.Where(r => r.State == "Completed").Select(r => r.CompletionMs)),
            failedTrials = cold.Concat(warm).Count(r => r.State != "Completed"),
        }, Json);
        var production = new JsonArray();
        foreach (var fixture in fixtures)
        {
            var trial = await ReasonAsync(client, fixture);
            allPassed &= trial["state"]!.GetValue<string>() == "Completed"
                && trial["quality"]!["Passed"]!.GetValue<bool>();
            production.Add(trial);
        }
        report["productionWorkflowTrials"] = production;

        var contexts = new JsonArray();
        foreach (var (context, repetitions) in new[] { (1024, 100), (4096, 150), (8192, 1800), (32768, 7000), (1024, 4000) })
        {
            var prompt = "EARLY_MARKER=17\n" + string.Concat(Enumerable.Repeat("synthetic filler line. ", repetitions))
                + "\nLATE_MARKER=23\nReport both marker values and their sum. Use exactly {\"answer\":\"text\"}.";
            var payload = Measurements.StreamingPayload(await CapturePayloadAsync(fixtures[0]), context);
            payload["prompt"] = prompt;
            var generation = await Measurements.StreamAsync(client, payload);
            var (answer, shape) = Measurements.ParseAnswer(generation.Response);
            var expected = new Fixture("context", "", "", "17 + 23 = 40", [@"\b17\b", @"\b23\b", @"\b40\b"], [], false);
            var quality = Quality.Score(expected, answer, shape);
            if (context == 4096)
                allPassed &= generation.State == "Completed" && quality.Passed;
            contexts.Add(JsonSerializer.SerializeToNode(new
            {
                requestedNumCtx = context, promptCharacters = prompt.Length,
                withinProduction4096CharacterBound = prompt.Length <= 4096,
                generation, markerQuality = quality,
                limitation = "prompt_eval_count measures accepted tokens, not exact original-token count; inspect truncation and model context metadata. This is not proof of the entire advertised window.",
            }, Json));
        }
        report["contextTrials"] = contexts;
        var cancelPayload = Measurements.StreamingPayload(await CapturePayloadAsync(fixtures[0]));
        cancelPayload["prompt"] = "Write a very long synthetic numbered list from 1 through 10000. Answer only as a JSON answer.";
        cancelPayload["options"]!["num_predict"] = 8192;
        using (var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(200)))
        {
            var cancellation = await Measurements.StreamAsync(client, cancelPayload, cancel.Token);
            report["streamCancellation"] = JsonSerializer.SerializeToNode(cancellation, Json);
            allPassed &= cancellation.State == "Cancelled";
        }
        using (var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(200)))
        {
            var cancellation = await ReasonAsync(client, fixtures[0], cancel.Token);
            report["productionCancellation"] = cancellation;
            allPassed &= cancellation["state"]!.GetValue<string>() == "Cancelled";
        }
        report["postCancellationLoadedModels"] = await Measurements.GetAsync(client, "/api/ps");
        var recovery = await ReasonAsync(client, fixtures[0]);
        report["postCancellationRecovery"] = recovery;
        allPassed &= recovery["state"]!.GetValue<string>() == "Completed"
            && recovery["quality"]!["Passed"]!.GetValue<bool>();
        report["serverCancellationLimitation"] = "Client cancellation/recovery do not alone prove server computation stopped. Correlate independent process/egress capture and post-cancel resource observations.";
        var missingPayload = Measurements.StreamingPayload(await CapturePayloadAsync(fixtures[0]));
        missingPayload["model"] = "r02-synthetic-model-that-is-not-installed";
        var missing = await Measurements.StreamAsync(client, missingPayload);
        report["realMissingModelError"] = JsonSerializer.SerializeToNode(missing, Json);
        allPassed &= missing.State == "Unavailable";
        report["calls"] = JsonSerializer.SerializeToNode(transport.Calls);
        report["experimentRequests"] = JsonSerializer.SerializeToNode(transport.GenerationPayloads);
        report["automatedQualityAndClientChecksPassed"] = allPassed;
        report["state"] = "Evidence collected; not accepted. Human rubric, floor/network proof and cancellation containment still required.";
        return allPassed ? 0 : 3;
    }

    private static async Task<string> CapturePayloadAsync(Fixture fixture)
    {
        using var transport = new LocalTransport(new StubHandler((request, _) => Task.FromResult(
            StubHandler.Json(request.RequestUri!.AbsolutePath == "/api/tags"
                ? SelfTests.Tags : SelfTests.Decision("""{"answer":"payload capture"}""")))));
        using var client = new HttpClient(transport);
        await new WindowsOllamaReasoner(client, new BuiltInCommandCatalog())
            .ReasonAsync(fixture.Prompt, SelfTests.Context, CancellationToken.None);
        return transport.GenerationPayloads.Single();
    }
}
