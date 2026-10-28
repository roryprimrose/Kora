using AwesomeAssertions;

using Kora.Application.Configuration;
using Kora.Application.Definitions;
using Kora.Application.UnitTests.Configuration;
using Kora.Core.Artifacts;
using Kora.Core.Auditing;
using Kora.Core.Customizations;

namespace Kora.Application.UnitTests.Definitions;

public sealed partial class InstructionEditorTests
{
    [Theory]
    [InlineData("success")]
    [InlineData("cancel")]
    [InlineData("revoked")]
    [InlineData("register-failure")]
    [InlineData("late-revocation")]
    [InlineData("audit-failure")]
    public async Task OriginalRegistrationReviewsExactScopeWithoutReadingOrWritingContent(string state)
    {
        await using var f = new Fixture();
        var files = new CustomizationFiles(scope: CustomizationScope.User);
        var use = new ArtifactUsePreferences(new LocalSharedSkillPreferencesTests.Store());
        var service = new CustomizationManagementService(files, f.Admission, use, f.Catalogue, f.Audit);
        if (state is "register-failure") { files.BeforeRegister = () => throw new IOException("Registration unavailable"); }
        if (state is "late-revocation") { files.BeforeRegister = () => f.Open = false; }
        if (state is "audit-failure")
        {
            f.Audit.BeforeWrite = entry =>
            { if (entry.Outcome == SecurityAuditOutcome.Succeeded) { throw new IOException("Unconfirmed audit"); } };
        }
        var action = () => service.RegisterInstruction(files.Source, (proposal, _) =>
        {
            proposal.Source.Should().Be(files.Source);
            proposal.NativeRevision.Should().Be("native-revision");
            if (state is "revoked") { f.Open = false; }
            return Task.FromResult(state is not "cancel");
        }, f.Eligible, TestContext.Current.CancellationToken);
        if (state is "success" or "cancel")
        {
            (await action()).Should().Be(state is "success");
            service.IsInstructionRegistered(files.Source).Should().Be(state is "success");
            if (state is "success")
            {
                f.Audit.Events.Select(entry => entry.Outcome).Should().Equal(SecurityAuditOutcome.Requested, SecurityAuditOutcome.Succeeded);
                service.RetireInstructionRegistration(files.Source);
                service.IsInstructionRegistered(files.Source).Should().BeFalse();
            }
        }
        else
        {
            await action.Should().ThrowAsync<Exception>();
            service.IsInstructionRegistered(files.Source).Should().BeFalse();
            f.Audit.Events.Should().NotContain(entry => entry.Outcome == SecurityAuditOutcome.Succeeded);
        }
        files.Reads.Should().Be(0);
        files.Writes.Should().Be(0);
        use.IsDisabled(files.Source.Id).Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WithdrawalRetiresRegistrationWithoutTouchingOriginalEvenWhenAuditFails(bool auditFailure)
    {
        await using var f = new Fixture();
        var files = new CustomizationFiles { Registered = true };
        var service = new CustomizationManagementService(files, f.Admission,
            new ArtifactUsePreferences(new LocalSharedSkillPreferencesTests.Store()), f.Catalogue, f.Audit);
        if (auditFailure) { f.Audit.BeforeWrite = _ => throw new IOException("Audit unavailable"); }
        var action = () => service.WithdrawInstruction(files.Source, f.Eligible, TestContext.Current.CancellationToken);
        if (auditFailure) { await action.Should().ThrowAsync<IOException>(); }
        else { await action(); f.Audit.Events.Last().Outcome.Should().Be(SecurityAuditOutcome.Succeeded); }
        files.Registered.Should().BeFalse();
        files.Reads.Should().Be(0);
        files.Writes.Should().Be(0);
    }

    [Theory]
    [InlineData("unchanged")]
    [InlineData("changed")]
    [InlineData("replaced")]
    [InlineData("foreign")]
    [InlineData("revoked")]
    [InlineData("new")]
    public async Task OriginalSavePreservesUnchangedUseAndHoldsOnlyChangedOrUncertainMutations(string state)
    {
        await using var f = new Fixture();
        var files = new CustomizationFiles(scope: CustomizationScope.User) { Registered = true };
        var use = new ArtifactUsePreferences(new LocalSharedSkillPreferencesTests.Store());
        var service = new CustomizationManagementService(files, f.Admission, use, f.Catalogue, f.Audit);
        var original = files.Current;
        var changed = original.Files.Select(file => file.WithText(file.Text + "\nChanged.")).ToArray();
        if (state is "replaced") { files.Current = new(files.Source, "replaced", files.Current.Files); }
        if (state is "foreign") { files.Current = new(files.Source with { Id = "file.other" }, original.Revision, original.Files); }
        if (state is "revoked") { files.AfterRead = () => f.Open = false; }
        var action = () => service.Save(files.Source, state is "new" ? null : original,
            state is "changed" or "new" ? changed : original.Files, _ => Task.FromResult(true),
            f.Eligible, TestContext.Current.CancellationToken);
        if (state is "replaced" or "foreign" or "revoked")
        {
            await action.Should().ThrowAsync<InvalidOperationException>();
            files.Writes.Should().Be(0);
            use.IsDisabled(files.Source.Id).Should().BeFalse();
            f.Audit.Events.Should().BeEmpty();
        }

        else
        {
            (await action()).Should().NotBeNull();
            files.Writes.Should().Be(state is "unchanged" ? 0 : 1);
            use.IsDisabled(files.Source.Id).Should().Be(state is not "unchanged");
            if (state is "unchanged") { f.Audit.Events.Should().BeEmpty(); }
            else { f.Audit.Events.Last().Outcome.Should().Be(SecurityAuditOutcome.Succeeded); }
        }
    }

    [Fact]
    public async Task DetectedOriginalConflictAuditsKnownNoPublicationRatherThanUncertainSuccess()
    {
        await using var f = new Fixture();
        var files = new CustomizationFiles(scope: CustomizationScope.User)
        { Registered = true, WriteFailure = new InstructionOriginalConflictException() };
        var use = new ArtifactUsePreferences(new LocalSharedSkillPreferencesTests.Store());
        var service = new CustomizationManagementService(files, f.Admission, use, f.Catalogue, f.Audit);
        var original = files.Current;
        var action = () => service.Save(files.Source, original,
            [original.Files.Single().WithText(original.Files.Single().Text + "\nChanged.")], _ => Task.FromResult(true),
            f.Eligible, TestContext.Current.CancellationToken);
        await action.Should().ThrowAsync<InstructionOriginalConflictException>();
        files.Writes.Should().Be(0);
        use.IsDisabled(files.Source.Id).Should().BeTrue();
        f.Audit.Events.Last().Outcome.Should().Be(SecurityAuditOutcome.Failed);
        f.Audit.Events.Last().ReasonCode.Should().Be("base-changed-not-published");
    }

    [Theory]
    [InlineData(CustomizationKind.Instruction)]
    [InlineData(CustomizationKind.Prompt)]
    [InlineData(CustomizationKind.Agent)]
    [InlineData(CustomizationKind.Skill)]
    public void UserValidationRetainsExistingKindsButOriginalInstructionsRequireSupportedNativeFormat(CustomizationKind kind)
    {
        var files = new CustomizationFiles(kind, CustomizationScope.User);
        CustomizationManagementService.ValidatePrimary(files.Source, files.Current.Files);
        if (kind != CustomizationKind.Instruction) { return; }
        foreach (var replacement in new[] { "kora-format: 99", "hooks: executable", "applyTo: **", "kora-format: 2" })
        {
            var malformed = files.Current.Files.Single().WithText(
                files.Current.Files.Single().Text.Replace("kora-format: 1", replacement, StringComparison.Ordinal));
            ((Action)(() => CustomizationManagementService.ValidatePrimary(files.Source, [malformed])))
                .Should().Throw<InvalidDataException>();
        }
    }
}