using AwesomeAssertions;

using Kora.Application.Configuration;
using Kora.Application.Definitions;
using Kora.Application.UnitTests.Configuration;
using Kora.Core.Artifacts;
using Kora.Core.Auditing;
using Kora.Core.Customizations;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Application.UnitTests.Definitions;

public sealed partial class InstructionEditorTests
{
    [Theory]
    [InlineData(CustomizationKind.Instruction, RequestOrigin.LocalUi)]
    [InlineData(CustomizationKind.Prompt, RequestOrigin.ActivatedVoice)]
    [InlineData(CustomizationKind.Agent, RequestOrigin.LocalUi)]
    public async Task SharedDeletionHoldsOnlyTheReviewedIdentityAndRetiresCapturedUse(CustomizationKind kind, RequestOrigin origin)
    {
        await using var f = new Fixture();
        using var input = HostActivity.BeginRoot(HostRequest.Create(origin), HostActivityLayer.Desktop, HostOperation.Request);
        var files = new CustomizationFiles(kind);
        var state = new LocalSharedSkillPreferencesTests.Store();
        var use = new ArtifactUsePreferences(state);
        var document = ArtifactFileDocument.Parse(files.Current.Files.Single().CopyBytes(),
            CustomizationLayout.ArtifactKind(kind), "kora-profile", "example");
        var selected = document.Definition with { UseControlId = files.Source.Id, PreservesCapturedUse = true };
        var unrelated = new ArtifactDefinition("unrelated", selected.Kind, "Unrelated", "Unrelated guidance",
            "unrelated", ["unrelated"], selected.Source, "1", selected.Digest, "Unrelated guidance")
        { UseControlId = "file.unrelated", PreservesCapturedUse = true };
        using var catalogue = new ArtifactCatalogue([selected, unrelated], use);
        use.SetDisabled(files.Source.Id, false, selected.Digest);
        use.SetDisabled("file.unrelated", false, unrelated.Digest);
        var service = new CustomizationManagementService(files, f.Admission, use, catalogue, f.Audit);
        catalogue.IsCapturedRevisionCurrent(selected.Id, selected.Digest).Should().BeTrue();
        files.AfterWrite = () => use.IsDisabled(files.Source.Id).Should().BeTrue("the hold must precede the irreversible effect");
        var result = await service.Delete(files.Current, _ => Task.FromResult(true), f.Eligible, TestContext.Current.CancellationToken);
        result.Should().Be(CustomizationDeletionOutcome.Deleted);
        files.Writes.Should().Be(1);
        catalogue.IsCapturedRevisionCurrent(selected.Id, selected.Digest).Should().BeFalse();
        catalogue.IsCapturedRevisionCurrent(unrelated.Id, unrelated.Digest).Should().BeTrue();
        new ArtifactUsePreferences(state).IsDisabled(files.Source.Id).Should().BeTrue();
        use.IsRevisionAllowed(files.Source.Id, selected.Digest).Should().BeFalse();
        catalogue.Publish([unrelated]);
        catalogue.Artifacts.Should().ContainSingle().Which.Id.Should().Be("unrelated");
        catalogue.Publish([selected, unrelated]);
        catalogue.IsCapturedRevisionCurrent(selected.Id, selected.Digest).Should().BeFalse("restoring bytes is not renewed authority");
        f.Audit.Events.Select(entry => entry.Outcome).Should().Equal(SecurityAuditOutcome.Requested, SecurityAuditOutcome.Succeeded);
        f.Audit.Events[0].Initiator.Should().Be(origin == RequestOrigin.ActivatedVoice
            ? SecurityAuditInitiator.VoiceCommand : SecurityAuditInitiator.LocalUser);
        f.Audit.Events.Should().OnlyContain(entry => entry.TargetId == files.Source.Id);
    }

