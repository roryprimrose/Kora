using Kora.Core.Auditing;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Windows.Dependencies;

/// <summary>Original-user development admission, explicitly not release qualification.</summary>
public sealed class DevelopmentPreviewAdmission(bool buildEnabled, TimeProvider time, ISecurityAuditLog audit) : IDisposable
{
    private readonly Lock gate = new();
    private DevelopmentPreviewLease? lease;
    private bool disposed;
    private long revision;
    public bool BuildEnabled => buildEnabled;

    public void Admit(DevelopmentPreviewCapability capabilities, Func<bool> eligible, Func<bool>? remainsEligible = null)
    {
        ArgumentNullException.ThrowIfNull(eligible);
        var host = HostActivity.RequireCurrent();
        const DevelopmentPreviewCapability known = DevelopmentPreviewCapability.LocalModel | DevelopmentPreviewCapability.Claude
            | DevelopmentPreviewCapability.Wake | DevelopmentPreviewCapability.BundledWorker;
        if (!buildEnabled || capabilities == DevelopmentPreviewCapability.None
            || (capabilities & ~known) != DevelopmentPreviewCapability.None
            || host.Request.Origin != RequestOrigin.LocalUi || host.Request.InvocationId is not null
            || host.Outcome != HostOperationOutcome.Unknown || host.Activity!.IsStopped || !eligible())
        { throw new InvalidOperationException("Development preview requires this build and current original native consent."); }
        var requested = new SecurityAuditEvent(Guid.NewGuid(), SecurityAuditCategory.SecurityApproval,
            "development-preview.admission", SecurityAuditOutcome.Requested, SecurityAuditInitiator.LocalUser,
            host.Request.TaskId.Value.ToString("D"));
        DevelopmentPreviewLease? previous;
        long expected;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            previous = TakeLease();
            expected = revision;
        }
        previous?.Dispose();
        try
        {
            audit.Write(requested);
            if (!eligible() || host.Activity.IsStopped || host.Outcome != HostOperationOutcome.Unknown)
            { throw new InvalidOperationException("Development review authority changed before admission."); }
            audit.Write(requested.WithOutcome(SecurityAuditOutcome.Succeeded));
            if (!eligible() || remainsEligible?.Invoke() == false
                || host.Activity.IsStopped || host.Outcome != HostOperationOutcome.Unknown)
            { throw new InvalidOperationException("Preview authority retired before receipt publication."); }
            lock (gate)
            {
                if (disposed || revision != expected)
                { throw new InvalidOperationException("Preview admission was revoked during audit or review."); }
                lease = new(capabilities, time.GetUtcNow().AddHours(1), time, remainsEligible ?? eligible);
            }
        }
        catch
        {
            audit.Write(requested.WithOutcome(SecurityAuditOutcome.Denied));
            throw;
        }
    }

    internal DevelopmentPreviewLease? Resolve(DevelopmentPreviewCapability capability)
    {
        if (capability is not (DevelopmentPreviewCapability.LocalModel or DevelopmentPreviewCapability.Claude
            or DevelopmentPreviewCapability.Wake or DevelopmentPreviewCapability.BundledWorker))
        { throw new ArgumentOutOfRangeException(nameof(capability)); }
        DevelopmentPreviewLease? captured;
        lock (gate)
        {
            if (disposed || !buildEnabled || lease is null) { return null; }
            captured = lease;
        }
        if (!captured.IsCurrent)
        {
            DevelopmentPreviewLease? retired = null;
            lock (gate)
            {
                if (ReferenceEquals(lease, captured)) { retired = TakeLease(); }
            }
            retired?.Dispose();
            return null;
        }
        lock (gate)
        {
            return !disposed && ReferenceEquals(lease, captured)
                && (captured.Capabilities & capability) == capability ? captured : null;
        }
    }

    public bool IsAllowed(DevelopmentPreviewCapability capability) => Resolve(capability) is not null;
    public void RevokeFromNative(Func<bool> eligible)
    {
        var host = HostActivity.RequireCurrent();
        if (host.Request.Origin != RequestOrigin.LocalUi || host.Request.InvocationId is not null
            || host.Activity!.IsStopped || host.Outcome != HostOperationOutcome.Unknown || !eligible())
        { throw new InvalidOperationException("Native preview revocation requires current original input."); }
        Revoke();
        var requested = new SecurityAuditEvent(Guid.NewGuid(), SecurityAuditCategory.SecurityApproval,
            "development-preview.revoke", SecurityAuditOutcome.Requested, SecurityAuditInitiator.LocalUser,
            host.Request.TaskId.Value.ToString("D"));
        audit.Write(requested);
        audit.Write(requested.WithOutcome(SecurityAuditOutcome.Succeeded));
    }
    public void Revoke()
    {
        DevelopmentPreviewLease? previous;
        lock (gate) { previous = TakeLease(); }
        previous?.Dispose();
    }
    private DevelopmentPreviewLease? TakeLease()
    {
        var previous = lease;
        lease = null;
        revision++;
        return previous;
    }
    public void Dispose()
    {
        DevelopmentPreviewLease? previous;
        lock (gate) { disposed = true; previous = TakeLease(); }
        previous?.Dispose();
    }
}
