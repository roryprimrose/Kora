using System.Diagnostics;

using Kora.Application.Communication;
using Kora.Application.Configuration;
using Kora.Application.Hosting;
using Kora.Application.Voice;
using Kora.Core.Auditing;
using Kora.Core.Communication;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;

namespace Kora.Application.UnitTests.Configuration;

internal sealed class CommunicationActivityFixture : IAsyncDisposable
{
    private readonly ActivityListener listener = new()
    {
        ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
        Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
    };

    internal CommunicationActivityFixture(ILogger<CommunicationActivityCallStateService>? observationLogger = null,
        ILogger<CommunicationActivityConfigurationService>? configurationLogger = null)
    {
        ActivitySource.AddActivityListener(listener);
        Preferences = new(Store);
        Observations = new(Source, Preferences, Time, observationLogger ?? NullLogger<CommunicationActivityCallStateService>.Instance);
        Policy = new(Observations);
        Admission = new(Authority, Authority, new HostTaskCoordinator(Authority));
        Configuration = new(Preferences, Observations, Admission, Audit,
            configurationLogger ?? NullLogger<CommunicationActivityConfigurationService>.Instance);
    }

    internal CancellationToken Token => TestContext.Current.CancellationToken;
    internal MemoryStore Store { get; } = new();
    internal Clock Time { get; } = new();
    internal Platform Source { get; } = new();
    internal AudioControlTestStore Authority { get; } = new();
    internal AuditSink Audit { get; } = new();
    internal AudioControlAdmission Admission { get; }
    internal LocalCommunicationActivityPreferences Preferences { get; }
    internal CommunicationActivityCallStateService Observations { get; }
    internal CommunicationActivityConfigurationService Configuration { get; }
    internal CallCommunicationPolicy Policy { get; }
    internal bool Eligible { get; set; } = true;

    internal async Task Initialize(CommunicationActivitySelection selection = CommunicationActivitySelection.None)
    {
        Preferences.Save(selection);
        Observations.BindHost(() => Eligible);
        Observations.ObservePreferences();
        if (selection == CommunicationActivitySelection.WindowsCommunicationsAudio && Source.IsQualified && Eligible)
        { await Source.Started.Task.WaitAsync(Token); }
        else { await Observations.JoinAsync(); }
    }

    internal Task<CallMutationOutcome> Select(CommunicationActivitySelection selection,
        RequestOrigin origin = RequestOrigin.LocalUi, long? revision = null, CancellationToken? token = null) =>
        Configuration.SelectAsync(selection, origin, revision ?? Policy.Current.Revision, Policy, () => Eligible, token ?? Token);

    public async ValueTask DisposeAsync()
    {
        Source.Release.TrySetResult();
        await Observations.DisposeAsync();
        Policy.Dispose();
        await Admission.DisposeAsync();
        listener.Dispose();
    }

    internal sealed class MemoryStore : IPreferenceStore
    {
        internal Dictionary<string, string> Values { get; } = new(StringComparer.Ordinal);
        internal Action<string>? BeforeRead { get; set; }
        internal Action<string>? BeforeWrite { get; set; }
        internal Action<string>? AfterWrite { get; set; }
        public string? ReadText(string fileName)
        {
            BeforeRead?.Invoke(fileName);
            return Values.GetValueOrDefault(fileName);
        }
        public string[]? ReadLines(string fileName) => ReadText(fileName)?.Split('\n');
        public void WriteText(string fileName, string contents)
        {
            BeforeWrite?.Invoke(fileName);
            Values[fileName] = contents;
            AfterWrite?.Invoke(fileName);
        }
        public void WriteLines(string fileName, IEnumerable<string> contents) => WriteText(fileName, string.Join('\n', contents));
        public void Delete(string fileName) { BeforeWrite?.Invoke(fileName); Values.Remove(fileName); AfterWrite?.Invoke(fileName); }
    }

    internal sealed class Clock : TimeProvider
    {
        private DateTimeOffset now = new(2026, 10, 26, 8, 0, 0, TimeSpan.Zero);
        internal List<Timer> Timers { get; } = [];
        public override DateTimeOffset GetUtcNow() => now;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new Timer(callback, state);
            Timers.Add(timer);
            return timer;
        }
        internal void Advance(TimeSpan age, bool tick = true) { now += age; if (tick) { foreach (var timer in Timers) { timer.Tick(); } } }
        internal sealed class Timer(TimerCallback callback, object? state) : ITimer
        {
            internal bool Disposed { get; private set; }
            internal void Tick() => callback(state);
            public bool Change(TimeSpan dueTime, TimeSpan period) => !Disposed;
            public void Dispose() => Disposed = true;
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }

    internal sealed class Platform : ICommunicationActivitySource
    {
        private long sequence;
        internal long FirstSequence { get; set; } = 1;
        internal Action? BeforeStart { get; set; }
        internal Action? AfterStart { get; set; }
        internal Action<CommunicationActivityObservation>? Publish { get; private set; }
        internal Guid Generation { get; private set; }
        internal Guid DeviceEpoch { get; } = Guid.NewGuid();
        internal TaskCompletionSource Started { get; set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool AutoRelease { get; set; } = true;
        internal int Opens { get; private set; }
        internal int Releases { get; private set; }
        internal Exception? OpenFailure { get; set; }
        internal Exception? DisposeFailure { get; set; }
        public bool IsQualified { get; set; } = true;
        public bool HasUnreleasedResources { get; set; }
        public async Task ObserveAsync(Guid generation, Action<CommunicationActivityObservation> publish, CancellationToken token)
        {
            BeforeStart?.Invoke();
            Generation = generation;
            Publish = publish;
            Opens++;
            sequence = FirstSequence;
            publish(new(generation, DeviceEpoch, sequence, CallState.Unknown,
                DateTimeOffset.MinValue, CommunicationActivityHealth.Starting));
            Started.TrySetResult();
            AfterStart?.Invoke();
            if (OpenFailure is { } error) { throw error; }
            using var registration = token.Register(() =>
            {
                Cancelled.TrySetResult();
                if (AutoRelease) { Release.TrySetResult(); }
            });
            await Release.Task;
            Releases++;
        }
        internal void Signal(CallState state, DateTimeOffset observedAt,
            CommunicationActivityHealth health = CommunicationActivityHealth.Healthy) =>
            Publish!(new(Generation, DeviceEpoch, ++sequence, state, observedAt, health));
        public ValueTask DisposeAsync()
        {
            if (DisposeFailure is { } error) { throw error; }
            HasUnreleasedResources = false;
            return ValueTask.CompletedTask;
        }
    }

    internal sealed class AuditSink : ISecurityAuditLog
    {
        internal List<SecurityAuditEvent> Events { get; } = [];
        internal List<HostRequest?> Requests { get; } = [];
        internal Action<SecurityAuditEvent>? BeforeWrite { get; set; }
        public void Write(SecurityAuditEvent auditEvent)
        {
            BeforeWrite?.Invoke(auditEvent);
            Events.Add(auditEvent);
            Requests.Add(HostActivity.Current?.Request);
        }
    }

    internal sealed class EnabledLogger<T> : ILogger<T>
    {
        internal List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));
    }
}