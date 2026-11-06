using AwesomeAssertions;

using Kora.Application.Definitions;
using Kora.Core.Artifacts;
using Kora.Core.Auditing;

namespace Kora.Application.UnitTests.Definitions;

public sealed partial class InstructionEditorTests
{
    [Theory]
    [InlineData(false, "\n")]
    [InlineData(true, "\r\n")]
    [InlineData(true, "\r")]
    public async Task RestoreUsesTheNormalWriterForExactWholePreviousBytesAsNewDisabledRevision(bool bom, string newline)
    {
        await using var f = new Fixture();
        var previous = Snapshot() with
        {
            Document = ArtifactFileDocument.Parse(ArtifactFileDocument.Encode(
                string.Join(newline, "---", "kora-format: 1", "name: example", "description: 'Previous metadata'",
                    "attribution: \"Original credit\"", "---", "PRIVATE_PREVIOUS_BODY", ""), bom),
                ArtifactKind.Instruction, "kora-profile", "example"),
        };
        f.Files.Current = previous;
        f.Catalogue.Publish(f.Revisions.Reconcile([Candidate(previous)]));
        await f.Actions.Activate(previous, _ => Task.FromResult(true), f.Eligible, CancellationToken.None);
        var saved = await f.Workflow.Save("example", previous, Snapshot("PRIVATE_CURRENT_BODY").Document.Text,
            false, new Review(), f.Eligible, CancellationToken.None);
        f.Catalogue.Publish(f.Revisions.Reconcile([Candidate(saved.Current!)]));
        var point = new OwnedInstructionRestorePoint(previous, saved.Current!);
        var previewCount = 0;
        var review = new Review();
        var restored = await f.Actions.Restore(point, (proposed, _) =>
        {
            previewCount++;
            proposed.CopyBytes().Should().Equal(previous.Document.CopyBytes());
            point.Current.Document.Definition.Content.Should().Be("PRIVATE_CURRENT_BODY");
            f.Files.Writes.Should().Be(1);
            return Task.FromResult(true);
        }, review, f.Eligible, CancellationToken.None);
        previewCount.Should().Be(1);
        review.Confirmations.Should().Be(1);
        review.Conflicts.Should().Be(0);
        restored.Published.Should().BeTrue();
        restored.Current!.Document.CopyBytes().Should().Equal(previous.Document.CopyBytes());
        restored.Current.Revision.Should().NotBe(previous.Revision).And.NotBe(saved.Current!.Revision);
        f.Catalogue.Publish(f.Revisions.Reconcile([Candidate(restored.Current)]));
        f.Catalogue.Artifacts.Should().BeEmpty();
        f.Audit.Events.Select(item => item.Outcome).Should().Equal(
            SecurityAuditOutcome.Requested, SecurityAuditOutcome.Succeeded,
            SecurityAuditOutcome.Requested, SecurityAuditOutcome.Succeeded,
            SecurityAuditOutcome.Requested, SecurityAuditOutcome.Succeeded);
        f.Audit.Events.Should().OnlyContain(item => !item.TargetId.Contains("PRIVATE", StringComparison.Ordinal));
        f.Files.Writes.Should().Be(2);
        var stale = () => f.Revisions.CaptureReviewEpoch(previous);
        stale.Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData("prompt-actions")]
    [InlineData("previous-kind")]
    [InlineData("current-kind")]
    [InlineData("name")]
    [InlineData("root")]
    [InlineData("previous-filename")]
    [InlineData("current-filename")]
    [InlineData("previous-legacy")]
    [InlineData("current-legacy")]
    [InlineData("same-bytes")]
    [InlineData("previous-revision")]
    [InlineData("current-revision")]
    [InlineData("previous-source")]
    [InlineData("current-source")]
    [InlineData("reserved")]
    public async Task RestorePreparationRejectsAnythingOtherThanExactCompatibleSameSourceNativeInstruction(string invalid)
    {
        await using var f = new Fixture(invalid is "prompt-actions" ? ArtifactKind.Prompt : ArtifactKind.Instruction);
        var previous = Snapshot();
        var current = Snapshot("Saved") with { FileIdentity = new string('c', 48) };
        switch (invalid)
        {
            case "previous-kind": previous = Snapshot(kind: ArtifactKind.Prompt); break;
            case "current-kind": current = Snapshot("Saved", kind: ArtifactKind.Prompt); break;
            case "name": previous = previous with { Name = "other" }; break;
            case "root": current = current with { DirectoryIdentity = new string('d', 48) }; break;
            case "previous-filename": previous = previous with { CanonicalFileName = "Other.instructions.md" }; break;
            case "current-filename":
                previous = previous with { CanonicalFileName = "EXAMPLE.instructions.md" };
                current = current with { CanonicalFileName = "EXAMPLE.instructions.md" }; break;
            case "previous-legacy": previous = Snapshot(native: false); break;
            case "current-legacy": current = Snapshot("Saved", native: false); break;
            case "same-bytes": current = previous; break;
            case "previous-revision": previous = previous with { FileIdentity = "invalid" }; break;
            case "current-revision": current = current with { DirectoryIdentity = "invalid" }; previous = previous with { DirectoryIdentity = "invalid" }; break;
            case "previous-source": previous = previous with { Document = ArtifactFileDocument.Parse(previous.Document.CopyBytes(), ArtifactKind.Instruction, "external", "example") }; break;
            case "current-source": current = current with { Document = ArtifactFileDocument.Parse(current.Document.CopyBytes(), ArtifactKind.Instruction, "external", "example") }; break;
            case "reserved":
                previous = previous with { Name = "help", CanonicalFileName = "help.instructions.md", Document = previous.Document.WithOwnedCopyName("help") };
                current = current with { Name = "help", CanonicalFileName = "help.instructions.md", Document = current.Document.WithOwnedCopyName("help") };
                break;
        }
        var prepare = () => f.Actions.PrepareRestore(new(previous, current));
        prepare.Should().Throw<InvalidDataException>();
        f.Files.Writes.Should().Be(0);
        f.Audit.Events.Should().BeEmpty();
    }

