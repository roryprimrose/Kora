using System.Diagnostics;
using System.Text.Json;

using AwesomeAssertions;

using Kora.Application.Auditing;
using Kora.Application.Diagnostics;
using Kora.Core.Auditing;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Windows.IntegrationTests.Audio;
using Kora.Windows.Storage;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Kora.Windows.IntegrationTests.Storage;

[Collection(nameof(ProcessGlobalEvidenceTestGroup))]
public sealed class DailyFileLoggerFactoryTests
{
    [Fact]
    public void Production_calendar_configuration_explicitly_disables_size_cutoff_without_new_roll_names()
    {
        var root = SourceRoot();
        File.ReadAllText(Path.Combine(root, "src", "Kora", "Program.cs"))
            .Should().Contain("DailyFileLoggerFactory.Create(paths, fileHealth)")
            .And.Contain("services.AddSingleton<ILoggerProvider>(provider => new EvidenceLoggerProvider(")
            .And.Contain("provider.GetRequiredService<WindowsSegmentedEvidenceSink>()")
            .And.Contain("requiredOrdinaryCopy: fileSink");
        // The 1 GiB boundary is checked at the production configuration seam, not with a huge file.
        File.ReadAllText(Path.Combine(root, "src", "Kora", "DailyFileLoggerFactory.cs"))
            .Should().Contain("fileSizeLimitBytes: null")
            .And.Contain("rollingInterval: RollingInterval.Day")
            .And.Contain("retainedFileCountLimit: 30")
            .And.Contain("retainedFileTimeLimit: TimeSpan.FromDays(30)")
            .And.Contain("rollOnFileSizeLimit: false")
            .And.Contain("shared: false")
            .And.Contain("DailyLogFilePolicy.RollingName");
    }

    [Fact]
    public void Production_logging_is_activated_before_the_startup_root_is_created()
    {
        var source = File.ReadAllText(Path.Combine(SourceRoot(), "src", "Kora", "Program.cs"));
        var startup = source.IndexOf("using (var startup = HostActivity.BeginRoot(", StringComparison.Ordinal);
        startup.Should().BeGreaterThan(0);
        source.IndexOf("provider = services.BuildServiceProvider();", StringComparison.Ordinal)
            .Should().BeGreaterThan(0).And.BeLessThan(startup);
        source.IndexOf("var startupLogger = provider.GetRequiredService<ILoggerFactory>()", StringComparison.Ordinal)
            .Should().BeGreaterThan(0).And.BeLessThan(startup,
                "DI must install the evidence listener before ActivitySource attempts the startup root");
    }

