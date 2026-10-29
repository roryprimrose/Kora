using Kora.Core.Auditing;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Skills;

using Microsoft.Extensions.Logging;

namespace Kora.Windows.Skills.RuntimeSetup;

public sealed partial class WindowsBundledRuntimeSetup : IAsyncDisposable
{
    private readonly IBundledRuntimeRegistrationPreferences preferences;
    private readonly IBundledRuntimeInstallationInspector inspector;
    private readonly ISecurityAuditLog audit;
    private readonly ILogger<WindowsBundledRuntimeSetup> logger;
    private readonly TimeProvider clock;
    private readonly SemaphoreSlim mutation = new(1, 1);
    private readonly Guid owner = Guid.NewGuid();
    private BundledRuntimeInspection? observed;
    private WindowsBundledScriptWorker? worker;
    private readonly Lock lifecycle = new();
    private Task? disposal;
    private int disposed;

    public WindowsBundledRuntimeSetup(IBundledRuntimeRegistrationPreferences preferences,
        ISecurityAuditLog audit, ILogger<WindowsBundledRuntimeSetup> logger)
        : this(preferences, new WindowsBundledRuntimeInstallationInspector(), audit, logger, TimeProvider.System) { }

    internal WindowsBundledRuntimeSetup(IBundledRuntimeRegistrationPreferences preferences,
        IBundledRuntimeInstallationInspector inspector, ISecurityAuditLog audit,
        ILogger<WindowsBundledRuntimeSetup> logger, TimeProvider clock)
    { this.preferences = preferences; this.inspector = inspector; this.audit = audit; this.logger = logger; this.clock = clock; }

    public string Description => Volatile.Read(ref observed)?.Description
        ?? "Runtime registration has not been confirmed by current native inspection. " + BundledRuntimeQualification.Requirements;

