using System.Diagnostics;
using System.Text.Json;
using AwesomeAssertions;
using GitHub.Copilot;
using GitHub.Copilot.Rpc;
using Microsoft.Extensions.AI;

[assembly: Xunit.v3.Parallelization(Mode = Xunit.Sdk.ParallelMode.None)]

namespace Kora.Rt1;

public sealed class ConformanceTests
{
    private static async Task RunAsync(string id, Func<Trial, Task> execute)
    {
        var runtime = new RuntimeFixture();
        var provider = new SyntheticProvider();
        var manager = new SyntheticProvider();
        var trial = new Trial(runtime, provider, manager);
        var passed = false;
        var cleaned = false;
        try
        {
            provider.Start();
            manager.Start();
            await runtime.StartAsync();
            await execute(trial).WaitAsync(TimeSpan.FromSeconds(45));
            await runtime.ScanScratchAsync();
            runtime.DiskMarkerFiles.Should().Be(0);
            provider.Requests.Concat(manager.Requests)
                .Count(request => request.Body.Contains(Candidate.Denied, StringComparison.Ordinal)
                    || request.Body.Contains(Candidate.Collected, StringComparison.Ordinal)).Should().Be(0);
            provider.InfrastructureErrors.Should().Be(0);
            manager.InfrastructureErrors.Should().Be(0);
            passed = true;
        }
        finally
        {
            try
            {
                try { await runtime.DisposeAsync(); }
                finally
                {
                    try { await provider.DisposeAsync(); }
                    finally { await manager.DisposeAsync(); }
                }
                runtime.StoreCount.Should().Be(0);
                cleaned = true;
            }
            finally
            {
                trial.Counters["providerRequests"] = provider.Requests.Count;
                trial.Counters["managementRequests"] = manager.Requests.Count;
                trial.Counters["finalRequests"] = runtime.Boundary.Receipts.Count;
                trial.Counters["blockedRequests"] = runtime.Boundary.Receipts.Count(receipt => receipt.Blocked);
                trial.Counters["deniedMarkerForwarded"] = provider.Requests.Concat(manager.Requests)
                    .Count(request => request.Body.Contains(Candidate.Denied, StringComparison.Ordinal));
                trial.Counters["finalCredentialInBody"] = runtime.Boundary.Receipts.Count(receipt => receipt.CredentialInBody);
                trial.Counters["volatileWrites"] = runtime.VolatileWrites;
                trial.Counters["diskFiles"] = runtime.DiskFiles;
                trial.Counters["diskMarkerFiles"] = runtime.DiskMarkerFiles;
                trial.Counters["ownedCleanupCompleted"] = cleaned ? 1 : 0;
                trial.Counters["ownedEffectWrites"] = runtime.OwnedEffectWrites;
                trial.Counters["ownedScratchReads"] = runtime.OwnedScratchReads;
                await Evidence.RecordAsync(new ProofRow(id, passed && cleaned ? trial.CapabilityStatus : "FAIL",
                    DateTimeOffset.UtcNow, trial.Counters, trial.Facts));
            }
        }
    }

    private static async Task ExpectProviderErrorAsync(CopilotSession session, string prompt)
    {
        var action = async () => await session.SendAndWaitAsync(prompt, TimeSpan.FromSeconds(12));
        await action.Should().ThrowAsync<InvalidOperationException>();
    }

    private static AIFunction Tool(Func<ToolInvocation, Task<ToolResultAIContent>> action) =>
        CopilotTool.DefineTool(action, factoryOptions: new AIFunctionFactoryOptions
        {
            Name = "rt1_scratch", Description = "Harmless synthetic owned-scratch counter; no OS action."
        });

