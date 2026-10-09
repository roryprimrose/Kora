using System.Security.Cryptography;
using System.Security.AccessControl;

using AwesomeAssertions;

using Kora.Application.Diagnostics;
using Kora.Core.Diagnostics;
using Kora.Core.Auditing;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Core.Storage;
using Kora.Windows.IntegrationTests.Audio;
using Kora.Windows.Storage;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests.Storage;

[Collection(nameof(DurableStorageCompositionTestGroup))]
public sealed class WindowsCommittedAuthorityAuditTests
{
    [WindowsFact]
    public async Task NativeAdvancedFiltersReadExactCommittedMetadataWithoutPromotingDiagnostics()
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        await fixture.GrantAsync("once");
        var original = fixture.Request;
        fixture.Request = InteractionStorageFixture.NewRequest();
        await fixture.AdmitAsync(newSession: true);
        var viewer = new Kora.EvidenceViewModel(Service(fixture), () => true,
            NullLogger<Kora.EvidenceViewModel>.Instance) { Source = EvidenceSource.AuthorityAudit };
        var bytes = SHA256.HashData(File.ReadAllBytes(fixture.DatabasePath));
        await viewer.SearchAsync();
        var all = viewer.Records.ToArray();
        all.Should().HaveCountGreaterThan(1);
        var selected = all.First(record => record.Host == original && record.ApprovalId != null);
        var filters = new Action[]
        {
            () => viewer.RequestFilter = original.RequestId.Value.ToString("D"),
            () => viewer.InvocationFilter = original.InvocationId!.Value.Value.ToString("D"),
            () => viewer.ApprovalFilter = selected.ApprovalId!.Value.ToString("D"),
            () => viewer.CorrelationFilter = selected.CorrelationId!.Value.ToString("D"),
            () => viewer.AuditOutcome = selected.Audit!.Outcome,
            () => viewer.FromFilter = selected.CommittedUtc!.Value.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            () => viewer.UntilFilter = selected.CommittedUtc!.Value.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
        };
        foreach (var apply in filters)
        {
            viewer.ClearAdvancedFilters();
            apply();
            await viewer.SearchAsync();
            viewer.Records.Should().Contain(record => record.Reference == selected.Reference)
                .And.OnlyContain(record => record.AuthorityProvenance != null && record.Reference.Source == EvidenceSource.AuthorityAudit);
        }
        viewer.RequestFilter = original.RequestId.Value.ToString("D");
        viewer.InvocationFilter = original.InvocationId!.Value.Value.ToString("D");
        viewer.ApprovalFilter = selected.ApprovalId!.Value.ToString("D");
        viewer.CorrelationFilter = selected.CorrelationId!.Value.ToString("D");
        viewer.AuditOutcome = selected.Audit!.Outcome;
        viewer.FromFilter = selected.CommittedUtc!.Value.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
        viewer.UntilFilter = viewer.FromFilter;
        await viewer.SearchAsync();
        viewer.Records.Should().ContainSingle().Which.Reference.Should().Be(selected.Reference);
        viewer.Status.Should().Contain(EvidencePage.AuthorityDisclosure);
        viewer.ResultText.Should().Contain("AuthorityProvenance");
        viewer.ClearAdvancedFilters();
        viewer.FromFilter = selected.CommittedUtc.Value.AddTicks(1).ToString("O", System.Globalization.CultureInfo.InvariantCulture);
        await viewer.SearchAsync();
        viewer.Records.Should().NotContain(record => record.Reference == selected.Reference);
        viewer.ClearAdvancedFilters();
        viewer.UntilFilter = selected.CommittedUtc.Value.AddTicks(-1).ToString("O", System.Globalization.CultureInfo.InvariantCulture);
        await viewer.SearchAsync();
        viewer.Records.Should().NotContain(record => record.Reference == selected.Reference);
        viewer.ClearAdvancedFilters();
        viewer.AuditOutcome = selected.Audit.Outcome == SecurityAuditOutcome.Succeeded
            ? SecurityAuditOutcome.Denied : SecurityAuditOutcome.Succeeded;
        viewer.CorrelationFilter = selected.CorrelationId.Value.ToString("D");
        await viewer.SearchAsync();
        viewer.Records.Should().BeEmpty();
        SHA256.HashData(File.ReadAllBytes(fixture.DatabasePath)).Should().Equal(bytes);
        viewer.Close();
    }

    [WindowsFact]
    public async Task Consolidation_preserves_commit_bytes_and_frozen_legacy_is_not_an_inspection_source()
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        var initial = await ReadBatchAsync(fixture, null);
        var digests = initial.Candidates.Select(c => c.Record.AuthorityProvenance!.CommitDigest).ToArray();
        fixture.StageLegacy(2);
        fixture.Reopen();
        await fixture.Store.InitializeAsync(fixture.Token);
        var migrated = await ReadBatchAsync(fixture, null);
        migrated.Candidates.Select(c => c.Record.AuthorityProvenance!.CommitDigest).Should().Equal(digests);
        using (var host = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Desktop, HostOperation.Evidence))
        {
            var oldLifetime = () => ((ICommittedAuthorityAuditReader)fixture.Store).ReadAsync(
                new() { Source = EvidenceSource.AuthorityAudit }, new(initial.Snapshot, initial.Candidates[0].Position),
                HostActivity.RequireCurrent().Request, fixture.Time.Now, fixture.Token).AsTask();
            await oldLifetime.Should().ThrowAsync<InvalidDataException>();
        }
        File.Delete(Path.Combine(fixture.Paths.LocalRoot, "HostStorageV1", "host.db"));
        (await ReadBatchAsync(fixture, null)).Candidates.Select(c => c.Record.AuthorityProvenance!.CommitDigest).Should().Equal(digests);
        fixture.Time.Now = fixture.Time.Now.AddDays(91);
        (await ReadBatchAsync(fixture, null)).Candidates.Should().OnlyContain(c => c.Record.Retention == EvidenceSegmentStatus.ExpiredButPresent);
    }

    [WindowsFact]
    public async Task Reader_requires_exact_live_original_local_ui_context_and_unknown_session_refuses()
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        var source = (ICommittedAuthorityAuditReader)fixture.Store;
        var target = new EvidenceQuery { Source = EvidenceSource.AuthorityAudit };
        var noHost = () => source.ReadAsync(target, null, fixture.Request, fixture.Time.Now, fixture.Token).AsTask();
        await noHost.Should().ThrowAsync<InvalidOperationException>();
        using (var host = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Desktop, HostOperation.Evidence))
        {
            var foreign = () => source.ReadAsync(target, null, fixture.Request, fixture.Time.Now, fixture.Token).AsTask();
            await foreign.Should().ThrowAsync<InvalidOperationException>();
            var unknownSession = () => Service(fixture).QueryAsync(target with { SessionId = new(Guid.NewGuid()) }, null, fixture.Token).AsTask();
            await unknownSession.Should().ThrowAsync<InvalidDataException>();
        }
        using (var host = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.ActivatedVoice), HostActivityLayer.Desktop, HostOperation.Evidence))
        {
            var wrongOrigin = () => source.ReadAsync(target, null, host.Request, fixture.Time.Now, fixture.Token).AsTask();
            await wrongOrigin.Should().ThrowAsync<InvalidOperationException>();
        }
    }

    [WindowsFact]
    public async Task Native_current_version_wait_task_cancel_and_audio_admission_are_actual_commits_not_mirrors_or_lookalikes()
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        var question = await WindowsSqliteTaskControlTests.WaitAsync(fixture);
        var observed = (await fixture.Store.ReadTaskAsync(fixture.Request.SessionId, fixture.Request.TaskId, fixture.Token))!;
        var workspace = WindowsSqliteSessionWorkspaceTests.Service(fixture, new());
        await workspace.CancelTaskAsync(WindowsSqliteTaskControlTests.Target(observed), RequestOrigin.LocalUi, () => true, fixture.Token);
        await using var admission = new Kora.Application.Voice.AudioControlAdmission(fixture.Store, fixture.Store, new(fixture.Tasks));
        await admission.RunAsync(RequestOrigin.LocalUi, () => true, static (_, _) => 42, fixture.Token);
        var sink = new WindowsSqliteEvidenceSink(fixture.Paths);
        var diagnostic = WindowsDailyEvidenceReaderTests.Produce(fixture.Request, 1)[0] with
        {
            Properties = new Dictionary<string, EvidenceValue>(StringComparer.Ordinal)
            {
                ["SecurityAudit"] = new(EvidenceValueKind.Boolean, "true"),
            },
            MessageTemplate = "task.cancel.wait",
        };
        using (var producer = HostActivity.BeginRoot(fixture.Request, HostActivityLayer.Application, HostOperation.Request))
        {
            diagnostic = diagnostic with { Trace = TraceSnapshot.Capture(producer.Activity) };
            sink.WriteDiagnostic(diagnostic);
        }
        WindowsDailyEvidenceReaderTests.Write(fixture.Paths, "kora-20261007.log", [diagnostic]);
        var query = new DurableEvidenceQuery(new WindowsEvidenceReader(new(sink), new(fixture.Paths), fixture.Store),
            new Access(), fixture.Time, NullLogger<DurableEvidenceQuery>.Instance);
        using (var host = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Desktop, HostOperation.Evidence))
        {
            var audio = await query.QueryAsync(new()
            {
                Source = EvidenceSource.AuthorityAudit, ActionId = "session.create.audio-control",
            }, null, fixture.Token);
            audio.Records.Should().ContainSingle().Which.AuthorityProvenance.Should().NotBeNull();
        }
        var viewer = new Kora.EvidenceViewModel(query, () => true, NullLogger<Kora.EvidenceViewModel>.Instance)
        {
            Source = EvidenceSource.AuthorityAudit, SafeText = "task.cancel.wait",
        };
        await viewer.SearchAsync();
        var cancelled = viewer.Records.Should().ContainSingle().Subject;
        cancelled.Audit!.ActionId.Should().Be("task.cancel.wait");
        cancelled.AuthorityProvenance!.QuestionId.Should().Be(question.Key.QuestionId);
        cancelled.AuthorityProvenance.Changes.Should().Contain(c => c.Kind == "task" && c.Revision == 2)
            .And.Contain(c => c.Kind == "question" && c.Revision == 2);
        viewer.ResultText.Should().Contain(cancelled.Reference.Citation).And.Contain("AuthorityProvenance");
        viewer.Select(cancelled);
        await viewer.ReadTraceAsync();
        viewer.Records.Should().ContainSingle();
        viewer.Records.Single().Reference.Should().Be(cancelled.Reference);
        viewer.Source = EvidenceSource.All;
        await viewer.SearchAsync();
        viewer.Records.Should().ContainSingle().Which.Reference.Source.Should().Be(EvidenceSource.Log);
        viewer.Source = EvidenceSource.CombinedLog;
        await viewer.SearchAsync();
        viewer.Records.Should().HaveCount(2).And.OnlyContain(r => r.AuthorityProvenance == null && r.Audit == null);
        viewer.Close();
        viewer.Records.Should().BeEmpty();
    }

    [Theory]
    [InlineData("replacement")]
    [InlineData("permissions")]
    [InlineData("uninitialized")]
    public async Task Current_file_identity_initialization_and_private_permissions_fail_closed(string scenario)
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        if (string.Equals(scenario, "replacement", StringComparison.Ordinal))
        {
            var bytes = File.ReadAllBytes(fixture.DatabasePath);
            File.Delete(fixture.DatabasePath);
            var directory = new RestrictedStorageDirectory(fixture.Paths, includeKeys: false, partitionName: HostInteractionSchema.Partition);
            using var file = directory.CreateNewFile(fixture.DatabasePath);
            file.Write(bytes);
        }
        else if (string.Equals(scenario, "permissions", StringComparison.Ordinal))
        {
            var security = new FileInfo(fixture.DatabasePath).GetAccessControl();
            security.SetAccessRuleProtection(isProtected: false, preserveInheritance: true);
            new FileInfo(fixture.DatabasePath).SetAccessControl(security);
        }
        else { fixture.Reopen(); }
        using var host = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Desktop, HostOperation.Evidence);
        var read = () => Service(fixture).QueryAsync(new() { Source = EvidenceSource.AuthorityAudit }, null, fixture.Token).AsTask();
        if (string.Equals(scenario, "permissions", StringComparison.Ordinal))
        {
            await read.Should().ThrowAsync<UnauthorizedAccessException>();
        }
        else { await read.Should().ThrowAsync<InvalidDataException>(); }
    }

    [WindowsFact]
    public async Task Committed_question_answer_and_cancel_are_source_qualified_typed_read_only_receipts()
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        var presented = await fixture.RunAsync(() => fixture.Questions.CreateAsync(fixture.Request,
            new("Version", QuestionKind.SingleChoice, [new("yes", "Yes"), new("no", "No")]),
            fixture.Time.Now.AddMinutes(5), fixture.Token));
        await fixture.RunAsync(() => fixture.Questions.SubmitAsync(presented.Question!.Key,
            new(["yes"]), RequestOrigin.LocalUi, fixture.Token));
        var pending = await fixture.RunAsync(() => fixture.Questions.CreateAsync(fixture.Request,
            new("Other", QuestionKind.Text, [], maximumTextLength: 32), fixture.Time.Now.AddMinutes(5), fixture.Token));
        await fixture.RunAsync(() => fixture.Questions.CancelAsync(pending.Question!.Key, fixture.Token));
        var count = fixture.Count("security_audit_events");
        var bytes = SHA256.HashData(File.ReadAllBytes(fixture.DatabasePath));
        var query = Service(fixture);
        using var host = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi),
            HostActivityLayer.Desktop, HostOperation.Evidence);
        var page = await query.QueryAsync(new() { Source = EvidenceSource.AuthorityAudit }, null, fixture.Token);
        page.Records.Count.Should().Be((int)count);
        page.Records.Select(r => r.AuditSequence).Should().BeInAscendingOrder();
        page.Records.Should().OnlyContain(r => r.Reference.Source == EvidenceSource.AuthorityAudit
            && r.AuthorityProvenance!.SchemaVersion == HostInteractionSchema.Version && r.Trace == null && r.RelatedSegments.Count == 0);
        page.Records.Should().Contain(r => r.AuthorityProvenance!.InteractionOutcome == HostInteractionOutcome.Answered);
        page.Records.Should().Contain(r => r.AuthorityProvenance!.InteractionOutcome == HostInteractionOutcome.Cancelled);
        var answer = page.Records.Single(r => r.AuthorityProvenance!.InteractionOutcome == HostInteractionOutcome.Answered);
        answer.Host.Should().Be(fixture.Request);
        answer.AuthorityProvenance!.QuestionId.Should().Be(presented.Question!.Key.QuestionId);
        answer.AuthorityProvenance.Changes.Should().Contain(c => c.Kind == "question");
        var cited = await query.QueryAsync(new() { Source = EvidenceSource.AuthorityAudit, Record = answer.Reference }, null, fixture.Token);
        DurableEvidenceQuery.Serialize(cited).Length.Should().BeLessThanOrEqualTo(EvidencePage.MaximumBytes);
        cited.Records.Single().Reference.Citation.Should().Be(answer.Reference.Citation);
        cited.Records.Single().AuthorityProvenance.Should().BeEquivalentTo(answer.AuthorityProvenance);
        var filtered = await query.QueryAsync(new()
        {
            Source = EvidenceSource.AuthorityAudit, SessionId = fixture.Request.SessionId,
            TaskId = fixture.Request.TaskId, TraceId = answer.AuthorityProvenance.TraceId,
            SpanId = answer.AuthorityProvenance.SpanId, ActionId = answer.Audit!.ActionId,
        }, null, fixture.Token);
        filtered.Records.Should().ContainSingle();
        SHA256.HashData(File.ReadAllBytes(fixture.DatabasePath)).Should().Equal(bytes);
        fixture.Count("security_audit_events").Should().Be(count);
    }

    [WindowsFact]
    public async Task Sequence_snapshot_pages_exclude_appends_and_reject_foreign_tampered_expired_cursors()
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        for (var index = 0; index < 52; index++)
        {
            await fixture.RunAsync(() => fixture.Questions.CreateAsync(fixture.Request,
                new("Question", QuestionKind.Text, [], maximumTextLength: 32), fixture.Time.Now.AddMinutes(5), fixture.Token));
        }
        var service = Service(fixture);
        var viewer = HostRequest.Create(RequestOrigin.LocalUi);
        var target = new EvidenceQuery { Source = EvidenceSource.AuthorityAudit };
        EvidencePage first;
        using (var host = HostActivity.BeginRoot(viewer, HostActivityLayer.Desktop, HostOperation.Evidence))
        {
            first = await service.QueryAsync(target, null, fixture.Token);
        }
        first.Records.Count.Should().BeInRange(1, EvidencePage.MaximumRecords);
        DurableEvidenceQuery.Serialize(first).Length.Should().BeLessThanOrEqualTo(EvidencePage.MaximumBytes);
        first.Cursor.Should().NotBeNull();
        var count = fixture.Count("security_audit_events");
        await fixture.RunAsync(() => fixture.Questions.CreateAsync(fixture.Request,
            new("Append", QuestionKind.Text, [], maximumTextLength: 32), fixture.Time.Now.AddMinutes(5), fixture.Token));
        using (var host = HostActivity.BeginRoot(viewer, HostActivityLayer.Desktop, HostOperation.Evidence))
        {
            var next = await service.QueryAsync(target, first.Cursor, fixture.Token);
            next.Records.Count.Should().Be((int)count - first.Records.Count);
            next.Cursor.Should().BeNull();
            next.Records.Should().OnlyContain(r => r.AuditSequence <= count);
            first.Records.Select(r => r.Reference).Intersect(next.Records.Select(r => r.Reference)).Should().BeEmpty();
            var tampered = () => service.QueryAsync(target, first.Cursor + "x", fixture.Token).AsTask();
            await tampered.Should().ThrowAsync<InvalidDataException>();
            var changedQuery = () => service.QueryAsync(target with { Text = "Append" }, first.Cursor, fixture.Token).AsTask();
            await changedQuery.Should().ThrowAsync<InvalidDataException>();
            fixture.Time.Now = fixture.Time.Now.AddMinutes(16);
            var expired = () => service.QueryAsync(target, first.Cursor, fixture.Token).AsTask();
            await expired.Should().ThrowAsync<InvalidDataException>();
        }
        using (var host = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Desktop, HostOperation.Evidence))
        {
            var foreign = () => service.QueryAsync(target, first.Cursor, fixture.Token).AsTask();
            await foreign.Should().ThrowAsync<InvalidDataException>();
        }
    }

    [Theory]
    [InlineData("UPDATE security_audit_events SET envelope='{}' WHERE sequence=1;")]
    [InlineData("DELETE FROM security_audit_events WHERE sequence=1;")]
    [InlineData("UPDATE authority_head SET hash=printf('%064d',1);")]
    [InlineData("PRAGMA user_version=2;")]
    public async Task Corrupt_missing_or_legacy_authority_never_returns_synthetic_success(string sql)
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        fixture.Mutate(sql);
        using var host = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Desktop, HostOperation.Evidence);
        var action = () => Service(fixture).QueryAsync(new() { Source = EvidenceSource.AuthorityAudit }, null, fixture.Token).AsTask();
        await action.Should().ThrowAsync<InvalidDataException>();
    }

    [WindowsFact]
    public async Task Concurrent_shared_lease_reads_and_commits_do_not_write_or_deadlock_and_cancel_is_observed()
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        var query = Service(fixture);
        var reads = Enumerable.Range(0, 8).Select(async _ =>
        {
            using var host = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Desktop, HostOperation.Evidence);
            return await query.QueryAsync(new() { Source = EvidenceSource.AuthorityAudit }, null, fixture.Token);
        });
        var write = fixture.RunAsync(() => fixture.Questions.CreateAsync(fixture.Request,
            new("Concurrent", QuestionKind.Text, [], maximumTextLength: 32), fixture.Time.Now.AddMinutes(5), fixture.Token));
        (await Task.WhenAll(reads)).Should().OnlyContain(p => p.Status == EvidencePageStatus.Available);
        await write;
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Desktop, HostOperation.Evidence);
        var action = () => query.QueryAsync(new() { Source = EvidenceSource.AuthorityAudit }, null, cancelled.Token).AsTask();
        await action.Should().ThrowAsync<OperationCanceledException>();
        var valid = await query.QueryAsync(new() { Source = EvidenceSource.AuthorityAudit }, null, fixture.Token);
        valid.Records.Should().NotBeEmpty();
    }

    private static DurableEvidenceQuery Service(InteractionStorageFixture fixture) =>
        new(new Reader(fixture.Store), new Access(), fixture.Time, NullLogger<DurableEvidenceQuery>.Instance);

    private static async Task<EvidenceReadBatch> ReadBatchAsync(InteractionStorageFixture fixture, EvidenceReadCheckpoint? checkpoint)
    {
        using var host = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Desktop, HostOperation.Evidence);
        return await ((ICommittedAuthorityAuditReader)fixture.Store).ReadAsync(new() { Source = EvidenceSource.AuthorityAudit },
            checkpoint, host.Request, fixture.Time.Now, fixture.Token);
    }

    private sealed class Access : IEvidenceQueryAccess
    {
        public bool CanInspect => true;
    }

    private sealed class Reader(ICommittedAuthorityAuditReader source) : IEvidenceReader
    {
        public ValueTask<EvidenceReadBatch> ReadAsync(EvidenceQuery query, EvidenceReadCheckpoint? checkpoint,
            HostRequest request, DateTimeOffset now, CancellationToken cancellationToken) =>
            source.ReadAsync(query, checkpoint, request, now, cancellationToken);
    }
}
