using AwesomeAssertions;

using Kora.Application.Diagnostics;
using Kora.Core.Diagnostics;

using Microsoft.Extensions.Logging;

namespace Kora.Application.UnitTests.Diagnostics;

[Collection("Host tracing")]
public sealed class BufferedEvidenceLifecycleTests
{
    [Fact]
    public void BufferingRejectsSinksWithoutCallTimeAdmission()
    {
        var sink = new SynchronousSink();
        var gaps = new Gaps();
        var create = () => new EvidenceLoggerProvider([sink], gaps, bufferDiagnostics: true);
        create.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task UnbufferedFlushIsAlreadyCompleteAndDisposalJoinsAnActiveCapture()
    {
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var token = TestContext.Current.CancellationToken;
        var sink = new SynchronousSink { Write = () =>
        {
            entered.SetResult();
            release.Wait(token);
        }};
        var provider = new EvidenceLoggerProvider([sink], new Gaps());
        await provider.FlushAsync(token);
        var state = new Dictionary<string, object?>(StringComparer.Ordinal) { ["{OriginalFormat}"] = "Fixture." };
        var write = Task.Run(() => provider.CreateLogger("fixture").Log(LogLevel.Information, new EventId(1), state,
            null, static (_, _) => "unused"), token);
        await entered.Task.WaitAsync(token);
        var dispose = provider.DisposeAsync().AsTask();
        try { dispose.IsCompleted.Should().BeFalse(); }
        finally { release.Set(); }
        await Task.WhenAll(write, dispose);
    }

    [Fact]
    public async Task UnexpectedWorkerFaultStopsLaterAdmissionAndIsObservedDuringShutdown()
    {
        var writer = new BufferedEvidenceWriter();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        writer.Enqueue(() =>
        {
            entered.SetResult();
            throw new InvalidDataException("Fixture unexpected sink contract failure.");
        });
        await entered.Task.WaitAsync(TestContext.Current.CancellationToken);
        var close = async () => await writer.DisposeAsync();
        await close.Should().ThrowAsync<InvalidDataException>();
        var admit = writer.RequireHealthy;
        admit.Should().Throw<IOException>().WithMessage("*stopped unexpectedly*");
    }

    [Fact]
    public async Task UnavailablePreparationReportsItsGapWithoutPoisoningIndependentBufferedDelivery()
    {
        var unavailable = new DeferredSink { Name = "unavailable", Unavailable = true };
        var delivered = 0;
        var healthy = new DeferredSink { Write = () => Interlocked.Increment(ref delivered) };
        var gaps = new Gaps();
        await using var provider = new EvidenceLoggerProvider([unavailable, healthy], gaps, bufferDiagnostics: true);
        var state = new Dictionary<string, object?>(StringComparer.Ordinal) { ["{OriginalFormat}"] = "Fixture." };
        provider.CreateLogger("fixture").Log(LogLevel.Information, new EventId(1), state,
            null, static (_, _) => "unused");
        await provider.FlushAsync(TestContext.Current.CancellationToken);
        delivered.Should().Be(1);
        gaps.Records.Should().Contain(gap => gap.Sink == "unavailable");
    }

    private sealed class Gaps : IEvidenceGapReporter
    {
        public System.Collections.Concurrent.ConcurrentQueue<EvidenceGap> Records { get; } = new();
        public void Report(EvidenceGap gap) => Records.Enqueue(gap);
    }

    private class SynchronousSink : IEvidenceSink
    {
        public string Name { get; init; } = "fixture";
        public Action Write { get; init; } = static () => { };
        public void WriteDiagnostic(DiagnosticEnvelope envelope) => Write();
        public void WriteActivity(CompletedActivityEnvelope envelope) => Write();
        public void WriteAudit(AuditEnvelope envelope) => Write();
    }

    private sealed class DeferredSink : SynchronousSink, IDeferredEvidenceSink
    {
        public bool Unavailable { get; init; }
        public Action PrepareDiagnostic(DiagnosticEnvelope envelope) =>
            Unavailable ? throw new DiagnosticRetentionUnavailableException() : () => WriteDiagnostic(envelope);
        public Action PrepareActivity(CompletedActivityEnvelope envelope) => () => WriteActivity(envelope);
    }
}