    public IBundledScriptWorker CreateWorker()
    {
        lock (lifecycle)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
            if (worker is not null) { return worker; }
            worker = new(() => mutation.CurrentCount != 0 && Volatile.Read(ref observed) is { } state
                ? BundledRuntimeQualification.Resolve(state) : null, ContainedPowerShellProcess.Create);
            return worker;
        }
    }

    public Task<BundledRuntimeInspection> InspectAsync(CancellationToken cancellationToken) => InspectCoreAsync(true, cancellationToken);

    private async Task<BundledRuntimeInspection> InspectCoreAsync(bool inspectInstallation, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        if (!await mutation.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        { throw new InvalidOperationException("Runtime inspection/setup is already active. Cancel or wait for its physical completion."); }
        try { return await ReadAsync(cancellationToken, inspectInstallation).ConfigureAwait(false); }
        finally { mutation.Release(); }
    }

    private async Task<BundledRuntimeInspection> ReadAsync(CancellationToken cancellationToken, bool inspectInstallation = true)
    {
        Volatile.Write(ref observed, null);
        try
        {
            var saved = await Task.Run(() => (Registration: preferences.Load(), Pending: preferences.LoadPending()), cancellationToken).ConfigureAwait(false);
            var installed = inspectInstallation ? await inspector.InspectAsync(cancellationToken).ConfigureAwait(false) : null;
            cancellationToken.ThrowIfCancellationRequested();
            ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
            var result = new BundledRuntimeInspection(installed, saved.Registration, inspectInstallation, saved.Pending);
            Volatile.Write(ref observed, result);
            return result;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException
            or System.ComponentModel.Win32Exception or System.Security.SecurityException)
        { InspectionFailed(logger, exception); throw; }
    }

    public async Task<BundledRuntimeSetupPlan> ReviewAsync(BundledRuntimeSetupOperation operation,
        Func<bool> current, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(current);
        if (!Enum.IsDefined(operation)) { throw new ArgumentOutOfRangeException(nameof(operation)); }
        var request = HostActivity.RequireCurrent().Request;
        if (request.Origin != RequestOrigin.LocalUi || !current())
        { throw new UnauthorizedAccessException("Runtime setup requires a fresh original-user native review."); }
        var state = await InspectCoreAsync(operation != BundledRuntimeSetupOperation.Forget, cancellationToken).ConfigureAwait(false);
        if (!current() || operation != BundledRuntimeSetupOperation.Forget && state.Installation is null
            || operation != BundledRuntimeSetupOperation.Prepare && state.EffectiveRegistration is null)
        { throw new InvalidOperationException("The selected setup operation has no current exact installation/registration to review."); }
        if (operation == BundledRuntimeSetupOperation.Verify
            && state.Preparation is not (BundledRuntimePreparation.Prepared or BundledRuntimePreparation.Unconfirmed))
        { throw new InvalidDataException("Verification cannot silently replace a changed or other-device inventory pin. Explicitly review Prepare for the new exact installation, or forget the registration."); }
        return new(owner, operation, state, request, current, clock.GetUtcNow().AddMinutes(5));
    }

    public async Task<BundledRuntimeInspection> ApplyAsync(BundledRuntimeSetupPlan plan, bool consent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        var request = HostActivity.RequireCurrent().Request;
        if (plan.Owner != owner || !plan.Consume() || !ReferenceEquals(request, plan.Request))
        { throw new InvalidOperationException("Runtime review is foreign, consumed or belongs to a different host request."); }
        if (!await mutation.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        { throw new InvalidOperationException("Runtime setup retains exclusive ownership until all owned I/O completes."); }
        using var activity = HostActivity.BeginChild(HostActivityLayer.Windows, HostOperation.Runtime);
        var auditEvent = new SecurityAuditEvent(Guid.NewGuid(), SecurityAuditCategory.ResourceWrite,
            "bundled-runtime." + plan.Operation.ToString().ToLowerInvariant(), SecurityAuditOutcome.Requested,
            SecurityAuditInitiator.LocalUser, "powershell.protected-installed");
        var uncertain = false;
        try
        {
            await WriteAuditAsync(auditEvent).ConfigureAwait(false);
            void RequireCurrent()
            {
                cancellationToken.ThrowIfCancellationRequested();
                ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
                if (!consent || !plan.Current() || clock.GetUtcNow() >= plan.Expires || worker is { IsQuiescent: false })
                { throw new UnauthorizedAccessException("Runtime consent, host/privacy/call/source revision or exclusive worker ownership is no longer current."); }
            }
            RequireCurrent();
            var inspectInstallation = plan.Operation != BundledRuntimeSetupOperation.Forget;
            var before = await ReadAsync(cancellationToken, inspectInstallation).ConfigureAwait(false);
            RequireCurrent();
            if (before.Registration != plan.Expected.Registration
                || before.PendingRegistration != plan.Expected.PendingRegistration
                || !string.Equals(before.Installation?.InventoryDigest, plan.Expected.Installation?.InventoryDigest, StringComparison.Ordinal)
                || !string.Equals(before.Installation?.DeviceDigest, plan.Expected.Installation?.DeviceDigest, StringComparison.Ordinal))
            { throw new InvalidOperationException("The exact reviewed installation or saved registration changed. Review again."); }
            var pin = plan.Operation == BundledRuntimeSetupOperation.Forget
                ? plan.Expected.EffectiveRegistration!
                : new BundledRuntimeRegistration(before.Installation!.InventoryDigest, before.Installation.DeviceDigest, false);
            Volatile.Write(ref observed, null);
            RequireCurrent();
            uncertain = true;
            var pendingPin = new BundledRuntimeRegistration(pin.InventoryDigest, pin.DeviceDigest, false);
            await Task.Run(() => { RequireCurrent(); preferences.SavePending(pendingPin); }, CancellationToken.None).ConfigureAwait(false);
            RequireCurrent();
            var pending = await ReadAsync(cancellationToken, inspectInstallation).ConfigureAwait(false);
            RequireCurrent();
            if (pending.PendingRegistration != pendingPin
                || plan.Operation != BundledRuntimeSetupOperation.Forget
                    && pending.Preparation != BundledRuntimePreparation.Unconfirmed)
            { throw new InvalidDataException("Runtime preparation readback did not match. State remains unconfirmed; inspect before retrying."); }
            var finalPin = plan.Operation == BundledRuntimeSetupOperation.Forget
                ? null : new BundledRuntimeRegistration(pin.InventoryDigest, pin.DeviceDigest, true);
            await Task.Run(() => { RequireCurrent(); preferences.Save(finalPin); }, CancellationToken.None).ConfigureAwait(false);
            RequireCurrent();
            var result = await ReadAsync(cancellationToken, inspectInstallation).ConfigureAwait(false);
            RequireCurrent();
            if (result.Registration != finalPin || result.PendingRegistration != pendingPin
                || finalPin is not null && result.Preparation != BundledRuntimePreparation.Unconfirmed)
            { throw new InvalidDataException("Final runtime registration readback failed. Do not infer completion; inspect and reconcile."); }
            await WriteAuditAsync(auditEvent.WithOutcome(SecurityAuditOutcome.Succeeded, "registration-only-no-execution")).ConfigureAwait(false);
            RequireCurrent();
            await Task.Run(() => { RequireCurrent(); preferences.SavePending(null); }, CancellationToken.None).ConfigureAwait(false);
            RequireCurrent();
            result = result with { PendingRegistration = null };
            Volatile.Write(ref observed, result);
            activity.Complete(HostOperationOutcome.Completed);
            return result;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException
            or OperationCanceledException or System.ComponentModel.Win32Exception or System.Security.SecurityException)
        {
            Volatile.Write(ref observed, null);
            SetupFailed(logger, exception, uncertain);
            activity.Complete(exception is OperationCanceledException ? HostOperationOutcome.Cancelled : HostOperationOutcome.Failed);
            await WriteAuditAsync(auditEvent.WithOutcome(uncertain ? SecurityAuditOutcome.Unknown
                : exception is OperationCanceledException ? SecurityAuditOutcome.Cancelled
                : exception is UnauthorizedAccessException ? SecurityAuditOutcome.Denied : SecurityAuditOutcome.Failed,
                uncertain ? "inspect-and-reconcile" : "no-registration-committed")).ConfigureAwait(false);
            throw;
        }
        finally { mutation.Release(); }
    }

    private Task WriteAuditAsync(SecurityAuditEvent auditEvent) => Task.Run(() => audit.Write(auditEvent));

    public ValueTask DisposeAsync()
    {
        lock (lifecycle)
        {
            Interlocked.Exchange(ref disposed, 1);
            Volatile.Write(ref observed, null);
            return new(disposal ??= DisposeCoreAsync());
        }
    }

    private async Task DisposeCoreAsync()
    {
        await mutation.WaitAsync().ConfigureAwait(false);
        mutation.Release();
        mutation.Dispose();
    }
}
