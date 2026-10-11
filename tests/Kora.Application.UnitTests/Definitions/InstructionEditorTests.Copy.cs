using AwesomeAssertions;

using Kora.Core.Artifacts;
using Kora.Core.Auditing;
using Kora.Core.Commands;
using Kora.Application.Definitions;

namespace Kora.Application.UnitTests.Definitions;

public sealed partial class InstructionEditorTests
{
    [Theory]
    [InlineData(ArtifactKind.Instruction)]
    [InlineData(ArtifactKind.Prompt)]
    public async Task RepeatedFailedCopiesDoNotAccumulateRevisionSlotsOrDisableOriginal(ArtifactKind kind)
    {
        await using var f = new Fixture(kind);
        var source = f.Files.Current!;
        await f.Actions.Activate(source, _ => Task.FromResult(true), f.Eligible, CancellationToken.None);
        f.Files.FailWrite = true;
        for (var index = 0; index < 129; index++)
        {
            var name = "separate-" + index;
            var operation = () => f.Workflow.SaveCopy(name, source, source.Document.Text, new Review(), f.Eligible, CancellationToken.None);
            await operation.Should().ThrowAsync<IOException>();
        }
        f.Files.FailWrite = false;
        var saved = await f.Workflow.SaveCopy("actual", source, source.Document.Text, new Review(), f.Eligible, CancellationToken.None);
        saved.Published.Should().BeTrue();
        f.Revisions.IsEnabled(source).Should().BeTrue();
        f.Catalogue.Publish(f.Revisions.Reconcile([Candidate(source), Candidate(saved.Current!)]));
        f.Catalogue.Artifacts.Should().ContainSingle().Which.Id.Should().Be(source.Document.Definition.Id);
    }

    [Theory]
    [InlineData(ArtifactKind.Instruction)]
    [InlineData(ArtifactKind.Prompt)]
    public async Task MaximumOriginalCanProduceShorterDraftCopyAndHeaderGrowthStillUsesAuthoritativeLimits(ArtifactKind kind)
    {
        await using var f = new Fixture(kind);
        var text = Snapshot(kind: kind).Document.Text;
        var original = ArtifactFileDocument.Parse(ArtifactFileDocument.Encode(text + new string('a',
            ArtifactDefinition.MaximumContentLength - System.Text.Encoding.UTF8.GetByteCount(text) - 3), true), kind, "kora-profile", "example");
        var source = new OwnedInstructionSnapshot("example", "a", "b", original);
        f.Actions.PrepareCopy("longer-destination-name", source, text).Proposed.CopyBytes().Length.Should().BeLessThan(65536);
        var header = "---\nkora-format: 1\nname: example\ndescription: Test\nattribution: ";
        var headerBytes = System.Text.Encoding.UTF8.GetByteCount(header[4..]) + 5;
        var maximumHeader = header + new string('a', 4096 - headerBytes) + "\n---\nBody";
        var document = ArtifactFileDocument.Parse(ArtifactFileDocument.Encode(maximumHeader, true), kind, "kora-profile", "example");
        document.RequireOwnedCopySource();
        source = source with { Document = document };
        f.Actions.PrepareCopy("shorter", source, maximumHeader).Proposed.Metadata["attribution"].Should().Be(document.Metadata["attribution"]);
        ((Action)(() => f.Actions.PrepareCopy("longer-name", source, maximumHeader))).Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData(ArtifactKind.Instruction)]
    [InlineData(ArtifactKind.Prompt)]
    public async Task CopyPreservesOriginalAndFrozenDraftAndIndependentDisabledIdentity(ArtifactKind kind)
    {
        await using var f = new Fixture(kind);
        var source = f.Files.Current!;
        await f.Actions.Activate(source, _ => Task.FromResult(true), f.Eligible, CancellationToken.None);
        var draft = Snapshot("Unsaved draft", kind: kind).Document.Text;
        var copy = f.Actions.PrepareCopy("separate", source, draft);
        copy.Source.Should().BeSameAs(source);
        copy.Draft.Text.Should().Be(draft);
        copy.DestinationName.Should().Be("separate");
        copy.Proposed.Text.Should().Be(draft.Replace("name: example", "name: separate", StringComparison.Ordinal));
        copy.AuditTarget.Should().StartWith("definition.owned-copy.").And.HaveLength(86);
        var saved = await f.Workflow.SaveCopy("separate", source, draft, new Review(), f.Eligible, CancellationToken.None);
        saved.Published.Should().BeTrue();
        saved.Current!.Document.CopyBytes().Should().Equal(copy.Proposed.CopyBytes());
        saved.Current.Revision.Should().NotBe(source.Revision);
        saved.Current.Document.Definition.Id.Should().NotBe(source.Document.Definition.Id);
        f.Files.Current.Should().BeSameAs(source);
        f.Revisions.IsEnabled(source).Should().BeTrue();
        f.Catalogue.Publish(f.Revisions.Reconcile([Candidate(source), Candidate(saved.Current)]));
        f.Catalogue.IsCurrent(source.Document.Definition.Id, source.Document.Definition.Digest).Should().BeTrue();
        var router = new ArtifactCommandRouter(f.Catalogue);
        var command = "/" + kind.ToString().ToLowerInvariant() + " separate";
        router.Match(command, "Kora").Invocation.Should().BeNull();
        await f.Actions.Activate(saved.Current, _ => Task.FromResult(true), f.Eligible, CancellationToken.None);
        router.Match(command, "Kora").Invocation.Should().NotBeNull();
        (await f.Actions.IsCurrent(Candidate(saved.Current), f.Eligible, CancellationToken.None)).Should().BeTrue();
        f.Files.Copies["separate"] = new("separate", "a", "b",
            ArtifactFileDocument.Parse(ArtifactFileDocument.Encode(copy.Proposed.Text + "Changed", false), kind, "kora-profile", "separate"));
        (await f.Actions.IsCurrent(Candidate(saved.Current), f.Eligible, CancellationToken.None)).Should().BeFalse();
        f.Files.Copies["separate"] = saved.Current;
        f.Catalogue.Publish(f.Revisions.Reconcile([Candidate(source), Candidate(saved.Current)]));
        router.Match(command, "Kora").Invocation.Should().BeNull();
        f.Revisions.IsEnabled(source).Should().BeTrue();
        f.Audit.Events.Should().Contain(item => string.Equals(item.ActionId, "definition." + kind.ToString().ToLowerInvariant() + ".copy", StringComparison.Ordinal)
            && string.Equals(item.TargetId, copy.AuditTarget, StringComparison.Ordinal) && item.Outcome == SecurityAuditOutcome.Succeeded);
    }

