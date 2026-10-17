using System.Collections.Concurrent;
using System.Diagnostics;

using AwesomeAssertions;

using Kora.Application.Auditing;
using Kora.Application.Diagnostics;
using Kora.Core.Auditing;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

using Microsoft.Extensions.Logging;

namespace Kora.Application.UnitTests.Diagnostics;

[Collection("Host tracing")]
public sealed class BufferedEvidenceLoggerProviderTests
{
    [Fact]
    public async Task AuditPersistenceDoesNotHoldTheDiagnosticCaptureLock()
    {
        using var sink = new GatedSink();
        sink.Release.Set();
        sink.AuditRelease.Reset();
        await using var provider = new EvidenceLoggerProvider([sink], sink, bufferDiagnostics: true);
        using var factory = LoggerFactory.Create(builder => builder.AddProvider(provider));
        var auditLog = new LoggerSecurityAuditLog(factory.CreateLogger<LoggerSecurityAuditLog>());
        var audit = new SecurityAuditEvent(Guid.NewGuid(), SecurityAuditCategory.ConfigurationWrite,
            "fixture.save", SecurityAuditOutcome.Requested, SecurityAuditInitiator.LocalUser, "fixture.target");
        var write = auditLog.WriteAsync(audit, TestContext.Current.CancellationToken);
        try
        {
            await sink.AuditStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
            Log(provider.CreateLogger("fixture"), State(1));
            write.IsCompleted.Should().BeFalse();
        }
        finally { sink.AuditRelease.Set(); }
        await write;
        await provider.FlushAsync(TestContext.Current.CancellationToken);
        sink.Diagnostics.Should().ContainSingle();
        sink.Audits.Should().ContainSingle();
    }

    [Fact]
    public async Task StalledPersistenceDoesNotBlockCaptureAndPreservesCallTimeContext()
    {
        using var sink = new GatedSink();
        await using var provider = new EvidenceLoggerProvider([sink], sink, bufferDiagnostics: true);
        var logger = provider.CreateLogger("fixture");
        var request = HostRequest.Create(RequestOrigin.LocalUi);
        string trace;
        string span;
        using (var host = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request))
        {
            trace = host.Activity!.TraceId.ToHexString();
            span = host.Activity.SpanId.ToHexString();
            using var scope = logger.BeginScope(new Dictionary<string, object?>(StringComparer.Ordinal) { ["Sequence"] = 7 });
            var state = State(1);
            Log(logger, state);
            await sink.Started.Task.WaitAsync(TestContext.Current.CancellationToken);
            state["Sequence"] = 99;
            Log(logger, State(2));
            sink.Diagnostics.Should().BeEmpty();
            host.Complete(HostOperationOutcome.Completed);
        }
        sink.Release.Set();
        await provider.FlushAsync(TestContext.Current.CancellationToken);

