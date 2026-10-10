using System.Text.Json;
using System.Reflection;

using AwesomeAssertions;

using Kora.Application.Auditing;
using Kora.Application.Configuration;
using Kora.Application.Diagnostics;
using Kora.Application.Hosting;
using Kora.Application.Skills;
using Kora.Core.Auditing;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Skills;
using Kora.Windows.Storage;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests.Storage;

// Real sinks subscribe process-wide; the existing composition group excludes foreign test callbacks at teardown.
[Collection(nameof(DurableStorageCompositionTestGroup))]
public sealed class WindowsSharedSkillWithdrawalTests
{
    [Fact]
    public void Real_global_evidence_fixture_uses_existing_exclusive_composition_collection()
    {
        typeof(WindowsSharedSkillWithdrawalTests).GetCustomAttribute<CollectionAttribute>()!.Name
            .Should().Be(nameof(DurableStorageCompositionTestGroup));
        typeof(DurableStorageCompositionTestGroup).GetCustomAttribute<CollectionDefinitionAttribute>()!
            .DisableParallelization.Should().BeTrue();
    }

    [Fact]
    public async Task Foreign_global_activity_can_hold_private_database_after_listener_disposal_until_callback_finishes()
    {
        using var paths = new OwnedStorageFixture();
        using var checkpoint = new PausedCommit(TestContext.Current.CancellationToken);
        var sink = new WindowsSqliteEvidenceSink(paths, null, null, checkpoint);
        sink.Initialize();
        using var provider = new EvidenceLoggerProvider([sink], new Gaps());
        var foreignRequest = HostRequest.Create(RequestOrigin.LocalUi);
        var foreign = Task.Run(() =>
        {
            using var activity = HostActivity.BeginRoot(foreignRequest, HostActivityLayer.Application, HostOperation.Request);
            activity.Complete(HostOperationOutcome.Completed);
        }, TestContext.Current.CancellationToken);
        try
        {
            (await Task.WhenAny(checkpoint.Entered.Task, foreign)).Should().BeSameAs(checkpoint.Entered.Task);
            checkpoint.Request.Should().BeSameAs(foreignRequest);
            provider.Dispose();
            foreign.IsCompleted.Should().BeFalse();
            // Removing a process-global listener does not join an already executing foreign callback.
            var remove = () => File.Delete(Path.Combine(paths.LocalRoot, WindowsSqliteEvidenceSink.PartitionName, "evidence.db"));
            remove.Should().Throw<IOException>();
        }
        finally { checkpoint.Release(); await foreign; }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Actual_atomic_preferences_native_lease_and_typed_audit_withdraw_only_known_consent(
        bool missingDirectory, bool failedTerminalAudit)
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.Tasks.InitializeAsync(fixture.Token);
        await fixture.Store.InitializeAsync(fixture.Token);
        var shared = Path.Combine(fixture.Paths.LocalRoot, "fixture-shared", "skills");
        Directory.CreateDirectory(shared);
        var file = Path.Combine(shared, "SKILL.md");
        const string privateText = "Private fixture skill text; no execution authority.";
        File.WriteAllText(file, privateText);
        var source = new SharedSkillSource(Guid.NewGuid(), "fixture-shared\\skills", new string('a', 48));
        var other = new SharedSkillSource(Guid.NewGuid(), "fixture-other\\skills", new string('b', 48));
        var preferences = new LocalSharedSkillPreferences(fixture.Paths);
        preferences.Save([source, other]);
        if (missingDirectory) { Directory.Delete(shared, recursive: true); }
        var sink = new WindowsSqliteEvidenceSink(fixture.Paths);
        sink.Initialize();
        var gaps = new Gaps();
        using var provider = new EvidenceLoggerProvider([sink], gaps);
        using var logs = LoggerFactory.Create(builder => builder.AddProvider(provider));
        var audit = new TerminalAudit(new LoggerSecurityAuditLog(logs.CreateLogger<LoggerSecurityAuditLog>()))
        { FailSucceeded = failedTerminalAudit };
        await using var admission = new SharedSkillAdmission(fixture.Store, fixture.Store, new HostTaskCoordinator(fixture.Tasks));
        var service = new SharedSkillDiscoveryService(preferences, new NeverRead(), admission, audit,
            NullLogger<SharedSkillDiscoveryService>.Instance);
        var confirmed = await service.LoadSourcesAsync(static () => true, fixture.Token);
        var withdrawal = () => service.UnregisterAsync(source, confirmed, static () => true, fixture.Token);
        if (failedTerminalAudit)
        {
            await withdrawal.Should().ThrowAsync<IOException>();
            var read = () => service.DiscoverAsync(other, static () => true, fixture.Token);
            await read.Should().ThrowAsync<InvalidOperationException>();
        }
        else { (await withdrawal()).Should().Equal(other); }
        new LocalSharedSkillPreferences(fixture.Paths).Load().Should().Equal(other);
        var preferenceDirectory = Path.Combine(fixture.Paths.LocalRoot, "Preferences");
        Directory.GetFiles(preferenceDirectory, "*.tmp").Should().BeEmpty();
        File.ReadAllText(Path.Combine(preferenceDirectory, "shared-skill-sources.json")).Should().NotContain(privateText);
        if (!missingDirectory) { File.ReadAllText(file).Should().Be(privateText); }
        else { Directory.Exists(shared).Should().BeFalse(); }
        fixture.Count("host_questions").Should().Be(0);
        fixture.Count("scoped_grants").Should().Be(0);
        gaps.Values.Should().BeEmpty();
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(fixture.Paths.LocalRoot, WindowsSqliteEvidenceSink.PartitionName, "evidence.db"),
            Mode = SqliteOpenMode.ReadOnly, Pooling = false,
        }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT envelope FROM security_audit_events ORDER BY audit_sequence;";
        using var rows = command.ExecuteReader();
        var events = new List<AuditEnvelope>();
        while (rows.Read()) { events.Add(JsonSerializer.Deserialize<AuditEnvelope>(rows.GetString(0))!); }
        events.Select(item => item.Audit.Outcome).Should().Equal(SecurityAuditOutcome.Requested,
            failedTerminalAudit ? SecurityAuditOutcome.Failed : SecurityAuditOutcome.Succeeded);
        events.Should().OnlyContain(item => item.Audit.ActionId == "skills.source.unregister"
            && item.Audit.TargetId == $"shared-source.{source.Id:N}"
            && item.Diagnostic.Host!.Origin == Kora.Core.Hosting.RequestOrigin.LocalUi
            && item.Diagnostic.Host.RequestId.Value == item.Audit.CorrelationId
            && item.Diagnostic.Trace != null);
        events[0].Diagnostic.Host.Should().Be(events[1].Diagnostic.Host);
        events[0].Diagnostic.Trace!.TraceId.Should().Be(events[1].Diagnostic.Trace!.TraceId);
        JsonSerializer.Serialize(events).Should().NotContain(privateText).And.NotContain(source.ProfileRelativeRoot);
        (await service.LoadSourcesAsync(static () => true, fixture.Token)).Should().Equal(other);
    }

    private sealed class NeverRead : ISharedSkillSourceReader
    {
        public ValueTask<SharedSkillSource> SelectAsync(string selectedRoot, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Withdrawal must not select or repair a directory.");
        public ValueTask<SharedSkillCatalogue> DiscoverAsync(SharedSkillSource source, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Withdrawal must not read shared content.");
    }

    private sealed class TerminalAudit(ISecurityAuditLog inner) : ISecurityAuditLog
    {
        internal bool FailSucceeded { get; init; }
        public void Write(SecurityAuditEvent auditEvent)
        {
            if (FailSucceeded && auditEvent.Outcome == SecurityAuditOutcome.Succeeded)
            { throw new IOException("Fixture terminal audit unavailable after durable preference publication."); }
            inner.Write(auditEvent);
        }
    }

    private sealed class Gaps : IEvidenceGapReporter
    {
        internal List<EvidenceGap> Values { get; } = [];
        public void Report(EvidenceGap gap) => Values.Add(gap);
    }

    private sealed class PausedCommit(CancellationToken token) : ISqliteTransactionCheckpoint, IDisposable
    {
        private readonly ManualResetEventSlim released = new();
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal HostRequest? Request { get; private set; }
        public void BeforeWrite(SqliteConnection connection, SqliteTransaction transaction) { }
        public void BeforeCommit(SqliteConnection connection, SqliteTransaction transaction)
        {
            Request = HostActivity.RequireCurrent().Request;
            Entered.SetResult();
            released.Wait(token);
        }
        internal void Release() => released.Set();
        public void Dispose() => released.Dispose();
    }
}