    [Theory]
    [InlineData(CustomizationKind.Instruction, "publish")]
    [InlineData(CustomizationKind.Prompt, "publish")]
    [InlineData(CustomizationKind.Agent, "publish")]
    [InlineData(CustomizationKind.Instruction, "reload-failure")]
    [InlineData(CustomizationKind.Prompt, "reload-failure")]
    [InlineData(CustomizationKind.Agent, "reload-failure")]
    [InlineData(CustomizationKind.Instruction, "recovered")]
    [InlineData(CustomizationKind.Instruction, "audit-publish")]
    [InlineData(CustomizationKind.Instruction, "hold-publish")]
    public async Task CatalogueInvalidationAfterReviewCannotCommitDeletion(CustomizationKind kind, string invalidation)
    {
        await using var f = new Fixture();
        var files = new CustomizationFiles(kind);
        var reviewed = files.Current;
        var use = new ArtifactUsePreferences(new LocalSharedSkillPreferencesTests.Store());
        var service = new CustomizationManagementService(files, f.Admission, use, f.Catalogue, f.Audit);
        var observed = f.Catalogue.SourceCandidates;
        void Invalidate()
        {
            if (invalidation is "reload-failure" or "recovered")
            { f.Catalogue.ReportReloadFailure(new IOException("Synthetic reload failure")); }
            if (invalidation is not "reload-failure") { f.Catalogue.Publish(observed); }
        }
        if (invalidation is "audit-publish")
        {
            f.Audit.BeforeWrite = entry =>
            { if (entry.Outcome is SecurityAuditOutcome.Requested) { Invalidate(); } };
        }
        else if (invalidation is "hold-publish") { use.Changed += (_, _) => Invalidate(); }
        else { files.BeforeCommit = Invalidate; }
        var operation = () => service.Delete(reviewed, _ => Task.FromResult(true), f.Eligible,
            TestContext.Current.CancellationToken);
        await operation.Should().ThrowAsync<InvalidOperationException>();
        files.Writes.Should().Be(0);
        files.Current.Should().BeSameAs(reviewed);
        use.IsDisabled(files.Source.Id).Should().Be(invalidation is not "audit-publish");
        f.Audit.Events.Select(entry => entry.Outcome).Should().Equal(SecurityAuditOutcome.Requested, SecurityAuditOutcome.Failed);
        f.Audit.Events.Should().NotContain(entry => entry.Outcome == SecurityAuditOutcome.Succeeded);
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("changed")]
    [InlineData("source")]
    [InlineData("privacy")]
    [InlineData("control")]
    [InlineData("catalogue")]
    [InlineData("reload-failure")]
    [InlineData("already-unavailable")]
    [InlineData("before-commit")]
    [InlineData("cancel-commit")]
    [InlineData("storage")]
    [InlineData("audit-request")]
    [InlineData("audit-failure")]
    public async Task UnconfirmedOrStaleDeletionNeverReportsSuccessOrDeletesAnotherSource(string failure)
    {
        await using var f = new Fixture();
        var files = new CustomizationFiles();
        var use = new ArtifactUsePreferences(new LocalSharedSkillPreferencesTests.Store());
        var service = new CustomizationManagementService(files, f.Admission, use, f.Catalogue, f.Audit);
        if (failure is "already-unavailable") { f.Catalogue.ReportReloadFailure(new IOException("Unavailable")); }
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        if (failure is "before-commit") { files.BeforeCommit = () => f.Open = false; }
        if (failure is "cancel-commit") { files.BeforeCommit = cancellation.Cancel; }
        if (failure is "storage" or "audit-failure") { files.WriteFailure = new IOException("No effect"); }
        f.Audit.BeforeWrite = entry =>
        {
            if (failure is "audit-request" && entry.Outcome == SecurityAuditOutcome.Requested
                || failure is "audit-failure" && entry.Outcome == SecurityAuditOutcome.Failed)
            { throw new IOException("Audit unavailable"); }
        };
        var operation = () => service.Delete(files.Current, _ =>
        {
            if (failure is "changed") { files.Current = new(files.Source, "changed", files.Current.Files); }
            if (failure is "source") { files.Current = new(files.Source with { Id = "other" }, files.Current.Revision, files.Current.Files); }
            if (failure is "privacy") { f.Open = false; }
            if (failure is "control") { use.SetDisabled("unrelated", true); }
            if (failure is "catalogue") { f.Catalogue.Publish([Snapshot("new").Document.Definition]); }
            if (failure is "reload-failure") { f.Catalogue.ReportReloadFailure(new IOException("Reload failed")); }
            return Task.FromResult(failure is not "cancel");
        }, f.Eligible, cancellation.Token);
        if (failure is "cancel") { (await operation()).Should().Be(CustomizationDeletionOutcome.Cancelled); }
        else { await operation.Should().ThrowAsync<Exception>(); }
        files.Writes.Should().Be(0);
        f.Audit.Events.Should().NotContain(entry => entry.Outcome == SecurityAuditOutcome.Succeeded);
    }

    [Theory]
    [InlineData("native")]
    [InlineData("terminal-admission")]
    [InlineData("terminal-audit")]
    [InlineData("both-audits")]
    [InlineData("host-terminal")]
    public async Task EffectedDeletionHasTypedUncertaintyAndNeverClaimsRollback(string failure)
    {
        await using var f = new Fixture();
        var files = new CustomizationFiles();
        var use = new ArtifactUsePreferences(new LocalSharedSkillPreferencesTests.Store());
        var service = new CustomizationManagementService(files, f.Admission, use, f.Catalogue, f.Audit);
        files.AfterWrite = () =>
        {
            if (failure is "terminal-admission") { f.Open = false; }
            if (failure is "native") { throw new CustomizationDeletionUnconfirmedException(new IOException("Readback unavailable")); }
        };
        f.Audit.BeforeWrite = entry =>
        {
            if (entry.Outcome == SecurityAuditOutcome.Succeeded && failure is "terminal-audit" or "both-audits"
                || entry.Outcome == SecurityAuditOutcome.Unknown && failure is "both-audits")
            { throw new IOException("Terminal audit unavailable"); }
        };
        if (failure is "host-terminal") { files.AfterWrite = () => f.Control.FailTerminal = true; }
        var operation = () => service.Delete(files.Current, _ => Task.FromResult(true), f.Eligible, TestContext.Current.CancellationToken);
        await operation.Should().ThrowAsync<CustomizationDeletionUnconfirmedException>();
        files.Writes.Should().Be(1);
        use.IsDisabled(files.Source.Id).Should().BeTrue();
        if (failure is not "host-terminal")
        { f.Audit.Events.Should().NotContain(entry => entry.Outcome == SecurityAuditOutcome.Succeeded); }
    }
}