        sink.Diagnostics.Select(row => row.Properties["Sequence"].CanonicalValue).Should().Equal("1", "2");
        sink.Diagnostics.Should().OnlyContain(row => row.Host == request
            && row.Trace!.TraceId == trace && row.Trace.SpanId == span);
        sink.Diagnostics.First().Scopes.Single()["Sequence"].CanonicalValue.Should().Be("7");
        sink.Activities.Should().ContainSingle().Which.Host.Should().Be(request);
        sink.PersistenceHadHostContext.Should().BeFalse();
    }

    [Fact]
    public async Task RequiredAuditWaitsForDeliveryInsteadOfAcknowledgingQueueAdmission()
    {
        using var sink = new GatedSink();
        await using var provider = new EvidenceLoggerProvider([sink], sink, bufferDiagnostics: true);
        using var factory = LoggerFactory.Create(builder => builder.AddProvider(provider));
        using var host = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi),
            HostActivityLayer.Application, HostOperation.Policy);
        Log(provider.CreateLogger("fixture"), State(1));
        await sink.Started.Task.WaitAsync(TestContext.Current.CancellationToken);
        var audit = new SecurityAuditEvent(Guid.NewGuid(), SecurityAuditCategory.ConfigurationWrite,
            "fixture.save", SecurityAuditOutcome.Requested, SecurityAuditInitiator.LocalUser, "fixture.target");
        var write = new LoggerSecurityAuditLog(factory.CreateLogger<LoggerSecurityAuditLog>())
            .WriteAsync(audit, TestContext.Current.CancellationToken);
        try
        {
            write.IsCompleted.Should().BeFalse();
            sink.Audits.Should().BeEmpty();
            sink.Diagnostics.Should().BeEmpty();
        }
        finally { sink.Release.Set(); }
        await write;
        sink.Audits.Should().ContainSingle().Which.Audit.Should().Be(audit);
    }

    [Fact]
    public async Task ShutdownJoinsAllAcceptedWritesWithoutBlockingTheCaller()
    {
        using var sink = new GatedSink();
        var provider = new EvidenceLoggerProvider([sink], sink, bufferDiagnostics: true);
        var logger = provider.CreateLogger("fixture");
        Log(logger, State(1));
        await sink.Started.Task.WaitAsync(TestContext.Current.CancellationToken);
        Log(logger, State(2));
        var disposal = provider.DisposeAsync().AsTask();
        try { disposal.IsCompleted.Should().BeFalse(); }
        finally { sink.Release.Set(); }
        await disposal;
        sink.Diagnostics.Should().HaveCount(2);
        var afterClose = () => Log(logger, State(3));
        afterClose.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public async Task SaturationFailsExplicitlyInsteadOfDroppingOrBlocking()
    {
        using var sink = new GatedSink();
        var provider = new EvidenceLoggerProvider([sink], sink, bufferDiagnostics: true);
        var logger = provider.CreateLogger("fixture");
        Log(logger, State(0));
        await sink.Started.Task.WaitAsync(TestContext.Current.CancellationToken);
        try
        {
            for (var index = 0; index < 1024; index++) { Log(logger, State(index + 1)); }
            var overflow = () => Log(logger, State(1025));
            overflow.Should().Throw<IOException>().WithMessage("*buffer is full*");
        }
        finally { sink.Release.Set(); }
        var dispose = async () => await provider.DisposeAsync();
        await dispose.Should().ThrowAsync<IOException>();
        sink.Diagnostics.Should().HaveCount(1025);
    }

    [Fact]
    public async Task BackgroundFailureAttemptsIndependentSinksAndClosesLaterAdmission()
    {
        using var failing = new GatedSink { Fail = true };
        using var healthy = new GatedSink();
        healthy.Release.Set();
        var provider = new EvidenceLoggerProvider([failing, healthy], healthy, bufferDiagnostics: true);
        Log(provider.CreateLogger("fixture"), State(1));
        await failing.Started.Task.WaitAsync(TestContext.Current.CancellationToken);
        var flushing = provider.FlushAsync(TestContext.Current.CancellationToken);
        failing.Release.Set();
        var flush = () => flushing;
        await flush.Should().ThrowAsync<IOException>();
        healthy.Diagnostics.Should().ContainSingle();
        healthy.Gaps.Should().Contain(gap => gap.Reason == EvidenceGapReason.SinkFailure);
        var laterLog = () => Log(provider.CreateLogger("fixture"), State(2));
        laterLog.Should().Throw<IOException>();
        var dispose = async () => await provider.DisposeAsync();
        await dispose.Should().ThrowAsync<IOException>();
    }

    private static Dictionary<string, object?> State(int sequence) => new(StringComparer.Ordinal)
    {
        ["{OriginalFormat}"] = "Fixture {Sequence}.",
        ["Sequence"] = sequence,
    };

    private static void Log(ILogger logger, Dictionary<string, object?> state) =>
        logger.Log(LogLevel.Information, new EventId(1), state, null, static (_, _) => "unused");

    private sealed class GatedSink : IDeferredEvidenceSink, IEvidenceGapReporter, IDisposable
    {
        private readonly CancellationToken token = TestContext.Current.CancellationToken;
        public string Name => "fixture";
        public ManualResetEventSlim Release { get; } = new();
        public ManualResetEventSlim AuditRelease { get; } = new(true);
        public TaskCompletionSource AuditStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ConcurrentQueue<DiagnosticEnvelope> Diagnostics { get; } = new();
        public ConcurrentQueue<CompletedActivityEnvelope> Activities { get; } = new();
        public ConcurrentQueue<AuditEnvelope> Audits { get; } = new();
        public ConcurrentQueue<EvidenceGap> Gaps { get; } = new();
        public bool Fail { get; init; }
        public bool PersistenceHadHostContext { get; private set; }
        public Action PrepareDiagnostic(DiagnosticEnvelope envelope) => () => WriteDiagnostic(envelope);
        public Action PrepareActivity(CompletedActivityEnvelope envelope) => () => WriteActivity(envelope);
        public void WriteDiagnostic(DiagnosticEnvelope envelope)
        {
            Started.TrySetResult();
            Release.Wait(token);
            PersistenceHadHostContext |= HostActivity.Current is not null;
            if (Fail) { throw new IOException("Fixture sink unavailable."); }
            Diagnostics.Enqueue(envelope);
        }
        public void WriteActivity(CompletedActivityEnvelope envelope) => Activities.Enqueue(envelope);
        public void WriteAudit(AuditEnvelope envelope)
        {
            AuditStarted.TrySetResult();
            AuditRelease.Wait(token);
            Audits.Enqueue(envelope);
        }
        public void Report(EvidenceGap gap) => Gaps.Enqueue(gap);
        public void Dispose()
        {
            Release.Dispose();
            AuditRelease.Dispose();
        }
    }
}
