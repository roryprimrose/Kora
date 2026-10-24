using GitHub.Copilot;
using Kora.Core.Auditing;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Providers;
using Microsoft.Extensions.Logging;

namespace Kora.Windows.Dependencies;

/// <summary>One explicitly entered volatile token and a fresh authenticated catalogue, never ambient sign-in.</summary>
public sealed partial class CopilotAccountBroker : ICopilotExecutionAccountSource, IAsyncDisposable
{
    private readonly ICopilotDeploymentApprovalSource deployments;
    private readonly TimeProvider time;
    private readonly ISecurityAuditLog audit;
    private readonly Func<CopilotClientOptions, ICopilotSdkClient> create;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Lock stateGate = new();
    private Dictionary<ProviderProfileTuple, CopilotExecutionAccount> accounts = [];
    private ProviderCapabilityCatalogue? catalogue;
    private CopilotDeploymentApproval? deployment;
    private ICopilotSdkClient? heldClient;
    private Task? heldOperation;
    private readonly ILogger<CopilotAccountBroker> logger;
    private long revision;
    private long epoch;
    private bool disposed;
    public bool IsQuiescent => gate.CurrentCount != 0 && heldClient is null;

    public CopilotAccountBroker(ICopilotDeploymentApprovalSource deployments, TimeProvider time, ISecurityAuditLog audit,
        ILogger<CopilotAccountBroker> logger)
        : this(deployments, time, audit, options => new CopilotSdkClient(options), logger) { }

    internal CopilotAccountBroker(ICopilotDeploymentApprovalSource deployments, TimeProvider time, ISecurityAuditLog audit,
        Func<CopilotClientOptions, ICopilotSdkClient> create, ILogger<CopilotAccountBroker> logger)
    { this.deployments = deployments; this.time = time; this.audit = audit; this.create = create; this.logger = logger; }

    public async Task<bool> CanSetUpAsync(CancellationToken token) =>
        await deployments.ResolveAsync(token).ConfigureAwait(false) is { } approved && approved.ExpiresAt > time.GetUtcNow();

    public async Task<ProviderCapabilityCatalogue> ObserveAsync(CancellationToken token)
    {
        var approved = await deployments.ResolveAsync(token).ConfigureAwait(false);
        lock (stateGate)
        {
            if (!disposed && heldClient is null && ReferenceEquals(approved, deployment)
                && approved is not null && approved.ExpiresAt > time.GetUtcNow()
                && catalogue?.IsCurrent(time.GetUtcNow()) == true) { return catalogue; }
            RevokeCore();
            var now = time.GetUtcNow();
            return new(ProviderService.Copilot, ProviderCatalogueStatus.AuthenticationRequired, null,
                new(Math.Max(1, revision)), now, now.AddMinutes(1), []);
        }
    }

    public async ValueTask<CopilotExecutionAccount?> ResolveAsync(ProviderProfileTuple profile, CancellationToken cancellationToken)
    {
        var current = await ObserveAsync(cancellationToken).ConfigureAwait(false);
        lock (stateGate)
        {
            return current.IsCurrent(time.GetUtcNow()) && accounts.TryGetValue(profile, out var account) ? account : null;
        }
    }

    public void Revoke()
    {
        lock (stateGate) { epoch++; RevokeCore(); }
    }

    public void Forget()
    {
        var issuer = HostActivity.RequireCurrent();
        if (issuer.Request.Origin != RequestOrigin.LocalUi)
        { throw new InvalidOperationException("Explicit account removal requires original native input."); }
        var requested = new SecurityAuditEvent(Guid.NewGuid(), SecurityAuditCategory.SecurityApproval,
            "model.copilot.account-forget", SecurityAuditOutcome.Requested, SecurityAuditInitiator.LocalUser,
            issuer.Request.TaskId.Value.ToString("D"));
        try { audit.Write(requested); }
        finally { Revoke(); }
        audit.Write(requested.WithOutcome(SecurityAuditOutcome.Succeeded));
    }

    private void RevokeCore() { accounts.Clear(); catalogue = null; deployment = null; }