    private static ToolResultAIContent Result(string status, bool unsafeMarker)
    {
        var sdkStatus = status switch
        {
            "success" => "success", "denied" => "denied", "rejected" => "rejected", _ => "failure"
        };
        return new ToolResultAIContent(new ToolResultObject
        {
            ResultType = sdkStatus,
            TextResultForLlm = JsonSerializer.Serialize(new
            {
                status, receipt = unsafeMarker ? Candidate.Denied : "RT1_APPROVED_RECEIPT", observed = true
            }),
            Error = sdkStatus == "failure" ? (unsafeMarker ? Candidate.Denied : "synthetic-failure") : null
        });
    }

    [Fact]
    public Task InitialContextAndStreamingUseActualPublicControls() => RunAsync("initial-streaming-auth", async trial =>
    {
        trial.Provider.Enqueue(Reply.Text);
        var submitted = 0;
        var transformed = 0;
        var session = await trial.Runtime.SessionAsync(trial.Provider, hooks: new SessionHooks
        {
            OnUserPromptSubmitted = (input, _) =>
            {
                submitted++;
                return Task.FromResult<UserPromptSubmittedHookOutput?>(new()
                {
                    ModifiedPrompt = input.Prompt.Replace(Candidate.Denied, "[withheld]", StringComparison.Ordinal)
                });
            },
            OnUserPromptTransformed = (input, _) =>
            {
                transformed++;
                return Task.FromResult<UserPromptTransformedHookOutput?>(new() { ModifiedTransformedPrompt = input.TransformedPrompt });
            }
        });
        var answer = await session.SendAndWaitAsync("RT1_APPROVED_INITIAL " + Candidate.Denied, TimeSpan.FromSeconds(12));
        answer!.Data.Content.Should().Be("RT1_APPROVED_ANSWER");
        submitted.Should().Be(1);
        transformed.Should().Be(1);
        trial.Runtime.Events.OfType<AssistantMessageDeltaEvent>().Count().Should().BeGreaterThanOrEqualTo(2);
        trial.Runtime.Events.OfType<SessionIdleEvent>().Should().NotBeEmpty();
        var request = trial.Provider.Requests.Single();
        request.CredentialInHeader.Should().BeTrue();
        request.Body.Should().Contain("RT1_APPROVED_INITIAL").And.Contain("RT1_APPROVED_SYSTEM");
        trial.Counters["submittedHooks"] = submitted;
        trial.Counters["transformedHooks"] = transformed;
        trial.Counters["streamDeltas"] = trial.Runtime.Events.OfType<AssistantMessageDeltaEvent>().Count();
        trial.Counters["syntheticCredentialHeader"] = request.CredentialInHeader ? 1 : 0;
    });

    [Fact]
    public Task FinalGateBlocksUnsanitizedInitialContext() => RunAsync("unsafe-initial-final-gate", async trial =>
    {
        var session = await trial.Runtime.SessionAsync(trial.Provider);
        await ExpectProviderErrorAsync(session, Candidate.Denied);
        trial.Provider.Requests.Should().BeEmpty();
        trial.Runtime.Boundary.Receipts.Should().Contain(receipt => receipt.Blocked && receipt.DeniedMarker);
    });

    [Fact]
    public Task RuntimeAddedAssistantHistoryCannotBypassFinalGate() => RunAsync("unsafe-runtime-history", async trial =>
    {
        trial.Provider.Enqueue(new Reply("text", Content: Candidate.Denied));
        var session = await trial.Runtime.SessionAsync(trial.Provider);
        await session.SendAndWaitAsync("RT1_APPROVED_FIRST", TimeSpan.FromSeconds(12));
        await ExpectProviderErrorAsync(session, "RT1_APPROVED_SECOND");
        trial.Provider.Requests.Count.Should().Be(1);
        trial.Runtime.Boundary.Receipts.Should().Contain(receipt => receipt.Blocked && receipt.DeniedMarker);
    });

