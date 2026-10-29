using AwesomeAssertions;

using Kora.Application.Configuration;
using Kora.Application.Definitions;
using Kora.Application.UnitTests.Configuration;
using Kora.Core.Auditing;
using Kora.Core.Customizations;
using Kora.Core.Artifacts;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.Definitions;

public sealed partial class InstructionEditorTests
{
    [Theory]
    [InlineData("existing")]
    [InlineData("new")]
    [InlineData("revision")]
    [InlineData("source")]
    [InlineData("destination")]
    [InlineData("withdrawal")]
    public async Task Editor_clarification_rechecks_original_source_without_writes(string state)
    {
        await using var f = new Fixture();
        var files = new CustomizationFiles();
        var expected = files.Current;
        var source = files.Source;
        var service = new CustomizationManagementService(files, f.Admission,
            new ArtifactUsePreferences(new LocalSharedSkillPreferencesTests.Store()), f.Catalogue, f.Audit);
        if (state is "revision") { files.Current = new(source, "newer", expected.Files); }
        if (state is "source") { files.Current = new(source with { Id = "file.other" }, expected.Revision, expected.Files); }
        if (state is "destination") { source = source with { Name = "different" }; }
        if (state is "withdrawal") { files.AfterRead = () => f.Open = false; }
        var operation = () => service.RequireUnchangedSource(source, state is "new" or "destination" ? null : expected,
            f.Eligible, TestContext.Current.CancellationToken);
        if (state is "existing" or "new") { await operation(); }
        else { await operation.Should().ThrowAsync<InvalidOperationException>(); }
        files.Writes.Should().Be(0);
        f.Audit.Events.Should().BeEmpty();
    }

    [Theory]
    [InlineData(ArtifactKind.Instruction)]
    [InlineData(ArtifactKind.Prompt)]
    public async Task Policy_enabled_owned_inventory_and_validation_remain_current_until_use_changes(ArtifactKind kind)
    {
        var use = new ArtifactUsePreferences(new LocalSharedSkillPreferencesTests.Store());
        await using var f = new Fixture(kind, use);
        var inventory = await f.Actions.ObserveInventory(["example"], f.Eligible, TestContext.Current.CancellationToken);
        f.Actions.IsInventoryCurrent(inventory).Should().BeTrue();
        var validation = await f.Actions.ValidateDraft("example", f.Files.Current, f.Files.Current!.Document.Text,
            NullLogger.Instance, f.Eligible, TestContext.Current.CancellationToken);
        f.Actions.IsDraftValidationCurrent(validation).Should().BeTrue();
        use.SetDisabled(f.Catalogue.Candidates.Single().Id, true);
        f.Actions.IsInventoryCurrent(inventory).Should().BeFalse();
        f.Actions.IsDraftValidationCurrent(validation).Should().BeFalse();
    }

    [Fact]
    public async Task Policy_enabled_parameter_prompt_can_preview_and_use_but_cannot_survive_disable_reenable()
    {
        var use = new ArtifactUsePreferences(new LocalSharedSkillPreferencesTests.Store());
        await using var f = new Fixture(ArtifactKind.Prompt, use);
        using var draft = await ParameterDraft(f);
        draft.CanUse.Should().BeTrue();
        (await draft.CaptureUse((candidate, token) => f.Actions.IsCurrent(candidate, f.Eligible, token),
            TestContext.Current.CancellationToken)).UserRequest.Should().Contain("private-value");
        use.SetDisabled(draft.Source.Id, true);
        use.SetDisabled(draft.Source.Id, false);
        draft.IsCurrent.Should().BeFalse();
    }

