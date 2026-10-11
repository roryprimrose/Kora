using AwesomeAssertions;

using Kora.Application.Definitions;
using Kora.Core.Artifacts;
using Kora.Core.Auditing;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Application.UnitTests.Definitions;

public sealed partial class InstructionEditorTests
{
    [Theory]
    [InlineData(ArtifactKind.Instruction, RequestOrigin.LocalUi)]
    [InlineData(ArtifactKind.Prompt, RequestOrigin.ActivatedVoice)]
    public async Task ExactDisableWithdrawsEveryNewLookupWithoutWritingAndFreshReviewIsIndependent(ArtifactKind kind, RequestOrigin origin)
    {
        await using var f = new Fixture(kind);
        using var input = HostActivity.BeginRoot(HostRequest.Create(origin), HostActivityLayer.Desktop, HostOperation.Request);
        var saved = f.Files.Current!;
        var bundled = new ArtifactDefinition("bundled.test", ArtifactKind.Prompt, "Bundled", "Test", "test",
            ["test"], "bundled", "1", new string('a', 64), "Bundled content");
        f.Catalogue.Publish([.. f.Catalogue.Candidates, bundled]);
        await f.Actions.Activate(saved, _ => Task.FromResult(true), f.Eligible, CancellationToken.None);
        var captured = Candidate(saved);
        var router = new ArtifactCommandRouter(f.Catalogue);
        var command = "/" + kind.ToString().ToLowerInvariant() + " example";
        router.Match(command, "Kora").Invocation.Should().NotBeNull();
        var auditCount = f.Audit.Events.Count;
        f.Audit.BeforeWrite = entry =>
        {
            var host = HostActivity.RequireCurrent();
            entry.CorrelationId.Should().Be(host.Request.RequestId.Value);
            host.Request.SessionId.Should().NotBeNull();
            host.Activity!.TraceId.ToString().Should().NotBe(new string('0', 32));
            host.Activity.Tags.Should().NotContain(tag => tag.Value != null && tag.Value.Contains(saved.Name, StringComparison.Ordinal));
            host.Activity.Baggage.Should().BeEmpty();
        };
        var result = await f.Actions.Disable(saved, _ => Task.FromResult(true), f.Eligible, CancellationToken.None);
        result.Cancelled.Should().BeFalse();
        result.WasEnabled.Should().BeTrue();
        result.Reviewed.Should().BeSameAs(saved);
        var events = f.Audit.Events.Skip(auditCount).ToArray();
        events.Select(entry => entry.Outcome).Should().Equal(SecurityAuditOutcome.Requested, SecurityAuditOutcome.Succeeded);
        events.Should().OnlyContain(entry => entry.CorrelationId == result.ControlReceipt
            && entry.ActionId == "definition." + kind.ToString().ToLowerInvariant() + ".disable-use");
        events[0].TargetId.Should().StartWith("definition.owned-use.").And.NotContain("example");
        events[0].Initiator.Should().Be(origin == RequestOrigin.ActivatedVoice
            ? SecurityAuditInitiator.VoiceCommand : SecurityAuditInitiator.LocalUser);
        f.Actions.IsReviewedUseEnabled(saved).Should().BeFalse();
        f.Catalogue.Artifacts.Should().Equal(bundled);
        f.Catalogue.Candidates.Should().Contain(item => item.OwnedInstructionName == "example" && !item.IsUseEnabled);
        f.Catalogue.IsCapturedRevisionCurrent(captured.Id, captured.Digest).Should().BeTrue();
        router.Match(command, "Kora").Invocation.Should().BeNull();
        router.Match("/example", "Kora").Invocation.Should().BeNull();
        router.Match("use example", "Kora").Invocation.Should().BeNull();
        router.Match("Kora, use example", "Kora").Invocation.Should().BeNull();
        (await f.Actions.IsCurrent(captured, f.Eligible, CancellationToken.None)).Should().BeFalse();
        f.Catalogue.IsCapturedRevisionCurrent(captured.Id, captured.Digest).Should().BeTrue();
        (await f.Actions.Inspect("example", f.Eligible, CancellationToken.None)).Should().BeSameAs(saved);
        (await f.Actions.List(f.Eligible, CancellationToken.None)).Should().Equal("example");
        f.Files.Writes.Should().Be(0);
        f.Files.Current.Should().BeSameAs(saved);
        var repeated = await f.Actions.Disable(saved, _ => Task.FromResult(true), f.Eligible, CancellationToken.None);
        repeated.WasEnabled.Should().BeFalse();
        f.Audit.Events.Last().ReasonCode.Should().Be("exact-revision-already-held");
        f.Catalogue.Publish(f.Revisions.Reconcile(f.Catalogue.Candidates));
        router.Match(command, "Kora").Invocation.Should().BeNull();
        new InstructionRevisionCatalogue(f.State, kind).Reconcile(f.Catalogue.Candidates)
            .Single(item => item.OwnedInstructionName != null).IsUseEnabled.Should().BeFalse();
        await f.Actions.Activate(saved, _ => Task.FromResult(true), f.Eligible, CancellationToken.None);
        router.Match(command, "Kora").Invocation.Should().NotBeNull();
        f.Actions.IsReviewedUseEnabled(saved).Should().BeTrue();
        f.Files.Writes.Should().Be(0);
    }