    [Fact]
    public Task ApprovedConversationHistoryIsObservedInFullFinalRequest() => RunAsync("approved-history", async trial =>
    {
        trial.Provider.Enqueue(Reply.Text, Reply.Text);
        var session = await trial.Runtime.SessionAsync(trial.Provider);
        await session.SendAndWaitAsync("RT1_APPROVED_FIRST", TimeSpan.FromSeconds(12));
        await session.SendAndWaitAsync("RT1_APPROVED_SECOND", TimeSpan.FromSeconds(12));
        var request = trial.Provider.Requests.Last();
        request.Body.Should().Contain("RT1_APPROVED_FIRST").And.Contain("RT1_APPROVED_SECOND").And.Contain("RT1_APPROVED_ANSWER");
        trial.Runtime.Boundary.Receipts.Count.Should().Be(2);
    });

    [Fact]
    public Task PreEffectDenialNeverInvokesTheTool() => RunAsync("denied-tool-zero-effects", async trial =>
    {
        var effects = 0;
        var hooks = 0;
        var tool = Tool(async _ =>
        {
            await trial.Runtime.WriteOwnedEffectAsync();
            effects++;
            return Result("success", true);
        });
        trial.Provider.Enqueue(new Reply("tool", tool.Name), Reply.Text);
        var session = await trial.Runtime.SessionAsync(trial.Provider, tools: [tool], hooks: new SessionHooks
        {
            OnPreToolUse = (_, _) =>
            {
                hooks++;
                return Task.FromResult<PreToolUseHookOutput?>(new() { PermissionDecision = "deny", PermissionDecisionReason = "synthetic-denial" });
            }
        });
        await session.SendAndWaitAsync("RT1_APPROVED_DENIAL_TRIAL", TimeSpan.FromSeconds(12));
        hooks.Should().Be(1);
        effects.Should().Be(0);
        trial.Runtime.OwnedEffectWrites.Should().Be(0);
        trial.Counters["preToolGates"] = hooks;
        trial.Counters["deniedToolEffects"] = effects;
    });

    [Theory]
    [InlineData("success")]
    [InlineData("failure")]
    [InlineData("denied")]
    [InlineData("rejected")]
    [InlineData("unavailable")]
    [InlineData("cancelled")]
    [InlineData("unknown")]
    [InlineData("timeout")]
    [InlineData("blocked")]
    [InlineData("conflict")]
    [InlineData("pending")]
    public Task EveryHostResultStatusReachesFinalMediation(string status) => RunAsync("unsafe-result-" + status, async trial =>
    {
        var effects = 0;
        var tool = Tool(async invocation =>
        {
            invocation.SessionId.Should().NotBeNullOrEmpty();
            await trial.Runtime.ReadOwnedScratchAsync();
            effects++;
            return Result(status, true);
        });
        trial.Provider.Enqueue(new Reply("tool", tool.Name), Reply.Text);
        var session = await trial.Runtime.SessionAsync(trial.Provider, tools: [tool]);
        if (status == "rejected")
        {
            await session.SendAndWaitAsync("RT1_APPROVED_RESULT_TRIAL", TimeSpan.FromSeconds(12));
            trial.Provider.Requests.Count.Should().Be(1);
            trial.Facts["immediateContinuation"] = "Rejected result ends the turn; next user send tests retained history.";
            await ExpectProviderErrorAsync(session, "RT1_APPROVED_FOLLOWUP");
        }
        else await ExpectProviderErrorAsync(session, "RT1_APPROVED_RESULT_TRIAL");
        effects.Should().Be(1);
        trial.Provider.Requests.Count.Should().Be(1);
        trial.Runtime.Boundary.Receipts.Should().Contain(receipt => receipt.Blocked && receipt.DeniedMarker);
        trial.Counters["admittedScratchReads"] = effects;
        trial.Facts["hostStatus"] = status;
        trial.Facts["sdkResultType"] = status is "success" or "denied" or "rejected" ? status : "failure";
    });

