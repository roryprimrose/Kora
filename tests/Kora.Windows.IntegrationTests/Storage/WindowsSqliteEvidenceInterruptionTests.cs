using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Principal;

using AwesomeAssertions;

using Kora.Core.Auditing;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Windows.IntegrationTests.Audio;
using Kora.Windows.Storage;

using Microsoft.Data.Sqlite;

namespace Kora.Windows.IntegrationTests.Storage;

[Collection(nameof(DurableStorageCompositionTestGroup))]
public sealed class WindowsSqliteEvidenceInterruptionTests
{
    private const string RootVariable = "KORA_EVIDENCE_CHILD_ROOT";
    private const string KindVariable = "KORA_EVIDENCE_CHILD_KIND";
    private const string ModeVariable = "KORA_EVIDENCE_CHILD_MODE";
    private static readonly string[] Tables =
        ["application_log_events", "security_audit_events", "activity_spans", "activity_links"];

    [Theory]
    [InlineData("diagnostic", "uncommitted")]
    [InlineData("diagnostic", "committed")]
    [InlineData("audit", "uncommitted")]
    [InlineData("audit", "committed")]
    [InlineData("activity", "uncommitted")]
    [InlineData("activity", "committed")]
    [InlineData("retention", "uncommitted")]
    [InlineData("retention", "committed")]
    [InlineData("audit", "missing-journal")]
    [InlineData("audit", "permissive-journal")]
    [InlineData("activity", "held-lease")]
    [InlineData("diagnostic", "held-database")]
    public async Task Owned_child_interruption_reopens_exact_evidence_or_refuses_unsafe_recovery(string kind, string mode)
    {
        using var paths = new OwnedStorageFixture();
        using var listener = Listen();
        var sink = new WindowsSqliteEvidenceSink(paths);
        sink.Initialize();
        foreach (var initial in new[] { "diagnostic", "audit", "activity" })
        {
            Write(sink, initial);
        }
        var before = Snapshot(paths);
        var database = DatabasePath(paths);
        var journal = database + "-journal";
        var permissions = new FileInfo(journal).GetAccessControl().GetSecurityDescriptorBinaryForm();
        var marker = Path.Combine(paths.LocalRoot, "evidence-child-ready");
        var start = OwnedStorageChildProcess.CreateStart(typeof(WindowsSqliteEvidenceInterruptionTests),
            nameof(Fixture_owned_child_evidence_only));
        start.Environment[RootVariable] = paths.LocalRoot;
        start.Environment[KindVariable] = kind;
        start.Environment[ModeVariable] = mode;
        using var child = Process.Start(start) ?? throw new InvalidOperationException("The owned evidence child did not start.");
        var output = child.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var error = child.StandardError.ReadToEndAsync(CancellationToken.None);
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(30));
            while (!File.Exists(marker))
            {
                if (child.HasExited)
                {
                    throw new InvalidOperationException($"The evidence child failed before its checkpoint: {await output} {await error}");
                }
                await Task.Delay(TimeSpan.FromMilliseconds(25), deadline.Token);
            }
            if (!string.Equals(mode, "committed", StringComparison.Ordinal))
            {
                OwnedStorageChildProcess.AssertHotJournal(journal);
            }
            child.Kill(entireProcessTree: true);
            await child.WaitForExitAsync(deadline.Token);
            await OwnedStorageChildProcess.WaitForReleasedDatabaseAsync(paths.LocalRoot, database, deadline.Token);
            if (mode is not ("uncommitted" or "committed"))
            {
                AssertRefusedReopen(paths, mode);
                return;
            }
            var reopened = new WindowsSqliteEvidenceSink(paths);
            reopened.Initialize();
            new FileInfo(journal).GetAccessControl().GetSecurityDescriptorBinaryForm().Should().Equal(permissions);
            using var identity = WindowsIdentity.GetCurrent();
            new FileInfo(journal).GetAccessControl().GetOwner(typeof(SecurityIdentifier)).Should().Be(identity.User);
            var after = Snapshot(paths);
            foreach (var table in Tables)
            {
                if (string.Equals(kind, "retention", StringComparison.Ordinal)
                    && string.Equals(mode, "committed", StringComparison.Ordinal)
                    && !string.Equals(table, "security_audit_events", StringComparison.Ordinal))
                {
                    after[table].Should().BeEmpty();
                    continue;
                }
                var added = string.Equals(mode, "committed", StringComparison.Ordinal) ? AddedRows(kind, table) : 0;
                after[table].Should().HaveCount(before[table].Length + added);
                after[table].Should().Contain(before[table], "all previously committed envelopes and links must survive");
            }
            new WindowsSqliteEvidenceSink(paths).Initialize();
            AssertSnapshot(Snapshot(paths), after);
            Write(reopened, "audit");
            using var connection = Open(paths);
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT audit_sequence FROM security_audit_events ORDER BY audit_sequence;";
            using var rows = command.ExecuteReader();
            var sequence = 0L;
            while (rows.Read())
            {
                rows.GetInt64(0).Should().Be(++sequence);
            }
            sequence.Should().Be(2 + (string.Equals(mode, "committed", StringComparison.Ordinal)
                && string.Equals(kind, "audit", StringComparison.Ordinal) ? 1 : 0));
            rows.Dispose();
            command.CommandText = "PRAGMA integrity_check;";
            command.ExecuteScalar().Should().Be("ok");
        }
        finally
        {
            if (!child.HasExited)
            {
                child.Kill(entireProcessTree: true);
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await child.WaitForExitAsync(cleanup.Token);
            }
            await Task.WhenAll(output, error);
        }
    }

    [Theory]
    [InlineData("diagnostic")]
    [InlineData("audit")]
    [InlineData("activity")]
    public void Precommit_failure_rolls_back_the_actual_envelope_sequence_and_all_links(string kind)
    {
        using var paths = new OwnedStorageFixture();
        using var listener = Listen();
        var sink = new WindowsSqliteEvidenceSink(paths);
        Write(sink, "audit");
        var before = Snapshot(paths);
        var checkpoint = new SqliteTransactionCheckpoint
        {
            Write = SqliteTransactionCheckpoint.SpillPages,
            Commit = (connection, _) =>
            {
                SqliteTransactionCheckpoint.FlushPages(connection);
                throw new IOException("Owned fixture precommit failure.");
            },
        };
        var failing = new WindowsSqliteEvidenceSink(paths, null, null, checkpoint);
        var write = () => Write(failing, kind);
        write.Should().Throw<IOException>().WithMessage("Owned fixture precommit failure.");
        new WindowsSqliteEvidenceSink(paths).Initialize();
        AssertSnapshot(Snapshot(paths), before);
        Write(sink, kind);
        var after = Snapshot(paths);
        foreach (var table in Tables)
        {
            after[table].Should().HaveCount(before[table].Length + AddedRows(kind, table));
        }
    }

    [WindowsFact]
    public async Task Fixture_owned_child_evidence_only()
    {
        var root = Environment.GetEnvironmentVariable(RootVariable);
        if (root is null)
        {
            return;
        }
        OwnedStorageChildProcess.RequireOwnedRoot(root);
        var kind = Environment.GetEnvironmentVariable(KindVariable);
        var mode = Environment.GetEnvironmentVariable(ModeVariable);
        if (kind is not ("diagnostic" or "audit" or "activity" or "retention")
            || mode is not ("uncommitted" or "committed" or "missing-journal"
                or "permissive-journal" or "held-lease" or "held-database"))
        {
            throw new InvalidOperationException("The owned evidence checkpoint is invalid.");
        }
        using var listener = Listen();
        var paths = new ExistingPaths(root);
        var checkpoint = string.Equals(mode, "committed", StringComparison.Ordinal) ? null : new SqliteTransactionCheckpoint
        {
            Write = SqliteTransactionCheckpoint.SpillPages,
            Commit = (connection, _) =>
            {
                SqliteTransactionCheckpoint.FlushPages(connection);
                OwnedStorageChildProcess.SignalAndBlock(root, "evidence-child-ready");
            },
        };
        var sink = new WindowsSqliteEvidenceSink(paths, null, null, checkpoint);
        Write(sink, kind);
        await File.WriteAllTextAsync(Path.Combine(root, "evidence-child-ready"), "owned committed evidence checkpoint",
            TestContext.Current.CancellationToken);
        await Task.Delay(Timeout.InfiniteTimeSpan, TestContext.Current.CancellationToken);
    }

    private static void AssertRefusedReopen(IApplicationDataPaths paths, string mode)
    {
        var database = DatabasePath(paths);
        var journal = database + "-journal";
        if (string.Equals(mode, "missing-journal", StringComparison.Ordinal))
        {
            File.Delete(journal);
        }
        else if (string.Equals(mode, "permissive-journal", StringComparison.Ordinal))
        {
            var info = new FileInfo(journal);
            var acl = info.GetAccessControl();
            acl.AddAccessRule(new(new SecurityIdentifier(WellKnownSidType.WorldSid, null),
                FileSystemRights.Read, AccessControlType.Allow));
            info.SetAccessControl(acl);
        }
        var databaseBytes = File.ReadAllBytes(database);
        var journalBytes = File.Exists(journal) ? File.ReadAllBytes(journal) : null;
        var journalAcl = new FileInfo(journal).Exists
            ? new FileInfo(journal).GetAccessControl().GetSecurityDescriptorBinaryForm() : null;
        using (var held = mode switch
        {
            "held-lease" => new FileStream(Path.Combine(Path.GetDirectoryName(database)!, "operation.lock"),
                FileMode.Open, FileAccess.ReadWrite, FileShare.None),
            "held-database" => new FileStream(database, FileMode.Open, FileAccess.ReadWrite, FileShare.None),
            _ => null,
        })
        {
            var initialize = () => new WindowsSqliteEvidenceSink(paths).Initialize();
            switch (mode)
            {
                case "missing-journal":
                case "held-database":
                    initialize.Should().Throw<InvalidDataException>();
                    break;
                case "permissive-journal":
                    initialize.Should().Throw<UnauthorizedAccessException>();
                    break;
                case "held-lease":
                    initialize.Should().Throw<IOException>();
                    break;
            }
        }
        File.ReadAllBytes(database).Should().Equal(databaseBytes);
        if (journalBytes is null)
        {
            File.Exists(journal).Should().BeFalse();
        }
        else
        {
            File.ReadAllBytes(journal).Should().Equal(journalBytes);
            new FileInfo(journal).GetAccessControl().GetSecurityDescriptorBinaryForm().Should().Equal(journalAcl!);
        }
    }

    private static void Write(WindowsSqliteEvidenceSink sink, string kind)
    {
        var request = HostRequest.Create(RequestOrigin.HostSystem);
        var audit = new SecurityAuditEvent(request.RequestId.Value, SecurityAuditCategory.ApplicationExecution,
            "fixture.storage", SecurityAuditOutcome.Unknown, SecurityAuditInitiator.System, "application.current");
        using var host = HostActivity.BeginAudit(request, audit);
        var diagnostic = new DiagnosticEnvelope(1, new(Guid.NewGuid()), DateTimeOffset.UtcNow, 1,
            "SyntheticStorage", "Information", "Kora.Tests", new string('x', 4000),
            new Dictionary<string, EvidenceValue>(StringComparer.Ordinal)
            {
                ["Padding"] = new(EvidenceValueKind.Text, new string('y', 1000)),
            }, [], TraceSnapshot.Capture(host.Activity), request, null, host.CorrelationId, host.ApprovalId);
        switch (kind)
        {
            case "diagnostic":
                sink.WriteDiagnostic(diagnostic);
                break;
            case "audit":
                sink.WriteAudit(new(diagnostic, audit));
                break;
            case "activity":
                var now = DateTimeOffset.UtcNow;
                var links = Enumerable.Range(0, 32).Select(_ =>
                    new ActivityLinkEnvelope(ActivityTraceId.CreateRandom().ToHexString(),
                        ActivitySpanId.CreateRandom().ToHexString())).ToArray();
                sink.WriteActivity(new(new(Guid.NewGuid()), TraceSnapshot.Capture(host.Activity)!, request,
                    now.AddSeconds(-1), now, HostOperationOutcome.Completed, links, host.CorrelationId, host.ApprovalId));
                break;
            case "retention":
                sink.PruneOrdinaryDiagnostics(DateTimeOffset.UtcNow.AddDays(31), CancellationToken.None);
                break;
            default:
                throw new InvalidOperationException("An unknown fixture evidence kind was requested.");
        }
    }

    private static int AddedRows(string kind, string table) => (kind, table) switch
    {
        ("diagnostic", "application_log_events") or ("audit", "security_audit_events")
            or ("activity", "activity_spans") => 1,
        ("activity", "activity_links") => 32,
        _ => 0,
    };

    private static Dictionary<string, string[]> Snapshot(IApplicationDataPaths paths)
    {
        using var connection = Open(paths);
        var snapshot = new Dictionary<string, string[]>(StringComparer.Ordinal);
        using var command = connection.CreateCommand();
        foreach (var table in Tables)
        {
            command.CommandText = $"SELECT envelope FROM {table} ORDER BY envelope;";
            using var rows = command.ExecuteReader();
            var envelopes = new List<string>();
            while (rows.Read())
            {
                envelopes.Add(rows.GetString(0));
            }
            snapshot.Add(table, envelopes.ToArray());
        }
        return snapshot;
    }

    private static void AssertSnapshot(Dictionary<string, string[]> actual, Dictionary<string, string[]> expected)
    {
        foreach (var table in Tables)
        {
            actual[table].Should().Equal(expected[table]);
        }
    }

    private static string DatabasePath(IApplicationDataPaths paths) =>
        Path.Combine(paths.LocalRoot, WindowsSqliteEvidenceSink.PartitionName, "evidence.db");

    private static SqliteConnection Open(IApplicationDataPaths paths)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath(paths), Mode = SqliteOpenMode.ReadWrite, Pooling = false,
        }.ToString());
        connection.Open();
        using var settings = connection.CreateCommand();
        settings.CommandText = "PRAGMA locking_mode=EXCLUSIVE; PRAGMA journal_mode=PERSIST; PRAGMA synchronous=FULL;";
        settings.ExecuteNonQuery();
        return connection;
    }

    private static ActivityListener Listen()
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }

    private sealed class ExistingPaths(string root) : IApplicationDataPaths
    {
        public string LocalRoot { get; } = root;
        public string RoamingRoot => Path.Combine(LocalRoot, "UnusedRoaming");
    }
}