    public async Task SignInAsync(string login, string credential, Func<bool> eligible, CancellationToken token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(login);
        ArgumentException.ThrowIfNullOrWhiteSpace(credential);
        if (login.Length > 128 || login.Any(char.IsControl) || credential.Length > 8192 || credential.Any(char.IsControl))
        { throw new InvalidDataException("Copilot account input exceeds the bounded credential channel."); }
        var issuer = HostActivity.RequireCurrent();
        if (issuer.Request.Origin != RequestOrigin.LocalUi || !eligible())
        { throw new InvalidOperationException("Copilot setup requires private original native input."); }
        using var activity = HostActivity.BeginChild(HostActivityLayer.Windows, HostOperation.Runtime);
        await gate.WaitAsync(token).ConfigureAwait(false);
        var requested = new SecurityAuditEvent(Guid.NewGuid(), SecurityAuditCategory.SecurityApproval,
            "model.copilot.account", SecurityAuditOutcome.Requested, SecurityAuditInitiator.LocalUser,
            issuer.Request.TaskId.Value.ToString("D"));
        var outcome = SecurityAuditOutcome.Denied;
        try
        {
            audit.Write(requested);
            long expectedEpoch;
            lock (stateGate)
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                RevokeCore();
                expectedEpoch = ++epoch;
                if (heldClient is not null) { throw new InvalidOperationException("Previous Copilot setup has unconfirmed termination."); }
            }
            var approved = await deployments.ResolveAsync(token).ConfigureAwait(false);
            if (approved is null || approved.ExpiresAt <= time.GetUtcNow())
            { throw new InvalidOperationException("Copilot runtime/account-use deployment is not approved. No sign-in was attempted."); }
            using var files = new CopilotVolatileSessionFiles(approved.WorkingDirectory);
            using var http = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false, UseProxy = false });
            ICopilotSdkClient? client = null;
            IList<ModelInfo>? models = null;
            try
            {
                client = create(new()
                {
                    Mode = CopilotClientMode.Empty, Connection = RuntimeConnection.ForStdio(approved.ExecutablePath),
                    GitHubToken = credential, UseLoggedInUser = false, Environment = approved.Environment,
                    BaseDirectory = approved.BaseDirectory, WorkingDirectory = approved.WorkingDirectory,
                    BuiltinPluginDirectories = [], LogLevel = CopilotLogLevel.None,
#pragma warning disable GHCP001 // The approved account-management process has virtual session storage and no inference transport.
                    RequestHandler = new CopilotSetupRequestHandler(http),
                    SessionFs = new() { InitialWorkingDirectory = approved.WorkingDirectory, SessionStatePath = approved.BaseDirectory,
                        Conventions = GitHub.Copilot.Rpc.SessionFsSetProviderConventions.Windows,
                        Capabilities = new() { Sqlite = true, Binary = true } },
#pragma warning restore GHCP001
                });
                heldClient = client;
                var authTask = client.AuthenticateAsync(token);
                heldOperation = authTask;
                var auth = await authTask.WaitAsync(token).ConfigureAwait(false);
                if (!auth.IsAuthenticated || !string.Equals(auth.Login, login, StringComparison.Ordinal)
                    || auth.AuthType is not ("token" or "env") || auth.Host is not ("github.com" or "https://github.com"))
                { throw new InvalidOperationException("Copilot authentication did not confirm the explicitly entered account."); }
                var modelsTask = client.ModelsAsync(token);
                heldOperation = modelsTask;
                models = await modelsTask.WaitAsync(token).ConfigureAwait(false);
            }
            finally
            {
                if (client is not null)
                {
                    await client.StopAsync().ConfigureAwait(false);
                    await client.DisposeAsync().ConfigureAwait(false);
                }
                await JoinOperationAsync().ConfigureAwait(false);
                heldClient = null;
            }
            token.ThrowIfCancellationRequested();
            if (!eligible() || issuer.Activity!.IsStopped || issuer.Outcome != HostOperationOutcome.Unknown
                || !ReferenceEquals(approved, await deployments.ResolveAsync(token).ConfigureAwait(false)))
            { throw new InvalidOperationException("Original private account admission or deployment changed during sign-in."); }
            var now = time.GetUtcNow();
            var expires = now.AddMinutes(5) < approved.ExpiresAt ? now.AddMinutes(5) : approved.ExpiresAt;
            var accountId = new ProviderAccountId(Guid.NewGuid().ToString("N"));
            var next = new HostRevision(checked(++revision));
            var enabled = models.Where(model => string.Equals(model.Policy?.State, "enabled", StringComparison.Ordinal)
                && model.Capabilities.Limits.MaxContextWindowTokens > 0).ToArray();
            if (enabled.Length is 0 or > 64) { throw new InvalidDataException("The authenticated Copilot catalogue is empty or exceeds its bound."); }
            var entries = enabled.Select(model => new ProviderCatalogueModel(new(model.Id), model.Id,
                new(ModelProviderIdentity.Copilot, new(Guid.NewGuid()), next),
                [new(new("default"), "Provider default", true)], [new(new("default"), "Provider default", true)])).ToArray();
            var snapshot = new ProviderCapabilityCatalogue(ProviderService.Copilot, ProviderCatalogueStatus.Current,
                accountId, next, now, expires, entries);
            var prepared = entries.ToDictionary(model => new ProviderProfileTuple(ProviderService.Copilot, accountId, next,
                model.Id, new("default"), new("default")), model => new CopilotExecutionAccount(
                    new(ProviderService.Copilot, accountId, next, model.Id, new("default"), new("default")), login, credential,
                    approved.ExecutablePath, approved.WorkingDirectory, approved.BaseDirectory, approved.Environment, expires));
            lock (stateGate)
            {
                token.ThrowIfCancellationRequested();
                if (disposed || epoch != expectedEpoch || !eligible()) { throw new InvalidOperationException("Account setup lost host admission before publication."); }
                accounts = prepared;
                catalogue = snapshot;
                deployment = approved;
            }
            outcome = SecurityAuditOutcome.Succeeded;
            try { audit.Write(requested.WithOutcome(outcome)); }
            catch { Revoke(); throw; }
            activity.Complete(HostOperationOutcome.Completed);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            Revoke();
            activity.Complete(HostOperationOutcome.Cancelled);
            audit.Write(requested.WithOutcome(SecurityAuditOutcome.Cancelled));
            throw;
        }
        catch
        {
            Revoke();
            activity.Complete(HostOperationOutcome.Failed);
            if (outcome != SecurityAuditOutcome.Succeeded) { audit.Write(requested.WithOutcome(outcome)); }
            throw;
        }
        finally { gate.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            lock (stateGate) { disposed = true; RevokeCore(); }
            if (heldClient is { } client)
            {
                await client.StopAsync().ConfigureAwait(false);
                await client.DisposeAsync().ConfigureAwait(false);
                await JoinOperationAsync().ConfigureAwait(false);
                heldClient = null;
            }
        }
        finally { gate.Release(); }
        GC.SuppressFinalize(this);
    }

    private async Task JoinOperationAsync()
    {
        if (heldOperation is not { } pending) { return; }
        try { await pending.ConfigureAwait(false); }
        catch (Exception exception) { SetupOutputDiscarded(logger, exception.GetType().Name); }
        heldOperation = null;
    }
}