    [Theory]
    [InlineData("success")]
    [InlineData("failure")]
    [InlineData("denied")]
    [InlineData("rejected")]
    [InlineData("unavailable")]
    [InlineData("cancelled")]
    [InlineData("unknown")]
    [InlineData("timeout")]
    [InlineData("blocked")]
    [InlineData("conflict")]
    [InlineData("pending")]
    public Task TruthfulSanitizedResultsCanContinue(string status) => RunAsync("sanitized-result-" + status, async trial =>
    {
        var effects = 0;
        var tool = Tool(async _ =>
        {
            await trial.Runtime.ReadOwnedScratchAsync();
            effects++;
            return Result(status, false);
        });
        trial.Provider.Enqueue(new Reply("tool", tool.Name), Reply.Text);
        var session = await trial.Runtime.SessionAsync(trial.Provider, tools: [tool]);
        await session.SendAndWaitAsync("RT1_APPROVED_RESULT_TRIAL", TimeSpan.FromSeconds(12));
        if (status == "rejected")
        {
            trial.Provider.Requests.Count.Should().Be(1);
            await session.SendAndWaitAsync("RT1_APPROVED_FOLLOWUP", TimeSpan.FromSeconds(12));
            trial.Facts["immediateContinuation"] = "Rejected result ends the turn; next user send tests retained history.";
        }
        effects.Should().Be(1);
        trial.Provider.Requests.Count.Should().Be(2);
        trial.Provider.Requests.Last().Body.Should().Contain(status).And.Contain("RT1_APPROVED_RECEIPT");
        trial.Counters["admittedScratchReads"] = effects;
        trial.Facts["hostStatus"] = status;
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task ThrownToolExceptionIsGatedBeforeForwarding(bool asynchronous) => RunAsync("unsafe-thrown-tool-exception-" + asynchronous, async trial =>
    {
        var effects = 0;
        var tool = Tool(_ =>
        {
            effects++;
            trial.Runtime.Boundary.RejectAll = true;
            if (asynchronous) return Task.FromException<ToolResultAIContent>(new InvalidOperationException(Candidate.Denied));
            throw new InvalidOperationException(Candidate.Denied);
        });
        trial.Provider.Enqueue(new Reply("tool", tool.Name), Reply.Text);
        var session = await trial.Runtime.SessionAsync(trial.Provider, tools: [tool]);
        await ExpectProviderErrorAsync(session, "RT1_APPROVED_EXCEPTION_TRIAL");
        effects.Should().Be(1);
        trial.Provider.Requests.Count.Should().Be(1);
        trial.Runtime.Boundary.Receipts.Should().Contain(receipt => receipt.Blocked && receipt.Reason == "trial-denial");
        trial.Counters["thrownToolCalls"] = effects;
        trial.Counters["exceptionMarkerAtFinalGate"] = trial.Runtime.Boundary.Receipts.Count(receipt => receipt.DeniedMarker);
        trial.Facts["boundary"] = "Actual SDK exception result inspected and denied regardless of SDK exception-message formatting.";
    });

    [Fact]
    public Task SuccessPostToolHookCanReplacePayloadButIsNotUniversalMediation() => RunAsync("success-post-tool-hook", async trial =>
    {
        var hooks = 0;
        var tool = Tool(_ => Task.FromResult(Result("success", true)));
        trial.Provider.Enqueue(new Reply("tool", tool.Name), Reply.Text);
        var session = await trial.Runtime.SessionAsync(trial.Provider, tools: [tool], hooks: new SessionHooks
        {
            OnPostToolUse = (_, _) =>
            {
                hooks++;
                return Task.FromResult<PostToolUseHookOutput?>(new() { ModifiedResult = Result("success", false).Result });
            }
        });
        await session.SendAndWaitAsync("RT1_APPROVED_SUCCESS_HOOK", TimeSpan.FromSeconds(12));
        hooks.Should().Be(1);
        trial.Provider.Requests.Count.Should().Be(2);
        trial.Provider.Requests.Last().Body.Should().Contain("RT1_APPROVED_RECEIPT");
        trial.Counters["successHooks"] = hooks;
    });

    [Fact]
    public Task HookOnlyFailureRemainsRejectedRegressionWitness() => RunAsync("hook-only-failure-witness", async trial =>
    {
        var successHooks = 0;
        var failureHooks = 0;
        var tool = Tool(_ => Task.FromResult(Result("failure", true)));
        trial.Provider.Enqueue(new Reply("tool", tool.Name), Reply.Text);
        var session = await trial.Runtime.SessionAsync(trial.Provider, tools: [tool], hooks: new SessionHooks
        {
            OnPostToolUse = (_, _) =>
            {
                successHooks++;
                return Task.FromResult<PostToolUseHookOutput?>(new() { ModifiedResult = Result("failure", false).Result });
            },
            OnPostToolUseFailure = (_, _) =>
            {
                failureHooks++;
                return Task.FromResult<PostToolUseFailureHookOutput?>(new() { AdditionalContext = "withhold denied content" });
            }
        });
        await ExpectProviderErrorAsync(session, "RT1_APPROVED_HOOK_WITNESS");
        successHooks.Should().Be(0);
        failureHooks.Should().Be(1);
        trial.Runtime.Boundary.Receipts.Should().Contain(receipt => receipt.Blocked && receipt.DeniedMarker);
        trial.CapabilityStatus = "FAIL";
        trial.Counters["successHooks"] = successHooks;
        trial.Counters["failureHooks"] = failureHooks;
        trial.Facts["interpretation"] = "Expected rejected hook-only architecture; final gate prevented forwarding.";
    });

    [Theory]
    [InlineData(401)]
    [InlineData(429)]
    [InlineData(500)]
    public Task SyntheticProviderErrorsAreExplicitAndRetryForwardingIsBounded(int status) => RunAsync("provider-error-" + status, async trial =>
    {
        trial.Provider.Enqueue(new Reply("error", Status: status));
        var session = await trial.Runtime.SessionAsync(trial.Provider, maximumRequests: 1);
        await ExpectProviderErrorAsync(session, "RT1_APPROVED_ERROR_TRIAL");
        trial.Provider.Requests.Count.Should().Be(1);
        trial.Runtime.Events.OfType<SessionErrorEvent>().Should().NotBeEmpty();
        trial.Counters["blockedRetries"] = trial.Runtime.Boundary.Receipts.Count(receipt => receipt.Reason == "retry-bound");
    });

    [Theory]
    [InlineData("drop")]
    [InlineData("malformed")]
    public Task FailedStreamingDoesNotBecomeSuccessfulAnswer(string kind) => RunAsync("stream-failure-" + kind, async trial =>
    {
        trial.Provider.Enqueue(new Reply(kind));
        var session = await trial.Runtime.SessionAsync(trial.Provider, maximumRequests: 1);
        await ExpectProviderErrorAsync(session, "RT1_APPROVED_STREAM_FAILURE");
        trial.Runtime.Events.OfType<AssistantMessageEvent>().Should().BeEmpty();
        trial.Provider.Requests.Count.Should().Be(1);
    });

    [Fact]
    public Task MinimalProfileHasNoAdvertisedBuiltinsAndUsesVolatileSessionIo() => RunAsync("minimal-profile-session-io", async trial =>
    {
        trial.Provider.Enqueue(new Reply("tool", "powershell", "{\"command\":\"Write-Output 'synthetic'\"}"), Reply.Text);
        var session = await trial.Runtime.SessionAsync(trial.Provider);
        await session.SendAndWaitAsync("RT1_APPROVED_MINIMAL_PROFILE", TimeSpan.FromSeconds(12));
        foreach (var request in trial.Provider.Requests)
        {
            using var json = JsonDocument.Parse(request.Body);
            if (json.RootElement.TryGetProperty("tools", out var tools)) tools.GetArrayLength().Should().Be(0);
        }
        trial.Runtime.VolatileWrites.Should().BeGreaterThan(0);
        trial.Runtime.Events.OfType<ToolExecutionCompleteEvent>().Should().NotBeEmpty();
        trial.Runtime.Events.OfType<ToolExecutionCompleteEvent>().Should().OnlyContain(evt => !evt.Data.Success);
        trial.Counters["advertisedBuiltins"] = 0;
        trial.Facts["scope"] = "Collection canary absent; scoped disk scan only, not lifecycle containment.";
    });

    [Fact]
    public Task ExecutionAndManagementAreIndependentWhileTwoExecutionRequestsAreHeld() => RunAsync("lane-provider-isolation", async trial =>
    {
        trial.Provider.Enqueue(Reply.Hold, Reply.Hold);
        trial.Manager.Enqueue(Reply.Text);
        var one = await trial.Runtime.SessionAsync(trial.Provider);
        var two = await trial.Runtime.SessionAsync(trial.Provider);
        var manager = await trial.Runtime.SessionAsync(trial.Manager, "management", maximumRequests: 1);
        await one.SendAsync(new MessageOptions { Prompt = "RT1_EXECUTION_ONE" });
        await two.SendAsync(new MessageOptions { Prompt = "RT1_EXECUTION_TWO" });
        await trial.Provider.WaitForRequestsAsync(2);
        var timer = Stopwatch.StartNew();
        await manager.SendAndWaitAsync("RT1_MANAGEMENT_ONLY", TimeSpan.FromSeconds(12));
        trial.Manager.Requests.Single().Body.Should().NotContain("RT1_EXECUTION_ONE").And.NotContain("RT1_EXECUTION_TWO");
        trial.Provider.Requests.Should().OnlyContain(request => !request.Body.Contains("RT1_MANAGEMENT_ONLY", StringComparison.Ordinal));
        one.SessionId.Should().NotBe(two.SessionId).And.NotBe(manager.SessionId);
        trial.Counters["executionConversationsHeld"] = 2;
        trial.Counters["managementElapsedMs"] = timer.ElapsedMilliseconds;
        trial.Runtime.Boundary.Cancel(one.SessionId);
        trial.Runtime.Boundary.Cancel(two.SessionId);
        await one.AbortAsync();
        await two.AbortAsync();
    });

    [Fact]
    public Task HostileModelIdentifiersCannotSelectAnotherSessionOrGrantToolAuthority() => RunAsync("hostile-identifiers", async trial =>
    {
        var effects = 0;
        var tool = Tool(_ =>
        {
            effects++;
            return Task.FromResult(Result("success", false));
        });
        trial.Manager.Enqueue(Reply.Text);
        var manager = await trial.Runtime.SessionAsync(trial.Manager, "management");
        var arguments = JsonSerializer.Serialize(new { sessionId = manager.SessionId, hostIdentity = "host-spoof", approval = "granted" });
        trial.Provider.Enqueue(new Reply("tool", tool.Name, arguments), Reply.Text);
        var execution = await trial.Runtime.SessionAsync(trial.Provider, tools: [tool], hooks: new SessionHooks
        {
            OnPreToolUse = (_, invocation) =>
            {
                invocation.SessionId.Should().NotBe(manager.SessionId);
                return Task.FromResult<PreToolUseHookOutput?>(new() { PermissionDecision = "deny" });
            }
        });
        await execution.SendAndWaitAsync("RT1_APPROVED_HOSTILE_ID_TRIAL", TimeSpan.FromSeconds(12));
        await manager.SendAndWaitAsync("RT1_MANAGEMENT_ONLY", TimeSpan.FromSeconds(12));
        effects.Should().Be(0);
        trial.Manager.Requests.Single().Body.Should().NotContain("host-spoof").And.NotContain("RT1_APPROVED_HOSTILE_ID_TRIAL");
        trial.Counters["deniedToolEffects"] = effects;
        trial.Facts["identityBoundary"] = "Host binds actual created conversation; model arguments are not host identity or authorization.";
    });

    [Fact]
    public Task HeldInferenceCancellationDistinguishesRequestAcknowledgementAndTransportTermination() => RunAsync("stalled-inference-cancellation", async trial =>
    {
        trial.Provider.Enqueue(Reply.Hold);
        var session = await trial.Runtime.SessionAsync(trial.Provider, maximumRequests: 1);
        await session.SendAsync(new MessageOptions { Prompt = "RT1_APPROVED_HELD_INFERENCE" });
        await trial.Provider.WaitForRequestsAsync(1);
        trial.Counters["cancellationRequested"] = 1;
        trial.Runtime.Boundary.Cancel(session.SessionId);
        await session.AbortAsync().WaitAsync(TimeSpan.FromSeconds(5));
        trial.Counters["abortAcknowledged"] = 1;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (Volatile.Read(ref trial.Runtime.Boundary.CancellationObserved) == 0)
            await Task.Delay(TimeSpan.FromMilliseconds(10), timeout.Token);
        trial.Counters["runtimeCancellationTokenObserved"] = trial.Runtime.Boundary.CancellationObserved;
        while (Volatile.Read(ref trial.Provider.ClosedConnections) == 0)
            await Task.Delay(TimeSpan.FromMilliseconds(10), timeout.Token);
        trial.Counters["providerConnectionTerminationObserved"] = 1;
        var before = trial.Runtime.Boundary.Receipts.Count;
        await session.SendAsync(new MessageOptions { Prompt = "RT1_APPROVED_LATE_REQUEST" });
        await trial.Runtime.Boundary.WaitForReceiptsAsync(before + 1);
        trial.Runtime.Boundary.Receipts.Last().Reason.Should().Be("cancelled");
        trial.Provider.Requests.Count.Should().Be(1);
        trial.Facts["scope"] = "Loopback connection terminated; no remote model-compute/billing or physical-effect cessation claim.";
    });

    [Fact]
    public Task AlreadyAdmittedNonCooperativeToolRemainsUnknownAtCancellation() => RunAsync("admitted-tool-cancellation-unknown", async trial =>
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var effects = 0;
        var tool = Tool(async _ =>
        {
            started.SetResult();
            await release.Task;
            await trial.Runtime.WriteOwnedEffectAsync();
            effects++;
            done.SetResult();
            return Result("success", true);
        });
        trial.Provider.Enqueue(new Reply("tool", tool.Name), Reply.Text);
        var session = await trial.Runtime.SessionAsync(trial.Provider, tools: [tool]);
        await session.SendAsync(new MessageOptions { Prompt = "RT1_APPROVED_NONCOOPERATIVE" });
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            trial.Runtime.Boundary.Cancel(session.SessionId);
            trial.Facts["effectOutcomeAtCancellation"] = "Unknown";
            trial.Counters["cancellationRequested"] = 1;
            await session.AbortAsync().WaitAsync(TimeSpan.FromSeconds(5));
            trial.Counters["abortAcknowledged"] = 1;
            effects.Should().Be(0);
        }
        finally { release.SetResult(); }
        await done.Task.WaitAsync(TimeSpan.FromSeconds(10));
        effects.Should().Be(1);
        trial.Runtime.OwnedEffectWrites.Should().Be(1);
        // An observed completion fence replaces a timing-only sleep; subsequent sends remain denied.
        var before = trial.Runtime.Boundary.Receipts.Count;
        await session.SendAsync(new MessageOptions { Prompt = "RT1_APPROVED_LATE_REQUEST" });
        await trial.Runtime.Boundary.WaitForReceiptsAsync(before + 1);
        trial.Runtime.Boundary.Receipts.Last().Reason.Should().Be("cancelled");
        trial.Provider.Requests.Count.Should().Be(1);
        trial.Counters["admittedEffectsAfterCancellation"] = effects;
        trial.Facts["eventPresentation"] = "No production presenter tested; downstream host must suppress late output.";
    });

