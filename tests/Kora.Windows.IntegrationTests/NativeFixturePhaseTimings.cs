using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Text.Json;

using Kora.NativeUxFixture;

using Microsoft.Testing.Platform.Extensions.Messages;

using Xunit;

using NativeFixturePhase = Kora.NativeUxFixture.NativeFixturePhaseScope.PhaseKind;
using NativeFixturePhaseOutcome = Kora.NativeUxFixture.NativeFixturePhaseScope.Outcome;

namespace Kora.Windows.IntegrationTests
{
    internal sealed class NativeFixturePhaseTimings
    {
        internal const string AttachmentName = "native-fixture-phases.json";
        private static readonly AsyncLocal<OwnerOperation?> CurrentOwner = new();
        private readonly Lock gate = new();
        private readonly TimeProvider clock;
        private readonly DateTimeOffset origin;
        private readonly long started;
        private readonly string testUniqueId;
        private readonly Action<string> publish;
        private readonly Dictionary<NativeFixturePhaseScope, PhaseEntry> phases = [];
        private ExceptionDispatchInfo? publicationFailure;

        internal NativeFixturePhaseTimings(string testUniqueId, TimeProvider clock,
            Action<string> publish)
        {
            this.testUniqueId = testUniqueId;
            this.clock = clock;
            this.publish = publish;
            origin = clock.GetUtcNow();
            started = clock.GetTimestamp();
        }

        internal static NativeFixturePhaseTimings Capture(ITestContext context)
        {
            if (CurrentOwner.Value is { IsActive: true } current) { return current.Owner; }
            var test = context.Test ?? throw new InvalidOperationException("Native phase evidence requires the original running test.");
            var key = "Kora.NativeFixturePhaseTimings." + test.UniqueID;
            lock (context.KeyValueStorage)
            {
                if (context.KeyValueStorage.TryGetValue(key, out var existing))
                {
                    return existing as NativeFixturePhaseTimings
                        ?? throw new InvalidOperationException("Native phase evidence has an invalid test owner.");
                }
                var owner = new NativeFixturePhaseTimings(test.UniqueID, TimeProvider.System,
                    json => context.AddAttachment(AttachmentName, json, replaceExistingValue: true));
                context.KeyValueStorage[key] = owner;
                return owner;
            }
        }

        internal async Task<T> RunAsync<T>(Func<Task<T>> action)
        {
            var previous = CurrentOwner.Value;
            var operation = new OwnerOperation(this);
            CurrentOwner.Value = operation;
            try
            {
                T result;
                try
                {
                    using var phase = new NativeFixturePhaseScope(NativeFixturePhase.NativeUi, Observe);
                    result = await phase.RunAsync(action);
                }
                catch (Exception exception)
                {
                    ExceptionDispatchInfo? failure;
                    lock (gate) { failure = publicationFailure; }
                    if (failure is not null && !ReferenceEquals(exception, failure.SourceException)
                        && !(exception is AggregateException aggregate
                            && aggregate.Flatten().InnerExceptions.Contains(failure.SourceException)))
                    {
                        throw new AggregateException("The native operation and phase evidence publication both failed.",
                            exception, failure.SourceException);
                    }
                    throw;
                }
                ThrowIfPublicationFailed();
                return result;
            }
            finally
            {
                operation.End();
                CurrentOwner.Value = previous;
            }
        }

        internal void Observe(NativeFixturePhaseScope phase, NativeFixturePhaseOutcome outcome)
        {
            lock (gate)
            {
                var now = clock.GetTimestamp();
                if (outcome == NativeFixturePhaseOutcome.Running)
                {
                    phases.Add(phase, new((phases.Count + 1).ToString(CultureInfo.InvariantCulture),
                        Name(phase.Phase), now));
                }
                else
                {
                    var entry = phases[phase];
                    if (entry.Outcome != NativeFixturePhaseOutcome.Running)
                    {
                        throw new InvalidOperationException("A native fixture phase already has a terminal outcome.");
                    }
                    entry.Outcome = outcome;
                    entry.Timing = new(origin + clock.GetElapsedTime(started, entry.Started),
                        origin + clock.GetElapsedTime(started, now), clock.GetElapsedTime(entry.Started, now));
                }
                var entries = phases.Values.ToArray();
                var elapsed = clock.GetElapsedTime(started, now);
                var timing = new TimingProperty(new(origin, origin + elapsed, elapsed),
                    entries.Where(entry => entry.Timing.HasValue)
                        .Select(entry => new StepTimingInfo(entry.Id, entry.Name, entry.Timing!.Value)).ToArray());
                var json = JsonSerializer.Serialize(new PhaseReport(1, testUniqueId, timing,
                    entries.Select(entry => new PhaseState(entry.Id, entry.Name, Outcome(entry.Outcome))).ToArray()));
                try { publish(json); }
                catch (Exception exception)
                {
                    // Report failure must not interrupt the fixture's physical cleanup.
                    publicationFailure ??= ExceptionDispatchInfo.Capture(exception);
                }
            }
        }

        internal void ThrowIfPublicationFailed()
        {
            lock (gate) { publicationFailure?.Throw(); }
        }

        internal void VerifyAfterFixtureCleanup()
        {
            // An active callback owns the outer exception/cleanup boundary, including using-statement unwind.
            if (CurrentOwner.Value is not { IsActive: true } current || !ReferenceEquals(current.Owner, this))
            {
                ThrowIfPublicationFailed();
            }
        }

        private static string Name(NativeFixturePhase phase) => phase switch
        {
            NativeFixturePhase.OwnedStorage => "owned-storage",
            NativeFixturePhase.Setup => "setup",
            NativeFixturePhase.Context => "context",
            NativeFixturePhase.NativeUi => "native-ui",
            NativeFixturePhase.Join => "join",
            NativeFixturePhase.EvidenceFlush => "evidence-flush",
            NativeFixturePhase.Disposal => "disposal",
            _ => throw new ArgumentOutOfRangeException(nameof(phase)),
        };

        private static string Outcome(NativeFixturePhaseOutcome outcome) => outcome switch
        {
            NativeFixturePhaseOutcome.Running => "running",
            NativeFixturePhaseOutcome.Completed => "completed",
            NativeFixturePhaseOutcome.Cancelled => "cancelled",
            NativeFixturePhaseOutcome.Failed => "failed",
            _ => throw new ArgumentOutOfRangeException(nameof(outcome)),
        };

        private sealed class PhaseEntry(string id, string name, long started)
        {
            internal string Id { get; } = id;
            internal string Name { get; } = name;
            internal long Started { get; } = started;
            internal NativeFixturePhaseOutcome Outcome { get; set; } = NativeFixturePhaseOutcome.Running;
            internal TimingInfo? Timing { get; set; }
        }

        internal sealed record PhaseReport(int Schema, string TestUniqueId,
            TimingProperty Timing, PhaseState[] PhaseStates);
        internal sealed record PhaseState(string Id, string Phase, string Outcome);

        private sealed class OwnerOperation(NativeFixturePhaseTimings owner)
        {
            private int active = 1;
            internal NativeFixturePhaseTimings Owner { get; } = owner;
            internal bool IsActive => Volatile.Read(ref active) != 0;
            internal void End() => Interlocked.Exchange(ref active, 0);
        }
    }
}
