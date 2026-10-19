using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Avalonia.Threading;
using AwesomeAssertions;
using Kora.Application.Diagnostics;
using Kora.Core.Diagnostics;
using Kora.Windows.IntegrationTests.Storage;
using Kora.Windows.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Kora.Windows.IntegrationTests.Presentation;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class DispatcherEvidenceResponsivenessTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Independent_file_delivery_keeps_actual_dispatcher_responsive_beyond_projection_capacity(bool batched)
    {
        using var paths = new OwnedStorageFixture();
        using var checkpoint = new SlowCommit();
        var sink = new WindowsSqliteEvidenceSink(paths, null, null, checkpoint);
        sink.Initialize();
        var bytes = SeedLargeHistory(sink, paths);
        var health = new FileEvidenceHealth();
        using var fileLogger = Kora.DailyFileLoggerFactory.Create(paths, health);
        using var copy = new TrackedFile(new Kora.FileEvidenceSink(fileLogger, health));
        try
        {
            IDeferredEvidenceSink projection = batched ? sink : new SingleWriteProjection(sink);
            await using var provider = new EvidenceLoggerProvider([copy, projection], copy,
                bufferDiagnostics: true, requiredOrdinaryCopy: copy);
            var logger = provider.CreateLogger("independent-reveal-fixture");
            var ready = new TaskCompletionSource<AvaloniaUiDispatcher>(TaskCreationOptions.RunContinuationsAsynchronously);
            var end = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var callbacks = 0;
            var drainStarted = 0L;
            checkpoint.Armed = true;
            var ui = HeadlessSession.RunAsync(async () =>
            {
                ready.SetResult(new AvaloniaUiDispatcher());
                await end.Task;
            });
            try
            {
                var dispatcher = await ready.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
                for (var batch = 0; batch < 12; batch++)
                {
                    var heartbeat = new TaskCompletionSource<TimeSpan>(TaskCreationOptions.RunContinuationsAsynchronously);
                    var started = Stopwatch.GetTimestamp();
                    for (var index = 0; index < 48; index++)
                    {
                        dispatcher.Post(() =>
                        {
                            Interlocked.Increment(ref callbacks);
                            if (logger.IsEnabled(LogLevel.Debug))
                            {
                                logger.Log(LogLevel.Debug, new EventId(9877),
                                    new Dictionary<string, object?>(StringComparer.Ordinal)
                                    {
                                        ["{OriginalFormat}"] = "Synthetic independent reveal diagnostic.",
                                    }, null, static (_, _) => "unused");
                            }
                        });
                    }
                    Dispatcher.UIThread.Post(() => heartbeat.SetResult(Stopwatch.GetElapsedTime(started)));
                    await checkpoint.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
                    var latency = await heartbeat.Task.WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
                    latency.Should().BeLessThan(TimeSpan.FromSeconds(1));
                    output.WriteLine("Batch {0}: 48 callbacks / 96 records; heartbeat {1} ms; SQLite still blocked.",
                        batch, latency.TotalMilliseconds);
                    for (var record = 0; record < 96; record++) { await copy.NextDeliveryAsync(); }
                }
                Volatile.Read(ref callbacks).Should().Be(576);
            }
            finally
            {
                drainStarted = Stopwatch.GetTimestamp();
                checkpoint.Release.Set();
                end.TrySetResult();
                await ui;
            }
            await provider.DisposeAsync();
            var drain = Stopwatch.GetElapsedTime(drainStarted);
            checkpoint.Attempts.Should().Be(batched ? 33 : 1025,
                "only batching changes the checked transaction count, not record admission or integrity checks");
            using var lease = sink.AcquireReadLease(TestContext.Current.CancellationToken);
            using var connection = sink.OpenReadOnly(TestContext.Current.CancellationToken);
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT evidence_id FROM activity_spans
                UNION ALL SELECT evidence_id FROM application_log_events WHERE event_id=9877;
                """;
            var projected = new List<Guid>();
            using (var rows = command.ExecuteReader())
            {
                while (rows.Read()) { projected.Add(Guid.Parse(rows.GetString(0))); }
            }
            fileLogger.Dispose();
            var copied = new List<Guid>();
            var gaps = new List<EvidenceGap>();
            foreach (var file in Directory.GetFiles(Path.Combine(paths.LocalRoot, DailyLogFilePolicy.DirectoryName)))
            {
                foreach (var line in File.ReadLines(file))
                {
                    using var json = JsonDocument.Parse(line);
                    var properties = json.RootElement.GetProperty("Properties");
                    if (properties.TryGetProperty("EvidenceEnvelope", out var diagnostic))
                    {
                        var row = WindowsSqliteEvidenceSink.DecodeDiagnostic(diagnostic.GetString()!);
                        row.EventId.Should().Be(9877);
                        row.Host.Should().NotBeNull();
                        row.Trace.Should().NotBeNull();
                        copied.Add(row.EvidenceId.Value);
                    }
                    if (properties.TryGetProperty("ActivityEvidence", out var activity))
                    {
                        using var payload = JsonDocument.Parse(activity.GetString()!);
                        copied.Add(payload.RootElement.GetProperty("EvidenceId").GetProperty("Value").GetGuid());
                    }
                    if (properties.TryGetProperty("EvidenceGap", out var gap))
                    {
                        gaps.Add(JsonSerializer.Deserialize<EvidenceGap>(gap.GetString()!)!);
                    }
                }
            }
            copied.Should().HaveCount(1152).And.OnlyHaveUniqueItems();
            projected.Should().HaveCount(1025).And.OnlyHaveUniqueItems();
            gaps.Should().HaveCount(127).And.OnlyContain(gap => gap.Sink == sink.Name
                && gap.Reason == EvidenceGapReason.ProjectionBackpressure);
            gaps.Select(gap => gap.EvidenceId!.Value.Value).Should().BeEquivalentTo(copied.Except(projected));
            output.WriteLine("Batched: {0}; scratch bytes: {1}; required file records: {2}; SQLite projections: {3}; explicit gaps: {4}; checked transactions: {5}; drain: {6} ms.",
                batched, bytes, copied.Count, projected.Count, gaps.Count, checkpoint.Attempts, drain.TotalMilliseconds);
        }
        finally { Serilog.Debugging.SelfLog.Disable(); }
    }

    [Theory]
    [InlineData(128)]
    [InlineData(384)]
    public async Task Ordinary_diagnostics_leave_dispatcher_responsive_under_one_second_while_large_store_commit_is_blocked(int count)
    {
        using var paths = new OwnedStorageFixture();
        using var checkpoint = new SlowCommit();
        var sink = new WindowsSqliteEvidenceSink(paths, null, null, checkpoint);
        sink.Initialize();
        var bytes = SeedLargeHistory(sink, paths);
        var health = new FileEvidenceHealth();
        using var fileLogger = Kora.DailyFileLoggerFactory.Create(paths, health);
        var file = new Kora.FileEvidenceSink(fileLogger, health);
        var gaps = new Gaps(file);
        try
        {
            using var provider = new EvidenceLoggerProvider([file, sink], gaps, bufferDiagnostics: true);
            var logger = provider.CreateLogger("diagnostic-reveal-fixture");
            var heartbeat = new TaskCompletionSource<TimeSpan>(TaskCreationOptions.RunContinuationsAsynchronously);
            var end = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            checkpoint.Armed = true;
            var started = Stopwatch.GetTimestamp();
            var ui = HeadlessSession.RunAsync(async () =>
            {
                var dispatcher = new AvaloniaUiDispatcher();
                for (var index = 0; index < count; index++)
                {
                    dispatcher.Post(() =>
                    {
                        if (logger.IsEnabled(LogLevel.Debug))
                        {
                            logger.Log(LogLevel.Debug, new EventId(9876),
                                new Dictionary<string, object?>(StringComparer.Ordinal)
                                {
                                    ["{OriginalFormat}"] = "Synthetic reveal diagnostic.",
                                }, null, static (_, _) => "unused");
                        }
                    });
                }
                Dispatcher.UIThread.Post(() => heartbeat.SetResult(Stopwatch.GetElapsedTime(started)));
                await end.Task;
            });
            try
            {
                await checkpoint.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
                var elapsed = await heartbeat.Task.WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
                elapsed.Should().BeLessThan(TimeSpan.FromSeconds(1));
                output.WriteLine("Scratch bytes: {0}; {1} UI diagnostics completed in {2} ms with SQLite blocked.",
                    bytes, count, elapsed.TotalMilliseconds);
            }
            finally
            {
                checkpoint.Release.Set();
                end.TrySetResult();
                await ui;
            }
            provider.Dispose();
            using var lease = sink.AcquireReadLease(TestContext.Current.CancellationToken);
            using var connection = sink.OpenReadOnly(TestContext.Current.CancellationToken);
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT evidence_id FROM application_log_events WHERE event_id=9876;";
            var projected = new List<Guid>();
            using (var rows = command.ExecuteReader())
            {
                while (rows.Read()) { projected.Add(Guid.Parse(rows.GetString(0))); }
            }
            fileLogger.Dispose();
            var copied = new List<Guid>();
            foreach (var log in Directory.GetFiles(Path.Combine(paths.LocalRoot, DailyLogFilePolicy.DirectoryName)))
            {
                foreach (var line in File.ReadLines(log))
                {
                    using var json = JsonDocument.Parse(line);
                    if (!json.RootElement.GetProperty("Properties").TryGetProperty("EvidenceEnvelope", out var envelope)) { continue; }
                    var diagnostic = WindowsSqliteEvidenceSink.DecodeDiagnostic(envelope.GetString()!);
                    if (diagnostic.EventId == 9876) { copied.Add(diagnostic.EvidenceId.Value); }
                }
            }
            copied.Should().HaveCount(count).And.OnlyHaveUniqueItems();
            projected.Should().BeEquivalentTo(copied);
            gaps.Items.Should().BeEmpty();
        }
        finally { Serilog.Debugging.SelfLog.Disable(); }
    }

    [Theory]
    [InlineData(false, 128)]
    [InlineData(true, 128)]
    [InlineData(true, 384)]
    public async Task Actual_dispatcher_delivers_every_admitted_span_after_slow_storage_is_released(
        bool buffered, int count)
    {
        using var paths = new OwnedStorageFixture();
        using var checkpoint = new SlowCommit();
        var sink = new WindowsSqliteEvidenceSink(paths, null, null, checkpoint);
        sink.Initialize();
        var bytes = SeedLargeHistory(sink, paths);
        output.WriteLine("Scratch evidence bytes: {0}; diagnostic rows: 2048; buffered: {1}; callbacks: {2}.", bytes, buffered, count);
        checkpoint.Armed = true;
        var health = new FileEvidenceHealth();
        using var fileLogger = Kora.DailyFileLoggerFactory.Create(paths, health);
        var copy = new Kora.FileEvidenceSink(fileLogger, health);
        var gaps = new Gaps(copy);
        try
        {
            using var provider = new EvidenceLoggerProvider([copy, sink], gaps, bufferDiagnostics: buffered);
            var heartbeat = new TaskCompletionSource<TimeSpan>(TaskCreationOptions.RunContinuationsAsynchronously);
            var endSession = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var callbacks = 0;
            var started = Stopwatch.GetTimestamp();
            var uiSession = HeadlessSession.RunAsync(async () =>
            {
                var dispatcher = new AvaloniaUiDispatcher();
                for (var index = 0; index < count; index++)
                {
                    dispatcher.Post(() => Interlocked.Increment(ref callbacks));
                }
                Dispatcher.UIThread.Post(() => heartbeat.SetResult(Stopwatch.GetElapsedTime(started)));
                await endSession.Task;
            });
            try
            {
                await checkpoint.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
                Volatile.Read(ref callbacks).Should().BeGreaterThanOrEqualTo(1);
                if (buffered)
                {
                    var latency = await heartbeat.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
                    output.WriteLine("Heartbeat latency before storage release: {0} ms.", latency.TotalMilliseconds);
                    Volatile.Read(ref callbacks).Should().Be(count);
                }
                else
                {
                    heartbeat.Task.IsCompleted.Should().BeFalse();
                    Volatile.Read(ref callbacks).Should().Be(1);
                    output.WriteLine("Synchronous span persistence blocked the heartbeat until storage release.");
                }
            }
            finally
            {
                checkpoint.Release.Set();
                endSession.TrySetResult();
                await uiSession;
            }
            await heartbeat.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            var drain = Stopwatch.GetTimestamp();
            provider.Dispose();
            output.WriteLine("Final provider drain: {0} ms; SQLite transactions: {1}; gaps: {2}.",
                Stopwatch.GetElapsedTime(drain).TotalMilliseconds, checkpoint.Attempts, gaps.Items.Count);
            gaps.Items.Should().BeEmpty();
            checkpoint.Attempts.Should().Be(count);
            using var lease = sink.AcquireReadLease(TestContext.Current.CancellationToken);
            using var connection = sink.OpenReadOnly(TestContext.Current.CancellationToken);
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT evidence_id FROM activity_spans ORDER BY evidence_id;";
            using var rows = command.ExecuteReader();
            var projected = new List<Guid>();
            while (rows.Read()) { projected.Add(Guid.Parse(rows.GetString(0))); }
            projected.Should().HaveCount(count);
            fileLogger.Dispose();
            var copies = new List<Guid>();
            var pressure = new List<Guid>();
            foreach (var file in Directory.GetFiles(Path.Combine(paths.LocalRoot, DailyLogFilePolicy.DirectoryName)))
            {
                foreach (var line in File.ReadLines(file))
                {
                    using var json = JsonDocument.Parse(line);
                    var values = json.RootElement.GetProperty("Properties");
                    if (values.TryGetProperty("ActivityEvidence", out var activity))
                    {
                        using var payload = JsonDocument.Parse(activity.GetString()!);
                        copies.Add(payload.RootElement.GetProperty("EvidenceId").GetProperty("Value").GetGuid());
                    }
                    if (values.TryGetProperty("EvidenceGap", out var gap))
                    {
                        using var payload = JsonDocument.Parse(gap.GetString()!);
                        pressure.Add(payload.RootElement.GetProperty("EvidenceId").GetProperty("Value").GetGuid());
                    }
                }
            }
            copies.Should().HaveCount(count).And.OnlyHaveUniqueItems();
            pressure.Should().BeEquivalentTo(copies.Except(projected));
        }
        finally { Serilog.Debugging.SelfLog.Disable(); }
    }

    private static long SeedLargeHistory(WindowsSqliteEvidenceSink sink, OwnedStorageFixture paths)
    {
        var seed = WindowsSqliteEvidenceSinkTests.Bootstrap();
        var properties = new Dictionary<string, EvidenceValue>(seed.Properties, StringComparer.Ordinal);
        for (var index = 0; index < 4; index++)
        {
            properties.Add($"field{index}", new(EvidenceValueKind.Text, new string('x', 1024)));
        }
        seed = seed with { Properties = properties };
        sink.WriteDiagnostic(seed);
        WindowsSqliteEvidenceSinkTests.SeedDiagnosticHistory(paths, seed, 2048);
        sink.Initialize();
        var bytes = new FileInfo(WindowsSqliteEvidenceSinkTests.DatabasePath(paths)).Length;
        bytes.Should().BeGreaterThanOrEqualTo(8 * 1024 * 1024);
        return bytes;
    }

    private sealed class SlowCommit : ISqliteTransactionCheckpoint, IDisposable
    {
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal ManualResetEventSlim Release { get; } = new();
        internal bool Armed { get; set; }
        internal int Attempts { get; private set; }
        public void BeforeWrite(SqliteConnection connection, SqliteTransaction transaction) { }
        public void BeforeCommit(SqliteConnection connection, SqliteTransaction transaction)
        {
            if (!Armed) { return; }
            Attempts++;
            Entered.TrySetResult();
            if (!Release.Wait(TimeSpan.FromSeconds(30)))
            {
                throw new IOException("Synthetic SQLite commit was not released.");
            }
        }
        public void Dispose() => Release.Dispose();
    }

    private sealed class Gaps(IEvidenceGapReporter file) : IEvidenceGapReporter
    {
        internal ConcurrentQueue<EvidenceGap> Items { get; } = new();
        public void Report(EvidenceGap gap)
        {
            file.Report(gap);
            Items.Enqueue(gap);
        }

    }

    private sealed class TrackedFile(Kora.FileEvidenceSink file) : IDeferredEvidenceSink, IEvidenceGapReporter, IDisposable
    {
        private readonly SemaphoreSlim delivered = new(0);
        public string Name => file.Name;
        public void WriteDiagnostic(DiagnosticEnvelope envelope) => file.WriteDiagnostic(envelope);
        public void WriteActivity(CompletedActivityEnvelope envelope) => file.WriteActivity(envelope);
        public void WriteAudit(AuditEnvelope envelope) => file.WriteAudit(envelope);
        public void Report(EvidenceGap gap) => file.Report(gap);
        public Action PrepareDiagnostic(DiagnosticEnvelope envelope) => Track(file.PrepareDiagnostic(envelope));
        public Action PrepareActivity(CompletedActivityEnvelope envelope) => Track(file.PrepareActivity(envelope));
        private Action Track(Action write) => () =>
        {
            write();
            delivered.Release();
        };
        public async Task NextDeliveryAsync() =>
            (await delivered.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken)).Should().BeTrue();
        public void Dispose() => delivered.Dispose();
    }

    private sealed class SingleWriteProjection(WindowsSqliteEvidenceSink sink) : IDeferredEvidenceSink
    {
        public string Name => sink.Name;
        public void WriteDiagnostic(DiagnosticEnvelope envelope) => sink.WriteDiagnostic(envelope);
        public void WriteActivity(CompletedActivityEnvelope envelope) => sink.WriteActivity(envelope);
        public void WriteAudit(AuditEnvelope envelope) => sink.WriteAudit(envelope);
        public Action PrepareDiagnostic(DiagnosticEnvelope envelope) => sink.PrepareDiagnostic(envelope);
        public Action PrepareActivity(CompletedActivityEnvelope envelope) => sink.PrepareActivity(envelope);
    }
}
