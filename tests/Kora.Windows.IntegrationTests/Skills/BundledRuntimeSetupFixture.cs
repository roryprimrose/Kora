using Kora.Core.Auditing;
using Kora.Core.Skills;
using Kora.Windows.Skills.RuntimeSetup;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests.Skills;

internal sealed class BundledRuntimeSetupFixture
{
    internal static BundledRuntimeInstallation Installation { get; } = new(
        @"C:\Program Files\PowerShell\7\pwsh.exe", "7.6.9.0", new('a', 64), new('b', 64),
        Array.AsReadOnly(new[] { new BundledRuntimeFile("pwsh.exe", 100, new('c', 64)) }),
        "Synthetic MIT notice, no native bytes", "Synthetic third-party notice");
    internal Preferences Saved { get; } = new();
    internal Inspector Native { get; } = new();
    internal Audit AuditLog { get; } = new();
    internal Clock Time { get; } = new();
    internal bool Current { get; set; } = true;
    internal WindowsBundledRuntimeSetup Service { get; }
    internal BundledRuntimeSetupFixture() => Service = Reconstruct();
    internal WindowsBundledRuntimeSetup Reconstruct() =>
        new(Saved, Native, AuditLog, NullLogger<WindowsBundledRuntimeSetup>.Instance, Time);
    internal Task<BundledRuntimeSetupPlan> Review(BundledRuntimeSetupOperation operation = BundledRuntimeSetupOperation.Prepare) =>
        Service.ReviewAsync(operation, () => Current, CancellationToken.None);

    internal sealed class Preferences : IBundledRuntimeRegistrationPreferences
    {
        internal BundledRuntimeRegistration? Value { get; set; }
        internal BundledRuntimeRegistration? Pending { get; set; }
        internal int Writes { get; private set; }
        internal Action? BeforeRead { get; set; }
        internal Action? AfterWrite { get; set; }
        internal Exception? ReadFailure { get; set; }
        internal Exception? WriteFailure { get; set; }
        public BundledRuntimeRegistration? Load()
        {
            BeforeRead?.Invoke();
            if (ReadFailure is { } failure) { throw failure; }
            return Value;
        }
        public void Save(BundledRuntimeRegistration? registration)
        {
            if (WriteFailure is { } failure) { throw failure; }
            Value = registration;
            Writes++;
            AfterWrite?.Invoke();
        }
        public BundledRuntimeRegistration? LoadPending()
        {
            if (ReadFailure is { } failure) { throw failure; }
            return Pending;
        }
        public void SavePending(BundledRuntimeRegistration? registration)
        {
            if (WriteFailure is { } failure) { throw failure; }
            Pending = registration;
            Writes++;
            AfterWrite?.Invoke();
        }
    }

    internal sealed class Inspector : IBundledRuntimeInstallationInspector
    {
        internal BundledRuntimeInstallation? Value { get; set; } = Installation;
        internal Func<Task>? Hold { get; set; }
        internal Action? AfterRead { get; set; }
        internal Exception? Failure { get; set; }
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<BundledRuntimeInstallation?> InspectAsync(CancellationToken cancellationToken)
        {
            Entered.TrySetResult();
            if (Hold is { } hold) { await hold(); }
            if (Failure is { } failure) { throw failure; }
            var value = Value;
            AfterRead?.Invoke();
            return value;
        }
    }

    internal sealed class Audit : ISecurityAuditLog
    {
        internal List<SecurityAuditEvent> Events { get; } = [];
        internal Action<SecurityAuditEvent>? BeforeWrite { get; set; }
        public void Write(SecurityAuditEvent auditEvent) { BeforeWrite?.Invoke(auditEvent); Events.Add(auditEvent); }
    }

    internal sealed class Clock : TimeProvider
    {
        internal DateTimeOffset Now { get; set; } = DateTimeOffset.UnixEpoch;
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
