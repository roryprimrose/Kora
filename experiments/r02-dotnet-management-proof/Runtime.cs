#pragma warning disable GHCP001 // Only RT1-approved public experimental controls.
using System.Diagnostics;
using GitHub.Copilot;
using GitHub.Copilot.Rpc;
using Kora.Rt1;
using Microsoft.Extensions.AI;

namespace Kora.Mg1;

internal sealed record Outcome(string Status, Proposal? Proposal, long ElapsedMs, bool AbortRequested,
    bool AbortAcknowledged, bool ConnectionTerminationObserved, string ComputationTermination);

internal sealed class Runtime : IAsyncDisposable
{
    private readonly HttpClient http = new(new SocketsHttpHandler { UseProxy = false, AllowAutoRedirect = false })
        { Timeout = Timeout.InfiniteTimeSpan };
    private readonly List<VolatileSessionFs> stores = [];
    private readonly List<Task> observers = [];
    private readonly CancellationTokenSource observationLifetime = new();
    private CopilotClient? client;
    private string? scratch;
    internal Boundary Boundary { get; }
    internal int EffectWrites;
    internal int CleanupCompleted;
    internal TaskCompletionSource EffectStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource EffectRelease { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource EffectCompleted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource AbortReceipt { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal Runtime() => Boundary = new Boundary(http);

    internal async Task StartAsync()
    {
        await Candidate.ValidateAsync();
        scratch = Path.Combine(ProofEvidence.Root, ".scratch", "trial-" + Guid.NewGuid().ToString("N"));
        var home = Path.Combine(scratch, "home");
        var workspace = Path.Combine(scratch, "workspace");
        Directory.CreateDirectory(home);
        Directory.CreateDirectory(workspace);
        var system = Environment.GetFolderPath(Environment.SpecialFolder.System);
        var windows = Directory.GetParent(system)!.FullName;
        client = new CopilotClient(new CopilotClientOptions
        {
            Connection = RuntimeConnection.ForStdio(Candidate.Executable),
            Mode = CopilotClientMode.Empty, UseLoggedInUser = false, LogLevel = CopilotLogLevel.None,
            WorkingDirectory = workspace, BaseDirectory = Path.Combine(scratch, "copilot"),
            Environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["SystemRoot"] = windows, ["WINDIR"] = windows, ["COMSPEC"] = Path.Combine(system, "cmd.exe"),
                ["PATH"] = system + ";" + Path.Combine(windows, "System32", "WindowsPowerShell", "v1.0"),
                ["PATHEXT"] = ".EXE;.COM;.BAT;.CMD", ["HOME"] = home, ["USERPROFILE"] = home,
                ["APPDATA"] = home, ["LOCALAPPDATA"] = home, ["TEMP"] = scratch, ["TMP"] = scratch,
                ["COPILOT_TELEMETRY_DISABLED"] = "1", ["OTEL_SDK_DISABLED"] = "true",
                ["COPILOT_CLI_DISABLE_WEBSOCKET_RESPONSES"] = "1",
                ["POWERSHELL_TELEMETRY_OPTOUT"] = "1", ["POWERSHELL_UPDATECHECK"] = "Off"
            },
            RequestHandler = Boundary, EnableRemoteSessions = false,
            SessionFs = new SessionFsConfig
            {
                InitialWorkingDirectory = VolatileSessionFs.VirtualRoot,
                SessionStatePath = Path.Combine(VolatileSessionFs.VirtualRoot, "state"),
                Conventions = SessionFsSetProviderConventions.Windows,
                Capabilities = new SessionFsSetProviderCapabilities { Sqlite = false }
            }
        });
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await client.StartAsync(timeout.Token);
        var status = await client.GetStatusAsync(timeout.Token);
        if (status.Version != "1.0.90" || status.ProtocolVersion != 3)
            throw new InvalidDataException("Runtime/profile pin mismatch.");
    }

    internal async Task<CopilotSession> SessionAsync(LoopbackProvider provider, Action<SessionEvent>? onEvent = null,
        bool execution = false, bool probe = false, string history = "", bool effect = false)
    {
        var store = new VolatileSessionFs();
        stores.Add(store);
        ICollection<AIFunctionDeclaration> tools = effect ? [CopilotTool.DefineTool(
            async (ToolInvocation _) =>
            {
                EffectStarted.TrySetResult();
                await EffectRelease.Task.WaitAsync(TimeSpan.FromSeconds(25));
                await File.WriteAllTextAsync(Path.Combine(scratch!, "owned-effect.txt"), "synthetic");
                Interlocked.Increment(ref EffectWrites);
                EffectCompleted.TrySetResult();
                return new ToolResultAIContent(new ToolResultObject { ResultType = "success", TextResultForLlm = "observed-owned-effect" });
            }, factoryOptions: new AIFunctionFactoryOptions { Name = "mg1_owned_effect",
                Description = "Harmless fixture-owned scratch effect; execution trial only." })] : [];
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var session = await client!.CreateSessionAsync(new SessionConfig
        {
            Model = "gpt-4.1",
            Provider = new GitHub.Copilot.ProviderConfig { Type = "openai", WireApi = "completions",
                BaseUrl = provider.BaseUri.AbsoluteUri, ApiKey = Candidate.Credential },
            SystemMessage = new SystemMessageConfig { Mode = SystemMessageMode.Replace,
                Content = execution ? "MG1_EXECUTION: held synthetic execution only." : Envelope.System },
            Tools = tools, AvailableTools = tools.Select(tool => tool.Name).ToList(),
            ExcludedTools = ["builtin:*", "mcp:*"], Streaming = true,
            Memory = new MemoryConfiguration { Enabled = false },
            InfiniteSessions = new InfiniteSessionConfig { Enabled = false },
            LargeOutput = new LargeToolOutputConfig { Enabled = false },
            EnableConfigDiscovery = false, SkipCustomInstructions = true, EnableSkills = false,
            EnableFileHooks = false, EnableHostGitOperations = false, EnableOnDemandInstructionDiscovery = false,
            EnableSessionStore = false, EnableSessionTelemetry = false, EnableFileChangeTracking = false,
            SkipEmbeddingRetrieval = true, EmbeddingCacheStorage = EmbeddingCacheStorageMode.InMemory,
            McpOAuthTokenStorage = McpOAuthTokenStorageMode.InMemory, RemoteSession = RemoteSessionMode.Off,
            McpServers = new Dictionary<string, McpServerConfig>(), CustomAgents = [], SkillDirectories = [],
            PluginDirectories = [], InstructionDirectories = [], RequestExtensions = false, RequestCanvasRenderer = false,
            ManageScheduleEnabled = false, EnableExperimentalMode = false, EnableManagedSettings = false,
            OnPermissionRequest = (request, invocation) => Task.FromResult(
                effect && request is PermissionRequestCustomTool custom && custom.ToolName == "mg1_owned_effect"
                && Boundary.ToolAllowed(invocation.SessionId) ? PermissionDecision.ApproveOnce()
                    : PermissionDecision.Reject("management-no-authority")),
            Hooks = string.IsNullOrEmpty(history) ? null : new SessionHooks
            {
                OnUserPromptSubmitted = (_, _) => Task.FromResult<UserPromptSubmittedHookOutput?>(
                    new UserPromptSubmittedHookOutput { AdditionalContext = history })
            },
            CreateSessionFsProvider = _ => store, OnEvent = onEvent
        }, timeout.Token);
        Boundary.Bind(session.SessionId, provider.BaseUri, execution, probe);
        return session;
    }

    internal async Task<Outcome> DispatchAsync(CopilotSession session, LoopbackProvider provider, Admission admission,
        Guid request, string prompt, ProvisionalOutput output, Task streamCompleted,
        CancellationToken cancellation = default)
    {
        var start = Stopwatch.StartNew();
        Envelope.Select(prompt, "");
        var deadline = Task.Delay(Envelope.DeadlineMs, CancellationToken.None);
        // The host races dispatch independently; an already-cancelled request must not cancel the abort receipt.
        var inference = session.SendAndWaitAsync(prompt, TimeSpan.FromSeconds(35), observationLifetime.Token);
        var cancelled = Task.Delay(Timeout.Infinite, cancellation);
        var winner = await Task.WhenAny(inference, deadline, cancelled);
        if (winner == inference && start.ElapsedMilliseconds < Envelope.DeadlineMs && !cancellation.IsCancellationRequested)
        {
            try
            {
                await inference;
                await streamCompleted.WaitAsync(TimeSpan.FromSeconds(1), CancellationToken.None);
                var proposal = output.Complete();
                if (start.ElapsedMilliseconds < Envelope.DeadlineMs && !cancellation.IsCancellationRequested)
                {
                    Boundary.Close(session.SessionId);
                    admission.End(request, true);
                    return new Outcome("Proposal", proposal, start.ElapsedMilliseconds, false, false,
                        provider.ConnectionsTerminated > 0, "Unknown: synthetic inference only");
                }
            }
            catch (Exception exception) when (exception is InvalidOperationException or InvalidDataException or System.Text.Json.JsonException)
            {
                Boundary.Close(session.SessionId);
                admission.End(request, false);
                return new Outcome("Degraded:" + exception.GetType().Name, null, start.ElapsedMilliseconds,
                    false, false, provider.ConnectionsTerminated > 0, "Unknown");
            }
        }
        Boundary.Close(session.SessionId);
        admission.End(request, false);
        // Start abort without awaiting either send or abort acknowledgement.
        var abort = session.AbortAsync(CancellationToken.None);
        observers.Add(ObserveAbortAsync(abort));
        observers.Add(ObserveInferenceAsync(inference));
        return new Outcome(cancellation.IsCancellationRequested ? "Cancelled" : "Deadline", null, start.ElapsedMilliseconds,
            true, false, provider.ConnectionsTerminated > 0, "Unknown");
    }

    internal bool AbortAcknowledged { get; private set; }
    internal string? AbortFailure { get; private set; }
    private async Task ObserveAbortAsync(Task abort)
    {
        try
        {
            await abort.WaitAsync(TimeSpan.FromSeconds(10));
            AbortAcknowledged = true;
            AbortReceipt.TrySetResult();
        }
        catch (Exception exception) when (exception is TimeoutException or InvalidOperationException or OperationCanceledException)
        { AbortFailure = exception.GetType().Name; AbortReceipt.TrySetException(exception); }
    }
    private static async Task ObserveInferenceAsync(Task inference)
    {
        try { await inference.WaitAsync(TimeSpan.FromSeconds(40)); }
        catch (Exception exception) when (exception is TimeoutException or InvalidOperationException or OperationCanceledException)
        { ProofEvidence.BackgroundFailures.Enqueue(exception.GetType().Name); }
    }
    public async ValueTask DisposeAsync()
    {
        Boundary.SendAcknowledgement.TrySetResult();
        EffectRelease.TrySetResult();
        await observationLifetime.CancelAsync();
        if (client is not null)
        {
            var stop = client.StopAsync();
            try { await stop.WaitAsync(TimeSpan.FromSeconds(10)); }
            catch (TimeoutException)
            {
                await client.ForceStopAsync().WaitAsync(TimeSpan.FromSeconds(10));
                await stop.WaitAsync(TimeSpan.FromSeconds(10));
            }
            await client.DisposeAsync();
        }
        await Task.WhenAll(observers).WaitAsync(TimeSpan.FromSeconds(10));
        observationLifetime.Dispose();
        Boundary.Dispose();
        http.Dispose();
        foreach (var store in stores) store.Clear();
        if (scratch is not null)
        {
            var parent = Path.GetFullPath(Path.Combine(ProofEvidence.Root, ".scratch")) + '\\';
            var path = Path.GetFullPath(scratch);
            if (!path.StartsWith(parent, StringComparison.OrdinalIgnoreCase)
                || !Path.GetFileName(path).StartsWith("trial-", StringComparison.Ordinal)
                || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0
                || Directory.EnumerateFileSystemEntries(path, "*", SearchOption.AllDirectories)
                    .Any(entry => (File.GetAttributes(entry) & FileAttributes.ReparsePoint) != 0))
                throw new IOException("Unsafe exact-owned cleanup path.");
            Directory.Delete(path, true);
        }
        CleanupCompleted = 1;
    }
}
#pragma warning restore GHCP001