    [Theory]
    [InlineData(ArtifactKind.Instruction)]
    [InlineData(ArtifactKind.Prompt)]
    public async Task CancelledDisablePreservesEnabledStateAndSourceAndHasCancelledReceipt(ArtifactKind kind)
    {
        await using var f = new Fixture(kind);
        var original = f.Files.Current!;
        await f.Actions.Activate(original, _ => Task.FromResult(true), f.Eligible, CancellationToken.None);
        var state = f.State.Text;
        var result = await f.Actions.Disable(original, _ => Task.FromResult(false), f.Eligible, CancellationToken.None);
        result.Cancelled.Should().BeTrue();
        f.Actions.IsReviewedUseEnabled(original).Should().BeTrue();
        f.State.Text.Should().Be(state);
        f.Files.Writes.Should().Be(0);
        f.Audit.Events.Last().Outcome.Should().Be(SecurityAuditOutcome.Cancelled);
    }

    [Theory]
    [InlineData("digest")]
    [InlineData("file")]
    [InlineData("directory")]
    [InlineData("missing")]
    [InlineData("source-read")]
    [InlineData("audit-request")]
    [InlineData("audit-terminal")]
    [InlineData("state-write")]
    [InlineData("state-read")]
    [InlineData("state-readback")]
    [InlineData("state-missing")]
    [InlineData("state-malformed")]
    [InlineData("privacy")]
    [InlineData("cancel-after-confirm")]
    [InlineData("cancel-after-revoke")]
    [InlineData("session")]
    [InlineData("generation")]
    [InlineData("terminal")]
    public async Task DisableFailuresAreExplicitHeldAndNeverWriteOrClaimCompletedUseRevocation(string failure)
    {
        await using var f = new Fixture();
        var original = f.Files.Current!;
        await f.Actions.Activate(original, _ => Task.FromResult(true), f.Eligible, CancellationToken.None);
        using var cancelled = new CancellationTokenSource();
        f.Audit.BeforeWrite = entry =>
        {
            if (!entry.ActionId.EndsWith("disable-use", StringComparison.Ordinal)) { return; }
            if (failure is "audit-request" && entry.Outcome == SecurityAuditOutcome.Requested
                || failure is "audit-terminal" && entry.Outcome == SecurityAuditOutcome.Succeeded)
            { throw new IOException("Mandatory audit unavailable"); }
            if (failure is "cancel-after-revoke" && entry.Outcome == SecurityAuditOutcome.Succeeded) { cancelled.Cancel(); }
        };
        var disable = () => f.Actions.Disable(original, _ =>
        {
            switch (failure)
            {
                case "digest": f.Files.Current = Snapshot("Changed"); break;
                case "file": f.Files.Current = original with { FileIdentity = new string('c', 48) }; break;
                case "directory": f.Files.Current = original with { DirectoryIdentity = new string('c', 48) }; break;
                case "missing": f.Files.Current = null; break;
                case "source-read": f.Files.FailRead = true; break;
                case "state-write": f.State.FailWrite = true; break;
                case "state-read": f.State.FailRead = true; break;
                case "state-readback": f.State.AfterWrite = () => f.State.Text = "{\"Version\":1,\"Revisions\":{}}"; break;
                case "state-missing": f.State.Text = null; break;
                case "state-malformed": f.State.Text = "invalid"; break;
                case "privacy": f.Open = false; break;
                case "cancel-after-confirm": cancelled.Cancel(); break;
                case "session": f.Control.Authority = f.Control.Authority! with { IsActive = false }; break;
                case "generation": f.Control.Authority = f.Control.Authority! with { Generation = new(f.Control.Authority.Generation.Value + 1) }; break;
                case "terminal": f.Control.FailTerminal = true; break;
            }
            return Task.FromResult(true);
        }, f.Eligible, cancelled.Token);
        var thrown = await disable.Should().ThrowAsync<InstructionUseRevocationUnconfirmedException>();
        thrown.Which.ControlReceipt.Should().NotBeEmpty();
        if (failure is "cancel-after-revoke") { thrown.Which.RevocationObserved.Should().BeTrue(); }
        f.Catalogue.Artifacts.Should().BeEmpty();
        f.Files.Writes.Should().Be(0);
        if (failure is not "audit-request" and not "terminal" and not "cancel-after-revoke")
        { f.Audit.Events.Last().Outcome.Should().Be(SecurityAuditOutcome.Unknown); }
    }

