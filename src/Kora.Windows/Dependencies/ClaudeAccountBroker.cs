using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kora.Core.Auditing;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Providers;
using Microsoft.Extensions.Logging;

namespace Kora.Windows.Dependencies;

/// <summary>Owns volatile approved credentials and one complete account-bound Models snapshot.</summary>
public sealed partial class ClaudeAccountBroker : IAsyncDisposable
{
    private readonly IClaudeDeploymentApprovalSource deployments;
    private readonly TimeProvider time;
    private readonly ISecurityAuditLog audit;
    private readonly ILogger<ClaudeAccountBroker> logger;
    private readonly Func<HttpClient> create;
    private readonly SemaphoreSlim operation = new(1, 1);
    private readonly Lock gate = new();
    private ClaudeAccount? account;
    private long epoch;
    private long revision;
    private bool disposed;

    public ClaudeAccountBroker(IClaudeDeploymentApprovalSource deployments, TimeProvider time,
        ISecurityAuditLog audit, ILogger<ClaudeAccountBroker> logger)
        : this(deployments, time, audit, logger, CreateHttpClient) { }

    internal ClaudeAccountBroker(IClaudeDeploymentApprovalSource deployments, TimeProvider time,
        ISecurityAuditLog audit, ILogger<ClaudeAccountBroker> logger, Func<HttpClient> create)
    { this.deployments = deployments; this.time = time; this.audit = audit; this.logger = logger; this.create = create; }

    internal static HttpClient CreateHttpClient() => new(new SocketsHttpHandler
    { AllowAutoRedirect = false, UseCookies = false, UseProxy = false, ActivityHeadersPropagator = null })
    { Timeout = Timeout.InfiniteTimeSpan };
    public bool IsQuiescent => operation.CurrentCount != 0;

    public async Task<bool> CanSetUpAsync(CancellationToken token) =>
        !disposed && await deployments.ResolveAsync(token).ConfigureAwait(false) is { } approved
            && approved.ExpiresAt > time.GetUtcNow();

    public void Revoke()
    {
        ClaudeAccount? previous;
        lock (gate) { epoch++; previous = account; account = null; }
        previous?.Dispose();
    }

    public void Forget()
    {
        var issuer = HostActivity.RequireCurrent();
        if (issuer.Request.Origin != RequestOrigin.LocalUi) { throw new InvalidOperationException("Claude credentials require native input."); }
        var requested = new SecurityAuditEvent(Guid.NewGuid(), SecurityAuditCategory.SecurityApproval,
            "model.claude.account-forget", SecurityAuditOutcome.Requested, SecurityAuditInitiator.LocalUser,
            issuer.Request.TaskId.Value.ToString("D"));
        try { audit.Write(requested); } finally { Revoke(); }
        audit.Write(requested.WithOutcome(SecurityAuditOutcome.Succeeded));
    }

    public async Task<ProviderCapabilityCatalogue> ObserveAsync(CancellationToken token)
    {
        ClaudeAccount? saved;
        lock (gate) { saved = account; }
        if (saved is not null && await CurrentAsync(saved, token).ConfigureAwait(false)) { return saved.Catalogue; }
        var status = await CanSetUpAsync(token).ConfigureAwait(false)
            ? ProviderCatalogueStatus.AuthenticationRequired : ProviderCatalogueStatus.Unavailable;
        var now = time.GetUtcNow();
        return new(ProviderService.Claude, status, null,
            new(Math.Max(1, Interlocked.Read(ref revision))), now, now.AddMinutes(1), []);
    }

    internal async ValueTask<ClaudeAccount?> ResolveAsync(ProviderProfileTuple profile, CancellationToken token)
    {
        ClaudeAccount? saved;
        lock (gate) { saved = account; }
        return saved is not null && await CurrentAsync(saved, token).ConfigureAwait(false)
            && ProviderProfileRules.Resolve(profile, saved.Catalogue, time.GetUtcNow()) is not null ? saved : null;
    }

    public async Task<bool> IsProfileCurrentAsync(ProviderProfileTuple profile, CancellationToken token) =>
        await ResolveAsync(profile, token).ConfigureAwait(false) is not null;

    internal async ValueTask<bool> CurrentAsync(ClaudeAccount expected, CancellationToken token)
    {
        var approved = await deployments.ResolveAsync(token).ConfigureAwait(false);
        var lease = approved is null ? null : await deployments.BindCredentialAsync(approved,
            expected.Lease.CredentialSha256, token).ConfigureAwait(false);
        lock (gate)
        {
            if (!disposed && ReferenceEquals(account, expected) && ReferenceEquals(approved, expected.Approval)
                && ReferenceEquals(lease, expected.Lease) && approved.ExpiresAt > time.GetUtcNow()
                && lease.ExpiresAt > time.GetUtcNow() && expected.Catalogue.IsCurrent(time.GetUtcNow())) { return true; }
            if (!ReferenceEquals(account, expected)) { return false; }
            epoch++; account = null;
        }
        expected.Dispose();
        return false;
    }

