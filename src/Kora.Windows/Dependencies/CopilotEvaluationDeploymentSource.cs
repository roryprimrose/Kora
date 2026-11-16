using System.Security.Cryptography;
using Kora.Core.Auditing;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Windows.Storage;
using Microsoft.Extensions.Logging;

namespace Kora.Windows.Dependencies;

/// <summary>Explicit, volatile live-evaluation admission; never production qualification.</summary>
public sealed partial class CopilotEvaluationDeploymentSource(
    IApplicationDataPaths paths, TimeProvider time, ISecurityAuditLog audit,
    ILogger<CopilotEvaluationDeploymentSource> logger) : ICopilotDeploymentApprovalSource, IDisposable
{
    private readonly Lock gate = new();
    private CopilotEvaluationCandidate? candidate;
    private FileStream? executable;
    private RestrictedStorageDirectory? storage;
    private CopilotDeploymentApproval? approval;
    private Func<bool>? eligible;
    private long revision;
    private bool disposed;

    public async Task<CopilotEvaluationCandidate> ObserveAsync(string path, Func<bool> admitted, CancellationToken token)
    {
        RequireNative(admitted);
        if (!Path.IsPathFullyQualified(path) || path.StartsWith(@"\\", StringComparison.Ordinal)
            || !string.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase)
            || new DriveInfo(Path.GetPathRoot(path)!).DriveType != DriveType.Fixed)
        { throw new InvalidDataException("Choose an installed Copilot executable on a local fixed volume."); }
        path = Path.GetFullPath(path);
        RestrictedStorageDirectory.RejectReparseAncestors(path);
        long expected;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            RevokeCore();
            expected = revision;
        }
        FileStream? opened = new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
        try
        {
            if (opened.Length is <= 0 or > 268435456)
            { throw new InvalidDataException("The runtime executable is empty or exceeds the evaluation bound."); }
            var digest = Convert.ToHexString(await SHA256.HashDataAsync(opened, token).ConfigureAwait(false));
            token.ThrowIfCancellationRequested();
            RequireNative(admitted);
            RestrictedStorageDirectory.RejectReparseAncestors(path);
            lock (gate)
            {
                if (disposed || revision != expected)
                { throw new InvalidOperationException("Runtime observation was revoked before review."); }
                candidate = new(path, digest, time.GetUtcNow().AddMinutes(5));
                executable = opened;
                opened = null;
                return candidate;
            }
        }
        finally { opened?.Dispose(); }
    }

    public void Admit(CopilotEvaluationCandidate reviewed, Func<bool> admitted, Func<bool> remainsEligible, CancellationToken token)
    {
        RequireNative(admitted);
        var issuer = HostActivity.RequireCurrent();
        var requested = new SecurityAuditEvent(Guid.NewGuid(), SecurityAuditCategory.SecurityApproval,
            "model.copilot.evaluation", SecurityAuditOutcome.Requested, SecurityAuditInitiator.LocalUser,
            issuer.Request.TaskId.Value.ToString("D"));
        lock (gate)
        {
            try
            {
                audit.Write(requested);
                token.ThrowIfCancellationRequested();
                if (disposed || !ReferenceEquals(candidate, reviewed) || executable is null
                    || reviewed.ExpiresAt <= time.GetUtcNow() || !remainsEligible())
                { throw new InvalidOperationException("The exact runtime review or private evaluation admission expired."); }
                VerifyExecutable(reviewed.ExecutablePath);
                var owned = new RestrictedStorageDirectory(paths, includeKeys: false,
                    partitionName: "CopilotEvaluation-" + Guid.NewGuid().ToString("N"));
                owned.CreateNew();
                var system = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                var environment = new Dictionary<string, string>(StringComparer.Ordinal)
                { ["SystemRoot"] = system, ["WINDIR"] = system, ["TEMP"] = owned.Artifacts, ["TMP"] = owned.Artifacts };
                var prepared = new CopilotDeploymentApproval(reviewed.ExecutablePath, owned.Artifacts,
                    owned.Root, environment, time.GetUtcNow().AddHours(1), isEvaluation: true);
                RequireNative(admitted);
                token.ThrowIfCancellationRequested();
                if (!remainsEligible()) { throw new InvalidOperationException("Private evaluation admission changed."); }
                audit.Write(requested.WithOutcome(SecurityAuditOutcome.Succeeded));
                storage = owned;
                eligible = remainsEligible;
                approval = prepared;
            }
            catch (Exception exception)
            {
                RevokeCore();
                EvaluationUnavailable(logger, exception.GetType().Name);
                audit.Write(requested.WithOutcome(token.IsCancellationRequested
                    ? SecurityAuditOutcome.Cancelled : SecurityAuditOutcome.Denied));
                throw;
            }
        }
    }

    public ValueTask<CopilotDeploymentApproval?> ResolveAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        lock (gate)
        {
            if (disposed || approval is null) { return ValueTask.FromResult<CopilotDeploymentApproval?>(null); }
            if (approval.ExpiresAt <= time.GetUtcNow() || eligible?.Invoke() != true)
            {
                RevokeCore();
                EvaluationUnavailable(logger, "expired-or-private-authority-changed");
                return ValueTask.FromResult<CopilotDeploymentApproval?>(null);
            }
            try
            {
                VerifyExecutable(approval.ExecutablePath);
                storage!.Verify();
                return ValueTask.FromResult<CopilotDeploymentApproval?>(approval);
            }
            catch (Exception exception)
            {
                RevokeCore();
                EvaluationUnavailable(logger, exception.GetType().Name);
                throw;
            }
        }
    }

    public void Revoke() { lock (gate) { RevokeCore(); } }

    private void VerifyExecutable(string path)
    {
        RestrictedStorageDirectory.RejectReparseAncestors(path);
        using var current = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (executable is null || !string.Equals(WindowsFileIdentity.Read(current),
            WindowsFileIdentity.Read(executable.SafeFileHandle), StringComparison.Ordinal))
        { throw new InvalidDataException("The selected runtime path no longer names the reviewed executable."); }
    }

    public void DiscardPendingReview()
    {
        lock (gate) { if (approval is null) { RevokeCore(); } }
    }

    private void RevokeCore()
    {
        revision++;
        approval = null;
        candidate = null;
        storage = null;
        eligible = null;
        executable?.Dispose();
        executable = null;
    }

    private static void RequireNative(Func<bool> admitted)
    {
        var issuer = HostActivity.RequireCurrent();
        if (issuer.Request.Origin != RequestOrigin.LocalUi || issuer.Request.InvocationId is not null
            || issuer.Activity!.IsStopped || issuer.Outcome != HostOperationOutcome.Unknown || !admitted())
        { throw new InvalidOperationException("Copilot evaluation requires current original private native input."); }
    }

    public void Dispose() { lock (gate) { disposed = true; RevokeCore(); } }
}