    [Theory]
    [InlineData(CustomizationKind.Instruction)]
    [InlineData(CustomizationKind.Prompt)]
    [InlineData(CustomizationKind.Skill)]
    [InlineData(CustomizationKind.Agent)]
    [InlineData(CustomizationKind.McpServer)]
    public async Task Customization_management_saves_exact_reviewed_source_disabled_and_audits_the_actual_result(CustomizationKind kind)
    {
        await using var f = new Fixture();
        var files = new CustomizationFiles(kind);
        var state = new LocalSharedSkillPreferencesTests.Store();
        var use = new ArtifactUsePreferences(state);
        var service = new CustomizationManagementService(files, f.Admission, use, f.Catalogue, f.Audit,
            new ArtifactDirectoryConfigurationService(new CustomizationMcpPreferences(), f.Audit));
        var changes = 0;
        void Changed(object? sender, EventArgs args) => changes++;
        service.UseChanged += Changed;
        (await service.List(f.Eligible, CancellationToken.None)).Should().ContainSingle().Which.Should().Be(files.Source);
        (await service.Create(kind, CustomizationScope.Kora, "example", f.Eligible, CancellationToken.None)).Should().Be(files.Source);
        (await service.Read(files.Source, false, f.Eligible, CancellationToken.None)).Should().BeSameAs(files.Current);
        (await service.Read(files.Source, true, f.Eligible, CancellationToken.None)).RecoveryDetails.Should().Be("Recovery");
        (await service.Save(files.Source, files.Current, files.Current.Files, _ => Task.FromResult(false),
            f.Eligible, CancellationToken.None)).Should().BeNull();
        files.Writes.Should().Be(0);
        f.Audit.Events.Should().BeEmpty();
        var saved = await service.Save(files.Source, files.Current, files.Current.Files, _ => Task.FromResult(true),
            f.Eligible, CancellationToken.None);
        saved.Should().BeSameAs(files.Current);
        files.Writes.Should().Be(1);
        new ArtifactUsePreferences(state).IsDisabled(files.Source.Id).Should().BeTrue();
        f.Audit.Events.Select(entry => entry.Outcome).Should().Equal(SecurityAuditOutcome.Requested, SecurityAuditOutcome.Succeeded);
        (await service.SetDisabled(files.Source, false, (review, _) =>
        {
            review.Should().BeSameAs(files.Current);
            return Task.FromResult(true);
        }, f.Eligible, CancellationToken.None)).Should().BeTrue();
        service.IsDisabled(files.Source).Should().BeFalse();
        if (kind != CustomizationKind.McpServer)
        {
            use.IsRevisionAllowed(files.Source.Id, files.Current.Files.Single().Digest).Should().BeTrue();
            new ArtifactUsePreferences(state).IsRevisionAllowed(files.Source.Id, files.Current.Files.Single().Digest).Should().BeTrue();
        }
        (await service.SetDisabled(files.Source, true, (review, _) =>
        {
            review.Should().BeNull("disabling does not need a content disclosure");
            return Task.FromResult(true);
        }, f.Eligible, CancellationToken.None)).Should().BeTrue();
        changes.Should().Be(3);
        service.UseChanged -= Changed;
        service.ObservedArtifact(files.Source).Should().BeNull();
    }

    [Theory]
    [InlineData("source")]
    [InlineData("control")]
    [InlineData("session")]
    [InlineData("cancel")]
    [InlineData("audit")]
    public async Task Customization_enable_rechecks_exact_source_control_session_and_audit(string failure)
    {
        await using var f = new Fixture();
        var files = new CustomizationFiles();
        var use = new ArtifactUsePreferences(new LocalSharedSkillPreferencesTests.Store());
        use.SetDisabled(files.Source.Id, true);
        var service = new CustomizationManagementService(files, f.Admission, use, f.Catalogue, f.Audit);
        if (failure is "audit")
        {
            f.Audit.BeforeWrite = entry =>
            { if (entry.Outcome == SecurityAuditOutcome.Succeeded) { throw new IOException("Audit failure"); } };
        }
        var action = () => service.SetDisabled(files.Source, false, (_, _) =>
        {
            switch (failure)
            {
                case "source": files.Current = new(files.Source, "changed", files.Current.Files); break;
                case "control": use.SetDisabled("another", true); break;
                case "session": f.Open = false; break;
            }
            return Task.FromResult(failure is not "cancel");
        }, f.Eligible, CancellationToken.None);
        if (failure is "cancel") { (await action()).Should().BeFalse(); }
        else { await action.Should().ThrowAsync<Exception>(); }
        use.IsDisabled(files.Source.Id).Should().BeTrue();
        f.Audit.Events.Should().NotContain(entry => entry.Outcome == SecurityAuditOutcome.Succeeded);
        files.Writes.Should().Be(0);
    }

    [Theory]
    [InlineData("session")]
    [InlineData("storage")]
    [InlineData("after-write")]
    [InlineData("cancel")]
    public async Task Customization_failed_or_revoked_save_never_reports_success(string failure)
    {
        await using var f = new Fixture();
        var files = new CustomizationFiles { FailWrite = failure is "storage" };
        var use = new ArtifactUsePreferences(new LocalSharedSkillPreferencesTests.Store());
        var service = new CustomizationManagementService(files, f.Admission, use, f.Catalogue, f.Audit);
        using var cancellation = new CancellationTokenSource();
        if (failure is "after-write") { files.AfterWrite = () => f.Open = false; }
        var action = () => service.Save(files.Source, files.Current, files.Current.Files, _ =>
        {
            if (failure is "session") { f.Open = false; }
            if (failure is "cancel") { cancellation.Cancel(); }
            return Task.FromResult(true);
        }, f.Eligible, cancellation.Token);
        await action.Should().ThrowAsync<Exception>();
        f.Audit.Events.Should().NotContain(entry => entry.Outcome == SecurityAuditOutcome.Succeeded);
        files.Writes.Should().Be(failure is "after-write" ? 1 : 0);
        if (failure is "storage" or "after-write")
        { use.IsDisabled(files.Source.Id).Should().BeTrue(); f.Audit.Events.Last().Outcome.Should().Be(SecurityAuditOutcome.Unknown); }
    }