    [Fact]
    public async Task CancellationBeforeConfirmationDoesNotInventRevocationAndUnreviewedKindsAreRejected()
    {
        await using var f = new Fixture();
        var original = f.Files.Current!;
        await f.Actions.Activate(original, _ => Task.FromResult(true), f.Eligible, CancellationToken.None);
        var cancel = () => f.Actions.Disable(original, _ => throw new OperationCanceledException(),
            f.Eligible, CancellationToken.None);
        await cancel.Should().ThrowAsync<OperationCanceledException>();
        f.Actions.IsReviewedUseEnabled(original).Should().BeTrue();
        f.Audit.Events.Last().Outcome.Should().Be(SecurityAuditOutcome.Cancelled);
        foreach (var invalid in new[] { Snapshot(native: false), Snapshot(kind: ArtifactKind.Prompt),
            original with { CanonicalFileName = "other.instructions.md" } })
        {
            var disable = () => f.Actions.Disable(invalid, _ => Task.FromResult(true), f.Eligible, CancellationToken.None);
            await disable.Should().ThrowAsync<InvalidDataException>();
        }
        f.Actions.IsReviewedUseEnabled(Snapshot(kind: ArtifactKind.Prompt)).Should().BeFalse();
    }

    [Fact]
    public async Task OldDisableAndActivationReceiptsCannotRetargetAReplacementRevision()
    {
        await using var f = new Fixture();
        var old = f.Files.Current!;
        await f.Actions.Activate(old, _ => Task.FromResult(true), f.Eligible, CancellationToken.None);
        var changed = Snapshot("Changed");
        f.Files.Current = changed;
        f.Catalogue.Publish(f.Revisions.Reconcile([Candidate(changed)]));
        await f.Actions.Activate(changed, _ => Task.FromResult(true), f.Eligible, CancellationToken.None);
        var stale = () => f.Actions.Disable(old, _ => Task.FromResult(true), f.Eligible, CancellationToken.None);
        await stale.Should().ThrowAsync<InvalidOperationException>();
        var activation = () => f.Actions.Activate(old, _ => Task.FromResult(true), f.Eligible, CancellationToken.None);
        await activation.Should().ThrowAsync<InvalidOperationException>();
        (await f.Actions.IsCurrent(Candidate(old), f.Eligible, CancellationToken.None)).Should().BeFalse();
        f.Actions.IsReviewedUseEnabled(changed).Should().BeTrue();
        f.Files.Writes.Should().Be(0);
    }

    [Fact]
    public async Task DisableFencesPreparedActivationAndUnchangedReloadBeforeCompletion()
    {
        await using var f = new Fixture();
        var original = f.Files.Current!;
        await f.Actions.Activate(original, _ => Task.FromResult(true), f.Eligible, CancellationToken.None);
        var epoch = f.Revisions.CaptureReviewEpoch(original);
        var receipt = f.Revisions.PrepareActivation(original, epoch);
        await f.Actions.Disable(original, _ => Task.FromResult(true), f.Eligible, CancellationToken.None);
        f.Revisions.PublishReconciled([Candidate(original)], f.Catalogue.Publish);
        ((Action)(() => f.Revisions.CompleteActivation(original, receipt, f.Catalogue))).Should().Throw<InvalidOperationException>();
        ((Action)(() => f.Revisions.PrepareActivation(original, epoch))).Should().Throw<InvalidOperationException>();
        f.Catalogue.Artifacts.Should().BeEmpty();
        var current = f.Revisions.CaptureReviewEpoch(original);
        var fresh = f.Revisions.PrepareActivation(original, current);
        ((Action)(() => f.Revisions.CompleteActivation(original, receipt, f.Catalogue))).Should().Throw<InvalidOperationException>();
        f.Revisions.CompleteActivation(original, fresh, f.Catalogue);
        f.Actions.IsReviewedUseEnabled(original).Should().BeTrue();
    }