    [Theory]
    [InlineData("preview-cancel")]
    [InlineData("confirm-cancel")]
    [InlineData("missing")]
    [InlineData("identity")]
    [InlineData("root")]
    [InlineData("digest")]
    [InlineData("rename")]
    [InlineData("access")]
    [InlineData("preview-source")]
    [InlineData("confirm-source")]
    [InlineData("native-conflict")]
    [InlineData("observed-aba")]
    [InlineData("reload-failure")]
    [InlineData("eligible")]
    [InlineData("session")]
    [InlineData("write")]
    [InlineData("readback")]
    [InlineData("audit-request")]
    [InlineData("audit-terminal")]
    [InlineData("cancel")]
    public async Task RestoreRejectsStaleUnknownAndCancelledWorkWithoutHistoricalOverwriteOrAuthority(string change)
    {
        await using var f = new Fixture();
        var previous = f.Files.Current!;
        var saved = await f.Workflow.Save("example", previous, Snapshot("Saved").Document.Text,
            false, new Review(), f.Eligible, CancellationToken.None);
        var current = saved.Current!;
        f.Catalogue.Publish(f.Revisions.Reconcile([Candidate(current)]));
        var point = new OwnedInstructionRestorePoint(previous, current);
        using var cancellation = new CancellationTokenSource();
        var review = new Review { Confirmed = change is not "confirm-cancel" };
        if (change is "missing") { f.Files.Current = null; }
        if (change is "identity") { f.Files.Current = current with { FileIdentity = new string('d', 48) }; }
        if (change is "root") { f.Files.Current = current with { DirectoryIdentity = new string('d', 48) }; }
        if (change is "digest") { f.Files.Current = Snapshot("External"); }
        if (change is "rename") { f.Files.Current = current with { CanonicalFileName = "renamed.instructions.md" }; }
        if (change is "access") { f.Files.FailRead = true; }
        if (change is "write") { f.Files.FailWrite = true; }
        if (change is "native-conflict") { f.Files.BeforeNativeComparison = () => f.Files.Current = Snapshot("External at publication"); }
        if (change is "readback") { f.Files.Unconfirmed = true; }
        if (change is "audit-request" or "audit-terminal")
        {
            f.Audit.BeforeWrite = item =>
            {
                if (item.Outcome == (change is "audit-request" ? SecurityAuditOutcome.Requested : SecurityAuditOutcome.Succeeded))
                { throw new IOException("Audit unavailable"); }
            };
        }
        review.BeforeConfirm = _ =>
        {
            if (change is "confirm-source") { f.Files.Current = Snapshot("External"); }
        };
        async Task<InstructionWriteResult> Restore() => await f.Actions.Restore(point, (_, _) =>
        {
            if (change is "preview-source") { f.Files.Current = Snapshot("External"); }
            if (change is "observed-aba")
            {
                f.Catalogue.Publish(f.Revisions.Reconcile([Candidate(Snapshot("External"))]));
                f.Catalogue.Publish(f.Revisions.Reconcile([Candidate(current)]));
            }
            if (change is "reload-failure") { f.Catalogue.ReportReloadFailure(new IOException("Unavailable")); }
            if (change is "eligible") { f.Open = false; }
            if (change is "session") { f.Control.Authority = f.Control.Authority! with { IsActive = false }; }
            if (change is "cancel") { cancellation.Cancel(); }
            return Task.FromResult(change is not "preview-cancel");
        }, review, f.Eligible, cancellation.Token);
        if (change is "preview-cancel" or "confirm-cancel")
        { (await Restore()).Published.Should().BeFalse(); }
        else
        { await ((Func<Task>)(async () => await Restore())).Should().ThrowAsync<Exception>(); }
        f.Files.Writes.Should().Be(change is "readback" or "audit-terminal" ? 2 : 1);
        review.Conflicts.Should().Be(0);
        f.Catalogue.Artifacts.Should().BeEmpty();
    }
}