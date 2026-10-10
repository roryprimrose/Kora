using System.Text;
using AwesomeAssertions;
using Kora.Core.Context;
using Kora.Core.Hosting;
using Kora.Core.Storage;
using Kora.Core.Diagnostics;
using Kora.Tools.Files;
using Kora.Windows.Context;
using Kora.Windows.Storage;
using Kora.Windows.IntegrationTests.Audio;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests.Storage;

public sealed partial class WindowsSqliteSessionAttachmentTests
{
    [Theory]
    [InlineData("")]
    [InlineData("# replacement café\r\n🙂 fresh exact historical snapshot\n")]
    [InlineData("maximum")]
    public async Task FreshVerifiedNativePickerCaptureAtomicallyReplacesExactSlotReopensCitationsAndRemovesWithoutChangingOriginals(string text)
    {
        using var fixture = await Initialize();
        var old = await Attach(fixture, Encoding.UTF8.GetBytes("OLD_REPLACEMENT_SENTINEL_529318"));
        var previous = await fixture.RunAsync(() => fixture.Store.PreviewRemoval(old.Session, fixture.Token));
        var hashes = ReadString(fixture, "SELECT group_concat(hash) FROM security_audit_events;");
        await NewControl(fixture, old.Session);
        var repository = new DirectoryInfo(AppContext.BaseDirectory);
        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "Kora.slnx"))) { repository = repository.Parent; }
        var scratch = Path.Combine(repository!.FullName, ".replacement-source-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        var path = Path.Combine(scratch, "fresh.md");
        byte[] bytes = string.Equals(text, "maximum", StringComparison.Ordinal)
            ? [0xef, 0xbb, 0xbf, .. Enumerable.Repeat((byte)'x', LocalFilePolicy.MaximumBytes - 3)]
            : [0xef, 0xbb, 0xbf, .. Encoding.UTF8.GetBytes(text)];
        try
        {
            await File.WriteAllBytesAsync(path, bytes, fixture.Token);
            await using var capture = new LocalSessionFileAttach(new WindowsLocalFileInspector(fixture.Paths),
                new Audit(), fixture.Time, NullLogger<LocalFilePreview>.Instance);
            SessionFileAttachment? replacement = null;
            using (var activity = HostActivity.BeginRoot(fixture.Request, HostActivityLayer.Application, HostOperation.Request))
            {
                (await capture.Select(new Picker(path), () => true, fixture.Token)).Should().Be(LocalFileOutcome.Reviewed);
                capture.Review!.Metadata.CanonicalPath.Should().Be(path);
                (await fixture.Store.ReadAttachment(old.Session, fixture.Token))!.File.Reference.Should().Be(old.File.Reference);
                (await capture.Execute(capture.Review.ReviewId, () => true, async (file, originalBytes, cancellation) =>
                {
                    replacement = await fixture.Store.Replace(fixture.Request, previous, file, originalBytes, () => true, cancellation);
                }, fixture.Token)).Should().Be(LocalFileOutcome.Admitted);
            }
            var current = replacement!;
            current.StorageRevision.Value.Should().Be(old.StorageRevision.Value + 1);
            current.File.Reference.SourceId.Should().NotBe(old.File.Reference.SourceId);
            current.File.Reference.RevisionId.Should().NotBe(old.File.Reference.RevisionId);
            current.File.Reference.ItemId.Should().NotBe(old.File.Reference.ItemId);
            current.File.Digest.Should().Be(LocalFilePolicy.Digest(bytes));
            var retrieval = new LocalFileLexicalRetrieval();
            retrieval.Search(current.File, old.File.Reference, "café", fixture.Time.Now, fixture.Token).Outcome
                .Should().Be(LocalFileSearchOutcome.Stale);
            var citations = retrieval.Search(current.File, current.File.Reference, "café", fixture.Time.Now, fixture.Token);
            fixture.Count("session_file").Should().Be(1);
            ReadString(fixture, "SELECT group_concat(hash) FROM security_audit_events;").Should().StartWith(hashes);
            ReadString(fixture, "SELECT group_concat(envelope) FROM security_audit_events;").Should().NotContain(path);
            foreach (var owned in new[] { fixture.DatabasePath, fixture.DatabasePath + "-journal" })
            {
                Encoding.UTF8.GetString(await File.ReadAllBytesAsync(owned, fixture.Token)).Should().NotContain("OLD_REPLACEMENT_SENTINEL_529318");
            }
            fixture.Reopen();
            await fixture.Store.InitializeAsync(fixture.Token);
            var reopened = (await fixture.RunAsync(() => fixture.Store.ReadAttachment(old.Session, fixture.Token)))!;
            reopened.Should().BeEquivalentTo(current, options => options.Excluding(item => item.File.Review.Cause));
            retrieval.Search(reopened.File, reopened.File.Reference, "café", fixture.Time.Now, fixture.Token)
                .Should().BeEquivalentTo(citations);
            var removal = await fixture.RunAsync(() => fixture.Store.PreviewRemoval(old.Session, fixture.Token));
            await NewControl(fixture, old.Session);
            await fixture.RunAsync(async () => await fixture.Store.Remove(fixture.Request, removal, () => true, fixture.Token));
            (await fixture.RunAsync(() => fixture.Store.ReadAttachment(old.Session, fixture.Token))).Should().BeNull();
            (await File.ReadAllBytesAsync(path, fixture.Token)).Should().Equal(bytes);
        }
        finally { Directory.Delete(scratch, recursive: true); }
    }

    [WindowsFact]
    public async Task FullProfileReplacementUsesItsOwnSlotAndPreservesEveryOtherRecordAndPriorAuthorityHash()
    {
        using var fixture = await Initialize();
        var first = await Attach(fixture, Encoding.UTF8.GetBytes("first retained"));
        var others = new List<SessionFileAttachment>();
        for (var index = 1; index < SessionFileAttachment.MaximumRetainedFiles; index++)
        {
            await NewSession(fixture);
            others.Add(await Attach(fixture, Encoding.UTF8.GetBytes("other " + index)));
        }
        var review = await fixture.RunAsync(() => fixture.Store.PreviewRemoval(first.Session, fixture.Token));
        await NewControl(fixture, first.Session);
        var replacement = await Replace(fixture, review, Encoding.UTF8.GetBytes("replacement at sixteen"));
        fixture.Count("session_file").Should().Be(16);
        foreach (var other in others)
        {
            (await fixture.RunAsync(() => fixture.Store.ReadAttachment(other.Session, fixture.Token)))
                .Should().BeEquivalentTo(other);
        }
        (await fixture.RunAsync(() => fixture.Store.ReadAttachment(first.Session, fixture.Token)))!.File.Reference
            .Should().Be(replacement.File.Reference);
        await NewSession(fixture);
        var overflow = () => Attach(fixture, Encoding.UTF8.GetBytes("not another slot"));
        await overflow.Should().ThrowAsync<InvalidOperationException>().WithMessage("*sixteen*");
    }

    [Theory]
    [InlineData("storage")]
    [InlineData("reference")]
    [InlineData("inventory")]
    [InlineData("session")]
    [InlineData("generation")]
    [InlineData("consent")]
    [InlineData("voice")]
    [InlineData("invocation")]
    [InlineData("task")]
    [InlineData("same-source")]
    [InlineData("artifact")]
    [InlineData("live")]
    [InlineData("unknown")]
    public async Task ExactOldInventoryNewLineageAndCurrentAdmissionFailClosedWithoutReplacingOldBody(string boundary)
    {
        using var fixture = await Initialize();
        var old = await Attach(fixture, Encoding.UTF8.GetBytes("old valid record"));
        var review = await fixture.RunAsync(() => fixture.Store.PreviewRemoval(old.Session, fixture.Token));
        if (string.Equals(boundary, "live", StringComparison.Ordinal) || string.Equals(boundary, "unknown", StringComparison.Ordinal))
        {
            await NewControl(fixture, old.Session);
            if (string.Equals(boundary, "unknown", StringComparison.Ordinal))
            {
                await fixture.RunAsync(async () => await fixture.Tasks.CommitAsync(new(fixture.Request, new(2), HostTaskState.Unknown), 1, fixture.Token));
            }
        }
        await NewControl(fixture, old.Session);
        review = boundary switch
        {
            "storage" => review with { StorageRevision = new(99) },
            "reference" => review with { File = review.File with { RevisionId = Guid.NewGuid() } },
            "inventory" => review with { InventoryRevision = "hostile" },
            "session" => review with { Session = new(Guid.NewGuid()) },
            "generation" => review with { Generation = new(99) },
            _ => review,
        };
        if (string.Equals(boundary, "artifact", StringComparison.Ordinal))
        {
            await File.WriteAllTextAsync(Path.Combine(fixture.Paths.LocalRoot, HostInteractionSchema.Partition, "Artifacts", "unknown.copy"), "held", fixture.Token);
        }
        byte[] bytes = Encoding.UTF8.GetBytes("new unadmitted source");
        var original = fixture.Request;
        var captureRequest = boundary switch
        {
            "voice" => new(original.RequestId, original.SessionId, original.TaskId, RequestOrigin.ActivatedVoice),
            "invocation" => new(original.RequestId, original.SessionId, original.TaskId, original.Origin, new(Guid.NewGuid())),
            "task" => new HostRequest(original.RequestId, original.SessionId, new(Guid.NewGuid()), original.Origin),
            _ => original,
        };
        var file = RevisionCandidate(fixture, captureRequest, bytes,
            string.Equals(boundary, "same-source", StringComparison.Ordinal) ? old.File.Reference.SourceId : Guid.NewGuid());
        var replace = () => fixture.RunAsync(() => fixture.Store.Replace(original, review, file, bytes,
            () => !string.Equals(boundary, "consent", StringComparison.Ordinal), fixture.Token));
        await replace.Should().ThrowAsync<Exception>();
        (await fixture.RunAsync(() => fixture.Store.ReadAttachment(old.Session, fixture.Token)))!
            .Should().BeEquivalentTo(old);
    }

    [Theory]
    [InlineData("audit")]
    [InlineData("commit")]
    [InlineData("swap-audit")]
    [InlineData("swap-gate")]
    [InlineData("uncertain-swap")]
    [InlineData("journal")]
    [InlineData("certification-audit")]
    [InlineData("certification-gate")]
    public async Task RollbackPreservesExactOldRecordButCommittedUncertifiedSwapHoldsDisclosureAndRequiresReviewedRemoval(string boundary)
    {
        using var fixture = await Initialize();
        var old = await Attach(fixture, Encoding.UTF8.GetBytes("OLD_ROLLBACK_OR_RETIRE_739157"));
        var review = await fixture.RunAsync(() => fixture.Store.PreviewRemoval(old.Session, fixture.Token));
        await NewControl(fixture, old.Session);
        var checkpoint = new AttachmentCheckpoint();
        fixture.Reopen(checkpoint);
        await fixture.Store.InitializeAsync(fixture.Token);
        var allowed = true;
        var audits = 0;
        var commits = 0;
        checkpoint.Audit = () =>
        {
            audits++;
            if (string.Equals(boundary, "audit", StringComparison.Ordinal)
                || string.Equals(boundary, "swap-audit", StringComparison.Ordinal) && audits == 2
                || string.Equals(boundary, "certification-audit", StringComparison.Ordinal) && audits == 3)
            {
                throw new IOException("required audit failed");
            }
        };
        checkpoint.Commit = () =>
        {
            commits++;
            if (string.Equals(boundary, "commit", StringComparison.Ordinal)
                || string.Equals(boundary, "swap-gate", StringComparison.Ordinal) && commits == 2
                || string.Equals(boundary, "certification-gate", StringComparison.Ordinal) && commits == 3) { allowed = false; }
        };
        checkpoint.Copies = () =>
        {
            if (string.Equals(boundary, "journal", StringComparison.Ordinal)) { throw new IOException("committed-journal verification failed"); }
        };
        checkpoint.CommitBoundary = (connection, transaction) =>
        {
            if (!string.Equals(boundary, "uncertain-swap", StringComparison.Ordinal) || commits != 2) { return; }
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                PRAGMA defer_foreign_keys=ON;
                INSERT INTO session_metadata VALUES('11111111-1111-1111-1111-111111111111',1,'rollback-only foreign key failure',NULL);
                """;
            command.ExecuteNonQuery();
        };
        var replace = () => Replace(fixture, review, Encoding.UTF8.GetBytes("NEW_HELD_REPLACEMENT_739157"), () => allowed);
        await replace.Should().ThrowAsync<Exception>();
        fixture.Reopen();
        await fixture.Store.InitializeAsync(fixture.Token);
        if (boundary is "audit" or "commit" or "swap-audit" or "swap-gate")
        {
            (await fixture.RunAsync(() => fixture.Store.ReadAttachment(old.Session, fixture.Token)))!
                .Should().BeEquivalentTo(old);
            return;
        }
        var read = () => fixture.RunAsync(() => fixture.Store.ReadAttachment(old.Session, fixture.Token));
        await read.Should().ThrowAsync<SessionFileReplacementHeldException>();
        var recovery = await fixture.RunAsync(() => fixture.Store.PreviewRemoval(old.Session, fixture.Token));
        recovery.ReplacementCopyVerificationPending.Should().BeTrue();
        if (string.Equals(boundary, "uncertain-swap", StringComparison.Ordinal))
        {
            recovery.ReplacementSwapUnconfirmed.Should().BeTrue();
            recovery.File.Should().Be(old.File.Reference);
        }
        else { recovery.File.Should().NotBe(old.File.Reference); }
        await NewControl(fixture, old.Session);
        await fixture.RunAsync(async () => await fixture.Store.Remove(fixture.Request, recovery, () => true, fixture.Token));
        fixture.Reopen();
        await fixture.Store.InitializeAsync(fixture.Token);
        (await fixture.RunAsync(() => fixture.Store.ReadAttachment(old.Session, fixture.Token))).Should().BeNull();
        foreach (var path in new[] { fixture.DatabasePath, fixture.DatabasePath + "-journal" })
        {
            var source = Encoding.UTF8.GetString(await File.ReadAllBytesAsync(path, fixture.Token));
            source.Should().NotContain("OLD_ROLLBACK_OR_RETIRE_739157").And.NotContain("NEW_HELD_REPLACEMENT_739157");
        }
    }

    private static LocalFileRevision RevisionCandidate(InteractionStorageFixture fixture, HostRequest original, byte[] bytes, Guid source) =>
        new(new(Guid.NewGuid(), source, original, new(@"C:\Synthetic\fresh.md", "fresh-native-identity", bytes.Length, fixture.Time.Now)),
            bytes, fixture.Time.Now);

    [WindowsFact]
    public async Task CompetingReplacementAndRemovalCannotReplayOldInventoryOrResurrectRetiredBody()
    {
        using var fixture = await Initialize();
        var old = await Attach(fixture, Encoding.UTF8.GetBytes("COMPETING_OLD_318675"));
        var stale = await fixture.RunAsync(() => fixture.Store.PreviewRemoval(old.Session, fixture.Token));
        await NewControl(fixture, old.Session);
        var current = await Replace(fixture, stale, Encoding.UTF8.GetBytes("COMPETING_CURRENT_318675"));
        await NewControl(fixture, old.Session);
        var replay = () => Replace(fixture, stale, Encoding.UTF8.GetBytes("never admitted"));
        await replay.Should().ThrowAsync<InvalidOperationException>();
        (await fixture.RunAsync(() => fixture.Store.ReadAttachment(old.Session, fixture.Token)))!.File.Reference
            .Should().Be(current.File.Reference);
        await FinishControl(fixture);
        var removal = await fixture.RunAsync(() => fixture.Store.PreviewRemoval(old.Session, fixture.Token));
        await NewControl(fixture, old.Session);
        await fixture.RunAsync(async () => await fixture.Store.Remove(fixture.Request, removal, () => true, fixture.Token));
        await NewControl(fixture, old.Session);
        var afterRemoval = () => Replace(fixture, removal, Encoding.UTF8.GetBytes("not resurrection"));
        await afterRemoval.Should().ThrowAsync<InvalidOperationException>();
        (await fixture.RunAsync(() => fixture.Store.ReadAttachment(old.Session, fixture.Token))).Should().BeNull();
        foreach (var path in new[] { fixture.DatabasePath, fixture.DatabasePath + "-journal" })
        {
            Encoding.UTF8.GetString(await File.ReadAllBytesAsync(path, fixture.Token))
                .Should().NotContain("COMPETING_OLD_318675").And.NotContain("COMPETING_CURRENT_318675");
        }
    }

    [WindowsFact]
    public async Task CompetingDoneLifecycleRejectsReplacementButKeepsHistoricalSnapshotPassiveAndUnchanged()
    {
        using var fixture = await Initialize();
        var old = await Attach(fixture, Encoding.UTF8.GetBytes("done historical source"));
        var review = await fixture.RunAsync(() => fixture.Store.PreviewRemoval(old.Session, fixture.Token));
        await NewControl(fixture, old.Session);
        await fixture.RunAsync(async () =>
        {
            await fixture.Store.ChangeIdleLifecycleAsync(fixture.Request, new(1), false, () => true, fixture.Token);
            await fixture.Tasks.CommitAsync(new(fixture.Request, new(2), HostTaskState.Succeeded), 1, fixture.Token);
        });
        await NewControl(fixture, old.Session);
        var replace = () => Replace(fixture, review, Encoding.UTF8.GetBytes("inactive denied"));
        await replace.Should().ThrowAsync<InvalidOperationException>();
        (await fixture.RunAsync(() => fixture.Store.ReadAttachment(old.Session, fixture.Token)))!.Should().BeEquivalentTo(old);
        (await fixture.Store.ReadSessionAsync(old.Session, fixture.Token))!.IsActive.Should().BeFalse();
    }

    private static Task<SessionFileAttachment> Replace(InteractionStorageFixture fixture, SessionFileRemoval previous,
        byte[] bytes, Func<bool>? gate = null) =>
        fixture.RunAsync(() => fixture.Store.Replace(fixture.Request, previous,
            RevisionCandidate(fixture, fixture.Request, bytes, Guid.NewGuid()), bytes, gate ?? (() => true), fixture.Token));
}
