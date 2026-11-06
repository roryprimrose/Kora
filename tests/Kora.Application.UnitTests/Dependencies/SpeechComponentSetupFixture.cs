using System.Diagnostics;

using Kora.Application.Dependencies;
using Kora.Core.Auditing;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;

namespace Kora.Testing;

internal sealed class SpeechComponentSetupFixture
{
    internal SpeechComponentDefinition Component { get; } = SpeechComponentFixture.Definition();
    internal MemoryStore Store { get; } = new();
    internal Runtime RuntimeAdapter { get; } = new();
    internal Audit AuditLog { get; } = new();
    internal Clock Time { get; } = new();
    internal DiagnosticLogger Logger { get; } = new();
    internal bool Eligible { get; set; } = true;
    internal Func<bool>? AdmissionOverride { get; set; }
    internal List<string> ProgressMessages { get; } = [];
    internal IProgress<string> Progress => new SynchronousProgress(ProgressMessages.Add);
    internal SpeechComponentSetupService Service { get; }

    internal SpeechComponentSetupFixture()
    {
        Service = Create(Store);
    }

    internal SpeechComponentSetupService Create(ISpeechComponentStore store) =>
        new(new([Component]), store, RuntimeAdapter, AuditLog, Logger, Time);

    internal Task<SpeechComponentSetupPlan> Review(SpeechComponentOperation operation = SpeechComponentOperation.Prepare) =>
        Service.ReviewAsync(Component.Identity, operation, () => AdmissionOverride?.Invoke() ?? Eligible, TestContext.Current.CancellationToken);

    internal sealed class SynchronousProgress(Action<string> report) : IProgress<string>
    {
        public void Report(string value) => report(value);
    }

