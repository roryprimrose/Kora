using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Globalization;
using System.Security.Cryptography;

using AwesomeAssertions;

using Kora.Core.Hosting;
using Kora.Core.Diagnostics;
using Kora.Core.Storage;
using Kora.Windows.IntegrationTests.Audio;

namespace Kora.Windows.IntegrationTests.Storage;

public sealed partial class WindowsSqliteSessionAttachmentTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CatalogueUsesActualMetadataOnlySelectAndDoesNotClaimCorruptBodyIntegrityOrReadOriginalPath(bool exact)
    {
        using var fixture = await Initialize();
        using var root = HostActivity.BeginRoot(fixture.Request, HostActivityLayer.Application, HostOperation.Request);
        var retained = await Attach(fixture, Encoding.UTF8.GetBytes("PRIVATE_BLOB_SENTINEL_923478"));
        var checkpoint = new AttachmentCheckpoint();
        fixture.Reopen(checkpoint);
        await fixture.Store.InitializeAsync(fixture.Token);
        var selects = 0;
        checkpoint.Metadata = command =>
        {
            selects++;
            command.Parameters["$limit"].Value.Should().Be(SessionAttachmentPage.MaximumScannedRows);
            using var actual = command.ExecuteReader();
            actual.FieldCount.Should().Be(6);
            Enumerable.Range(0, actual.FieldCount).Select(actual.GetName).Should().NotContain("body");
            actual.Read().Should().BeTrue();
            actual.GetName(4).Should().Be("length(body)");
            actual.GetName(5).Should().Be("typeof(body)");
        };
        var clock = await fixture.Store.ReadRetentionAsync(retained.Session, fixture.Token);
        var tasks = fixture.Count("host_tasks");
        var authority = fixture.Count("security_audit_events");
        fixture.Mutate("UPDATE session_file SET body=zeroblob(length(body));");
        var page = await fixture.Store.ReadAttachmentPage(new(false, exact ? retained.Session.Value.ToString("D") : null), null, () => true, fixture.Token);
        selects.Should().Be(1);
        page.Validate();
        var summary = page.Records.Should().ContainSingle().Subject;
        summary.File.Should().Be(retained.File.Reference);
        summary.StoredBytes.Should().Be(retained.File.Review.Metadata.ByteLength);
        summary.CopyStatus.Should().Be(AttachmentCopyStatus.Confirmed, "certification headers are not byte-integrity verification");
        JsonSerializer.Serialize(page).Should().NotContain("PRIVATE_BLOB_SENTINEL").And.NotContain("Synthetic")
            .And.NotContain("CanonicalPath").And.NotContain("FileIdentity");
        fixture.Count("host_tasks").Should().Be(tasks);
        fixture.Count("security_audit_events").Should().Be(authority);
        var full = () => fixture.Store.ReadAttachment(retained.Session, fixture.Token).AsTask();
        await full.Should().ThrowAsync<InvalidDataException>();
        fixture.Mutate("UPDATE session_file SET body=X'" + Convert.ToHexString(Encoding.UTF8.GetBytes("PRIVATE_BLOB_SENTINEL_923478")) + "';");
        (await fixture.Store.ReadRetentionAsync(retained.Session, fixture.Token)).Should().Be(clock);
    }

    [WindowsFact]
    public async Task CatalogueLabelsDoneAndRemovedSessionSlotsAndRejectsOldSummaryWithoutResumingOrRenewingActivity()
    {
        using var fixture = await Initialize();
        using var root = HostActivity.BeginRoot(fixture.Request, HostActivityLayer.Application, HostOperation.Request);
        var retained = await Attach(fixture, Encoding.UTF8.GetBytes("historical private source"));
        var active = (await fixture.Store.ReadAttachmentPage(new(), null, () => true, fixture.Token)).Records.Single();
        active.SessionState.Should().Be(AttachmentSessionState.Active);
        await fixture.Store.VerifyAttachmentSummary(active, () => true, fixture.Token);
        await NewControl(fixture, retained.Session);
        var done = await fixture.RunAsync(() => fixture.Store.ChangeIdleLifecycleAsync(fixture.Request, new(1), false, () => true, fixture.Token));
        await FinishControl(fixture);
        var clock = await fixture.Store.ReadRetentionAsync(retained.Session, fixture.Token);
        var audit = fixture.Count("security_audit_events");
        var observed = (await fixture.Store.ReadAttachmentPage(new(), null, () => true, fixture.Token)).Records.Single();
        observed.SessionState.Should().Be(AttachmentSessionState.Done);
        observed.SessionGeneration.Should().Be(done.Generation);
        observed.CapturedGeneration.Should().Be(retained.Generation);
        await fixture.Store.VerifyAttachmentSummary(observed, () => true, fixture.Token);
        var stale = () => fixture.Store.VerifyAttachmentSummary(active, () => true, fixture.Token).AsTask();
        await stale.Should().ThrowAsync<InvalidOperationException>();
        (await fixture.Store.ReadRetentionAsync(retained.Session, fixture.Token)).Should().Be(clock);
        fixture.Count("security_audit_events").Should().Be(audit);
        var preview = await fixture.Store.PreviewDispositionAsync(retained.Session, done.Generation, 0, fixture.Token);
        await NewControl(fixture, retained.Session);
        await fixture.RunAsync(() => fixture.Store.DisposeSessionAsync(fixture.Request, preview, () => true, fixture.Token));
        var all = await fixture.Store.ReadAttachmentPage(new(true), null, () => true, fixture.Token);
        all.Records.Single().SessionState.Should().Be(AttachmentSessionState.Removed);
        all.Records.Single().CopyStatus.Should().Be(AttachmentCopyStatus.Unavailable);
        all.Records.Single().StoredBytes.Should().Be(0);
        (await fixture.Store.ReadAttachmentPage(new(), null, () => true, fixture.Token)).Records.Should().BeEmpty();
        var removed = () => fixture.Store.VerifyAttachmentSummary(observed, () => true, fixture.Token).AsTask();
        await removed.Should().ThrowAsync<InvalidOperationException>();
    }

    [Theory]
    [InlineData("replace")]
    [InlineData("remove")]
    [InlineData("held")]
    public async Task ExactCatalogueSelectionNeverRetargetsReplacementRemovalOrUncertifiedCopy(string transition)
    {
        using var fixture = await Initialize();
        using var root = HostActivity.BeginRoot(fixture.Request, HostActivityLayer.Application, HostOperation.Request);
        var original = await Attach(fixture, Encoding.UTF8.GetBytes("old retained exact source"));
        var summary = (await fixture.Store.ReadAttachmentPage(new(), null, () => true, fixture.Token)).Records.Single();
        var review = await fixture.Store.PreviewRemoval(original.Session, fixture.Token);
        await NewControl(fixture, original.Session);
        if (transition is "remove")
        {
            await fixture.RunAsync(async () => await fixture.Store.Remove(fixture.Request, review, () => true, fixture.Token));
        }
        else if (transition is "held")
        {
            var checkpoint = new AttachmentCheckpoint { Copies = () => throw new IOException("Owned-copy verification held.") };
            fixture.Reopen(checkpoint);
            await fixture.Store.InitializeAsync(fixture.Token);
            var replace = () => Replace(fixture, review, Encoding.UTF8.GetBytes("new held source"));
            await replace.Should().ThrowAsync<IOException>();
        }
        else { await Replace(fixture, review, Encoding.UTF8.GetBytes("new source")); }
        var current = (await fixture.Store.ReadAttachmentPage(new(true), null, () => true, fixture.Token)).Records.Single();
        current.StorageRevision.Value.Should().Be(summary.StorageRevision.Value + 1);
        current.CopyStatus.Should().Be(transition switch
        {
            "remove" => AttachmentCopyStatus.Unavailable,
            "held" => AttachmentCopyStatus.Held,
            _ => AttachmentCopyStatus.Confirmed,
        });
        var stale = () => fixture.Store.VerifyAttachmentSummary(summary, () => true, fixture.Token).AsTask();
        await stale.Should().ThrowAsync<InvalidOperationException>();
        if (transition is "held")
        {
            var held = () => fixture.Store.VerifyAttachmentSummary(current, () => true, fixture.Token).AsTask();
            await held.Should().ThrowAsync<InvalidOperationException>();
            var body = () => fixture.Store.ReadAttachment(current.Session, fixture.Token).AsTask();
            await body.Should().ThrowAsync<SessionFileReplacementHeldException>();
        }
    }

    [WindowsFact]
    public async Task CataloguePagesHistoricalRemovalSlotsBeyondLiveCapWithoutEvictionAndEmptyRetainedPageCanContinue()
    {
        using var fixture = await Initialize();
        using var root = HostActivity.BeginRoot(fixture.Request, HostActivityLayer.Application, HostOperation.Request);
        for (var index = 0; index < 65; index++)
        {
            if (index != 0) { await NewSession(fixture); }
            var attachment = await Attach(fixture, Encoding.UTF8.GetBytes("removed slot " + index));
            var review = await fixture.Store.PreviewRemoval(attachment.Session, fixture.Token);
            await NewControl(fixture, attachment.Session);
            await fixture.RunAsync(async () => await fixture.Store.Remove(fixture.Request, review, () => true, fixture.Token));
        }
        var retained = await fixture.Store.ReadAttachmentPage(new(), null, () => true, fixture.Token);
        retained.Records.Should().BeEmpty();
        retained.ScannedRows.Should().Be(64);
        retained.ScanLimitReached.Should().BeTrue();
        retained.Next.Should().NotBeNull();
        var last = await fixture.Store.ReadAttachmentPage(new(), retained.Next, () => true, fixture.Token);
        last.Records.Should().BeEmpty();
        last.ScannedRows.Should().Be(1);
        last.Next.Should().BeNull();
        var all = new List<SessionAttachmentSummary>();
        string? cursor = null;
        do
        {
            var page = await fixture.Store.ReadAttachmentPage(new(true), cursor, () => true, fixture.Token);
            page.Records.Length.Should().BeLessThanOrEqualTo(16);
            JsonSerializer.SerializeToUtf8Bytes(page).Length.Should().BeLessThanOrEqualTo(65536);
            page.Records.Should().OnlyContain(row => row.CopyStatus == AttachmentCopyStatus.Unavailable && row.StoredBytes == 0);
            all.AddRange(page.Records);
            cursor = page.Next;
        } while (cursor is not null);
        all.Select(row => row.Session).Distinct().Should().HaveCount(65);
        fixture.Count("session_file").Should().Be(65);
        var wrongQuery = () => fixture.Store.ReadAttachmentPage(new(true), retained.Next, () => true, fixture.Token).AsTask();
        await wrongQuery.Should().ThrowAsync<InvalidDataException>();
        using (var otherViewer = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Application, HostOperation.Request))
        {
            var otherScope = () => fixture.Store.ReadAttachmentPage(new(), retained.Next, () => true, fixture.Token).AsTask();
            await otherScope.Should().ThrowAsync<InvalidDataException>();
        }
        var tamper = () => fixture.Store.ReadAttachmentPage(new(), retained.Next + "0", () => true, fixture.Token).AsTask();
        await tamper.Should().ThrowAsync<InvalidDataException>();
        fixture.Time.Now = fixture.Time.Now.AddMinutes(2);
        var expired = () => fixture.Store.ReadAttachmentPage(new(), retained.Next, () => true, fixture.Token).AsTask();
        await expired.Should().ThrowAsync<InvalidDataException>();
        var fresh = await fixture.Store.ReadAttachmentPage(new(true), null, () => true, fixture.Token);
        await NewSession(fixture);
        await Attach(fixture, Encoding.UTF8.GetBytes("new retained current observation"));
        var changed = () => fixture.Store.ReadAttachmentPage(new(true), fresh.Next, () => true, fixture.Token).AsTask();
        await changed.Should().ThrowAsync<InvalidDataException>();
    }

    [Theory]
    [InlineData("UPDATE session_file SET descriptor=json_set(descriptor,'$.Format',99);")]
    [InlineData("UPDATE session_file SET descriptor=json_set(descriptor,'$.Metadata.CanonicalPath','C:\\\\Secrets\\\\credential.md');")]
    [InlineData("UPDATE session_file SET descriptor=json_set(descriptor,'$.Reference',NULL);")]
    [InlineData("PRAGMA ignore_check_constraints=ON; UPDATE session_file SET descriptor=CAST(zeroblob(8193) AS TEXT);")]
    [InlineData("PRAGMA ignore_check_constraints=ON; UPDATE session_file SET body=zeroblob(262145);")]
    [InlineData("UPDATE session_file SET body=zeroblob(length(body)+1);")]
    [InlineData("UPDATE work_sessions SET state=2;")]
    [InlineData("UPDATE memory_profile SET profile_id='11111111-1111-1111-1111-111111111111';")]
    [InlineData("DELETE FROM session_file;")]
    [InlineData("DELETE FROM security_audit_events WHERE sequence=(SELECT audit_sequence FROM session_file);")]
    [InlineData("UPDATE security_audit_events SET envelope=json_set(envelope,'$.SessionGeneration.Value',99) WHERE sequence=1;")]
    [InlineData("PRAGMA user_version=7;")]
    public async Task CatalogueRejectsUnknownRawTypesScopeDescriptorAuditAndDowngradedAuthorityWithoutEmptySuccess(string mutation)
    {
        using var fixture = await Initialize();
        using var root = HostActivity.BeginRoot(fixture.Request, HostActivityLayer.Application, HostOperation.Request);
        await Attach(fixture, Encoding.UTF8.GetBytes("retained bounded original"));
        fixture.Mutate(mutation);
        var read = () => fixture.Store.ReadAttachmentPage(new(true), null, () => true, fixture.Token).AsTask();
        await read.Should().ThrowAsync<Exception>();
    }

    [Theory]
    [InlineData("UPDATE session_file SET body=CAST(body AS TEXT);")]
    [InlineData("UPDATE session_file SET descriptor=zeroblob(8192);")]
    [InlineData("UPDATE session_file SET revision=1.5;")]
    [InlineData("UPDATE work_sessions SET generation=1.5;")]
    public async Task CatalogueRejectsCorruptRawSqliteTypesEvenWhenExactCanonicalSchemaIsRestored(string mutation)
    {
        using var fixture = await Initialize();
        using var root = HostActivity.BeginRoot(fixture.Request, HostActivityLayer.Application, HostOperation.Request);
        await Attach(fixture, Encoding.UTF8.GetBytes("exact raw column type"));
        fixture.Mutate("""
            PRAGMA writable_schema=ON;
            UPDATE sqlite_schema SET sql=replace(sql,') STRICT',')') WHERE name IN ('session_file','work_sessions');
            PRAGMA writable_schema=RESET;
            """);
        fixture.Mutate(mutation);
        fixture.Mutate("""
            PRAGMA writable_schema=ON;
            UPDATE sqlite_schema SET sql=sql||' STRICT' WHERE name IN ('session_file','work_sessions');
            PRAGMA writable_schema=RESET;
            """);
        var read = () => fixture.Store.ReadAttachmentPage(new(), null, () => true, fixture.Token).AsTask();
        await read.Should().ThrowAsync<InvalidDataException>();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CatalogueMetadataReadCannotDiscloseAfterPrivateClosureOrCancellation(bool cancel)
    {
        using var fixture = await Initialize();
        using var root = HostActivity.BeginRoot(fixture.Request, HostActivityLayer.Application, HostOperation.Request);
        await Attach(fixture, Encoding.UTF8.GetBytes("private source"));
        var allowed = true;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(fixture.Token);
        var checkpoint = new AttachmentCheckpoint();
        fixture.Reopen(checkpoint);
        await fixture.Store.InitializeAsync(fixture.Token);
        checkpoint.Metadata = _ => { if (cancel) { cancellation.Cancel(); } else { allowed = false; } };
        var read = () => fixture.Store.ReadAttachmentPage(new(exactMediaType: "text/plain"), null, () => allowed, cancellation.Token).AsTask();
        await read.Should().ThrowAsync<Exception>();
    }

    [WindowsFact]
    public async Task CatalogueRejectsChangedEmptyProfileAndOldObservationSourceAfterReinitialization()
    {
        using var fixture = await Initialize();
        using var root = HostActivity.BeginRoot(fixture.Request, HostActivityLayer.Application, HostOperation.Request);
        var empty = await fixture.Store.ReadAttachmentPage(new(), null, () => true, fixture.Token);
        empty.Records.Should().BeEmpty();
        var retained = await Attach(fixture, Encoding.UTF8.GetBytes("current observation"));
        var selected = (await fixture.Store.ReadAttachmentPage(new(), null, () => true, fixture.Token)).Records.Single();
        fixture.Reopen();
        await fixture.Store.InitializeAsync(fixture.Token);
        var old = () => fixture.Store.VerifyAttachmentSummary(selected, () => true, fixture.Token).AsTask();
        await old.Should().ThrowAsync<InvalidOperationException>();
        var removal = await fixture.Store.PreviewRemoval(retained.Session, fixture.Token);
        await NewControl(fixture, retained.Session);
        await fixture.RunAsync(async () => await fixture.Store.Remove(fixture.Request, removal, () => true, fixture.Token));
        fixture.Mutate("UPDATE memory_profile SET profile_id='11111111-1111-1111-1111-111111111111';");
        var changed = () => fixture.Store.ReadAttachmentPage(new(), null, () => true, fixture.Token).AsTask();
        await changed.Should().ThrowAsync<InvalidDataException>();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CatalogueValidatesFiniteAuthoritySegmentsBeforeExecutingAnyAttachmentMetadataSelect(bool exact)
    {
        using var fixture = await Initialize();
        using var root = HostActivity.BeginRoot(fixture.Request, HostActivityLayer.Application, HostOperation.Request);
        var retained = await Attach(fixture, Encoding.UTF8.GetBytes("bounded audit authority source"));
        var checkpoint = new AttachmentCheckpoint();
        fixture.Reopen(checkpoint);
        await fixture.Store.InitializeAsync(fixture.Token);
        AppendCatalogueAuditReceipts(fixture, SessionAttachmentPage.MaximumAuditRows + 1);
        var reads = 0;
        checkpoint.Metadata = _ => reads++;
        var query = new SessionAttachmentQuery(false, exact ? retained.Session.Value.ToString("D") : null);
        var first = await fixture.Store.ReadAttachmentPage(query, null, () => true, fixture.Token);
        first.AuthorityValidationPending.Should().BeTrue();
        first.AuditScannedRows.Should().Be(4096);
        first.ScannedRows.Should().Be(0);
        first.Records.Should().BeEmpty();
        first.Next.Should().NotBeNull();
        reads.Should().Be(0, "no attachment descriptor SELECT is executed before complete bounded typed authority validation");
        var wrongQuery = () => fixture.Store.ReadAttachmentPage(new(false, Guid.NewGuid().ToString("D")), first.Next, () => true, fixture.Token).AsTask();
        await wrongQuery.Should().ThrowAsync<InvalidDataException>();
        var second = await fixture.Store.ReadAttachmentPage(query, first.Next, () => true, fixture.Token);
        second.AuthorityValidationPending.Should().BeFalse();
        second.Records.Single().File.Should().Be(retained.File.Reference);
        second.Next.Should().BeNull();
        second.AuditScannedRows.Should().BeLessThan(4096);
        reads.Should().Be(1);
    }

    [WindowsFact]
    public async Task SavedDraftQueryCompletesBoundedAuthorityPrefixBeforeDisclosingEvenAnExactIdMatch()
    {
        using var fixture = await Initialize();
        using var root = HostActivity.BeginRoot(fixture.Request, HostActivityLayer.Application, HostOperation.Request);
        var saved = await WindowsSqliteConversationTests.Save(fixture,
            await fixture.Store.ReadConversationDraftAsync(fixture.Request.SessionId, fixture.Token), "PRIVATE_PENDING_DRAFT");
        var observed = (await fixture.Store.ReadSavedDraftPage(SessionListFilter.All, null, 1, () => true, fixture.Token)).Records.Single();
        await Attach(fixture, Encoding.UTF8.GetBytes("authority segment fixture"));
        AppendCatalogueAuditReceipts(fixture, SessionAttachmentPage.MaximumAuditRows + 1);
        var query = new SessionListSearch(SessionListSearchKind.ExactId, SessionListFilter.All, saved.Session.SessionId.Value.ToString("D"));
        var metadata = new SavedDraftMetadataFilter(saved.Draft.Revision, 1, 16384, 1, 4096,
            observed.Source, observed.Profile);
        var first = await fixture.Store.ReadSavedDraftPage(SessionListFilter.All, null, 1, () => true, fixture.Token, query, metadata);
        first.AuthorityPending.Should().BeTrue();
        first.AuditScanned.Should().Be(4096);
        first.Scanned.Should().Be(0);
        first.Records.Should().BeEmpty();
        first.Next.Should().NotBeNull();
        var changed = () => fixture.Store.ReadSavedDraftPage(SessionListFilter.All, first.Next, 1, () => true, fixture.Token,
            new(SessionListSearchKind.NameSubstring, SessionListFilter.All, "unmatched")).AsTask();
        await changed.Should().ThrowAsync<InvalidDataException>("validation progress also belongs to the original query");
        var wrongMetadata = () => fixture.Store.ReadSavedDraftPage(SessionListFilter.All, first.Next, 1, () => true,
            fixture.Token, query, new(saved.Draft.Revision + 1, 1, 16384)).AsTask();
        await wrongMetadata.Should().ThrowAsync<InvalidDataException>();
        foreach (var alternate in new SavedDraftMetadataFilter[]
        {
            new(saved.Draft.Revision, 1, 16384, 2, 4096, observed.Source, observed.Profile),
            new(saved.Draft.Revision, 1, 16384, 1, 4095, observed.Source, observed.Profile),
            new(saved.Draft.Revision, 1, 16384, 1, 4096, Guid.NewGuid(), observed.Profile),
            new(saved.Draft.Revision, 1, 16384, 1, 4096, observed.Source, new(Guid.NewGuid())),
        })
        {
            var wrongScalars = () => fixture.Store.ReadSavedDraftPage(SessionListFilter.All, first.Next, 1, () => true,
                fixture.Token, query, alternate).AsTask();
            await wrongScalars.Should().ThrowAsync<InvalidDataException>("scalar bounds bind authority-prefix progress too");
        }
        var second = await fixture.Store.ReadSavedDraftPage(SessionListFilter.All, first.Next, 1, () => true, fixture.Token, query, metadata);
        second.AuthorityPending.Should().BeFalse();
        second.Records.Should().ContainSingle().Which.Session.SessionId.Should().Be(saved.Session.SessionId);
        second.Scanned.Should().Be(1);
        second.Next.Should().BeNull();
        second.AuditScanned.Should().BeLessThan(4096);
        await WindowsSqliteConversationTests.Save(fixture, saved, "new saved revision");
        var changedRevision = () => fixture.Store.ReadSavedDraftPage(SessionListFilter.All, first.Next, 1, () => true,
            fixture.Token, query, metadata).AsTask();
        await changedRevision.Should().ThrowAsync<InvalidDataException>("a draft change revokes authority progress too");
        fixture.Mutate("UPDATE security_audit_events SET envelope=json_set(envelope,'$.SessionGeneration.Value',99) WHERE sequence=1;");
        var irrelevantAuthority = () => fixture.Store.ReadSavedDraftPage(SessionListFilter.All, null, 1, () => true,
            fixture.Token, new(SessionListSearchKind.ExactId, SessionListFilter.All, Guid.NewGuid().ToString("D")),
            new(long.MaxValue, 0, 0, 0, 0, Guid.NewGuid(), new(Guid.NewGuid()))).AsTask();
        await irrelevantAuthority.Should().ThrowAsync<Exception>("even an unknown exact ID and impossible metadata match validate irrelevant prefix rows");
    }

    private static void AppendCatalogueAuditReceipts(InteractionStorageFixture fixture, int count)
    {
        using var connection = fixture.OpenRaw();
        using var transaction = connection.BeginTransaction();
        using var head = connection.CreateCommand();
        head.Transaction = transaction;
        head.CommandText = "SELECT sequence,hash FROM authority_head WHERE singleton=1;";
        long sequence;
        string hash;
        using (var row = head.ExecuteReader())
        {
            row.Read().Should().BeTrue();
            sequence = row.GetInt64(0);
            hash = row.GetString(1);
        }
        head.CommandText = "SELECT envelope FROM security_audit_events ORDER BY sequence DESC LIMIT 1;";
        var receipt = JsonNode.Parse((string)head.ExecuteScalar()!)!;
        receipt["Changes"] = new JsonArray();
        for (var index = 0; index < count; index++)
        {
            fixture.Token.ThrowIfCancellationRequested();
            var correlation = Guid.NewGuid().ToString("D");
            receipt["Audit"]!["CorrelationId"] = correlation;
            var json = receipt.ToJsonString();
            sequence++;
            var next = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
                sequence.ToString(CultureInfo.InvariantCulture) + "\n" + hash + "\n" + json)));
            using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = "INSERT INTO security_audit_events VALUES($sequence,$correlation,$previous,$hash,$envelope);";
            insert.Parameters.AddWithValue("$sequence", sequence);
            insert.Parameters.AddWithValue("$correlation", correlation);
            insert.Parameters.AddWithValue("$previous", hash);
            insert.Parameters.AddWithValue("$hash", next);
            insert.Parameters.AddWithValue("$envelope", json);
            insert.ExecuteNonQuery();
            hash = next;
        }
        head.CommandText = "UPDATE authority_head SET sequence=$sequence,hash=$hash WHERE singleton=1;";
        head.Parameters.AddWithValue("$sequence", sequence);
        head.Parameters.AddWithValue("$hash", hash);
        head.ExecuteNonQuery();
        transaction.Commit();
    }
}