    [Fact]
    public Task AProviderRedirectCannotEscapeTheBoundTransport() => RunAsync("redirect-not-followed", async trial =>
    {
        trial.Provider.Enqueue(new Reply("redirect", Content: new Uri(trial.Manager.BaseUri, "/v1/chat/completions").AbsoluteUri));
        var session = await trial.Runtime.SessionAsync(trial.Provider, maximumRequests: 1);
        await ExpectProviderErrorAsync(session, "RT1_APPROVED_REDIRECT");
        trial.Provider.Requests.Count.Should().Be(1);
        trial.Manager.Requests.Should().BeEmpty();
        trial.Facts["transport"] = "Public handler owns non-proxy, non-redirecting HTTP client; redirect was not followed.";
    });

    [Fact]
    public Task MismatchedBoundDestinationIsDeniedBeforeAnyProviderSend() => RunAsync("destination-mismatch", async trial =>
    {
        var session = await trial.Runtime.SessionAsync(trial.Provider, expectedDestination: trial.Manager.BaseUri);
        await ExpectProviderErrorAsync(session, "RT1_APPROVED_WRONG_DESTINATION");
        trial.Provider.Requests.Should().BeEmpty();
        trial.Manager.Requests.Should().BeEmpty();
        trial.Runtime.Boundary.Receipts.Should().OnlyContain(receipt => receipt.Reason == "destination");
    });

