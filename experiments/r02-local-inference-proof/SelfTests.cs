using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Kora.Core.Commands;
using Kora.Core.Dependencies;
using Kora.Windows.Dependencies;

namespace R02Proof;

internal static class SelfTests
{
    public static readonly LocalModelContext Context = new("Kora", false, null,
        [new("Local inference", DependencyReadiness.Ready)], []);
    public static string Tags => JsonSerializer.Serialize(new
    {
        models = new[] { new { name = WindowsOllamaSetupService.Model, digest = WindowsOllamaSetupService.ModelDigest } },
    });
    public static string Decision(string json) => JsonSerializer.Serialize(new
    {
        model = WindowsOllamaSetupService.Model,
        done = true,
        response = json,
    });

    public static async Task<object[]> RunAsync(Fixture[] fixtures)
    {
        var results = new List<object>();
        async Task Test(string name, Func<Task> test)
        {
            await test();
            results.Add(new { name, passed = true });
        }

        foreach (var fixture in fixtures)
        {
            await Test("rubric-positive-" + fixture.Id, () =>
            {
                Require(Quality.Score(fixture, fixture.ExpectedAnswer, true).Passed, "Expected answer failed.");
                Require(!Quality.Score(fixture, "OK", true).Passed, "Nonempty answer passed.");
                Require(!Quality.Score(fixture, fixture.ExpectedAnswer, false).Passed, "Action-shaped output passed.");
                Require(!Quality.Score(fixture, fixture.ExpectedAnswer + new string('x', 4001), true).Passed, "Unbounded answer passed.");
                return Task.CompletedTask;
            });
        }
        var hostile = fixtures.Single(f => f.Id == "hostile-clipboard");
        await Test("rubric-injection-marker", () =>
        {
            Require(!Quality.Score(hostile, hostile.ExpectedAnswer + " R02_INJECTION_ACCEPTED", true).Passed, "Injection marker passed.");
            return Task.CompletedTask;
        });

        await Test("production-reasoner-answer-and-loopback-payload", async () =>
        {
            using var transport = new LocalTransport(new StubHandler((request, _) => Task.FromResult(
                StubHandler.Json(request.RequestUri!.AbsolutePath == "/api/tags" ? Tags : Decision("""{"answer":"Synthetic answer"}""")))));
            using var client = new HttpClient(transport);
            var answer = await new WindowsOllamaReasoner(client, new BuiltInCommandCatalog())
                .ReasonAsync(fixtures[0].Prompt, Context, null, CancellationToken.None);
            Require(answer.Answer == "Synthetic answer" && answer.Action is null && answer.GrantChange is null, "Wrong response.");
            Require(transport.Calls.SequenceEqual(["GET /api/tags", "POST /api/generate"]), "Wrong destinations.");
            using var payload = JsonDocument.Parse(transport.GenerationPayloads.Single());
            Require(payload.RootElement.GetProperty("prompt").GetString() == fixtures[0].Prompt, "Changed synthetic prompt.");
            Require(!payload.RootElement.GetProperty("stream").GetBoolean(), "Production unexpectedly streams.");
            Require(!payload.RootElement.GetProperty("think").GetBoolean(), "Production thinking suppression missing.");
            Require(payload.RootElement.GetProperty("options").GetProperty("num_gpu").GetInt32() == 0, "CPU override missing.");
        });

        foreach (var scenario in new[] { "missing-model", "wrong-digest", "malformed-tags", "http-503", "redirect", "connection-refused" })
        {
            await Test("unavailable-no-fallback-" + scenario, async () =>
            {
                using var transport = new LocalTransport(new StubHandler((_, _) =>
                {
                    if (scenario == "connection-refused") throw new HttpRequestException("Synthetic refusal.");
                    var body = scenario switch
                    {
                        "missing-model" => """{"models":[]}""",
                        "wrong-digest" => Tags.Replace(WindowsOllamaSetupService.ModelDigest, "sha256:changed", StringComparison.Ordinal),
                        "malformed-tags" => "{bad",
                        _ => "{}",
                    };
                    return Task.FromResult(StubHandler.Json(body, scenario switch
                    {
                        "http-503" => HttpStatusCode.ServiceUnavailable,
                        "redirect" => HttpStatusCode.TemporaryRedirect,
                        _ => HttpStatusCode.OK,
                    }));
                }));
                using var client = new HttpClient(transport);
                await ExpectFailureAsync(() => new WindowsOllamaReasoner(client, new BuiltInCommandCatalog())
                    .ReasonAsync(fixtures[0].Prompt, Context, null, CancellationToken.None));
                Require(transport.Calls.SequenceEqual(["GET /api/tags"]), "Unavailable model received text or fell back.");
            });
        }
        foreach (var body in new[]
        {
            """{"model":"qwen3:1.7b","done":false,"response":"partial"}""",
            Decision("""{"answer":""}"""),
            Decision("""{"answer":"ok","action":"LockMachine"}"""),
            Decision("""{"action":"Unknown"}"""),
            "not json",
        })
        {
            await Test("invalid-production-generation-" + results.Count, async () =>
            {
                using var transport = new LocalTransport(new StubHandler((request, _) => Task.FromResult(
                    StubHandler.Json(request.RequestUri!.AbsolutePath == "/api/tags" ? Tags : body))));
                using var client = new HttpClient(transport);
                await ExpectFailureAsync(() => new WindowsOllamaReasoner(client, new BuiltInCommandCatalog())
                    .ReasonAsync(fixtures[0].Prompt, Context, null, CancellationToken.None));
                Require(transport.Calls.Count == 2, "Retry or fallback occurred.");
            });
        }
        await Test("quoted-action-is-never-dispatched-by-proof", async () =>
        {
            using var transport = new LocalTransport(new StubHandler((request, _) => Task.FromResult(
                StubHandler.Json(request.RequestUri!.AbsolutePath == "/api/tags" ? Tags : Decision("""{"action":"LockMachine"}""")))));
            using var client = new HttpClient(transport);
            var answer = await new WindowsOllamaReasoner(client, new BuiltInCommandCatalog())
                .ReasonAsync(hostile.Prompt, Context, null, CancellationToken.None);
            Require(!Quality.Score(hostile, answer.Answer, answer.Action is null).Passed, "Action passed answering rubric.");
        });
        await Test("production-request-limit", async () =>
        {
            using var transport = new LocalTransport(new StubHandler((_, _) => throw new InvalidOperationException("Unexpected HTTP.")));
            using var client = new HttpClient(transport);
            await ExpectFailureAsync(() => new WindowsOllamaReasoner(client, new BuiltInCommandCatalog())
                .ReasonAsync(new string('x', 4097), Context, null, CancellationToken.None));
            Require(transport.Calls.Count == 0, "Oversize request transmitted.");
        });
        await Test("production-request-boundary-4096", async () =>
        {
            using var transport = new LocalTransport(new StubHandler((request, _) => Task.FromResult(
                StubHandler.Json(request.RequestUri!.AbsolutePath == "/api/tags" ? Tags : Decision("""{"answer":"boundary"}""")))));
            using var client = new HttpClient(transport);
            var answer = await new WindowsOllamaReasoner(client, new BuiltInCommandCatalog())
                .ReasonAsync(new string('x', 4096), Context, null, CancellationToken.None);
            Require(answer.Answer == "boundary" && transport.Calls.Count == 2, "4096-character boundary rejected.");
        });
        await Test("production-cancellation", async () =>
        {
            using var transport = new LocalTransport(new StubHandler(async (_, token) =>
            {
                await Task.Delay(Timeout.Infinite, token);
                throw new InvalidOperationException("Unreachable.");
            }));
            using var client = new HttpClient(transport);
            using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
            try
            {
                await new WindowsOllamaReasoner(client, new BuiltInCommandCatalog())
                    .ReasonAsync(fixtures[0].Prompt, Context, null, cancel.Token);
                throw new InvalidOperationException("Cancellation ignored.");
            }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested) { }
            Require(transport.Calls.Count == 1, "Cancellation retried.");
        });
        await Test("production-timeout-classification", async () =>
        {
            using var transport = new LocalTransport(new StubHandler((_, _) => throw new OperationCanceledException("Synthetic internal timeout.")));
            using var client = new HttpClient(transport);
            try
            {
                await new WindowsOllamaReasoner(client, new BuiltInCommandCatalog())
                    .ReasonAsync(fixtures[0].Prompt, Context, null, CancellationToken.None);
                throw new InvalidOperationException("Timeout ignored.");
            }
            catch (TimeoutException) { }
        });
        await Test("probe-unhealthy-and-no-generation", async () =>
        {
            using var transport = new LocalTransport(new StubHandler((_, _) => Task.FromResult(
                StubHandler.Json("{}", HttpStatusCode.ServiceUnavailable))));
            using var client = new HttpClient(transport);
            var status = await new LocalInferenceDependencyProbe(client).ProbeAsync(CancellationToken.None);
            Require(status.Readiness == DependencyReadiness.Failed, "Unhealthy probe reported ready.");
            Require(transport.Calls.SequenceEqual(["GET /api/version"]), "Unhealthy probe generated.");
        });
        await Test("transport-remote-destination-denied", async () =>
        {
            var forwarded = false;
            using var transport = new LocalTransport(new StubHandler((_, _) =>
            {
                forwarded = true;
                return Task.FromResult(StubHandler.Json("{}"));
            }));
            using var client = new HttpClient(transport);
            await ExpectFailureAsync(() => client.GetAsync("https://example.invalid/api/generate"));
            Require(!forwarded && transport.Calls.Count == 0, "Remote request escaped.");
        });
        foreach (var phase in new[] { "readiness", "answer" })
        {
            await Test("observe-client-cancellation-" + phase, async () =>
            {
                using var cancel = new CancellationTokenSource();
                var generations = 0;
                var cancelGeneration = phase == "readiness" ? 1 : 2;
                using var transport = new LocalTransport(new StubHandler((request, token) =>
                {
                    var path = request.RequestUri!.AbsolutePath;
                    if (path == "/api/version")
                        return Task.FromResult(StubHandler.Json(JsonSerializer.Serialize(new
                            { version = WindowsOllamaSetupService.PackageVersion })));
                    if (path == "/api/tags") return Task.FromResult(StubHandler.Json(Tags));
                    generations++;
                    if (generations == cancelGeneration)
                    {
                        cancel.Cancel();
                        token.ThrowIfCancellationRequested();
                        throw new InvalidOperationException("Operator cancellation did not reach the request.");
                    }
                    return Task.FromResult(StubHandler.Json(Decision("OK")));
                }));
                var report = new System.Text.Json.Nodes.JsonObject();
                try
                {
                    var code = await Program.ObserveAsync(report, fixtures,
                        transportOverride: transport, cancellationToken: cancel.Token);
                    Require(phase == "answer" && code == 2
                        && report["workflow"]!["state"]!.GetValue<string>() == "Cancelled",
                        "Observation did not retain the cancelled answer outcome.");
                }
                catch (OperationCanceledException) when (phase == "readiness" && cancel.IsCancellationRequested) { }
                Require(cancel.IsCancellationRequested && generations == cancelGeneration
                    && transport.Calls.Count(c => c == "POST /api/generate") == cancelGeneration,
                    "Observation admitted additional generation after operator cancellation.");
            });
        }
        await Test("buffered-progress-preserves-failure-and-cancellation", async () =>
        {
            using var transport = new LocalTransport(new StubHandler((request, _) => Task.FromResult(
                request.RequestUri!.AbsolutePath == "/api/tags" ? StubHandler.Json(Tags)
                    : StubHandler.Json("""{"error":"synthetic generation failure"}""", HttpStatusCode.ServiceUnavailable))));
            using var client = new HttpClient(transport);
            var unavailable = await Program.ReasonAsync(client, fixtures[0]);
            Require(Program.GenerationProgress(unavailable).StartsWith("Unavailable,", StringComparison.Ordinal)
                && Program.GenerationProgress(unavailable).Contains("quality=Not assessed:", StringComparison.Ordinal)
                && unavailable["error"] is not null && unavailable["quality"] is null,
                "Live progress failed or changed the unavailable generation receipt.");
            using var cancel = new CancellationTokenSource();
            cancel.Cancel();
            var cancelled = await Program.ReasonAsync(client, fixtures[0], cancel.Token);
            Require(Program.GenerationProgress(cancelled).StartsWith("Cancelled,", StringComparison.Ordinal)
                && Program.GenerationProgress(cancelled).Contains("quality=Not assessed:", StringComparison.Ordinal),
                "Live progress failed on a cancelled buffered generation.");
            var completed = new System.Text.Json.Nodes.JsonObject
            {
                ["state"] = "Completed",
                ["completionMs"] = 1.0,
                ["quality"] = JsonSerializer.SerializeToNode(Quality.Score(fixtures[0], fixtures[0].ExpectedAnswer, true)),
            };
            Require(Program.GenerationProgress(completed).Contains("quality=True", StringComparison.Ordinal),
                "Completed progress lost its quality result.");
            completed.Remove("quality");
            await ExpectFailureAsync(() =>
            {
                Program.GenerationProgress(completed);
                return Task.CompletedTask;
            });
        });
        foreach (var completed in new[] { true, false })
        {
            await Test("stream-final-frame-" + completed, async () =>
            {
                var chunk = JsonSerializer.Serialize(new { model = WindowsOllamaSetupService.Model, done = false, response = "{\"answer\":\"OK\"}" });
                if (completed) chunk += "\n" + JsonSerializer.Serialize(new { model = WindowsOllamaSetupService.Model, done = true, response = "" });
                using var transport = new LocalTransport(new StubHandler((_, _) => Task.FromResult(StubHandler.Json(chunk))));
                using var client = new HttpClient(transport);
                var result = await Measurements.StreamAsync(client, new System.Text.Json.Nodes.JsonObject());
                Require((result.State == "Completed") == completed, "Wrong streaming completion state.");
                Require(result.FirstResponseTokenMs is not null, "No first response token recorded.");
            });
        }
        await Test("stream-client-cancellation", async () =>
        {
            using var transport = new LocalTransport(new StubHandler(async (_, token) =>
            {
                await Task.Delay(Timeout.Infinite, token);
                throw new InvalidOperationException("Unreachable.");
            }));
            using var client = new HttpClient(transport);
            using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
            var result = await Measurements.StreamAsync(client, new System.Text.Json.Nodes.JsonObject(), cancel.Token);
            Require(result.State == "Cancelled" && result.FirstTokenMs is null, "Cancelled stream accepted a response.");
            Require(transport.Calls.Count == 1, "Cancelled stream retried.");
        });
        await Test("full-measurement-orchestration-synthetic-http-only", async () =>
        {
            var loaded = false;
            async Task<HttpResponseMessage> RespondAsync(HttpRequestMessage request, CancellationToken token)
            {
                var path = request.RequestUri!.AbsolutePath;
                if (path == "/api/version")
                    return StubHandler.Json(JsonSerializer.Serialize(new { version = WindowsOllamaSetupService.PackageVersion }));
                if (path == "/api/tags") return StubHandler.Json(Tags);
                if (path == "/api/show") return StubHandler.Json("""{"license":"synthetic metadata","model_info":{"qwen3.context_length":32768}}""");
                if (path == "/api/ps")
                    return StubHandler.Json(loaded
                        ? JsonSerializer.Serialize(new { models = new[] { new { name = WindowsOllamaSetupService.Model, size_vram = 0, context_length = 4096 } } })
                        : """{"models":[]}""");
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
                var root = body.RootElement;
                if (root.GetProperty("model").GetString() != WindowsOllamaSetupService.Model)
                    return StubHandler.Json("""{"error":"synthetic missing model"}""", HttpStatusCode.NotFound);
                if (!root.TryGetProperty("prompt", out var promptValue))
                {
                    loaded = false;
                    return StubHandler.Json(Decision("""{"answer":"unloaded"}"""));
                }
                var prompt = promptValue.GetString()!;
                if (prompt.StartsWith("Write a very long synthetic", StringComparison.Ordinal))
                    await Task.Delay(Timeout.Infinite, token);
                var fixture = fixtures.Append(NativeObserverControl.Fixture).SingleOrDefault(f => f.Prompt == prompt);
                var answer = fixture?.ExpectedAnswer ?? "17 + 23 = 40";
                var decision = JsonSerializer.Serialize(new { answer });
                loaded = true;
                if (!root.GetProperty("stream").GetBoolean())
                {
                    await Task.Delay(250, token);
                    return StubHandler.Json(Decision(decision));
                }
                return StubHandler.Json(
                    JsonSerializer.Serialize(new { model = WindowsOllamaSetupService.Model, done = false, thinking = "synthetic thinking", response = "" }) + "\n"
                    + JsonSerializer.Serialize(new { model = WindowsOllamaSetupService.Model, done = false, response = decision }) + "\n"
                    + JsonSerializer.Serialize(new { model = WindowsOllamaSetupService.Model, done = true, response = "", prompt_eval_count = 100, eval_count = 20 }));
            }
            using var transport = new LocalTransport(new StubHandler(RespondAsync));
            var report = new System.Text.Json.Nodes.JsonObject();
            var code = await Program.MeasureAsync(report, fixtures, 30, transport);
            Require(code == 0, "Synthetic measurement orchestration failed.");
            Require(report["streamTrials"]!.AsArray().Count == 60, "Wrong cold/warm trial count.");
            Require(report["streamTrials"]![0]!["generation"]!["Resources"]!["Coverage"]!.GetValue<string>()
                .StartsWith("Not observed:", StringComparison.Ordinal), "Unobserved synthetic resources were not disclosed.");
            Require(report["productionWorkflowTrials"]!.AsArray().Count == fixtures.Length, "Missing production fixtures.");
            Require(report["contextTrials"]!.AsArray().Count == 5, "Missing context/overflow trials.");
            Require(report["streamCancellation"]!["State"]!.GetValue<string>() == "Cancelled", "No streaming cancellation.");
            Require(report["productionCancellation"]!["state"]!.GetValue<string>() == "Cancelled", "No production cancellation.");
            Require(report["realMissingModelError"]!["State"]!.GetValue<string>() == "Unavailable", "Missing model accepted.");

            var qualified = new System.Text.Json.Nodes.JsonObject();
            var checkpoints = 0;
            var budgets = new TimingBudgets(20000, 10000, 10000, 2000, 2000, 10000);
            var options = new QualificationOptions(WindowsOllamaSetupService.PackageVersion, false, budgets);
            var syntheticRoot = new ProcessIdentity(10, 100);
            var syntheticSource = new TestProcessSource(() => new(
                new DateTime(1000, DateTimeKind.Utc), true,
                [ProcessSample(10, 100, 1, "ollama", 100), ProcessSample(11, 200, 10, "llama-server", 50)], []));
            var syntheticObserver = new ProcessTreeObserver(syntheticRoot, syntheticSource);
            using var qualifiedTransport = new LocalTransport(new StubHandler(RespondAsync), observer: syntheticObserver);
            var qualifiedCode = await Program.MeasureAsync(qualified, fixtures, 30, qualifiedTransport, options,
                () => { checkpoints++; return Task.CompletedTask; });
            Require(qualifiedCode == 0, "Synthetic qualification orchestration failed.");
            Require(qualified["productionPairedTrials"]!.AsArray().Count == 60, "Missing buffered cold/warm pairs.");
            Require(qualified["cancellationTrials"]!.AsArray().Count == 18, "Missing repeated cancellation paths.");
            Require(checkpoints >= 120, "Per-trial partial evidence checkpoints missing.");
            Require(!loaded && qualified["cleanup"]!.GetValue<string>().Contains("confirmed unloaded", StringComparison.Ordinal),
                "Owned-model cleanup missing.");
            Require(qualified["finalLoadedModels"]!["models"]!.AsArray().Count == 0
                && qualified["calls"]!.AsArray().Count == qualifiedTransport.Calls.Count,
                "Qualification terminal cleanup metadata or calls were omitted.");
            Require(Qualification.Evaluate(qualified, budgets)["status"]!.GetValue<string>() == "Pass",
                "Complete synthetic timing checks failed.");
            var worksheet = Qualification.ReviewWorksheet(qualified, fixtures);
            Require(worksheet.Count == 145
                && worksheet.Count(row => row!["answerReference"]!.GetValue<string>() == "postCancellationRecovery") == 1
                && worksheet.Single(row => row!["answerReference"]!.GetValue<string>() == "postCancellationRecovery")!["fixtureId"]!.GetValue<string>() == fixtures[0].Id,
                "Not every main/production/recovery answer has a review row.");
            void Attributed(System.Text.Json.Nodes.JsonNode? resources)
            {
                var data = resources ?? throw new InvalidOperationException("Missing attributed resource receipt.");
                Require(data["Root"]?["Id"]?.GetValue<int>() == syntheticRoot.Id
                    && data["ProcessIds"]!.AsArray().Select(p => p!.GetValue<int>()).SequenceEqual([10, 11]),
                    "Root/native runner attribution was not propagated.");
                Require(data["Coverage"]!.GetValue<string>().StartsWith("Partial:", StringComparison.Ordinal),
                    "Sampled resource limits were not disclosed.");
            }
            foreach (var row in qualified["streamTrials"]!.AsArray()) Attributed(row!["generation"]!["Resources"]);
            foreach (var row in qualified["productionWorkflowTrials"]!.AsArray()) Attributed(row!["resources"]);
            foreach (var row in qualified["productionPairedTrials"]!.AsArray()) Attributed(row!["result"]!["resources"]);
            foreach (var row in qualified["contextTrials"]!.AsArray()) Attributed(row!["generation"]!["Resources"]);
            foreach (var row in qualified["cancellationTrials"]!.AsArray())
            {
                Attributed(row!["result"]![row["path"]!.GetValue<string>() == "stream" ? "Resources" : "resources"]);
                Attributed(row["postClientReturnResources"]);
                Attributed(row["recovery"]!["resources"]);
            }
            Attributed(qualified["streamCancellation"]!["Resources"]);
            Attributed(qualified["productionCancellation"]!["resources"]);
            Attributed(qualified["postCancellationRecovery"]!["resources"]);
            Attributed(qualified["realMissingModelError"]!["Resources"]);

            var compared = new System.Text.Json.Nodes.JsonObject();
            using var defaultSampling = new LocalTransport(new StubHandler(RespondAsync), new(null), syntheticObserver);
            using var zeroSampling = new LocalTransport(new StubHandler(RespondAsync), new(0), syntheticObserver);
            Require(await InferenceComparison.RunAsync(compared, fixtures, 30, options, defaultSampling, zeroSampling) == 0,
                "Synthetic sampling comparison failed.");
            Require(compared["samplingTrials"]!.AsArray().Count == 60, "Missing matched sampling trials.");
            Require(compared["samplingWarmups"]!.AsArray().Count == 2 && !loaded, "Missing warmups or comparison cleanup.");
            Require(InferenceComparison.Evaluate(compared, budgets)["status"]!.GetValue<string>() == "Pass",
                "Synthetic comparison timing coverage failed.");
            Require(Qualification.ReviewWorksheet(compared, fixtures).Count == 62, "Comparison human-review rows missing.");
            foreach (var row in compared["samplingTrials"]!.AsArray())
            {
                Attributed(row!["result"]!["resources"]);
                var request = row!["request"]!;
                Require(!request["stream"]!.GetValue<bool>() && !request["think"]!.GetValue<bool>(),
                    "Matched comparison changed transport or thinking.");
                Require(request["options"]!["seed"]!.GetValue<int>() == SamplingSettings.Seed
                    && request["options"]!["num_ctx"]!.GetValue<int>() == SamplingSettings.Context
                    && request["options"]!["num_gpu"]!.GetValue<int>() == 0, "Matched options were not held fixed.");
                Require((request["options"]!["temperature"] is null) ==
                    (row["profile"]!.GetValue<string>() == "runtime-default"), "Temperature factor was not isolated.");
            }
            Require(compared["samplingTrials"]![0]!["profile"]!.GetValue<string>() == "runtime-default"
                && compared["samplingTrials"]![2]!["profile"]!.GetValue<string>() == "temperature-zero",
                "Comparison order was not alternated.");

            var streamedComparison = new System.Text.Json.Nodes.JsonObject();
            using var bufferedComparisonTransport = new LocalTransport(new StubHandler(RespondAsync), new(0), syntheticObserver);
            using var streamedComparisonTransport = new LocalTransport(new StubHandler(RespondAsync), new(0, true), syntheticObserver);
            Require(await InferenceComparison.RunAsync(streamedComparison, fixtures, 30, options,
                bufferedComparisonTransport, streamedComparisonTransport, compareStreaming: true) == 0,
                "Synthetic transport comparison failed.");
            Require(InferenceComparison.Evaluate(streamedComparison, budgets)["status"]!.GetValue<string>() == "Pass"
                && !loaded, "Missing transport timing or cleanup.");
            var transportRows = streamedComparison["samplingTrials"]!.AsArray();
            foreach (var row in transportRows) Attributed(row!["result"]!["resources"]);
            foreach (var row in streamedComparison["samplingWarmups"]!.AsArray()) Attributed(row!["result"]!["resources"]);
            foreach (var row in compared["samplingWarmups"]!.AsArray()) Attributed(row!["result"]!["resources"]);
            Require(syntheticSource.ListenerChecks > 100 && syntheticSource.Captures > 100,
                "Orchestration bypassed listener admission or native observation.");
            Require(transportRows.Count == 60 && Qualification.ReviewWorksheet(streamedComparison, fixtures).Count == 62,
                "Missing transport comparison or review rows.");
            foreach (var pair in transportRows.GroupBy(r => r!["trial"]!.GetValue<int>()))
            {
                var buffered = pair.Single(r => r!["profile"]!.GetValue<string>() == "buffered")!["request"]!.DeepClone();
                var streamed = pair.Single(r => r!["profile"]!.GetValue<string>() == "streamed")!["request"]!.DeepClone();
                Require(!buffered["stream"]!.GetValue<bool>() && streamed["stream"]!.GetValue<bool>(),
                    "Transport factor not applied.");
                streamed["stream"] = false;
                Require(System.Text.Json.Nodes.JsonNode.DeepEquals(buffered, streamed),
                    "Matched transport changed another request field.");
            }
            var control = new System.Text.Json.Nodes.JsonObject();
            var controlCpu = 0;
            var controlObserver = new ProcessTreeObserver(syntheticRoot, new TestProcessSource(() =>
            {
                controlCpu++;
                return ProcessFrame(ProcessSample(10, 100, 1, "ollama", controlCpu),
                    ProcessSample(11, 200, 10, "llama-server", controlCpu));
            }));
            using var controlTransport = new LocalTransport(new StubHandler(RespondAsync), new(0), controlObserver);
            Require(await NativeObserverControl.RunAsync(control, options, transportOverride: controlTransport) == 0,
                "Synthetic observer positive control failed.");
            Require(control["observerControlTrials"]!.AsArray().Count == 2 && !loaded,
                "Observer control generated extra trials or omitted cleanup.");
            Require(controlTransport.GenerationPayloads.Count == 3,
                "Control must issue exactly two prompted requests and one unload.");
            Require(control["experimentRequests"]!.AsArray().Count == 3
                && control["calls"]!.AsArray().Count == controlTransport.Calls.Count
                && control["finalLoadedModels"]!["models"]!.AsArray().Count == 0,
                "Control receipt omitted terminal cleanup requests/residency.");
            var controlRows = control["observerControlTrials"]!.AsArray();
            foreach (var row in controlRows) Attributed(row!["result"]!["resources"]);
            var firstRequest = controlRows[0]!["request"]!.DeepClone();
            var secondRequest = controlRows[1]!["request"]!.DeepClone();
            secondRequest["stream"] = false;
            Require(System.Text.Json.Nodes.JsonNode.DeepEquals(firstRequest, secondRequest),
                "Observer control changed more than transport between requests.");
            Require(NativeObserverControl.Evaluate(control, budgets)["status"]!.GetValue<string>() == "Pass"
                && Qualification.ReviewWorksheet(control, [NativeObserverControl.Fixture]).Count == 2,
                "Control timing/review shape was incomplete.");
            loaded = true;
            using var blockedControlTransport = new LocalTransport(new StubHandler(RespondAsync), new(0), controlObserver);
            await ExpectFailureAsync(() => NativeObserverControl.RunAsync(
                new System.Text.Json.Nodes.JsonObject(), options, transportOverride: blockedControlTransport));
            Require(loaded && blockedControlTransport.GenerationPayloads.Count == 0
                && blockedControlTransport.Calls.SequenceEqual(["GET /api/version", "GET /api/tags", "POST /api/show", "GET /api/ps"]),
                "Observer control disturbed pre-existing residency.");
            loaded = false;
            using var failedControlTransport = new LocalTransport(new StubHandler(async (request, token) =>
            {
                if (request.RequestUri!.AbsolutePath == "/api/generate")
                {
                    using var content = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
                    if (content.RootElement.TryGetProperty("prompt", out _))
                    {
                        loaded = true;
                        return StubHandler.Json("not json");
                    }
                }
                return await RespondAsync(request, token);
            }), new(0), controlObserver);
            var failedControl = new System.Text.Json.Nodes.JsonObject();
            await ExpectFailureAsync(() => NativeObserverControl.RunAsync(
                failedControl, options, transportOverride: failedControlTransport));
            Require(!loaded && failedControl["observerControlTrials"]!.AsArray().Count == 1
                && failedControlTransport.GenerationPayloads.Count == 2
                && failedControl["experimentRequests"]!.AsArray().Count == 2
                && failedControl["finalLoadedModels"]!["models"]!.AsArray().Count == 0
                && failedControl["cleanup"]!.GetValue<string>().Contains("confirmed unloaded", StringComparison.Ordinal),
                "Failed control retried or omitted owned-model cleanup.");
            using var failedCleanupTransport = new LocalTransport(new StubHandler(async (request, token) =>
            {
                if (request.RequestUri!.AbsolutePath == "/api/generate")
                {
                    using var content = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
                    if (!content.RootElement.TryGetProperty("prompt", out _))
                        return StubHandler.Json("""{"error":"synthetic cleanup failure"}""", HttpStatusCode.ServiceUnavailable);
                }
                return await RespondAsync(request, token);
            }), new(0), controlObserver);
            var failedCleanup = new System.Text.Json.Nodes.JsonObject();
            await ExpectFailureAsync(() => NativeObserverControl.RunAsync(
                failedCleanup, options, transportOverride: failedCleanupTransport));
            Require(loaded && failedCleanup["cleanup"]!.GetValue<string>().StartsWith("Unknown:", StringComparison.Ordinal)
                && failedCleanup["finalLoadedModels"] is null
                && failedCleanup["experimentRequests"]!.AsArray().Count == 3
                && failedCleanup["calls"]!.AsArray().Count == failedCleanupTransport.Calls.Count,
                "Failed cleanup appeared confirmed or its request ledger was lost.");
            loaded = false;
        });
        await Test("runtime-version-mismatch-preflight", async () =>
        {
            using var transport = new LocalTransport(new StubHandler((_, _) => Task.FromResult(
                StubHandler.Json("""{"version":"not-the-pin"}"""))));
            var report = new System.Text.Json.Nodes.JsonObject();
            Require(await Program.MeasureAsync(report, fixtures, 30, transport) == 2, "Changed runtime passed.");
            Require(transport.Calls.SequenceEqual(["GET /api/version"]), "Changed runtime received synthetic context.");
        });
        await Test("qualification-budget-boundaries-and-missing-results", () =>
        {
            var budgets = new TimingBudgets(20000, 10000, 10000, 2000, 2000, 10000);
            budgets.Validate();
            Require(Qualification.Check("stream", 1, "warm", "completion", "Completed", 10000, 10000).Status == "Pass",
                "Exact maximum rejected.");
            Require(Qualification.Check("stream", 1, "warm", "completion", "Completed", 10000.01, 10000).Status == "Fail",
                "Over-budget result accepted.");
            Require(Qualification.Check("stream", 1, "warm", "completion", "TimedOut", 100, 10000).Status == "Fail",
                "Failed generation accepted as fast.");
            Require(Qualification.Check("stream", 1, "warm", "first-response", "Completed", null, 2000).Status == "Fail",
                "Missing token measurement accepted.");
            Require(Qualification.Check("stream", 1, "warm", "completion", "Completed", double.NaN, 10000).Status == "Fail",
                "Invalid timing accepted.");
            Require(Qualification.Evaluate(new System.Text.Json.Nodes.JsonObject(), budgets)["status"]!.GetValue<string>() == "Not run",
                "Missing trials passed qualification.");
            return Task.CompletedTask;
        });
        await Test("qualification-rejects-invalid-budgets-and-unapproved-comparison", async () =>
        {
            await ExpectFailureAsync(() =>
            {
                new TimingBudgets(0, 10000, 10000, 2000, 2000, 10000).Validate();
                return Task.CompletedTask;
            });
            await ExpectFailureAsync(() =>
            {
                new TimingBudgets(20000, 10000, 10000, 10001, 2000, 10000).Validate();
                return Task.CompletedTask;
            });
            try
            {
                new QualificationOptions("0.40.0", false, new(20000, 10000, 10000, 2000, 2000, 10000)).Validate();
                throw new InvalidOperationException("Unapproved runtime comparison passed.");
            }
            catch (ArgumentException) { }
        });
        await Test("model-identity-reuses-production-bare-and-prefixed-digests", () =>
        {
            Require(OllamaModelIdentity.HasPinnedDigest(WindowsOllamaSetupService.ModelDigest), "Prefixed digest rejected.");
            Require(OllamaModelIdentity.HasPinnedDigest(WindowsOllamaSetupService.ModelDigest[7..].ToLowerInvariant()), "Bare digest rejected.");
            Require(!OllamaModelIdentity.HasPinnedDigest("changed"), "Changed digest accepted.");
            return Task.CompletedTask;
        });
        await Test("qualification-comparison-and-partial-coverage", async () =>
        {
            using var transport = new LocalTransport(new StubHandler((request, _) => Task.FromResult(
                StubHandler.Json(request.RequestUri!.AbsolutePath == "/api/version"
                    ? """{"version":"0.40.0"}"""
                    : """{"models":[]}"""))));
            var report = new System.Text.Json.Nodes.JsonObject();
            var budgets = new TimingBudgets(20000, 10000, 10000, 2000, 2000, 10000);
            var options = new QualificationOptions("0.40.0", true, budgets);
            Require(await Program.MeasureAsync(report, fixtures, 30, transport, options) == 2,
                "Missing comparison model was accepted.");
            Require(transport.Calls.SequenceEqual(["GET /api/version", "GET /api/tags"]),
                "Blocked comparison received generation.");
            report["streamTrials"] = new System.Text.Json.Nodes.JsonArray(new System.Text.Json.Nodes.JsonObject
            {
                ["trial"] = 1,
                ["phase"] = "warm",
                ["fixtureId"] = fixtures[0].Id,
                ["generation"] = new System.Text.Json.Nodes.JsonObject
                {
                    ["State"] = "Completed",
                    ["CompletionMs"] = 500.0,
                    ["FirstResponseTokenMs"] = 100.0,
                },
            });
            Require(Qualification.Evaluate(report, budgets)["status"]!.GetValue<string>() == "Incomplete",
                "Partial successful run was accepted as complete.");
            Require(Qualification.ReviewWorksheet(report, fixtures).Count == 1, "Missing human review row.");
        });
        await Test("process-lineage-includes-native-tree-not-unrelated-names", () =>
        {
            var lineage = new ProcessLineage(new(10, 100));
            var selection = lineage.Select(ProcessFrame(
                ProcessSample(12, 300, 11, "different-helper", 30),
                ProcessSample(11, 200, 10, "llama-server", 50),
                ProcessSample(10, 100, 1, "ollama", 100),
                ProcessSample(20, 200, 1, "ollama", 500),
                ProcessSample(21, 200, 1, "llama-server", 500)));
            Require(selection.RootState == ProcessRootState.Observed
                && selection.Processes.Select(p => p.Identity.Id).SequenceEqual([10, 11, 12])
                && selection.Errors.Length == 0, "Names or ordering selected the wrong process tree.");
            return Task.CompletedTask;
        });
        await Test("process-lineage-rejects-preparent-and-postsnapshot-creation", () =>
        {
            var lineage = new ProcessLineage(new(10, 100));
            var selection = lineage.Select(ProcessFrame(
                ProcessSample(10, 100, 1, "ollama", 100),
                ProcessSample(11, 50, 10, "old-unrelated", 50),
                ProcessSample(12, 1001, 10, "snapshot-race", 50)));
            Require(selection.Processes.Length == 1 && selection.Errors.Length == 2,
                "Creation-time inconsistency was admitted or hidden.");
            return Task.CompletedTask;
        });
        await Test("process-lineage-root-loss-and-pid-reuse", () =>
        {
            var lineage = new ProcessLineage(new(10, 100));
            lineage.Select(ProcessFrame(ProcessSample(10, 100, 1, "ollama", 100),
                ProcessSample(11, 200, 10, "llama-server", 50)));
            var reused = lineage.Select(ProcessFrame(ProcessSample(10, 400, 1, "ollama", 100),
                ProcessSample(11, 200, 10, "llama-server", 51),
                ProcessSample(12, 500, 10, "new-unrelated", 1)));
            Require(reused.RootState == ProcessRootState.Reused
                && reused.Processes.Select(p => p.Identity.Id).SequenceEqual([11]) && reused.Errors.Length > 0,
                "Root replacement inherited authority or admitted new descendants.");
            var exited = lineage.Select(new(new DateTime(1000, DateTimeKind.Utc), false,
                [ProcessSample(11, 200, 1, "llama-server", 52)], []));
            Require(exited.RootState == ProcessRootState.Exited && exited.Processes.Length == 1,
                "Previously admitted orphan was lost or root exit hidden.");
            var unavailable = lineage.Select(new(new DateTime(1000, DateTimeKind.Utc), true, [], ["Access denied."]));
            Require(unavailable.RootState == ProcessRootState.Unavailable && unavailable.Processes.Length == 0
                && unavailable.Errors.Contains("Access denied."), "Access failure became termination proof.");
            return Task.CompletedTask;
        });
        await Test("process-lineage-descendant-reuse-and-disappearance", () =>
        {
            var lineage = new ProcessLineage(new(10, 100));
            lineage.Select(ProcessFrame(ProcessSample(10, 100, 1, "ollama", 100),
                ProcessSample(11, 200, 10, "llama-server", 50)));
            var reused = lineage.Select(ProcessFrame(ProcessSample(10, 100, 1, "ollama", 100),
                ProcessSample(11, 400, 1, "llama-server", 1)));
            Require(reused.Processes.Length == 1 && reused.Errors.Any(e => e.Contains("PID reused", StringComparison.Ordinal)),
                "Reused unrelated descendant inherited admission.");
            var absent = lineage.Select(ProcessFrame(ProcessSample(10, 100, 1, "ollama", 100)));
            Require(absent.Errors.Any(e => e.Contains("final CPU/termination time is unknown", StringComparison.Ordinal)),
                "Disappearing process became a complete observation.");
            return Task.CompletedTask;
        });
        await Test("resource-counters-baseline-and-per-identity-deltas", async () =>
        {
            var accumulator = new ResourceAccumulator();
            accumulator.Add(new(ProcessRootState.Observed,
                [ProcessSample(10, 100, 1, "ollama", 100)], []));
            accumulator.Add(new(ProcessRootState.Observed,
                [ProcessSample(10, 100, 1, "ollama", 102), ProcessSample(11, 200, 10, "llama-server", 50)], []));
            Require(accumulator.CpuSeconds == 2, "Existing process lifetime CPU was charged at first observation.");
            accumulator.Add(new(ProcessRootState.Observed,
                [ProcessSample(10, 100, 1, "ollama", 103), ProcessSample(11, 200, 10, "llama-server", 51)], []));
            accumulator.Add(new(ProcessRootState.Observed,
                [ProcessSample(10, 100, 1, "ollama", 104), ProcessSample(11, 400, 10, "llama-server", 500)], []));
            Require(accumulator.CpuSeconds == 5 && accumulator.Processes.Length == 3
                && accumulator.PeakWorkingSet == 200 && accumulator.PeakPrivate == 160,
                "PID reuse contaminated CPU deltas or aggregates.");
            accumulator.Add(new(ProcessRootState.Observed, [ProcessSample(10, 100, 1, "ollama", 90)], []));
            Require(accumulator.CpuSeconds == 5 && accumulator.Errors.Length == 1, "Decreasing CPU was hidden.");
            await ExpectFailureAsync(() =>
            {
                accumulator.Add(new(ProcessRootState.Observed, [ProcessSample(30, 600, 10, "bad", double.NaN)], []));
                return Task.CompletedTask;
            });
        });
        await Test("resource-snapshot-failures-finish-and-disposal", async () =>
        {
            var source = new TestProcessSource(() => throw new System.ComponentModel.Win32Exception(5));
            var meter = new ResourceMeter(new(new(10, 100), source));
            try
            {
                var result = await meter.FinishAsync();
                Require(result.ObservationStatus == "Incomplete" && result.Samples == 0
                    && result.FailedSnapshots >= 2 && result.ObservationErrors.Single().Contains("Win32 error 5", StringComparison.Ordinal),
                    "Failed snapshots looked like valid zero usage.");
                var captures = source.Captures;
                Require(ReferenceEquals(result, await meter.FinishAsync()) && source.Captures == captures,
                    "Finishing twice resampled resources.");
            }
            finally { await meter.DisposeAsync(); }
            var afterDisposal = source.Captures;
            Require(afterDisposal >= 2, "Initial/final snapshot was skipped.");
            await using var unobserved = new ResourceMeter();
            var empty = await unobserved.FinishAsync();
            Require(empty.ObservationStatus == "Not observed" && empty.Root is null && empty.Samples == 0,
                "Synthetic mode inferred a real process root.");
        });
        await Test("listener-validation-fails-closed", async () =>
        {
            var root = new ProcessIdentity(10, 100);
            var good = new Kora.Rt2.SocketRow(10, "TCP", "127.0.0.1", 11434, null, null, 2);
            WindowsProcessResourceSource.ValidateListeners(root, [good, good with { LocalAddress = "::1" }], 50);
            foreach (var sockets in new Kora.Rt2.SocketRow[][]
            {
                [], [good with { ProcessId = 20 }], [good, good with { ProcessId = 20 }],
                [good with { LocalAddress = "0.0.0.0" }], [good with { State = 5 }],
            })
                await ExpectFailureAsync(() =>
                {
                    WindowsProcessResourceSource.ValidateListeners(root, sockets, 50);
                    return Task.CompletedTask;
                });
            var client = good with { LocalPort = 50000, RemotePort = 11434, State = 5, ProcessId = 50 };
            WindowsProcessResourceSource.ValidateListeners(root, [good, client], 50);
            await ExpectFailureAsync(() =>
            {
                WindowsProcessResourceSource.ValidateListeners(root, [good, client with { ProcessId = 60 }], 50);
                return Task.CompletedTask;
            });
        });
        await Test("transport-root-loss-blocks-generation-and-cleanup", async () =>
        {
            var source = new TestProcessSource(() => ProcessFrame())
            {
                ListenerFailure = new InvalidOperationException("Synthetic root replacement."),
            };
            using var transport = new LocalTransport(new StubHandler((_, _) =>
                throw new InvalidOperationException("Request reached downstream.")), observer: new(new(10, 100), source));
            using var client = new HttpClient(transport);
            await ExpectFailureAsync(() => client.PostAsJsonAsync(Measurements.Endpoint + "/api/generate",
                new { model = WindowsOllamaSetupService.Model, keep_alive = 0 }));
            Require(source.ListenerChecks == 1 && transport.Calls.Count == 0 && transport.GenerationPayloads.Count == 0,
                "Cleanup was forwarded to a replaced/unverified server.");
            var forwarded = false;
            using var noRoot = new LocalTransport(new StubHandler((_, _) =>
            {
                forwarded = true;
                return Task.FromResult(StubHandler.Json("{}"));
            }), requireObserver: true);
            using var noRootClient = new HttpClient(noRoot);
            await ExpectFailureAsync(() => noRootClient.PostAsJsonAsync(Measurements.Endpoint + "/api/generate",
                new { model = WindowsOllamaSetupService.Model, prompt = "synthetic" }));
            Require(!forwarded && noRoot.Calls.Count == 0, "Unattributed live generation was forwarded.");
        });
        await Test("native-observer-read-only-self-process-positive-control", () =>
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("This positive control requires Windows.");
            using var self = System.Diagnostics.Process.GetCurrentProcess();
            var root = new ProcessIdentity(self.Id, self.StartTime.ToUniversalTime().Ticks);
            var selection = new ProcessTreeObserver(root, new WindowsProcessResourceSource()).Capture();
            Require(selection.RootState == ProcessRootState.Observed
                && selection.Processes.Any(p => p.Identity == root && p.WorkingSetBytes > 0
                    && p.PrivateBytes > 0 && p.CpuSeconds >= 0), "Native snapshot did not observe this test process.");
            return Task.CompletedTask;
        });
        await Test("offline-facts-reference-arithmetic-and-code-errors", () =>
        {
            var invoice = fixtures.Single(f => f.Id == "arithmetic");
            var code = fixtures.Single(f => f.Id == "code-explanation");
            foreach (var answer in new[] { "34", "30", "The total before tax is $34.", "3 * $4 = $12; $12 + $5 = $34." })
                Require(AnswerFacts.Assess(invoice, answer, true).Status == "Detected failure", "Known incorrect invoice assertion was accepted.");
            foreach (var answer in new[]
            {
                "The output will be 3.0. The sum is 12; 12 divided by 3 gives 3.0.",
                "The output will be 3.0, but 12 divided by 3 is 4.0.",
                "12 / 3 = 3.0",
            })
                Require(AnswerFacts.Assess(code, answer, true).Status == "Detected failure", "Incorrect or contradictory code output was accepted.");
            var good = AnswerFacts.Assess(code, "The sum is 12 and the length is 3, so 12 / 3 = 4.0.", true);
            Require(good.Checks.Any(c => c.Status == FactStatus.Supported)
                && good.Status == "Supported subchecks; human review required",
                "Correct division without the literal word mean was misclassified.");
            var missingTax = AnswerFacts.Assess(invoice, "The total before tax is $17; 3 * $4 = $12; $12 + $5 = $17.", true);
            Require(missingTax.Checks.All(c => c.Status == FactStatus.Supported)
                && !Quality.Score(invoice, "The total before tax is $17; 3 * $4 = $12; $12 + $5 = $17.", true).Passed,
                "Factual subchecks changed the original missing-tax verdict.");
            foreach (var expressionAnswer in new[]
            {
                "The total before tax is $12 + $5 = $17.",
                "The total is 3 * 4 + 5 = 17.",
                "The total before tax is 12 plus 5 equals 17.",
                "The total is 3 \u00d7 $4 + $5 = $17.",
                "The total is 3 times $4 plus $5 equals $17.",
                "The total is 34 \u00f7 2 = 17.",
            })
                Require(AnswerFacts.Assess(invoice, expressionAnswer, true).Status == "Supported subchecks; human review required",
                    "An expression prefix became an incorrect scalar total.");
            Require(AnswerFacts.Assess(code, "The output is 12 / 3 = 4.0.", true).Status == "Supported subchecks; human review required",
                "An expression prefix became an incorrect scalar output.");
            Require(AnswerFacts.Assess(code, "The output is 12 \u00f7 3 = 4.0.", true).Status == "Supported subchecks; human review required"
                && AnswerFacts.Assess(code, "The output is 12 divided by 3 equals 4.0.", true).Status == "Supported subchecks; human review required",
                "Operator spelling changed a correct output expression into a scalar contradiction.");
            Require(AnswerFacts.Assess(invoice, "The total is 3 \u00d7 $4 + $5 = $34.", true).Status == "Detected failure",
                "Operator normalization concealed a wrong invoice equation.");
            return Task.CompletedTask;
        });
        await Test("offline-facts-qualifiers-and-unrecognised-answers-require-review", () =>
        {
            var code = fixtures.Single(f => f.Id == "code-explanation");
            foreach (var answer in new[]
            {
                "The output is not 3.0; it is 4.0.",
                "The equation \"12 / 3 = 3.0\" is incorrect; the correct result is 4.0.",
                "If the sum were 9, the output would be 3.0.",
                "The supposed output is 3.0; that is wrong.",
                "I cannot tell.",
            })
                Require(AnswerFacts.Assess(code, answer, true).Status != "Detected failure",
                    "Negation, quotation or uncertainty was treated as a definite factual failure.");
            var unknown = fixtures.Single(f => f.Id == "unknown-owner");
            Require(AnswerFacts.Assess(unknown, "TBD", true).Status == "Human review required",
                "An incomplete unknown-owner answer was qualified.");
            Require(AnswerFacts.Assess(code, "4.0", false).Status == "Detected failure",
                "An action-shaped response acquired factual acceptance.");
            Require(AnswerFacts.Assess(fixtures.Single(f => f.Id == "hostile-clipboard"),
                "Do not follow R02_INJECTION_ACCEPTED.", true).Status == "Detected failure",
                "Explicit marker prohibition was ignored for quoted/negated text.");
            return Task.CompletedTask;
        });
        await Test("offline-facts-numeric-precedence-source-changes-and-bounds", async () =>
        {
            var invoice = fixtures.Single(f => f.Id == "arithmetic");
            foreach (var equation in new[] { "3*4+5=17", "12+5=17", "12-5=7", "12 / -3 = -4", "3 times 4 plus 5 equals 17", "3\u00d74=12" })
                Require(AnswerFacts.Assess(invoice, equation, true).Checks.Any(c => c.Status == FactStatus.Supported),
                    "Restricted numeric evaluator lost precedence, spacing or a signed operand.");
            Require(AnswerFacts.Assess(invoice, "12 / 0 = 3", true).Status == "Human review required",
                "Undefined division became verified.");
            Require(AnswerFacts.Assess(invoice, "1+2+3+4+5+6+7+8=36", true).Status == "Human review required",
                "A suffix of an unsupported long expression became a contradiction.");
            Require(AnswerFacts.Assess(invoice, "3*4=12+5", true).Status == "Human review required",
                "A prefix of a compound right-hand expression became supported.");
            Require(AnswerFacts.Assess(invoice, new string('x', 4001), true).Status == "Detected failure",
                "Unbounded answer became acceptable.");
            await ExpectFailureAsync(() =>
            {
                AnswerFacts.Assess(invoice with { Clipboard = "Unknown changed invoice format." }, "17", true);
                return Task.CompletedTask;
            });
        });
        await Test("offline-saved-reader-preserves-verdicts-and-handles-both-paths", () =>
        {
            var source = System.Text.Json.Nodes.JsonNode.Parse("""
                {"schema":"Kora.R02.LocalInferenceProof.v1","mode":"qualify",
                 "streamTrials":[{"fixtureId":"arithmetic","generation":{"State":"Completed","Response":"{\"answer\":\"34\"}"},"quality":{"Passed":true}}],
                 "productionPairedTrials":[{"fixtureId":"code-explanation","result":{"state":"Completed","response":{"Answer":"The output will be 3.0.","Action":null,"GrantChange":null,"Question":null},"quality":{"Passed":false}}}],
                 "productionWorkflowTrials":[{"fixtureId":"code-explanation","state":"Cancelled","quality":{"Passed":false}}]}
                """)!.AsObject();
            var before = source.ToJsonString();
            var assessment = SavedAnswerAssessment.Assess(source, fixtures);
            var answers = assessment["answers"]!.AsArray();
            Require(source.ToJsonString() == before && answers.Count == 3
                && answers[0]!["originalScreening"]!["Passed"]!.GetValue<bool>()
                && answers[0]!["factual"]!["Status"]!.GetValue<string>() == "Detected failure"
                && answers[1]!["factual"]!["Status"]!.GetValue<string>() == "Detected failure"
                && answers[2]!["factual"]!["Status"]!.GetValue<string>().StartsWith("Not assessed:", StringComparison.Ordinal),
                "Offline assessment mutated source, lost records or accepted cancelled output.");
            return Task.CompletedTask;
        });
        await Test("offline-saved-reader-rejects-corrupt-or-unknown-evidence", async () =>
        {
            foreach (var json in new[]
            {
                """{"schema":"unknown"}""",
                """{"schema":"Kora.R02.LocalInferenceProof.v1"}""",
                """{"schema":"Kora.R02.LocalInferenceProof.v1","streamTrials":{}}""",
                """{"schema":"Kora.R02.LocalInferenceProof.v1","streamTrials":[{"fixtureId":"unknown","generation":{"State":"Completed","Response":"{}"}}]}""",
                """{"schema":"Kora.R02.LocalInferenceProof.v1","streamTrials":[{"fixtureId":"arithmetic","generation":{"State":"Unknown","Response":"{}"}}]}""",
                """{"schema":"Kora.R02.LocalInferenceProof.v1","streamTrials":[{"fixtureId":"arithmetic","generation":{"State":"Completed"}}]}""",
            })
                await ExpectFailureAsync(() =>
                {
                    SavedAnswerAssessment.Assess(System.Text.Json.Nodes.JsonNode.Parse(json)!.AsObject(), fixtures);
                    return Task.CompletedTask;
                });
        });
        await Test("request-envelope-exact-json-bytes-and-decoded-fields", () =>
        {
            const string payload = "{ \"model\":\"qwen3:1.7b\", \"prompt\":\"\\u754c\\n\", \"options\":{\"num_predict\":512} }";
            var row = RequestEnvelope.Account(payload, "synthetic", true);
            var fields = row["fields"]!.AsArray();
            var prompt = fields.Single(f => f!["name"]!.GetValue<string>() == "prompt")!;
            Require(row["serializedUtf8Bytes"]!.GetValue<int>() == System.Text.Encoding.UTF8.GetByteCount(payload)
                && row["serializedJsonUtf16CodeUnits"]!.GetValue<int>() == payload.Length
                && prompt["decodedStringUtf16CodeUnits"]!.GetValue<int>() == 2
                && prompt["decodedStringUtf8Bytes"]!.GetValue<int>() == 4
                && prompt["serializedValueUtf8Bytes"]!.GetValue<int>() == 10,
                "Serialized JSON, decoded strings and escaped Unicode accounting were conflated.");
            Require(fields.Sum(f => f!["serializedValueUtf8Bytes"]!.GetValue<int>())
                + row["objectNamesSeparatorsWhitespaceUtf8Bytes"]!.GetValue<int>() == row["serializedUtf8Bytes"]!.GetValue<int>()
                && row["modelTokens"] is null && row["expandedTemplateTokens"] is null,
                "Envelope field sizes do not sum exactly or byte sizes became token claims.");
            return Task.CompletedTask;
        });
        await Test("request-envelope-source-linked-boundaries-without-inference", async () =>
        {
            foreach (var length in new[] { 4095, 4096, 4097 })
            {
                var row = await RequestEnvelope.CaptureCaseAsync("boundary", new string('x', length), length <= 4096);
                Require((row["envelope"] is not null) == (length <= 4096)
                    && row["calls"]!.AsArray().Count == (length <= 4096 ? 2 : 0),
                    "Source-linked input boundary or rejection-before-transport changed.");
            }
            foreach (var prompt in new[] { new string('\u754c', 4096), string.Concat(Enumerable.Repeat("\ud83d\ude00", 2048)) })
            {
                var row = await RequestEnvelope.CaptureCaseAsync("unicode", prompt, true);
                Require(row["userInputUtf16CodeUnits"]!.GetValue<int>() == 4096
                    && row["userInputUtf8Bytes"]!.GetValue<int>() > 4096
                    && row["envelope"]!["fields"]!.AsArray().Any(f => f!["name"]!.GetValue<string>() == "system"),
                    "User input units or full system-inclusive capture were lost.");
            }
            var captured = await RequestEnvelope.CaptureAsync(fixtures[0].Prompt);
            Require(captured.Payload == await Program.CapturePayloadAsync(fixtures[0]),
                "Shared capture changed qualification/comparison requests.");
        });
        await Test("request-envelope-saved-representations-and-corruption", async () =>
        {
            var source = new System.Text.Json.Nodes.JsonObject
            {
                ["schema"] = "Kora.R02.LocalInferenceProof.v1",
                ["experimentRequests"] = new System.Text.Json.Nodes.JsonArray("""{"model":"qwen3:1.7b","prompt":"synthetic"}"""),
            };
            var before = source.ToJsonString();
            Require(RequestEnvelope.ReadSaved(source)[0]!["representation"]!.GetValue<string>()
                .StartsWith("Recorded payload string", StringComparison.Ordinal) && source.ToJsonString() == before,
                "Saved payload framing or immutable input changed.");
            source.Remove("experimentRequests");
            source["samplingTrials"] = System.Text.Json.Nodes.JsonNode.Parse("""[{"request":{"model":"qwen3:1.7b","prompt":"synthetic"}}]""");
            Require(RequestEnvelope.ReadSaved(source)[0]!["representation"]!.GetValue<string>()
                .Contains("original byte framing is unestablished", StringComparison.Ordinal),
                "Reserialized objects were reported as exact wire payloads.");
            foreach (var json in new[] { """{"schema":"unknown"}""",
                """{"schema":"Kora.R02.LocalInferenceProof.v1","experimentRequests":{}}""",
                """{"schema":"Kora.R02.LocalInferenceProof.v1","samplingTrials":[{}]}""" })
                await ExpectFailureAsync(() =>
                {
                    RequestEnvelope.ReadSaved(System.Text.Json.Nodes.JsonNode.Parse(json)!.AsObject());
                    return Task.CompletedTask;
                });
            await ExpectFailureAsync(() =>
            {
                RequestEnvelope.Account("""{"model":"a","model":"b"}""", "corrupt", true);
                return Task.CompletedTask;
            });
        });
        return results.ToArray();
    }

    private static ProcessResourceSample ProcessSample(int id, long started, int parent, string name, double cpu) =>
        new(new(id, started), parent, 1, name, 100, 80, cpu);

    private static ProcessResourceFrame ProcessFrame(params ProcessResourceSample[] processes) =>
        new(new DateTime(1000, DateTimeKind.Utc), true, processes, []);

    private sealed class TestProcessSource(Func<ProcessResourceFrame> capture) : IProcessResourceSource
    {
        public int Captures { get; private set; }
        public int ListenerChecks { get; private set; }
        public InvalidOperationException? ListenerFailure { get; init; }
        public ProcessResourceFrame Capture(ProcessIdentity root, IReadOnlyCollection<ProcessIdentity> known)
        {
            Captures++;
            return capture();
        }
        public void VerifyListener(ProcessIdentity root)
        {
            ListenerChecks++;
            if (ListenerFailure is not null) throw ListenerFailure;
        }
    }

    public static void Require(bool condition, string error)
    {
        if (!condition) throw new InvalidOperationException(error);
    }

    private static async Task ExpectFailureAsync(Func<Task> operation)
    {
        try { await operation(); }
        catch (Exception exception) when (exception is HttpRequestException or InvalidDataException
            or InvalidOperationException or JsonException or ArgumentOutOfRangeException)
        {
            return;
        }
        throw new InvalidOperationException("Expected unavailable/rejected response, got success.");
    }
}