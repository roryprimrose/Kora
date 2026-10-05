// Only these reviewed public experimental controls are used; no private SDK access.
#pragma warning disable GHCP001
using System.Collections.Concurrent;
using GitHub.Copilot;
using GitHub.Copilot.Rpc;
using Microsoft.Extensions.AI;

namespace Kora.Rt1;

internal sealed class RuntimeFixture : IAsyncDisposable
{
    private readonly HttpClient http = new(new SocketsHttpHandler { UseProxy = false, AllowAutoRedirect = false })
    {
        Timeout = TimeSpan.FromSeconds(20)
    };
    private readonly List<CopilotSession> sessions = [];
    private readonly List<VolatileSessionFs> stores = [];
    private CopilotClient? client;
    private string? scratch;
    internal RequestBoundary Boundary { get; }
    internal ConcurrentQueue<SessionEvent> Events { get; } = new();
    internal int CleanupCompleted;
    internal int DiskMarkerFiles;
    internal int DiskFiles;
    internal int OwnedEffectWrites;
    internal int OwnedScratchReads;
    internal int VolatileWrites => stores.Sum(store => store.Writes);
    internal int StoreCount => stores.Sum(store => store.Count);
    internal GetStatusResponse Status { get; private set; } = null!;

    internal RuntimeFixture() => Boundary = new RequestBoundary(http);

    internal async Task StartAsync()
    {
        await Candidate.ValidateAsync();
        var parent = Path.Combine(Candidate.Root, ".scratch");
        Directory.CreateDirectory(parent);
        scratch = Path.Combine(parent, "trial-" + Guid.NewGuid().ToString("N"));
        var workspace = Path.Combine(scratch, "workspace");
        var home = Path.Combine(scratch, "home");
        Directory.CreateDirectory(workspace);
        Directory.CreateDirectory(home);
        await File.WriteAllTextAsync(Path.Combine(workspace, "AGENTS.md"), Candidate.Collected);
        await File.WriteAllTextAsync(Path.Combine(scratch, "fixture-read.txt"), "synthetic owned input");
        var system = Environment.GetFolderPath(Environment.SpecialFolder.System);
        var windows = Directory.GetParent(system)!.FullName;
        var env = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["SystemRoot"] = windows, ["WINDIR"] = windows, ["COMSPEC"] = Path.Combine(system, "cmd.exe"),
            ["PATH"] = system + ";" + Path.Combine(windows, "System32", "WindowsPowerShell", "v1.0"),
            ["PATHEXT"] = ".EXE;.COM;.BAT;.CMD", ["HOME"] = home, ["USERPROFILE"] = home,
            ["APPDATA"] = home, ["LOCALAPPDATA"] = home, ["TEMP"] = scratch, ["TMP"] = scratch,
            ["COPILOT_TELEMETRY_DISABLED"] = "1", ["OTEL_SDK_DISABLED"] = "true",
            ["COPILOT_CLI_DISABLE_WEBSOCKET_RESPONSES"] = "1",
            ["POWERSHELL_TELEMETRY_OPTOUT"] = "1", ["POWERSHELL_UPDATECHECK"] = "Off"
        };
        client = new CopilotClient(new CopilotClientOptions
        {
            Connection = RuntimeConnection.ForStdio(Candidate.Executable),
            Mode = CopilotClientMode.Empty, UseLoggedInUser = false, LogLevel = CopilotLogLevel.None,
            WorkingDirectory = workspace, BaseDirectory = Path.Combine(scratch, "copilot"),
            Environment = env, RequestHandler = Boundary, EnableRemoteSessions = false,
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
        Status = await client.GetStatusAsync(timeout.Token);
        if (Status.Version != "1.0.90" || Status.ProtocolVersion != 3)
            throw new InvalidDataException("Observed runtime/protocol differs from RT1 pin.");
    }

    internal async Task<CopilotSession> SessionAsync(SyntheticProvider provider, string lane = "execution",
        ICollection<AIFunctionDeclaration>? tools = null, SessionHooks? hooks = null, int maximumRequests = 8,
        Uri? expectedDestination = null, bool rejectSessionWrites = false)
    {
        var store = new VolatileSessionFs { RejectWrites = rejectSessionWrites };
        stores.Add(store);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var session = await client!.CreateSessionAsync(new SessionConfig
        {
            Model = "gpt-4.1",
            Provider = new GitHub.Copilot.ProviderConfig
            {
                Type = "openai", WireApi = "completions", BaseUrl = provider.BaseUri.AbsoluteUri,
                ApiKey = Candidate.Credential
            },
            SystemMessage = new SystemMessageConfig
            {
                Mode = SystemMessageMode.Replace, Content = "RT1_APPROVED_SYSTEM: synthetic fixture, no authority."
            },
            Tools = tools ?? [], AvailableTools = tools?.Select(tool => tool.Name).ToList() ?? [],
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
                request is PermissionRequestCustomTool custom
                    && tools is not null && tools.Any(tool => tool.Name == custom.ToolName)
                    && Boundary.CanInvokeTool(invocation.SessionId)
                    ? PermissionDecision.ApproveOnce()
                    : PermissionDecision.Reject("synthetic-denial")),
            Hooks = hooks, CreateSessionFsProvider = _ => store, OnEvent = Events.Enqueue
        }, timeout.Token);
        sessions.Add(session);
        Boundary.Bind(session.SessionId, expectedDestination ?? provider.BaseUri, lane, Guid.NewGuid().ToString("N"), maximumRequests);
        return session;
    }