    [Fact]
    public Task RuntimeSessionWriteFailuresDoNotFallbackToDisk() => RunAsync("session-io-write-failure", async trial =>
    {
        trial.Provider.Enqueue(Reply.Text);
        var session = await trial.Runtime.SessionAsync(trial.Provider, rejectSessionWrites: true);
        await ExpectProviderErrorAsync(session, "RT1_APPROVED_VOLATILE_FAILURE");
        trial.Runtime.SessionWriteRejections.Should().BeGreaterThan(0);
        trial.Runtime.StoreCount.Should().Be(0);
        trial.Counters["sessionWriteRejections"] = trial.Runtime.SessionWriteRejections;
        trial.Runtime.Events.OfType<SessionErrorEvent>().Should().NotBeEmpty();
        trial.Counters["sessionErrorEvents"] = trial.Runtime.Events.OfType<SessionErrorEvent>().Count();
        trial.Facts["boundary"] = "Actual provider I/O error became an SDK session error; no context-marker disk fallback observed.";
    });

    [Fact]
    public Task SdkWaitTimeoutDoesNotCertifyInferenceTermination() => RunAsync("wait-timeout-is-not-abort", async trial =>
    {
        trial.Provider.Enqueue(Reply.Hold);
        var session = await trial.Runtime.SessionAsync(trial.Provider);
        var pending = session.SendAndWaitAsync("RT1_APPROVED_WAIT_TIMEOUT", TimeSpan.FromMilliseconds(500));
        await trial.Provider.WaitForRequestsAsync(1);
        var action = async () => await pending;
        await action.Should().ThrowAsync<TimeoutException>();
        trial.Runtime.Boundary.CancellationObserved.Should().Be(0);
        trial.Provider.ClosedConnections.Should().Be(0);
        trial.Counters["timeoutObserved"] = 1;
        trial.Counters["terminationAtTimeout"] = 0;
        trial.Facts["outcomeAtTimeout"] = "Unknown; SDK wait timeout does not abort runtime inference.";
        trial.Runtime.Boundary.Cancel(session.SessionId);
        await session.AbortAsync().WaitAsync(TimeSpan.FromSeconds(5));
        trial.Counters["explicitAbortAcknowledged"] = 1;
    });
}