    public async Task SignInAsync(string credential, Func<bool> eligible, CancellationToken token)
    {
        var issuer = HostActivity.RequireCurrent();
        if (issuer.Request.Origin != RequestOrigin.LocalUi || !eligible())
        { throw new InvalidOperationException("Private original native input is required for Claude setup."); }
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(2), time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, deadline.Token);
        token = linked.Token;
        await operation.WaitAsync(token).ConfigureAwait(false);
        using var activity = HostActivity.BeginChild(HostActivityLayer.Windows, HostOperation.Runtime);
        var requested = new SecurityAuditEvent(Guid.NewGuid(), SecurityAuditCategory.SecurityApproval,
            "model.claude.account", SecurityAuditOutcome.Requested, SecurityAuditInitiator.LocalUser,
            issuer.Request.TaskId.Value.ToString("D"));
        var outcome = SecurityAuditOutcome.Denied;
        try
        {
            audit.Write(requested);
            long admittedEpoch;
            ClaudeAccount? previous;
            lock (gate) { ObjectDisposedException.ThrowIf(disposed, this); previous = account; account = null; admittedEpoch = ++epoch; }
            previous?.Dispose();
            var approval = await deployments.ResolveAsync(token).ConfigureAwait(false);
            if (approval is null || approval.ExpiresAt <= time.GetUtcNow())
            { throw new InvalidOperationException("Claude deployment, terms and cost approval are unavailable. No credential was accepted."); }
            ArgumentException.ThrowIfNullOrWhiteSpace(credential);
            if (credential.Length > 8192 || credential.Any(character => character is < '!' or > '~'))
            { throw new InvalidDataException("Invalid bounded native credential."); }
            var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(credential)));
            var lease = await deployments.BindCredentialAsync(approval, digest, token).ConfigureAwait(false);
            if (lease is null || !string.Equals(lease.CredentialSha256, digest, StringComparison.Ordinal) || lease.ExpiresAt <= time.GetUtcNow())
            { throw new InvalidOperationException("No host-approved credential/workspace lease. No authentication was attempted."); }
            bool Live() => !disposed && eligible() && !issuer.Activity!.IsStopped
                && issuer.Outcome == HostOperationOutcome.Unknown && Interlocked.Read(ref epoch) == admittedEpoch
                && approval.ExpiresAt > time.GetUtcNow() && lease.ExpiresAt > time.GetUtcNow();
            using var http = create();
            var models = new List<ProviderCatalogueModel>();
            var limits = new Dictionary<ProviderChoiceId, int>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var snapshotRevision = new HostRevision(Interlocked.Increment(ref revision));
            string? after = null;
            for (var page = 0; ; page++)
            {
                token.ThrowIfCancellationRequested();
                if (page >= 64 || !Live() || !ReferenceEquals(approval, await deployments.ResolveAsync(token).ConfigureAwait(false))
                    || !ReferenceEquals(lease, await deployments.BindCredentialAsync(approval, digest, token).ConfigureAwait(false)))
                { throw new InvalidOperationException("Claude setup approval changed or pagination exceeded its bound."); }
                var path = "https://api.anthropic.com/v1/models?limit=64&lifecycle=active"
                    + (after is null ? string.Empty : "&after_id=" + Uri.EscapeDataString(after));
                using var request = new HttpRequestMessage(HttpMethod.Get, path);
                AddHeaders(request, credential, approval, lease);
                using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) { throw new HttpRequestException("Claude Models authentication/catalogue was not confirmed."); }
                using var json = ClaudeMessagesRuntime.Parse(await ReadBoundedAsync(response.Content, 262144, token).ConfigureAwait(false));
                var root = json.RootElement;
                var data = root.GetProperty("data");
                if (data.ValueKind != JsonValueKind.Array || data.GetArrayLength() > 64) { throw new InvalidDataException("Incomplete Models page."); }
                foreach (var model in data.EnumerateArray())
                {
                    if (models.Count == 64 || model.GetProperty("type").GetString() is not "model"
                        || model.GetProperty("lifecycle").GetString() is not "active")
                    { throw new InvalidDataException("Only a complete bounded active catalogue is supported."); }
                    var id = model.GetProperty("id").GetString() ?? throw new InvalidDataException("Missing model ID.");
                    if (!ids.Add(id)) { throw new InvalidDataException("Duplicate Models entry."); }
                    var input = model.GetProperty("max_input_tokens").GetInt32();
                    var output = model.GetProperty("max_tokens").GetInt32();
                    if (input <= 0 || output <= 0) { throw new InvalidDataException("Unknown model token limits."); }
                    var caps = model.GetProperty("capabilities");
                    var thinking = caps.GetProperty("thinking");
                    if (!thinking.GetProperty("types").GetProperty("disabled").GetProperty("supported").GetBoolean())
                    { throw new InvalidDataException("The text-only profile requires native disabled thinking."); }
                    var effort = caps.GetProperty("effort");
                    var options = new List<ProviderProfileOption>();
                    if (effort.GetProperty("supported").GetBoolean())
                    {
                        foreach (var level in effort.EnumerateObject())
                        {
                            if (level.Name is "supported") { continue; }
                            if (level.Name is not ("low" or "medium" or "high" or "xhigh" or "max"))
                            { throw new InvalidDataException("Unknown native effort level."); }
                            if (level.Value.ValueKind != JsonValueKind.Null && level.Value.GetProperty("supported").GetBoolean())
                            { options.Add(new(new(level.Name), "Thinking disabled; effort " + level.Name, false)); }
                        }
                        if (options.Count == 0) { throw new InvalidDataException("Incomplete effort capability."); }
                    }
                    else { options.Add(new(new("disabled"), "Thinking disabled; default effort", true)); }
                    var choice = new ProviderChoiceId(id);
                    var ceiling = Math.Min(input, 8192);
                    models.Add(new(choice, model.GetProperty("display_name").GetString() ?? id,
                        new(ModelProviderIdentity.Claude, new(Guid.NewGuid()), snapshotRevision), options,
                        [new(new(ceiling.ToString(CultureInfo.InvariantCulture)), "Exact input ceiling: "
                            + ceiling.ToString(CultureInfo.InvariantCulture) + " tokens", false, ceiling)]));
                    limits.Add(choice, Math.Min(output, 4096));
                }
                if (!root.GetProperty("has_more").GetBoolean()) { break; }
                var last = root.GetProperty("last_id").GetString();
                if (data.GetArrayLength() == 0 || last is null || string.Equals(last, after, StringComparison.Ordinal)
                    || !string.Equals(data[data.GetArrayLength() - 1].GetProperty("id").GetString(), last, StringComparison.Ordinal))
                { throw new InvalidDataException("Incomplete pagination cursor."); }
                after = last;
            }
            token.ThrowIfCancellationRequested();
            if (models.Count == 0 || !Live()
                || !ReferenceEquals(approval, await deployments.ResolveAsync(token).ConfigureAwait(false))
                || !ReferenceEquals(lease, await deployments.BindCredentialAsync(approval, digest, token).ConfigureAwait(false)))
            { throw new InvalidDataException("No complete current Claude snapshot."); }
            var now = time.GetUtcNow();
            var expires = new[] { now.AddMinutes(5), approval.ExpiresAt, lease.ExpiresAt }.Min();
            var catalogue = new ProviderCapabilityCatalogue(ProviderService.Claude, ProviderCatalogueStatus.Current,
                lease.Account, snapshotRevision, now, expires, models);
            audit.Write(requested.WithOutcome(SecurityAuditOutcome.Succeeded));
            lock (gate)
            {
                if (!Live()) { throw new InvalidOperationException("Claude setup changed before publication."); }
                account = new(credential, approval, lease, catalogue, limits);
            }
            outcome = SecurityAuditOutcome.Succeeded;
            activity.Complete(HostOperationOutcome.Completed);
        }
        catch (Exception exception)
        {
            Revoke();
            SetupFailed(logger, exception.GetType().Name);
            outcome = exception is OperationCanceledException ? SecurityAuditOutcome.Cancelled : SecurityAuditOutcome.Unknown;
            activity.Complete(exception is OperationCanceledException ? HostOperationOutcome.Cancelled : HostOperationOutcome.Failed);
            throw;
        }
        finally
        {
            credential = string.Empty;
            try { if (outcome != SecurityAuditOutcome.Succeeded) { audit.Write(requested.WithOutcome(outcome)); } }
            finally { operation.Release(); }
        }
    }

    internal static void AddHeaders(HttpRequestMessage request, string credential, ClaudeDeploymentApproval approval, ClaudeCredentialLease lease)
    {
        request.Headers.Add("x-api-key", credential);
        request.Headers.Add("anthropic-version", approval.ApiVersion);
        request.Headers.Add("anthropic-workspace-id", lease.Workspace);
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.ConnectionClose = true;
    }

    internal static async Task<byte[]> ReadBoundedAsync(HttpContent content, int maximum, CancellationToken token)
    {
        if (content.Headers.ContentLength > maximum) { throw new InvalidDataException("Claude response exceeds its byte bound."); }
        await using var stream = await content.ReadAsStreamAsync(token).ConfigureAwait(false);
        using var memory = new MemoryStream();
        var buffer = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, maximum + 1 - (int)memory.Length)), token).ConfigureAwait(false)) != 0)
        {
            await memory.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
            if (memory.Length > maximum) { throw new InvalidDataException("Claude response exceeds its byte bound."); }
        }
        return memory.ToArray();
    }

    public async ValueTask DisposeAsync()
    {
        lock (gate) { disposed = true; }
        Revoke();
        await operation.WaitAsync().ConfigureAwait(false);
        operation.Release();
    }
}