    private static string SourceRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, ".github", "copilot-instructions.md")))
        {
            directory = directory.Parent;
        }
        return directory?.FullName ?? throw new InvalidOperationException("Source root unavailable.");
    }

    [WindowsFact]
    public void Production_file_sink_round_trips_every_typed_pair_across_reopening_and_sticky_failure_stops_admission()
    {
        using var paths = new OwnedStorageFixture();
        using var listener = Listen();
        var expected = new List<AuditEnvelope>();
        try
        {
            for (var batch = 0; batch < 2; batch++)
            {
                var health = new FileEvidenceHealth();
                using var logger = Kora.DailyFileLoggerFactory.Create(paths, health);
                var sink = new Kora.FileEvidenceSink(logger, health);
                for (var index = 0; index < 33; index++)
                {
                    var audit = Audit();
                    using var host = HostActivity.BeginAudit(HostRequest.Create(RequestOrigin.LocalUi), audit);
                    foreach (var outcome in new[] { SecurityAuditOutcome.Requested, SecurityAuditOutcome.Succeeded })
                    {
                        var envelope = new AuditEnvelope(Diagnostic(host), audit.WithOutcome(outcome));
                        sink.WriteAudit(envelope);
                        expected.Add(envelope);
                    }
                }
                var before = ReadCopies(paths);
                before.Should().HaveCount(expected.Count);
                health.Fail();
                var auditWrite = () => sink.WriteAudit(expected[^1]);
                auditWrite.Should().Throw<IOException>();
                var report = () => sink.Report(new(DateTimeOffset.UtcNow, sink.Name, EvidenceGapReason.SinkFailure, null, null));
                report.Should().Throw<IOException>();
                ReadCopies(paths).Should().BeEquivalentTo(before, options => options.WithStrictOrdering());
            }
            var copies = ReadCopies(paths);
            copies.Should().HaveCount(132);
            copies.Select(copy => copy.Diagnostic.EvidenceId).Should().OnlyHaveUniqueItems()
                .And.Equal(expected.Select(copy => copy.Diagnostic.EvidenceId));
            copies.Should().BeEquivalentTo(expected, options => options.WithStrictOrdering());
            Directory.GetFiles(Path.Combine(paths.LocalRoot, DailyLogFilePolicy.DirectoryName)).Should().ContainSingle();
        }
        finally { Serilog.Debugging.SelfLog.Disable(); }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Required_terminal_failure_attempts_independent_real_sink_reports_gap_and_never_returns_success(bool fileFails)
    {
        using var paths = new OwnedStorageFixture();
        var health = new FileEvidenceHealth();
        var checkpoint = new FailedCommit();
        var database = new WindowsSqliteEvidenceSink(paths, null, null, checkpoint);
        var gaps = new Gaps();
        try
        {
            using var fileLogger = Kora.DailyFileLoggerFactory.Create(paths, health);
            var file = new Kora.FileEvidenceSink(fileLogger, health);
            using var provider = new EvidenceLoggerProvider([file, database], gaps);
            using var factory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Debug).AddProvider(provider));
            var bridge = new LoggerSecurityAuditLog(factory.CreateLogger<LoggerSecurityAuditLog>());
            var audit = Audit();
            using var host = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi),
                HostActivityLayer.Application, HostOperation.Request);
            bridge.Write(audit);
            if (fileFails) { health.Fail(); }
            else { checkpoint.Fail = true; }

            var terminal = () => bridge.Write(audit.WithOutcome(SecurityAuditOutcome.Succeeded));

            try { terminal.Should().Throw<IOException>(); }
            finally { provider.Dispose(); }
            gaps.Items.Should().NotBeEmpty().And.OnlyContain(gap => gap.Sink == (fileFails ? file.Name : database.Name)
                && gap.Reason == EvidenceGapReason.SinkFailure);
            var copies = ReadCopies(paths);
            copies.Should().HaveCount(fileFails ? 1 : 2);
            copies[0].Audit.Should().BeEquivalentTo(audit);
            using var lease = database.AcquireReadLease(TestContext.Current.CancellationToken);
            using var connection = database.OpenReadOnly(TestContext.Current.CancellationToken);
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT envelope FROM security_audit_events ORDER BY audit_sequence;";
            using var rows = command.ExecuteReader();
            var stored = new List<AuditEnvelope>();
            while (rows.Read()) { stored.Add(WindowsSqliteEvidenceSink.DecodeAudit(rows.GetString(0))); }
            stored.Should().HaveCount(fileFails ? 2 : 1);
            stored[0].Audit.Should().BeEquivalentTo(audit);
            if (fileFails)
            {
                stored[^1].Audit.Should().BeEquivalentTo(audit.WithOutcome(SecurityAuditOutcome.Succeeded));
            }
            else
            {
                copies[^1].Audit.Should().BeEquivalentTo(audit.WithOutcome(SecurityAuditOutcome.Succeeded));
            }
        }
        finally { Serilog.Debugging.SelfLog.Disable(); }
    }

    private static List<AuditEnvelope> ReadCopies(OwnedStorageFixture paths)
    {
        var result = new List<AuditEnvelope>();
        foreach (var path in Directory.GetFiles(Path.Combine(paths.LocalRoot, DailyLogFilePolicy.DirectoryName)))
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            while (reader.ReadLine() is { } line)
            {
                using var json = JsonDocument.Parse(line);
                var properties = json.RootElement.GetProperty("Properties");
                if (!properties.TryGetProperty("TypedAuditEnvelope", out var audit)) { continue; }
                properties.GetProperty("EvidenceAuthority").GetString().Should().Be("TypedAuditCopy");
                var diagnostic = WindowsSqliteEvidenceSink.DecodeDiagnostic(properties.GetProperty("EvidenceEnvelope").GetString()!);
                var typed = JsonSerializer.Deserialize<SecurityAuditEvent>(audit.GetString()!)!;
                properties.GetProperty("EvidenceId").GetGuid().Should().Be(diagnostic.EvidenceId.Value);
                result.Add(new(diagnostic, typed));
            }
        }
        return result;
    }

    private static SecurityAuditEvent Audit() => new(Guid.NewGuid(), SecurityAuditCategory.ApplicationExecution,
        "fixture.audit", SecurityAuditOutcome.Requested, SecurityAuditInitiator.LocalUser, "application.current", Guid.NewGuid());

    private static DiagnosticEnvelope Diagnostic(HostActivity host) => new(1, new(Guid.NewGuid()), DateTimeOffset.UtcNow,
        1, "AuditFixture", "Information", "Kora.Tests", "Content-free audit fixture.",
        new Dictionary<string, EvidenceValue>(StringComparer.Ordinal), [], TraceSnapshot.Capture(host.Activity),
        host.Request, null, host.CorrelationId, host.ApprovalId);

    private static ActivityListener Listen()
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }

    private sealed class Gaps : IEvidenceGapReporter
    {
        internal List<EvidenceGap> Items { get; } = [];
        public void Report(EvidenceGap gap) => Items.Add(gap);
    }

    private sealed class FailedCommit : ISqliteTransactionCheckpoint
    {
        internal bool Fail { get; set; }
        public void BeforeWrite(SqliteConnection connection, SqliteTransaction transaction) { }
        public void BeforeCommit(SqliteConnection connection, SqliteTransaction transaction)
        {
            if (Fail) { throw new IOException("Synthetic required audit commit failure."); }
        }
    }
}