    [Theory]
    [InlineData(ArtifactKind.Instruction)]
    [InlineData(ArtifactKind.Prompt)]
    public async Task CancelCopyLeavesDraftSourceActivationAndInventoryUnchanged(ArtifactKind kind)
    {
        await using var f = new Fixture(kind);
        var source = f.Files.Current!;
        await f.Actions.Activate(source, _ => Task.FromResult(true), f.Eligible, CancellationToken.None);
        var draft = Snapshot("Unsaved", kind: kind).Document.Text;
        var result = await f.Workflow.SaveCopy("separate", source, draft, new Review { Confirmed = false }, f.Eligible, CancellationToken.None);
        result.Published.Should().BeFalse();
        f.Files.Writes.Should().Be(0);
        f.Files.Copies.Should().BeEmpty();
        f.Files.Current.Should().BeSameAs(source);
        f.Revisions.IsEnabled(source).Should().BeTrue();
        draft.Should().Be(Snapshot("Unsaved", kind: kind).Document.Text);
        f.Audit.Events.Should().NotContain(item => item.ActionId.EndsWith(".copy", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("occupied")]
    [InlineData("readback")]
    [InlineData("storage")]
    [InlineData("audit")]
    [InlineData("privacy")]
    [InlineData("session")]
    [InlineData("catalogue")]
    [InlineData("late-session")]
    public async Task CopyFailureHoldsOnlyDestinationAndDoesNotOverwriteOrRetargetSource(string failure)
    {
        await using var f = new Fixture();
        var source = f.Files.Current!;
        await f.Actions.Activate(source, _ => Task.FromResult(true), f.Eligible, CancellationToken.None);
        var proposed = f.Actions.PrepareCopy("separate", source, source.Document.Text).Proposed;
        var occupied = new OwnedInstructionSnapshot("separate", "a", "b", proposed);
        if (failure is "occupied") { f.Files.Copies["separate"] = occupied; }
        f.Files.Unconfirmed = failure is "readback";
        f.Files.FailWrite = failure is "storage";
        if (failure is "late-session") { f.Files.AfterWrite = () => f.Control.Authority = f.Control.Authority! with { IsActive = false }; }
        if (failure is "audit")
        { f.Audit.BeforeWrite = item => { if (item.ActionId.EndsWith(".copy", StringComparison.Ordinal) && item.Outcome == SecurityAuditOutcome.Succeeded) { throw new IOException("Terminal audit failed"); } }; }
        var review = new Review
        {
            BeforeConfirm = _ =>
            {
                if (failure is "privacy") { f.Open = false; }
                if (failure is "session") { f.Control.Authority = f.Control.Authority! with { IsActive = false }; }
            },
        };
        var operation = () => f.Workflow.SaveCopy("separate", source, source.Document.Text, review, f.Eligible, CancellationToken.None,
            _ => failure is not "catalogue");
        await operation.Should().ThrowAsync<Exception>();
        f.Files.Current.Should().BeSameAs(source);
        f.Revisions.IsEnabled(source).Should().BeTrue();
        f.Catalogue.IsCurrent(source.Document.Definition.Id, source.Document.Definition.Digest).Should().BeTrue();
        f.Revisions.IsEnabled(occupied).Should().BeFalse();
        if (failure is "occupied") { f.Files.Copies["separate"].Should().BeSameAs(occupied); }
        if (failure is "readback" or "audit" or "catalogue" or "late-session")
        { f.Audit.Events.Last().Outcome.Should().Be(SecurityAuditOutcome.Unknown); }
    }

    [Fact]
    public async Task CopyFreezesReviewedSourceAndNeverSelectsLaterDiskContent()
    {
        await using var f = new Fixture();
        var source = f.Files.Current!;
        var draft = Snapshot("Frozen unsaved body").Document.Text;
        var result = await f.Workflow.SaveCopy("separate", source, draft,
            new Review { BeforeConfirm = _ => f.Files.Current = Snapshot("External newer") }, f.Eligible, CancellationToken.None);
        result.Current!.Document.Definition.Content.Should().Be("Frozen unsaved body");
        f.Files.Current!.Document.Definition.Content.Should().Be("External newer");
    }

    [Theory]
    [InlineData("same")]
    [InlineData("alias")]
    [InlineData("kind")]
    [InlineData("legacy")]
    [InlineData("reserved")]
    [InlineData("metadata")]
    [InlineData("collision")]
    public async Task CopyRejectsUnsupportedOriginalOrProposalWithoutRepairingOrNormalizing(string condition)
    {
        await using var f = new Fixture();
        var source = Snapshot(native: condition is not "legacy", kind: condition is "kind" ? ArtifactKind.Prompt : ArtifactKind.Instruction);
        if (condition is "alias") { source = source with { CanonicalFileName = "Example.instructions.md" }; }
        if (condition is "collision")
        { f.Catalogue.Publish([Candidate(source), ArtifactFileDocument.Parse(ArtifactFileDocument.Encode("---\nname: separate\ndescription: Test\n---\nBody", false), ArtifactKind.Instruction, "additional", "other").Definition]); }
        var draft = source.Document.Text;
        if (condition is "metadata") { draft = draft.Replace("description:", "unknown:", StringComparison.Ordinal); }
        var operation = () => f.Actions.PrepareCopy(condition is "same" ? "example" : condition is "reserved" ? "settings" : "separate", source, draft);
        operation.Should().Throw<Exception>();
        source.Document.Text.Should().NotContain("name: separate");
        f.Files.Writes.Should().Be(0);
    }

    [Fact]
    public async Task CopyApprovalCannotAuthorizeDifferentMutationInputs()
    {
        await using var f = new Fixture();
        var source = f.Files.Current!;
        var copy = f.Actions.PrepareCopy("separate", source, source.Document.Text);
        foreach (var condition in new[] { "base", "proposal", "name" })
        {
            var operation = () => f.Actions.Save(condition is "name" ? "other" : "separate",
                condition is "base" ? source : null, condition is "proposal" ? source.Document : copy.Proposed,
                _ => Task.FromResult(true), f.Eligible, CancellationToken.None, copy);
            await operation.Should().ThrowAsync<InvalidDataException>();
        }
        f.Files.Writes.Should().Be(0);
    }
}
