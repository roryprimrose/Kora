using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text.Json;
using AwesomeAssertions;
using GitHub.Copilot;
using GitHub.Copilot.Rpc;
using Kora.Rt1;
using Microsoft.Extensions.AI;

[assembly: Xunit.v3.Parallelization(Mode = Xunit.Sdk.ParallelMode.None)]

namespace Kora.Rt2;

public sealed class LifecycleTests
{
    private static string Root([CallerFilePath] string file = "") => Path.GetDirectoryName(file)!;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private static async Task RunAsync(string id, Func<RuntimeFixture, SyntheticProvider, Task> action)
    {
        var started = DateTimeOffset.UtcNow;
        using var source = new ActivitySource("Kora.Experiments.Rt2", "1.0.0");
        using var listener = new ActivityListener
        {
            ShouldListenTo = value => value == source,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData
        };
        ActivitySource.AddActivityListener(listener);
        using var activity = source.StartActivity("runtime.lifecycle.observation");
        activity?.SetTag("rt2.trial.id", id);
        var scratch = Path.Combine(Candidate.Root, ".scratch");
        var observer = new LifecycleObserver(scratch);
        var runtime = new RuntimeFixture();
        var provider = new SyntheticProvider();
        var passed = false;
        var cleaned = false;
        FileReceipt[] recoverable = [];
        object? observation = null;
        var liveAfterShutdown = -1;
        try
        {
            observer.Start();
            observer.MarkPhase("pre-startup");
            provider.Start();
            await runtime.StartAsync();
            await observer.WaitForRuntimeAsync();
            observer.MarkPhase("runtime-initialized-status-verified");
            await action(runtime, provider).WaitAsync(TimeSpan.FromSeconds(45));
            observer.MarkPhase("session-path-complete");
            recoverable = await observer.InspectRecoverableFilesAsync();
            recoverable.Should().OnlyContain(file => !file.DeniedMarker && !file.CredentialMarker);
            // The intentionally denied discovery input is fixture-created, never
            // admitted context or a runtime-created transcript.
            recoverable.Where(file => file.DiscoveryCanary).Should().OnlyContain(file => file.RelativePath.EndsWith("AGENTS.md", StringComparison.Ordinal));
            provider.Requests.Should().OnlyContain(request =>
                !request.Body.Contains(Candidate.Denied, StringComparison.Ordinal)
                && !request.Body.Contains(Candidate.Credential, StringComparison.Ordinal)
                && !request.Body.Contains(Candidate.Collected, StringComparison.Ordinal));
            provider.InfrastructureErrors.Should().Be(0);
            passed = true;
        }
        finally
        {
            try
            {
                observer.MarkPhase("shutdown-requested");
                try { await runtime.DisposeAsync(); }
                finally { await provider.DisposeAsync(); }
                liveAfterShutdown = observer.LiveOwnedProcesses();
                liveAfterShutdown.Should().Be(0, "observed PID/start-time identities must terminate, not just acknowledge abort");
                runtime.CleanupCompleted.Should().Be(1);
                runtime.StoreCount.Should().Be(0);
                cleaned = true;
                observer.MarkPhase("observed-owned-quiescence");
            }
            finally
            {
                await observer.DisposeAsync();
                activity?.SetStatus(ActivityStatusCode.Error, "rt2.all_path_observation_blocked");
                observation = observer.Receipt();
                var report = new
                {
                    Id = id, StartedUtc = started, EndedUtc = DateTimeOffset.UtcNow,
                    Trial = passed && cleaned ? "PASS" : "FAIL",
                    Rt2 = "BLOCKED: incomplete all-path attribution/prevention; passing trial is not RT2 acceptance",
                    Profile = "unchanged linked RT1 minimal HTTP/stdio profile, approved primary source-built bytes",
                    TraceId = activity?.TraceId.ToString(), SpanId = activity?.SpanId.ToString(),
                    Observation = observation, RecoverableBeforeCleanup = recoverable,
                    ObservedImageSha256 = await observer.ImageHashesAsync(),
                    ProviderRequests = provider.Requests.Count,
                    DeniedMarkersAtProvider = provider.Requests.Count(value => value.Body.Contains(Candidate.Denied, StringComparison.Ordinal)),
                    CredentialInModelBody = provider.Requests.Count(value => value.Body.Contains(Candidate.Credential, StringComparison.Ordinal)),
                    SyntheticAuthenticationHeaders = provider.Requests.Count(value => value.CredentialInHeader),
                    BoundaryReceipts = runtime.Boundary.Receipts.ToArray(),
                    runtime.Boundary.CancellationObserved, runtime.Boundary.WebSocketsDenied,
                    runtime.VolatileWrites, runtime.SessionWriteRejections,
                    runtime.OwnedEffectWrites, runtime.CleanupCompleted, LiveObservedOwnedProcessesAfterShutdown = liveAfterShutdown,
                    RuntimeEventTypes = runtime.Events.GroupBy(value => value.GetType().Name)
                        .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal),
                    SdkAssemblySha256 = await Candidate.HashAsync(typeof(CopilotClient).Assembly.Location),
                    FixtureAssemblySha256 = await Candidate.HashAsync(typeof(LifecycleTests).Assembly.Location),
                    NativeLauncherSha256 = await Candidate.HashAsync(Candidate.Executable),
                    NativePayloadSha256 = await Candidate.HashAsync(Path.Combine(Candidate.RuntimeDirectory, "runtime.node"))
                };
                Directory.CreateDirectory(Path.Combine(Root(), "evidence", "trials"));
                await File.WriteAllTextAsync(Path.Combine(Root(), "evidence", "trials", id + ".json"), JsonSerializer.Serialize(report, JsonOptions));
            }
        }
    }

    private static async Task ErrorAsync(CopilotSession session, string prompt)
    {
        var send = async () => await session.SendAndWaitAsync(prompt, TimeSpan.FromSeconds(12));
        await send.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public Task HappyStreamingHistoryAndVolatileIo() => RunAsync("happy-history", async (runtime, provider) =>
    {
        provider.Enqueue(Reply.Text, Reply.Text);
        var session = await runtime.SessionAsync(provider);
        var answer = await session.SendAndWaitAsync("RT1_APPROVED_INITIAL", TimeSpan.FromSeconds(12));
        answer!.Data.Content.Should().Be("RT1_APPROVED_ANSWER");
        await session.SendAndWaitAsync("RT1_APPROVED_FOLLOWUP", TimeSpan.FromSeconds(12));
        provider.Requests.Last().Body.Should().Contain("RT1_APPROVED_INITIAL").And.Contain("RT1_APPROVED_ANSWER");
        runtime.VolatileWrites.Should().BeGreaterThan(0);
        runtime.Events.OfType<AssistantMessageDeltaEvent>().Should().NotBeEmpty();
    });

    [Fact]
    public Task DeniedInitialContentNeverReachesProvider() => RunAsync("denied-context", async (runtime, provider) =>
    {
        var session = await runtime.SessionAsync(provider);
        await ErrorAsync(session, Candidate.Denied);
        provider.Requests.Should().BeEmpty();
        runtime.Boundary.Receipts.Should().Contain(value => value.Blocked && value.DeniedMarker);
    });

    [Theory]
    [InlineData(401)]
    [InlineData(429)]
    [InlineData(500)]
    public Task AuthenticationErrorAndRetryAreTruthful(int status) => RunAsync("error-retry-" + status, async (runtime, provider) =>
    {
        provider.Enqueue(new Reply("error", Status: status));
        var session = await runtime.SessionAsync(provider, maximumRequests: 1);
        await ErrorAsync(session, "RT1_APPROVED_ERROR");
        provider.Requests.Count.Should().Be(1);
        runtime.Events.OfType<SessionErrorEvent>().Should().NotBeEmpty();
        if (status != 401) runtime.Boundary.Receipts.Should().Contain(value => value.Reason == "retry-bound");
    });

    [Theory]
    [InlineData("failure")]
    [InlineData("denied")]
    public Task UnsafeResultDeniedBeforeContinuation(string status) => RunAsync("result-" + status, async (runtime, provider) =>
    {
        var tool = CopilotTool.DefineTool(async (ToolInvocation _) =>
        {
            await runtime.ReadOwnedScratchAsync();
            return new ToolResultAIContent(new ToolResultObject { ResultType = status, TextResultForLlm = Candidate.Denied });
        }, factoryOptions: new AIFunctionFactoryOptions { Name = "rt2_scratch", Description = "Harmless owned scratch read" });
        provider.Enqueue(new Reply("tool", tool.Name), Reply.Text);
        var session = await runtime.SessionAsync(provider, tools: [tool]);
        await ErrorAsync(session, "RT1_APPROVED_RESULT");
        runtime.OwnedScratchReads.Should().Be(1);
        provider.Requests.Count.Should().Be(1);
        runtime.Boundary.Receipts.Should().Contain(value => value.Blocked && value.DeniedMarker);
    });

    [Fact]
    public Task DeniedToolHasNoEffect() => RunAsync("tool-pre-effect-denial", async (runtime, provider) =>
    {
        var tool = CopilotTool.DefineTool(async (ToolInvocation _) =>
        {
            await runtime.WriteOwnedEffectAsync();
            return "synthetic";
        }, factoryOptions: new AIFunctionFactoryOptions { Name = "rt2_scratch", Description = "Harmless owned scratch write" });
        provider.Enqueue(new Reply("tool", tool.Name), Reply.Text);
        var session = await runtime.SessionAsync(provider, tools: [tool], hooks: new SessionHooks
        {
            OnPreToolUse = (_, _) => Task.FromResult<PreToolUseHookOutput?>(new() { PermissionDecision = "deny" })
        });
        await session.SendAndWaitAsync("RT1_APPROVED_DENIAL", TimeSpan.FromSeconds(12));
        runtime.OwnedEffectWrites.Should().Be(0);
    });

    [Fact]
    public Task ExcludedBuiltinsStayUnavailable() => RunAsync("excluded-builtins", async (runtime, provider) =>
    {
        provider.Enqueue(new Reply("tool", "powershell", "{\"command\":\"Write-Output 'synthetic'\"}"), Reply.Text);
        var session = await runtime.SessionAsync(provider);
        await session.SendAndWaitAsync("RT1_APPROVED_EMPTY", TimeSpan.FromSeconds(12));
        foreach (var request in provider.Requests)
        {
            using var json = JsonDocument.Parse(request.Body);
            if (json.RootElement.TryGetProperty("tools", out var tools)) tools.GetArrayLength().Should().Be(0);
        }
        runtime.Events.OfType<ToolExecutionCompleteEvent>().Should().OnlyContain(value => !value.Data.Success);
        runtime.OwnedEffectWrites.Should().Be(0);
    });

    [Fact]
    public Task VolatileWriteDenialIsNotDiskFallbackSuccess() => RunAsync("session-io-denial", async (runtime, provider) =>
    {
        provider.Enqueue(Reply.Text);
        var session = await runtime.SessionAsync(provider, rejectSessionWrites: true);
        await ErrorAsync(session, "RT1_APPROVED_VOLATILE_DENIAL");
        runtime.SessionWriteRejections.Should().BeGreaterThan(0);
        runtime.StoreCount.Should().Be(0);
    });

    [Fact]
    public Task HeldRequestCancellationObservesConnectionTermination() => RunAsync("held-cancellation", async (runtime, provider) =>
    {
        provider.Enqueue(Reply.Hold);
        var session = await runtime.SessionAsync(provider);
        await session.SendAsync(new MessageOptions { Prompt = "RT1_APPROVED_HELD" });
        await provider.WaitForRequestsAsync(1);
        runtime.Boundary.Cancel(session.SessionId);
        await session.AbortAsync().WaitAsync(TimeSpan.FromSeconds(5));
        using var bound = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (Volatile.Read(ref runtime.Boundary.CancellationObserved) == 0 || Volatile.Read(ref provider.ClosedConnections) == 0)
            await Task.Delay(TimeSpan.FromMilliseconds(10), bound.Token);
        var before = runtime.Boundary.Receipts.Count;
        await session.SendAsync(new MessageOptions { Prompt = "RT1_APPROVED_LATE" });
        await runtime.Boundary.WaitForReceiptsAsync(before + 1);
        runtime.Boundary.Receipts.Last().Reason.Should().Be("cancelled");
        provider.Requests.Count.Should().Be(1);
    });

    [Fact]
    public Task AdmittedEffectCompletesAfterAbortAndWasUnknownAtCancellation() => RunAsync("admitted-effect-unknown", async (runtime, provider) =>
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tool = CopilotTool.DefineTool(async (ToolInvocation _) =>
        {
            started.SetResult();
            await release.Task;
            await runtime.WriteOwnedEffectAsync();
            completed.SetResult();
            return new ToolResultAIContent(new ToolResultObject { ResultType = "success", TextResultForLlm = Candidate.Denied });
        }, factoryOptions: new AIFunctionFactoryOptions { Name = "rt2_scratch", Description = "Harmless noncooperative owned write" });
        provider.Enqueue(new Reply("tool", tool.Name), Reply.Text);
        var session = await runtime.SessionAsync(provider, tools: [tool]);
        await session.SendAsync(new MessageOptions { Prompt = "RT1_APPROVED_NONCOOPERATIVE" });
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            runtime.Boundary.Cancel(session.SessionId);
            await session.AbortAsync().WaitAsync(TimeSpan.FromSeconds(5));
            runtime.OwnedEffectWrites.Should().Be(0, "the admitted operation remains Unknown, not rolled back");
        }
        finally { release.TrySetResult(); }
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        runtime.OwnedEffectWrites.Should().Be(1);
        var before = runtime.Boundary.Receipts.Count;
        await session.SendAsync(new MessageOptions { Prompt = "RT1_APPROVED_AFTER_EFFECT" });
        await runtime.Boundary.WaitForReceiptsAsync(before + 1);
        runtime.Boundary.Receipts.Last().Blocked.Should().BeTrue();
        provider.Requests.Count.Should().Be(1);
    });

    [Fact]
    public Task WaitTimeoutDoesNotTerminateTransport() => RunAsync("timeout-not-termination", async (runtime, provider) =>
    {
        provider.Enqueue(Reply.Hold);
        var session = await runtime.SessionAsync(provider);
        var send = session.SendAndWaitAsync("RT1_APPROVED_TIMEOUT", TimeSpan.FromMilliseconds(500));
        await provider.WaitForRequestsAsync(1);
        var action = async () => await send;
        await action.Should().ThrowAsync<TimeoutException>();
        runtime.Boundary.CancellationObserved.Should().Be(0);
        provider.ClosedConnections.Should().Be(0);
        runtime.Boundary.Cancel(session.SessionId);
        await session.AbortAsync().WaitAsync(TimeSpan.FromSeconds(5));
    });

    [Fact]
    public async Task LiveFileAndSocketPositiveControlsAreVisibleBeforeDeletion()
    {
        var root = Path.Combine(Root(), ".scratch", "control-" + Guid.NewGuid().ToString("N"));
        await using var observer = new LifecycleObserver(root);
        var listener = new TcpListener(IPAddress.Loopback, 0);
        var started = DateTimeOffset.UtcNow;
        try
        {
            observer.Start();
            listener.Start();
            using var client = new TcpClient();
            await client.ConnectAsync((IPEndPoint)listener.LocalEndpoint, TestContext.Current.CancellationToken).AsTask()
                .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            using var accepted = await listener.AcceptTcpClientAsync(TestContext.Current.CancellationToken).AsTask()
                .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            observer.Sample();
            var path = Path.Combine(root, "positive.txt");
            await File.WriteAllTextAsync(path, Candidate.Denied + " " + Candidate.Credential, TestContext.Current.CancellationToken);
            var captured = await observer.InspectRecoverableFilesAsync();
            captured.Single().DeniedMarker.Should().BeTrue();
            captured.Single().CredentialMarker.Should().BeTrue();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (true)
            {
                using var receipt = JsonDocument.Parse(JsonSerializer.Serialize(observer.Receipt(), JsonOptions));
                if (receipt.RootElement.GetProperty("fileNotifications").GetArrayLength() > 0) break;
                await Task.Delay(20, deadline.Token);
            }
            using var snapshot = JsonDocument.Parse(JsonSerializer.Serialize(observer.Receipt(), JsonOptions));
            snapshot.RootElement.GetProperty("socketSnapshots").EnumerateArray().Should().Contain(value =>
                value.GetProperty("processId").GetInt32() == Environment.ProcessId &&
                value.GetProperty("remotePort").ValueKind == JsonValueKind.Number &&
                value.GetProperty("remotePort").GetInt32() == ((IPEndPoint)listener.LocalEndpoint).Port);
            snapshot.RootElement.GetProperty("managedDiagnosticEventCounts").EnumerateObject().Should().NotBeEmpty();
            File.Delete(path);
            await observer.DisposeAsync();
            await File.WriteAllTextAsync(Path.Combine(Root(), "evidence", "positive-controls.json"),
                JsonSerializer.Serialize(new { StartedUtc = started, EndedUtc = DateTimeOffset.UtcNow, Status = "PASS",
                    MarkerFileObservedBeforeDeletion = captured, Observation = observer.Receipt(),
                    SyntheticOnly = true, Interpretation = "Detection, not prevention; only intentionally authorized owned control markers persisted" }, JsonOptions),
                TestContext.Current.CancellationToken);
        }
        finally
        {
            listener.Stop();
            Directory.Delete(root, recursive: true);
        }
    }
}