    internal async Task ReadOwnedScratchAsync()
    {
        var content = await File.ReadAllTextAsync(Path.Combine(scratch!, "fixture-read.txt"));
        if (content != "synthetic owned input") throw new InvalidDataException("Owned scratch input changed.");
        Interlocked.Increment(ref OwnedScratchReads);
    }

    internal async Task WriteOwnedEffectAsync()
    {
        var path = Path.Combine(scratch!, "effect-" + Guid.NewGuid().ToString("N") + ".txt");
        await File.WriteAllTextAsync(path, "synthetic owned effect");
        Interlocked.Increment(ref OwnedEffectWrites);
    }

    internal int SessionWriteRejections => stores.Sum(store => store.WriteRejections);

    internal async Task ScanScratchAsync()
    {
        foreach (var file in Directory.EnumerateFiles(scratch!, "*", SearchOption.AllDirectories))
        {
            DiskFiles++;
            if (Path.GetFileName(file) == "AGENTS.md") continue;
            var text = await File.ReadAllTextAsync(file);
            if (text.Contains(Candidate.Denied, StringComparison.Ordinal)
                || text.Contains("RT1_APPROVED", StringComparison.Ordinal))
                DiskMarkerFiles++;
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (client is not null)
            {
                foreach (var session in sessions) Boundary.Cancel(session.SessionId);
                var stop = client.StopAsync();
                try { await stop.WaitAsync(TimeSpan.FromSeconds(10)); }
                catch (TimeoutException)
                {
                    await client.ForceStopAsync().WaitAsync(TimeSpan.FromSeconds(10));
                    await stop.WaitAsync(TimeSpan.FromSeconds(10));
                }
                await client.DisposeAsync();
            }
        }
        finally
        {
            Boundary.Dispose();
            http.Dispose();
            foreach (var store in stores) store.Clear();
        }
        CleanupScratch();
        CleanupCompleted = 1;
    }

    private void CleanupScratch()
    {
        if (scratch is null) return;
        var resolved = Path.GetFullPath(scratch);
        var parent = Path.Combine(Candidate.Root, ".scratch") + Path.DirectorySeparatorChar;
        if (!resolved.StartsWith(parent, StringComparison.OrdinalIgnoreCase)
            || !Path.GetFileName(resolved).StartsWith("trial-", StringComparison.Ordinal)
            || (File.GetAttributes(resolved) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Unsafe scratch cleanup target.");
        if (Directory.EnumerateFileSystemEntries(resolved, "*", SearchOption.AllDirectories)
            .Any(path => (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0))
            throw new IOException("Refusing scratch cleanup through a reparse point.");
        Directory.Delete(resolved, recursive: true);
    }
}
#pragma warning restore GHCP001