    [Fact]
    public async Task QueuedOldActivationCannotPrepareAfterSuccessfulDisable()
    {
        await using var f = new Fixture();
        var saved = f.Files.Current!;
        await f.Actions.Activate(saved, _ => Task.FromResult(true), f.Eligible, CancellationToken.None);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var confirmed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var disabling = f.Actions.Disable(saved, _ => { entered.SetResult(); return confirmed.Task; }, f.Eligible, CancellationToken.None);
        await entered.Task;
        var activating = f.Actions.Activate(saved, _ => Task.FromResult(true), f.Eligible, CancellationToken.None);
        confirmed.SetResult(true);
        (await disabling).WasEnabled.Should().BeTrue();
        await ((Func<Task>)(async () => await activating)).Should().ThrowAsync<InvalidOperationException>();
        f.Catalogue.Artifacts.Should().BeEmpty();
        f.State.Text.Should().Contain("\"example\":null");
    }

    [Theory]
    [InlineData(ArtifactKind.Instruction)]
    [InlineData(ArtifactKind.Prompt)]
    public async Task DisabledOriginalCopiesRemainNewDisabledRevisionsWithoutInheritedAuthority(ArtifactKind kind)
    {
        await using var f = new Fixture(kind);
        var original = f.Files.Current!;
        await f.Actions.Activate(original, _ => Task.FromResult(true), f.Eligible, CancellationToken.None);
        await f.Actions.Disable(original, _ => Task.FromResult(true), f.Eligible, CancellationToken.None);
        var copied = await f.Workflow.SaveCopy("new-copy", original, original.Document.Text,
            new Review(), f.Eligible, CancellationToken.None,
            saved =>
            {
                f.Revisions.PublishReconciled([.. f.Catalogue.Candidates, Candidate(saved)], f.Catalogue.Publish);
                return true;
            });
        copied.Published.Should().BeTrue();
        f.Catalogue.Artifacts.Should().BeEmpty();
        f.Files.Current.Should().BeSameAs(original);
        f.Files.Writes.Should().Be(1);
        (await f.Actions.Activate(copied.Current!, _ => Task.FromResult(true), f.Eligible, CancellationToken.None)).Should().BeTrue();
        f.Catalogue.Artifacts.Should().ContainSingle().Which.OwnedInstructionName.Should().Be("new-copy");
        f.Actions.IsReviewedUseEnabled(original).Should().BeFalse();
    }

    [Fact]
    public async Task ReplacementDuringDisableReviewDoesNotWithdrawFreshReplacementUse()
    {
        await using var f = new Fixture();
        var old = f.Files.Current!;
        await f.Actions.Activate(old, _ => Task.FromResult(true), f.Eligible, CancellationToken.None);
        var replacement = Snapshot("New current content");
        var disable = () => f.Actions.Disable(old, _ =>
        {
            f.Files.Current = replacement;
            f.Revisions.PublishReconciled([Candidate(replacement)], f.Catalogue.Publish);
            var receipt = f.Revisions.PrepareActivation(replacement);
            f.Revisions.CompleteActivation(replacement, receipt, f.Catalogue);
            return Task.FromResult(true);
        }, f.Eligible, CancellationToken.None);
        await disable.Should().ThrowAsync<InvalidOperationException>();
        f.Actions.IsReviewedUseEnabled(replacement).Should().BeTrue();
        f.Audit.Events.Last().Outcome.Should().Be(SecurityAuditOutcome.Failed);
    }

    [Fact]
    public async Task DisabledCapturedUseCannotIgnoreLaterNativeSourceReadLoss()
    {
        await using var f = new Fixture();
        var saved = f.Files.Current!;
        await f.Actions.Activate(saved, _ => Task.FromResult(true), f.Eligible, CancellationToken.None);
        await f.Actions.Disable(saved, _ => Task.FromResult(true), f.Eligible, CancellationToken.None);
        f.Catalogue.IsCapturedRevisionCurrent(saved.Document.Definition.Id, saved.Document.Definition.Digest).Should().BeTrue();
        f.Files.FailRead = true;
        (await f.Actions.IsCurrent(Candidate(saved), f.Eligible, CancellationToken.None)).Should().BeFalse();
        f.Catalogue.IsCapturedRevisionCurrent(saved.Document.Definition.Id, saved.Document.Definition.Digest).Should().BeFalse();
    }
}
