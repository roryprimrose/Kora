using System.Net;
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
        model = WindowsOllamaSetupService.Model, done = true, response = json,
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
                .ReasonAsync(fixtures[0].Prompt, Context, CancellationToken.None);
            Require(answer.Answer == "Synthetic answer" && answer.Action is null && answer.GrantChange is null, "Wrong response.");
            Require(transport.Calls.SequenceEqual(["GET /api/tags", "POST /api/generate"]), "Wrong destinations.");
            using var payload = JsonDocument.Parse(transport.GenerationPayloads.Single());
            Require(payload.RootElement.GetProperty("prompt").GetString() == fixtures[0].Prompt, "Changed synthetic prompt.");
            Require(!payload.RootElement.GetProperty("stream").GetBoolean(), "Production unexpectedly streams.");
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
                    .ReasonAsync(fixtures[0].Prompt, Context, CancellationToken.None));
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
                    .ReasonAsync(fixtures[0].Prompt, Context, CancellationToken.None));
                Require(transport.Calls.Count == 2, "Retry or fallback occurred.");
            });
        }
        await Test("quoted-action-is-never-dispatched-by-proof", async () =>
        {
            using var transport = new LocalTransport(new StubHandler((request, _) => Task.FromResult(
                StubHandler.Json(request.RequestUri!.AbsolutePath == "/api/tags" ? Tags : Decision("""{"action":"LockMachine"}""")))));
            using var client = new HttpClient(transport);
            var answer = await new WindowsOllamaReasoner(client, new BuiltInCommandCatalog())
                .ReasonAsync(hostile.Prompt, Context, CancellationToken.None);
            Require(!Quality.Score(hostile, answer.Answer, answer.Action is null).Passed, "Action passed answering rubric.");
        });
        await Test("production-request-limit", async () =>
        {
            using var transport = new LocalTransport(new StubHandler((_, _) => throw new InvalidOperationException("Unexpected HTTP.")));
            using var client = new HttpClient(transport);
            await ExpectFailureAsync(() => new WindowsOllamaReasoner(client, new BuiltInCommandCatalog())
                .ReasonAsync(new string('x', 4097), Context, CancellationToken.None));
            Require(transport.Calls.Count == 0, "Oversize request transmitted.");
        });
        await Test("production-request-boundary-4096", async () =>
        {
            using var transport = new LocalTransport(new StubHandler((request, _) => Task.FromResult(
                StubHandler.Json(request.RequestUri!.AbsolutePath == "/api/tags" ? Tags : Decision("""{"answer":"boundary"}""")))));
            using var client = new HttpClient(transport);
            var answer = await new WindowsOllamaReasoner(client, new BuiltInCommandCatalog())
                .ReasonAsync(new string('x', 4096), Context, CancellationToken.None);
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
                    .ReasonAsync(fixtures[0].Prompt, Context, cancel.Token);
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
                    .ReasonAsync(fixtures[0].Prompt, Context, CancellationToken.None);
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
            using var transport = new LocalTransport(new StubHandler(async (request, token) =>
            {
                var path = request.RequestUri!.AbsolutePath;
                if (path == "/api/version")
                    return StubHandler.Json(JsonSerializer.Serialize(new { version = WindowsOllamaSetupService.PackageVersion }));
                if (path == "/api/tags") return StubHandler.Json(Tags);
                if (path == "/api/show") return StubHandler.Json("""{"license":"synthetic metadata","model_info":{"qwen3.context_length":32768}}""");
                if (path == "/api/ps")
                    return StubHandler.Json(loaded
                        ? JsonSerializer.Serialize(new { models = new[] { new { name = WindowsOllamaSetupService.Model, size_vram = 0 } } })
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
                var fixture = fixtures.SingleOrDefault(f => f.Prompt == prompt);
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
            }));
            var report = new System.Text.Json.Nodes.JsonObject();
            var code = await Program.MeasureAsync(report, fixtures, 30, transport);
            Require(code == 0, "Synthetic measurement orchestration failed.");
            Require(report["streamTrials"]!.AsArray().Count == 60, "Wrong cold/warm trial count.");
            Require(report["productionWorkflowTrials"]!.AsArray().Count == fixtures.Length, "Missing production fixtures.");
            Require(report["contextTrials"]!.AsArray().Count == 5, "Missing context/overflow trials.");
            Require(report["streamCancellation"]!["State"]!.GetValue<string>() == "Cancelled", "No streaming cancellation.");
            Require(report["productionCancellation"]!["state"]!.GetValue<string>() == "Cancelled", "No production cancellation.");
            Require(report["realMissingModelError"]!["State"]!.GetValue<string>() == "Unavailable", "Missing model accepted.");
        });
        await Test("runtime-version-mismatch-preflight", async () =>
        {
            using var transport = new LocalTransport(new StubHandler((_, _) => Task.FromResult(
                StubHandler.Json("""{"version":"not-the-pin"}"""))));
            var report = new System.Text.Json.Nodes.JsonObject();
            Require(await Program.MeasureAsync(report, fixtures, 30, transport) == 2, "Changed runtime passed.");
            Require(transport.Calls.SequenceEqual(["GET /api/version"]), "Changed runtime received synthetic context.");
        });
        return results.ToArray();
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