    internal sealed class Clock : TimeProvider
    {
        internal DateTimeOffset Now { get; set; } = new(2026, 10, 23, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    internal sealed class MemoryStore : ISpeechComponentStore
    {
        internal SpeechComponentSnapshot State { get; set; } = new(false, false, SpeechComponentOwnership.Missing, "missing");
        internal int Inspections { get; private set; }
        internal int Prepares { get; private set; }
        internal int Removals { get; private set; }
        internal Exception? InspectionFailure { get; set; }
        internal Exception? WriteFailure { get; set; }
        internal bool WrongRemoval { get; set; }
        internal bool CorruptPrepare { get; set; }
        internal Action? BeforeWrite { get; set; }
        internal Action? AfterWrite { get; set; }
        internal Action? AfterInspect { get; set; }
        public Task<SpeechComponentSnapshot> InspectAsync(SpeechComponentDefinition component, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Inspections++;
            if (InspectionFailure is { } failure) { throw failure; }
            var state = State;
            AfterInspect?.Invoke();
            return Task.FromResult(state);
        }
        public Task PrepareAsync(SpeechComponentDefinition component, SpeechComponentSnapshot expected, Func<bool> admitted,
            IProgress<string> progress, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            BeforeWrite?.Invoke();
            if (!admitted()) { throw new UnauthorizedAccessException("Fixture admission revoked."); }
            if (WriteFailure is { } failure) { throw failure; }
            Prepares++;
            State = new(true, !CorruptPrepare, SpeechComponentOwnership.Managed, "installed");
            AfterWrite?.Invoke();
            return Task.CompletedTask;
        }
        public Task RemoveAsync(SpeechComponentDefinition component, SpeechComponentSnapshot expected, Func<bool> admitted,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            BeforeWrite?.Invoke();
            if (!admitted()) { throw new UnauthorizedAccessException("Fixture admission revoked."); }
            if (WriteFailure is { } failure) { throw failure; }
            Removals++;
            State = WrongRemoval ? new(false, false, SpeechComponentOwnership.RemovalPending, "pending")
                : new(false, false, SpeechComponentOwnership.Missing, "missing");
            AfterWrite?.Invoke();
            return Task.CompletedTask;
        }
    }

    internal sealed class Runtime : ISpeechComponentRuntime
    {
        internal SpeechComponentCompatibility Compatibility { get; set; } = SpeechComponentCompatibility.Compatible;
        internal SpeechComponentMutationState MutationState { get; set; } = SpeechComponentMutationState.Acquired;
        internal SpeechComponentFunctionalEvidence Functional { get; set; } = SpeechComponentFunctionalEvidence.Verified;
        internal SpeechComponentFunctionalEvidence ExistingFunctional { get; set; } = SpeechComponentFunctionalEvidence.NotChecked;
        internal Exception? ObservationFailure { get; set; }
        internal int Checks { get; private set; }
        internal int Leases { get; private set; }
        internal int Releases { get; private set; }
        internal Exception? ReleaseFailure { get; set; }
        internal Action? BeforeCheck { get; set; }
        internal Action? AfterRelease { get; set; }
        internal bool ReturnNoMutation { get; set; }
        internal bool ReturnNoObservation { get; set; }
        internal bool ReturnNoVerification { get; set; }
        internal long ConsumerRevision { get; set; }
        internal string? NativeIdentity { get; set; } = "fixture-native";
        internal bool ConsumersKnown { get; set; } = true;
        internal string? Dependents { get; set; } = "None; exact fixture-owned runtime.";
        internal int Holds { get; private set; }
        public void HoldUnavailable(SpeechComponentDefinition component) => Holds++;
        public SpeechComponentRuntimeObservation Observe(SpeechComponentDefinition component)
        {
            if (ObservationFailure is { } failure) { throw failure; }
            if (ReturnNoObservation) { return null!; }
            return new(Compatibility, ExistingFunctional, "Fixture-only installed runtime observation; no probe was run.")
            {
                ConsumerRevision = ConsumerRevision,
                NativeIdentity = NativeIdentity,
                ConsumersKnown = ConsumersKnown,
                Dependents = Dependents,
            };
        }
        public ValueTask<SpeechComponentMutationAdmission> TryAcquireExclusiveAsync(SpeechComponentDefinition component, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (ReturnNoMutation) { return ValueTask.FromResult<SpeechComponentMutationAdmission>(null!); }
            if (MutationState != SpeechComponentMutationState.Acquired)
            {
                return ValueTask.FromResult(new SpeechComponentMutationAdmission(MutationState));
            }
            Leases++;
            return ValueTask.FromResult(new SpeechComponentMutationAdmission(SpeechComponentMutationState.Acquired, new Lease(this)));
        }
        public Task<SpeechComponentRuntimeObservation> VerifyAsync(SpeechComponentDefinition component, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Checks++;
            BeforeCheck?.Invoke();
            if (ReturnNoVerification) { return Task.FromResult<SpeechComponentRuntimeObservation>(null!); }
            return Task.FromResult(new SpeechComponentRuntimeObservation(Compatibility, Functional,
                "Fixture-only explicit verification result."));
        }
        private sealed class Lease(Runtime owner) : IAsyncDisposable
        {
            public ValueTask DisposeAsync()
            {
                owner.Releases++;
                if (owner.ReleaseFailure is { } failure) { throw failure; }
                owner.AfterRelease?.Invoke();
                return ValueTask.CompletedTask;
            }
        }
    }

    internal sealed class DiagnosticLogger : ILogger<SpeechComponentSetupService>
    {
        internal bool Enabled { get; set; } = true;
        internal List<EventId> Events { get; } = [];
        internal List<string> Messages { get; } = [];
        internal List<Exception?> Exceptions { get; } = [];
        public bool IsEnabled(LogLevel logLevel) => Enabled;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Events.Add(eventId);
            Messages.Add(formatter(state, exception));
            Exceptions.Add(exception);
        }
    }

    internal sealed class Audit : ISecurityAuditLog
    {
        internal List<SecurityAuditEvent> Events { get; } = [];
        internal List<HostRequest> Requests { get; } = [];
        internal List<ActivityContext> Traces { get; } = [];
        internal SecurityAuditOutcome? FailOn { get; set; }
        public void Write(SecurityAuditEvent auditEvent)
        {
            if (auditEvent.Outcome == FailOn) { throw new IOException("Fixture audit unavailable."); }
            Events.Add(auditEvent);
            Requests.Add(HostActivity.RequireCurrent().Request);
            Traces.Add(Activity.Current!.Context);
        }
    }
}