    [Fact]
    public async Task Customization_builtin_disable_and_enable_do_not_read_or_modify_binary_resources()
    {
        await using var f = new Fixture();
        var files = new CustomizationFiles();
        var use = new ArtifactUsePreferences(new LocalSharedSkillPreferencesTests.Store());
        var service = new CustomizationManagementService(files, f.Admission, use, f.Catalogue, f.Audit);
        var builtin = files.Source with { Scope = CustomizationScope.BuiltIn, IsReadOnly = true };
        foreach (var disabled in new[] { true, false })
        {
            (await service.SetDisabled(builtin, disabled, (review, _) => Task.FromResult(review is null),
            f.Eligible, CancellationToken.None)).Should().BeTrue();
        }
        files.Reads.Should().Be(0);
        files.Writes.Should().Be(0);
    }

    [Fact]
    public async Task Customization_inspection_revalidates_after_read_and_observes_exact_mapping_only()
    {
        await using var f = new Fixture();
        var files = new CustomizationFiles();
        var service = new CustomizationManagementService(files, f.Admission,
            new(new LocalSharedSkillPreferencesTests.Store()), f.Catalogue, f.Audit);
        var original = f.Catalogue.Candidates.Single();
        service.ObservedArtifact(files.Source with { Id = original.Id }).Should().BeSameAs(original);
        f.Catalogue.Publish([original with { UseControlId = files.Source.Id }]);
        service.ObservedArtifact(files.Source).Should().NotBeNull();
        files.AfterRead = () => f.Open = false;
        await service.Invoking(value => value.Read(files.Source, false, f.Eligible, CancellationToken.None))
            .Should().ThrowAsync<InvalidOperationException>();
        var user = files.Source with { Scope = CustomizationScope.User };
        CustomizationManagementService.ValidatePrimary(user, files.Current.Files);
        files.Writes.Should().Be(0);
    }

    private sealed class CustomizationFiles : ICustomizationStore
    {
        internal readonly CustomizationSource Source;
        internal CustomizationSnapshot Current;
        internal int Writes;
        internal int Reads;
        internal bool FailWrite;
        internal Exception? WriteFailure;
        internal Action? AfterWrite;
        internal Action? AfterRead;
        internal bool Registered;
        internal Action? BeforeRegister;
        internal CustomizationFiles(CustomizationKind kind = CustomizationKind.Instruction, CustomizationScope scope = CustomizationScope.Kora)
        {
            Source = new(kind == CustomizationKind.McpServer
                ? ArtifactUseIdentity.Mcp(ArtifactUseIdentity.McpServerId("source", "example")) : "file.example",
                kind, scope, "example", "source", CustomizationLayout.PrimaryFile(kind, "example"));
            Current = new(Source, "original", [new(Source.PrimaryFile,
                Kora.Core.Artifacts.ArtifactFileDocument.Encode(CustomizationLayout.Template(kind, "example"), false))]);
        }
        public Task<InstructionOriginalRegistration> PrepareInstructionRegistration(CustomizationSource source, CancellationToken token) =>
            Task.FromResult(new InstructionOriginalRegistration(source, "native-revision"));
        public Task RegisterInstruction(InstructionOriginalRegistration proposal, Func<bool> eligible, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (!eligible()) { throw new InvalidOperationException("Closed"); }
            Registered = true;
            BeforeRegister?.Invoke();
            return Task.CompletedTask;
        }
        public void WithdrawInstruction(CustomizationSource source) => Registered = false;
        public bool IsInstructionRegistered(CustomizationSource source) => Registered;
        public Task<IReadOnlyList<CustomizationSource>> List(CancellationToken token) =>
            Task.FromResult<IReadOnlyList<CustomizationSource>>([Source]);
        public Task<CustomizationSource> Create(CustomizationKind kind, CustomizationScope scope, string name, CancellationToken token) =>
            Task.FromResult(Source);
        public Task<CustomizationSnapshot> Read(CustomizationSource source, CancellationToken token)
        { Reads++; AfterRead?.Invoke(); return Task.FromResult(Current); }
        public Task<CustomizationSnapshot> ReadRecovery(CustomizationSource source, CancellationToken token) =>
            Task.FromResult(new CustomizationSnapshot(Source, Current.Revision, Current.Files, "Recovery"));
        public Task<CustomizationSnapshot> Save(CustomizationSource source, CustomizationSnapshot? expected,
            IReadOnlyList<CustomizationFile> files, Func<bool> eligible,
            Func<Func<CustomizationSnapshot>, Task<CustomizationSnapshot>> commit, CancellationToken token) => commit(() =>
        {
            token.ThrowIfCancellationRequested();
            if (!eligible()) { throw new InvalidOperationException("Closed"); }
            if (WriteFailure is { } failure) { throw failure; }
            if (FailWrite) { throw new IOException("Publication failed"); }
            Writes++;
            Current = new(source, "saved", files);
            AfterWrite?.Invoke();
            return Current;
        });
    }
}