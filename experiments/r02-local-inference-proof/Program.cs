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
            if (args.Length > 0 && args[0] == "assess-answers")
                return await SavedAnswerAssessment.RunAsync(args[1..]);
            if (args.Length > 0 && args[0] == "account-envelope")
                return await RequestEnvelope.RunAsync(args[1..]);
            if (args.Length < 3 || args[1] != "--output"
                || args[0] is not ("self-test" or "observe" or "measure" or "qualify" or "compare-sampling" or "compare-streaming" or "observer-control"))
                throw new ArgumentException("Usage: self-test|observe|measure|qualify|compare-sampling|compare-streaming|observer-control --output FILE [--server-pid PID --server-started-ticks TICKS --exclusive-runtime --trials 30]; live generation requires --server-pid; qualification/comparison/control modes require --budgets FILE [--runtime-version VERSION --comparison-runtime].");
            var mode = args[0];
            var qualificationMode = mode is "qualify" or "compare-sampling" or "compare-streaming" or "observer-control";
            var output = Path.GetFullPath(args[2]);
            if (File.Exists(output)) throw new IOException("Refusing to overwrite an existing evidence file.");
            var trials = 30;
            var exclusive = false;
            var runtimeVersion = WindowsOllamaSetupService.PackageVersion;
            var comparisonRuntime = false;
            int? serverProcessId = null;
            long? serverStartedTicks = null;
            string? budgetPath = null;
            for (var i = 3; i < args.Length; i++)
            {
                if (args[i] == "--exclusive-runtime") exclusive = true;
                else if (mode != "self-test" && args[i] == "--server-pid" && ++i < args.Length
                    && int.TryParse(args[i], out var pid) && pid > 0) serverProcessId = pid;
                else if (mode != "self-test" && args[i] == "--server-started-ticks" && ++i < args.Length
                    && long.TryParse(args[i], out var ticks) && ticks > 0 && ticks <= DateTime.MaxValue.Ticks)
                    serverStartedTicks = ticks;
                else if (qualificationMode && args[i] == "--comparison-runtime") comparisonRuntime = true;
                else if (qualificationMode && args[i] == "--runtime-version" && ++i < args.Length) runtimeVersion = args[i];
                else if (qualificationMode && args[i] == "--budgets" && ++i < args.Length) budgetPath = args[i];
                else if (mode != "observer-control" && args[i] == "--trials" && ++i < args.Length
                    && int.TryParse(args[i], out var count) && count is >= 30 and <= 1000) trials = count;
                else throw new ArgumentException("Unknown option or invalid trial count (30-1000 required).");
            }
            if (mode is "measure" or "qualify" or "compare-sampling" or "compare-streaming" or "observer-control" && !exclusive)
                throw new ArgumentException("Measurement unloads/reloads the model. Use --exclusive-runtime only with consent on a dedicated test runtime.");
            if (mode is "measure" or "qualify" or "compare-sampling" or "compare-streaming" or "observer-control" && serverProcessId is null)
                throw new ArgumentException("Live measurement requires an explicitly selected --server-pid.");
            if (serverStartedTicks is not null && serverProcessId is null)
                throw new ArgumentException("--server-started-ticks requires --server-pid.");
            QualificationOptions? qualification = null;
            if (qualificationMode)
            {
                if (budgetPath is null) throw new ArgumentException("Qualification/comparison modes require --budgets FILE with pre-agreed timing limits.");
                qualification = new(runtimeVersion, comparisonRuntime, await TimingBudgets.LoadAsync(budgetPath));
                qualification.Validate();
            }

            var fixtures = JsonSerializer.Deserialize<Fixture[]>(
                await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "fixtures.json")), Json)
                ?? throw new InvalidDataException("No fixtures.");
            SelfTests.Require(fixtures.Length >= 6 && fixtures.Select(f => f.Id).Distinct().Count() == fixtures.Length,
                "Invalid fixture inventory.");
            SelfTests.Require(fixtures.All(f => f.Prompt.Length <= 4096), "Fixture exceeds production request bound.");
            var observer = serverProcessId is { } selectedPid ? ProcessTreeObserver.Admit(selectedPid) : null;
            if (serverStartedTicks is { } expectedTicks && observer?.Root.StartedUtcTicks != expectedTicks)
                throw new InvalidOperationException("Selected server creation time differs from the operator's preflight; no request is admitted.");
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
                ["resourceObservationRoot"] = JsonSerializer.SerializeToNode(observer?.Root),
                ["resourceObservationLimitation"] = "PID/listener identity is attribution, not ownership, exclusive use, complete lifecycle or per-request computation proof.",
            };
            var code = 0;
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            await using (var reservation = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                await reservation.WriteAsync(System.Text.Encoding.UTF8.GetBytes(report.ToJsonString(Json)));
            using var stop = new CancellationTokenSource();
            ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
            {
                eventArgs.Cancel = true;
                stop.Cancel();
            };
            Console.CancelKeyPress += cancelHandler;
            try
            {
                if (mode == "self-test")
                    report["tests"] = JsonSerializer.SerializeToNode(await SelfTests.RunAsync(fixtures), Json);
                else if (mode == "observe")
                    code = await ObserveAsync(report, fixtures, observer, cancellationToken: stop.Token);
                else if (mode == "observer-control")
                    code = await NativeObserverControl.RunAsync(report,
                        qualification ?? throw new InvalidOperationException("Missing control options."),
                        observer, checkpoint: () => SaveReportAsync(output, report), cancellationToken: stop.Token);
                else if (mode is "compare-sampling" or "compare-streaming")
                    code = await InferenceComparison.RunAsync(report, fixtures, trials,
                        qualification ?? throw new InvalidOperationException("Missing comparison options."),
                        checkpoint: () => SaveReportAsync(output, report), cancellationToken: stop.Token,
                        compareStreaming: mode == "compare-streaming", observer: observer);
                else
                    code = await MeasureAsync(report, fixtures, trials, qualification: qualification,
                        checkpoint: () => SaveReportAsync(output, report), cancellationToken: stop.Token, observer: observer);
            }
            catch (Exception exception) when (exception is IOException or JsonException
                or InvalidOperationException or HttpRequestException or TimeoutException or OperationCanceledException)
            {
                report["state"] = "Failed; retain partial evidence, do not interpret as acceptance.";
                report["error"] = exception.GetType().Name + ": " + exception.Message;
                Console.Error.WriteLine(report["error"]!.GetValue<string>());
                code = 3;
            }
            finally
            {
                Console.CancelKeyPress -= cancelHandler;
            }
            if (qualification is not null)
            {
                report["timingAssessment"] = mode == "observer-control"
                    ? NativeObserverControl.Evaluate(report, qualification.Budgets)
                    : mode is "compare-sampling" or "compare-streaming"
                    ? InferenceComparison.Evaluate(report, qualification.Budgets)
                    : Qualification.Evaluate(report, qualification.Budgets);
                report["humanReviewWorksheet"] = Qualification.ReviewWorksheet(report,
                    mode == "observer-control" ? [NativeObserverControl.Fixture] : fixtures);
                if (code == 0 && report["timingAssessment"]!["status"]!.GetValue<string>() != "Pass") code = 3;
            }
            report["finishedUtc"] = DateTimeOffset.UtcNow.ToString("O");
            await SaveReportAsync(output, report);
            Console.WriteLine($"{mode}: evidence written to {output}; exit {code}.");
            return code;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or JsonException
            or InvalidOperationException or HttpRequestException or TimeoutException or System.ComponentModel.Win32Exception
            or OverflowException or System.Text.RegularExpressions.RegexMatchTimeoutException)
        {
            Console.Error.WriteLine($"{exception.GetType().Name}: {exception.Message}");
            return 1;
        }
    }

    private static async Task SaveReportAsync(string output, JsonObject report)
    {
        var temporary = output + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, report.ToJsonString(Json) + Environment.NewLine);
            File.Move(temporary, output, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    internal static async Task<int> ObserveAsync(JsonObject report, Fixture[] fixtures,
        ProcessTreeObserver? observer = null, LocalTransport? transportOverride = null,
        CancellationToken cancellationToken = default)
    {
        using var transport = transportOverride ?? LocalTransport.Real(observer);
        using var client = new HttpClient(transport) { Timeout = Timeout.InfiniteTimeSpan };
        var watch = Stopwatch.StartNew();
        var status = await new LocalInferenceDependencyProbe(client).ProbeAsync(cancellationToken);
        report["probe"] = JsonSerializer.SerializeToNode(new
        {
            state = status.Readiness.ToString(),
            status.Detail,
            completionMs = watch.Elapsed.TotalMilliseconds,
        });
        cancellationToken.ThrowIfCancellationRequested();
        report["workflow"] = await ReasonAsync(client, fixtures[0], cancellationToken, transport.Observer);
        report["calls"] = JsonSerializer.SerializeToNode(transport.Calls);
        report["fallback"] = "No remote client/provider, no redirect/proxy, no retry or asset acquisition path invoked.";
        return status.Readiness == DependencyReadiness.Ready
            && report["workflow"]!["state"]!.GetValue<string>() == "Completed"
            && report["workflow"]!["quality"]!["Passed"]!.GetValue<bool>() ? 0 : 2;
    }

    internal static string GenerationProgress(JsonObject result)
    {
        var state = result["state"]?.GetValue<string>()
            ?? throw new InvalidDataException("Missing generation outcome in progress receipt.");
        var quality = state is "Cancelled" or "Unavailable" or "TimedOut"
            ? "Not assessed: request did not complete"
            : state == "Completed"
                ? (result["quality"]?["Passed"]?.GetValue<bool>()
                    ?? throw new InvalidDataException("Missing quality receipt for completed generation.")).ToString()
                : throw new InvalidDataException("Unsupported generation outcome in progress receipt.");
        var completionMs = result["completionMs"]?.GetValue<double>()
            ?? throw new InvalidDataException("Missing completion timing in progress receipt.");
        return $"{state}, completion={completionMs:F0} ms, quality={quality}";
    }

    internal static async Task<JsonObject> ReasonAsync(HttpClient client, Fixture fixture,
        CancellationToken cancellationToken = default, ProcessTreeObserver? observer = null)
    {
        var watch = Stopwatch.StartNew();
        long cancellationTicks = -1;
        using var registration = cancellationToken.Register(() =>
            Interlocked.Exchange(ref cancellationTicks, watch.ElapsedTicks));
        await using var meter = new ResourceMeter(observer);
        var result = new JsonObject { ["fixtureId"] = fixture.Id, ["path"] = "source-linked production reasoner; CPU-only transport override; otherwise production defaults" };
        try
        {
            var answer = await new WindowsOllamaReasoner(client, new BuiltInCommandCatalog())
                .ReasonAsync(fixture.Prompt, SelfTests.Context, null, cancellationToken);
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
        var requestedTicks = Interlocked.Read(ref cancellationTicks);
        double? requestedMs = requestedTicks < 0 ? null : requestedTicks * 1000.0 / Stopwatch.Frequency;
        result["cancellationRequestedMs"] = requestedMs;
        result["clientCancellationLatencyMs"] = result["state"]!.GetValue<string>() == "Cancelled" && requestedMs.HasValue
            ? Math.Max(0, watch.Elapsed.TotalMilliseconds - requestedMs.Value) : null;
        result["completionMs"] = watch.Elapsed.TotalMilliseconds;
        result["firstTokenMs"] = null;
        result["firstTokenLimitation"] = "Production uses stream=false; first-token latency is not observable on this path.";
        result["resources"] = JsonSerializer.SerializeToNode(await meter.FinishAsync(), Json);
        return result;
    }

    internal static async Task<int> MeasureAsync(JsonObject report, Fixture[] fixtures, int trials,
        LocalTransport? transportOverride = null, QualificationOptions? qualification = null,
        Func<Task>? checkpoint = null, CancellationToken cancellationToken = default, ProcessTreeObserver? observer = null)
    {
        qualification?.Validate();
        using var transport = transportOverride ?? LocalTransport.Real(observer);
        using var client = new HttpClient(transport) { Timeout = TimeSpan.FromMinutes(2) };
        report["testedProfile"] = JsonSerializer.SerializeToNode(new
        {
            runtimeVersion = qualification?.RuntimeVersion ?? WindowsOllamaSetupService.PackageVersion,
            comparisonRuntime = qualification?.ComparisonRuntime ?? false,
            productionPinsChanged = false,
            resourceBudgets = "Not approved; no resource qualification claimed.",
        });
        report["requestedTrialsPerPhase"] = trials;
        if (qualification is not null) report["timingBudgets"] = JsonSerializer.SerializeToNode(qualification.Budgets);
        try
        {
            var identity = await VerifyProfileAsync(client,
                qualification?.RuntimeVersion ?? WindowsOllamaSetupService.PackageVersion, cancellationToken);
            report["runtimeMetadata"] = identity.Version;
            report["modelCatalogue"] = identity.Catalogue;
            report["modelMetadata"] = await Measurements.ShowAsync(client, cancellationToken);
            var initialPs = await Measurements.GetAsync(client, "/api/ps", cancellationToken);
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

        try
        {
            var records = new JsonArray();
            report["streamTrials"] = records;
            report["trialDefinition"] = $"Exactly {trials} cold and {trials} immediately paired warm trials; six rotating fixtures; model-unloaded cold (OS cache not flushed).";
            var allPassed = true;
            var cold = new List<GenerationResult>();
            var warm = new List<GenerationResult>();
            for (var i = 0; i < trials; i++)
            {
                var fixture = fixtures[i % fixtures.Length];
                if (qualification is not null)
                    await VerifyProfileAsync(client, qualification.RuntimeVersion, cancellationToken);
                // Capture the exact current production request without calling a real model.
                var productionPayload = await CapturePayloadAsync(fixture);
                foreach (var phase in new[] { "cold", "warm" })
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (phase == "cold") await Measurements.UnloadAsync(client, cancellationToken);
                    var payload = Measurements.StreamingPayload(productionPayload);
                    var generation = await Measurements.StreamAsync(client, payload, cancellationToken, transport.Observer);
                    var (answer, answerOnly) = Measurements.ParseAnswer(generation.Response);
                    var quality = Quality.Score(fixture, answer, answerOnly);
                    var ps = await Measurements.GetAsync(client, "/api/ps", cancellationToken);
                    var cpuOnly = Measurements.CpuOnly(ps);
                    allPassed &= generation.State == "Completed" && quality.Passed && cpuOnly;
                    records.Add(JsonSerializer.SerializeToNode(new
                    {
                        trial = i + 1,
                        phase,
                        fixtureId = fixture.Id,
                        request = payload,
                        generation,
                        quality,
                        cpuOnlyVerified = cpuOnly,
                        loadedModels = ps,
                    }, Json));
                    (phase == "cold" ? cold : warm).Add(generation);
                    if (transportOverride is null)
                        Console.WriteLine($"stream {phase} {i + 1}/{trials} {fixture.Id}: {generation.State}, completion={generation.CompletionMs:F0} ms, quality={quality.Passed}, cpuOnly={cpuOnly}");
                    if (checkpoint is not null) await checkpoint();
                    if (qualification is not null && !cpuOnly)
                        throw new InvalidOperationException("Selected-model-only CPU residency was not confirmed; stopping further generation.");
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
            report["productionWorkflowTrials"] = production;
            foreach (var fixture in fixtures)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var trial = await ReasonAsync(client, fixture, cancellationToken, transport.Observer);
                allPassed &= trial["state"]!.GetValue<string>() == "Completed"
                    && trial["quality"]!["Passed"]!.GetValue<bool>();
                production.Add(trial);
                if (checkpoint is not null) await checkpoint();
            }
            if (qualification is not null)
            {
                var paired = new JsonArray();
                report["productionPairedTrials"] = paired;
                for (var i = 0; i < trials; i++)
                {
                    var fixture = fixtures[i % fixtures.Length];
                    await VerifyProfileAsync(client, qualification.RuntimeVersion, cancellationToken);
                    foreach (var phase in new[] { "cold", "warm" })
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (phase == "cold") await Measurements.UnloadAsync(client, cancellationToken);
                        var result = await ReasonAsync(client, fixture, cancellationToken, transport.Observer);
                        var ps = await Measurements.GetAsync(client, "/api/ps", cancellationToken);
                        var cpuOnly = Measurements.CpuOnly(ps);
                        allPassed &= result["state"]!.GetValue<string>() == "Completed"
                            && result["quality"]!["Passed"]!.GetValue<bool>() && cpuOnly;
                        paired.Add(new JsonObject
                        {
                            ["trial"] = i + 1,
                            ["phase"] = phase,
                            ["fixtureId"] = fixture.Id,
                            ["result"] = result,
                            ["loadedModels"] = ps,
                            ["cpuOnlyVerified"] = cpuOnly,
                        });
                        if (transportOverride is null)
                            Console.WriteLine($"production {phase} {i + 1}/{trials} {fixture.Id}: {GenerationProgress(result)}, cpuOnly={cpuOnly}");
                        if (checkpoint is not null) await checkpoint();
                        if (!cpuOnly)
                            throw new InvalidOperationException("Buffered CPU-only residency was not confirmed; stopping further generation.");
                    }
                }
            }

            var contexts = new JsonArray();
            report["contextTrials"] = contexts;
            foreach (var (context, repetitions) in new[] { (1024, 100), (4096, 150), (8192, 1800), (32768, 7000), (1024, 4000) })
            {
                var prompt = "EARLY_MARKER=17\n" + string.Concat(Enumerable.Repeat("synthetic filler line. ", repetitions))
                    + "\nLATE_MARKER=23\nReport both marker values and their sum. Use exactly {\"answer\":\"text\"}.";
                var payload = Measurements.StreamingPayload(await CapturePayloadAsync(fixtures[0]), context);
                payload["prompt"] = prompt;
                cancellationToken.ThrowIfCancellationRequested();
                var generation = await Measurements.StreamAsync(client, payload, cancellationToken, transport.Observer);
                var (answer, shape) = Measurements.ParseAnswer(generation.Response);
                var expected = new Fixture("context", "", "", "17 + 23 = 40", [@"\b17\b", @"\b23\b", @"\b40\b"], [], false);
                var quality = Quality.Score(expected, answer, shape);
                if (context == 4096)
                    allPassed &= generation.State == "Completed" && quality.Passed;
                contexts.Add(JsonSerializer.SerializeToNode(new
                {
                    requestedNumCtx = context,
                    promptCharacters = prompt.Length,
                    withinProduction4096CharacterBound = prompt.Length <= 4096,
                    generation,
                    markerQuality = quality,
                    limitation = "prompt_eval_count measures accepted tokens, not exact original-token count; inspect truncation and model context metadata. This is not proof of the entire advertised window.",
                }, Json));
                if (transportOverride is null) Console.WriteLine($"context {context}: {generation.State}, markers={quality.Passed}");
                if (checkpoint is not null) await checkpoint();
            }
            var cancelPayload = Measurements.StreamingPayload(await CapturePayloadAsync(fixtures[0]));
            cancelPayload["prompt"] = "Write a very long synthetic numbered list from 1 through 10000. Answer only as a JSON answer.";
            cancelPayload["options"]!["num_predict"] = 8192;
            using (var cancel = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                cancel.CancelAfter(TimeSpan.FromMilliseconds(200));
                var cancellation = await Measurements.StreamAsync(client, cancelPayload, cancel.Token, transport.Observer);
                report["streamCancellation"] = JsonSerializer.SerializeToNode(cancellation, Json);
                allPassed &= cancellation.State == "Cancelled";
            }
            using (var cancel = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                cancel.CancelAfter(TimeSpan.FromMilliseconds(200));
                var cancellation = await ReasonAsync(client, fixtures[0], cancel.Token, transport.Observer);
                report["productionCancellation"] = cancellation;
                allPassed &= cancellation["state"]!.GetValue<string>() == "Cancelled";
            }
            report["postCancellationLoadedModels"] = await Measurements.GetAsync(client, "/api/ps", cancellationToken);
            var recovery = await ReasonAsync(client, fixtures[0], cancellationToken, transport.Observer);
            report["postCancellationRecovery"] = recovery;
            allPassed &= recovery["state"]!.GetValue<string>() == "Completed"
                && recovery["quality"]!["Passed"]!.GetValue<bool>();
            report["serverCancellationLimitation"] = "Client cancellation/recovery do not alone prove server computation stopped. Correlate independent process/egress capture and post-cancel resource observations.";
            if (qualification is not null)
            {
                var cancellations = new JsonArray();
                report["cancellationTrials"] = cancellations;
                foreach (var path in new[] { "stream", "production" })
                    for (var repetition = 1; repetition <= 3; repetition++)
                        foreach (var delayMs in new[] { 50, 200, 1000 })
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            using var cancel = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                            cancel.CancelAfter(TimeSpan.FromMilliseconds(delayMs));
                            JsonNode result = path == "stream"
                                ? JsonSerializer.SerializeToNode(await Measurements.StreamAsync(client, cancelPayload, cancel.Token, transport.Observer), Json)!
                                : await ReasonAsync(client, fixtures[0], cancel.Token, transport.Observer);
                            var state = result[path == "stream" ? "State" : "state"]!.GetValue<string>();
                            // This observation window is diagnostic sampling, never proof of physical computation cessation.
                            await using var residual = new ResourceMeter(transport.Observer);
                            if (transportOverride is null) await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
                            var residualResources = await residual.FinishAsync();
                            var next = await ReasonAsync(client, fixtures[0], cancellationToken, transport.Observer);
                            allPassed &= state is "Cancelled" or "Completed" && next["state"]!.GetValue<string>() == "Completed"
                                && next["quality"]!["Passed"]!.GetValue<bool>();
                            cancellations.Add(new JsonObject
                            {
                                ["trial"] = repetition,
                                ["scheduledCancellationMs"] = delayMs,
                                ["path"] = path,
                                ["result"] = result,
                                ["recovery"] = next,
                                ["postClientReturnResources"] = JsonSerializer.SerializeToNode(residualResources, Json),
                                ["serverCessationStatus"] = "Blocked: sampled resources cannot establish exact server computation cessation.",
                                ["raceOutcome"] = state == "Completed" ? "Completed before cancellation; not a cancellation pass." : state,
                            });
                            if (transportOverride is null)
                                Console.WriteLine($"cancel {path} repeat={repetition} delay={delayMs} ms: {state}; recovery={next["state"]}");
                            if (checkpoint is not null) await checkpoint();
                        }
            }
            var missingPayload = Measurements.StreamingPayload(await CapturePayloadAsync(fixtures[0]));
            missingPayload["model"] = "r02-synthetic-model-that-is-not-installed";
            var missing = await Measurements.StreamAsync(client, missingPayload, cancellationToken, transport.Observer);
            report["realMissingModelError"] = JsonSerializer.SerializeToNode(missing, Json);
            allPassed &= missing.State == "Unavailable";
            report["calls"] = JsonSerializer.SerializeToNode(transport.Calls);
            report["experimentRequests"] = JsonSerializer.SerializeToNode(transport.GenerationPayloads);
            report["automatedQualityAndClientChecksPassed"] = allPassed;
            report["state"] = "Evidence collected; not accepted. Human rubric, floor/network proof and cancellation containment still required.";
            return allPassed ? 0 : 3;
        }
        finally
        {
            try
            {
                if (qualification is not null)
                {
                    try
                    {
                        using var cleanupTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                        await VerifyProfileAsync(client, qualification.RuntimeVersion, cleanupTimeout.Token);
                        report["finalLoadedModels"] = await Measurements.UnloadAsync(client, cleanupTimeout.Token);
                        report["cleanup"] = "Selected test model confirmed unloaded; pre-existing server and assets retained.";
                    }
                    catch (Exception exception) when (exception is HttpRequestException or JsonException or IOException
                        or InvalidOperationException or OperationCanceledException)
                    {
                        report["cleanup"] = "Unknown: " + exception.GetType().Name + ": " + exception.Message;
                        throw new InvalidOperationException("Could not confirm owned model cleanup; inspect before any replay.", exception);
                    }
                }
            }
            finally
            {
                report["calls"] = JsonSerializer.SerializeToNode(transport.Calls);
                report["experimentRequests"] = JsonSerializer.SerializeToNode(transport.GenerationPayloads);
                if (checkpoint is not null) await checkpoint();
            }
        }
    }

    internal static async Task<string> CapturePayloadAsync(Fixture fixture)
    {
        var captured = await RequestEnvelope.CaptureAsync(fixture.Prompt);
        return captured.Payload ?? throw new InvalidDataException("Fixture request was rejected: " + captured.Rejection);
    }

    internal static async Task<(JsonNode Version, JsonNode Catalogue)> VerifyProfileAsync(
        HttpClient client, string runtimeVersion, CancellationToken cancellationToken)
    {
        var version = await Measurements.GetAsync(client, "/api/version", cancellationToken);
        SelfTests.Require(version["version"]?.GetValue<string>() == runtimeVersion,
            "Installed runtime is not the explicitly selected version; refusing generation or residency changes.");
        var catalogue = await Measurements.GetAsync(client, "/api/tags", cancellationToken);
        var selected = catalogue["models"]!.AsArray()
            .SingleOrDefault(m => m!["name"]?.GetValue<string>() == WindowsOllamaSetupService.Model);
        SelfTests.Require(OllamaModelIdentity.HasPinnedDigest(selected?["digest"]?.GetValue<string>()),
            "Pinned model is missing or changed; refusing generation or residency changes.");
        return (version, catalogue);
    }
}