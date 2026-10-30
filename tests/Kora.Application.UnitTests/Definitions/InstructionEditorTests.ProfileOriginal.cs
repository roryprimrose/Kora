using AwesomeAssertions;

using Kora.Application.Configuration;
using Kora.Application.Definitions;
using Kora.Application.UnitTests.Configuration;
using Kora.Core.Artifacts;
using Kora.Core.Customizations;

namespace Kora.Application.UnitTests.Definitions;

public sealed partial class InstructionEditorTests
{
    [Theory]
    [InlineData(false, "\n")]
    [InlineData(true, "\r\n")]
    [InlineData(true, "\r")]
    public async Task ProfileEditorOpensOnlyExistingRegistrationAndSeparatelySavesExactBytesDisabled(bool bom, string newline)
    {
        await using var f = new Fixture();
        var files = new CustomizationFiles(scope: CustomizationScope.User);
        var use = new ArtifactUsePreferences(new LocalSharedSkillPreferencesTests.Store());
        var service = new CustomizationManagementService(files, f.Admission, use, f.Catalogue, f.Audit);
        var workflow = new ProfileInstructionEditorWorkflow(service);
        workflow.RegisteredInstruction.Should().BeNull();
        files.Reads.Should().Be(0);
        files.Writes.Should().Be(0);
        files.Registered = true;
        var registration = workflow.RegisteredInstruction!;
        workflow.IsCurrent(registration).Should().BeTrue();
        var text = "---\nname: 'example'\nkora-format: 1\ndescription: \"Original description\"\nattribution: 'Author'\n---\n# Heading\nExact body.\n"
            .Replace("\n", newline, StringComparison.Ordinal);
        files.Current = new(files.Source, "original", [new(files.Source.PrimaryFile, ArtifactFileDocument.Encode(text, bom))]);
        var original = await workflow.Open(registration, f.Eligible, TestContext.Current.CancellationToken);
        original.Files.Single().Text.Should().Be(text);
        files.Writes.Should().Be(0);
        var unchanged = await workflow.Save(registration, original, text, new ProfileOriginalReview(), f.Eligible, TestContext.Current.CancellationToken);
        unchanged.Saved.Should().BeTrue();
        files.Writes.Should().Be(0);
        use.IsDisabled(files.Source.Id).Should().BeFalse();
        var draft = text.Replace("Exact body.", "Changed body.", StringComparison.Ordinal);
        var review = new ProfileOriginalReview();
        var outcome = await workflow.Save(registration, original, draft, review, f.Eligible, TestContext.Current.CancellationToken);
        outcome.Saved.Should().BeTrue();
        outcome.Reloaded.Should().BeFalse();
        outcome.Current.Files.Single().CopyBytes().Should().Equal(ArtifactFileDocument.Encode(draft, bom));
        review.Originals.Should().ContainSingle().Which.Should().BeSameAs(original);
        review.Proposals.Single().Text.Should().Be(draft);
        review.Proposals.Single().HasBom.Should().Be(bom);
        files.Writes.Should().Be(1);
        use.IsDisabled(files.Source.Id).Should().BeTrue();
        f.Catalogue.Artifacts.Should().BeEmpty();
    }

    [Theory]
    [InlineData(InstructionConflictChoice.Cancel)]
    [InlineData(InstructionConflictChoice.Reload)]
    [InlineData(InstructionConflictChoice.Overwrite)]
    public async Task ProfileOriginalContentConflictRequiresExplicitChoiceAndFreshCompleteOverwriteReview(InstructionConflictChoice choice)
    {
        await using var f = new Fixture();
        var files = new CustomizationFiles(scope: CustomizationScope.User) { Registered = true, DetectOriginalConflicts = true };
        var workflow = ProfileWorkflow(f, files);
        var registration = workflow.RegisteredInstruction!;
        var original = await workflow.Open(registration, f.Eligible, TestContext.Current.CancellationToken);
        var draft = original.Files.Single().Text + "\nRetained draft.";
        var external = new CustomizationSnapshot(files.Source, "external",
            [original.Files.Single().WithText(original.Files.Single().Text + "\nExternal.")]);
        files.Current = external;
        var review = new ProfileOriginalReview { Choice = choice };
        var result = await workflow.Save(registration, original, draft, review, f.Eligible, TestContext.Current.CancellationToken);
        review.Conflicts.Should().Be(1);
        result.Saved.Should().Be(choice == InstructionConflictChoice.Overwrite);
        result.Reloaded.Should().Be(choice == InstructionConflictChoice.Reload);
        if (choice == InstructionConflictChoice.Overwrite)
        {
            review.Originals.Should().Equal(original, external);
            review.Proposals.Should().OnlyContain(document => string.Equals(document.Text, draft, StringComparison.Ordinal));
            files.Current.Files.Single().Text.Should().Be(draft);
        }
        else
        {
            files.Writes.Should().Be(0);
            result.Current.Should().BeSameAs(choice == InstructionConflictChoice.Reload ? external : original);
            files.Current.Should().BeSameAs(external);
        }
    }

    [Fact]
    public async Task ProfileOriginalRepeatedConflictDoesNotReuseOverwriteApproval()
    {
        await using var f = new Fixture();
        var files = new CustomizationFiles(scope: CustomizationScope.User) { Registered = true, DetectOriginalConflicts = true };
        var workflow = ProfileWorkflow(f, files);
        var registration = workflow.RegisteredInstruction!;
        var original = files.Current;
        var attempts = 0;
        files.BeforeSave = () =>
        {
            if (++attempts <= 2)
            { files.Current = new(files.Source, "external" + attempts, [original.Files.Single().WithText(original.Files.Single().Text + "\nExternal " + attempts)]); }
        };
        var review = new ProfileOriginalReview { Choice = InstructionConflictChoice.Overwrite };
        var result = await workflow.Save(registration, original, original.Files.Single().Text + "\nDraft.", review, f.Eligible, TestContext.Current.CancellationToken);
        result.Saved.Should().BeTrue();
        review.Conflicts.Should().Be(2);
        review.Originals.Should().HaveCount(3);
        files.Writes.Should().Be(1);
    }

    [Fact]
    public async Task ProfileOriginalUnchangedDraftStillOffersDetectedContentConflictWithoutWriting()
    {
        await using var f = new Fixture();
        var files = new CustomizationFiles(scope: CustomizationScope.User) { Registered = true };
        var workflow = ProfileWorkflow(f, files);
        var original = files.Current;
        files.Current = new(files.Source, "external", [original.Files.Single().WithText(original.Files.Single().Text + "\nExternal.")]);
        var review = new ProfileOriginalReview();
        var result = await workflow.Save(workflow.RegisteredInstruction!, original, original.Files.Single().Text,
            review, f.Eligible, TestContext.Current.CancellationToken);
        result.Saved.Should().BeFalse();
        review.Conflicts.Should().Be(1);
        files.Writes.Should().Be(0);
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("ineligible")]
    [InlineData("withdrawn")]
    [InlineData("renewed")]
    [InlineData("late-read-withdrawal")]
    [InlineData("late-read-renewal")]
    public async Task ProfileOriginalOpeningDoesNotPromoteStaleConsentOrReplacedRegistration(string state)
    {
        await using var f = new Fixture();
        var files = new CustomizationFiles(scope: CustomizationScope.User) { Registered = true };
        var workflow = ProfileWorkflow(f, files);
        var registration = workflow.RegisteredInstruction!;
        using var cancellation = new CancellationTokenSource();
        if (state is "cancel") { cancellation.Cancel(); }
        if (state is "ineligible") { f.Open = false; }
        if (state is "withdrawn") { files.Registered = false; }
        if (state is "renewed") { files.Registration = registration with { }; }
        if (state is "late-read-withdrawal") { files.AfterRead = () => files.Registered = false; }
        if (state is "late-read-renewal") { files.AfterRead = () => files.Registration = registration with { }; }
        await ((Func<Task>)(() => workflow.Open(registration, f.Eligible, cancellation.Token))).Should().ThrowAsync<Exception>();
        files.Reads.Should().Be(state.StartsWith("late-", StringComparison.Ordinal) ? 1 : 0);
        files.Writes.Should().Be(0);
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("withdraw")]
    [InlineData("renew")]
    [InlineData("input")]
    [InlineData("conflict-withdraw")]
    [InlineData("conflict-cancel")]
    [InlineData("uncertain")]
    public async Task ProfileOriginalSaveRetainsDraftAndForbidsUnauthorizedOrUnconfirmedSuccess(string state)
    {
        await using var f = new Fixture();
        var files = new CustomizationFiles(scope: CustomizationScope.User) { Registered = true };
        var use = new ArtifactUsePreferences(new LocalSharedSkillPreferencesTests.Store());
        var workflow = new ProfileInstructionEditorWorkflow(new(files, f.Admission, use, f.Catalogue, f.Audit));
        var registration = workflow.RegisteredInstruction!;
        var original = files.Current;
        var draft = original.Files.Single().Text + "\nDraft.";
        using var cancellation = new CancellationTokenSource();
        var review = new ProfileOriginalReview { Confirmed = state is not "cancel" };
        review.BeforeConfirm = () =>
        {
            if (state is "withdraw") { files.Registered = false; }
            if (state is "renew") { files.Registration = registration with { }; }
            if (state is "input") { f.Open = false; }
        };
        if (state.StartsWith("conflict-", StringComparison.Ordinal))
        {
            files.WriteFailure = new InstructionOriginalConflictException();
            review.BeforeConflict = () =>
            {
                if (state is "conflict-withdraw") { files.Registered = false; }
                else { cancellation.Cancel(); }
            };
        }
        if (state is "uncertain") { files.WriteFailure = new InstructionPublicationUnconfirmedException(false, new IOException("Unconfirmed")); }
        var operation = () => workflow.Save(registration, original, draft, review, f.Eligible, cancellation.Token);
        if (state is "cancel")
        {
            var result = await operation();
            result.Saved.Should().BeFalse();
            result.Current.Should().BeSameAs(original);
            use.IsDisabled(files.Source.Id).Should().BeFalse();
        }
        else { await operation.Should().ThrowAsync<Exception>(); }
        files.Writes.Should().Be(0);
        files.Current.Should().BeSameAs(original);
        review.Proposals.Should().OnlyContain(document => string.Equals(document.Text, draft, StringComparison.Ordinal));
        f.Audit.Events.Should().NotContain(entry => entry.Outcome == Kora.Core.Auditing.SecurityAuditOutcome.Succeeded);
    }

    [Theory]
    [InlineData("kind")]
    [InlineData("scope")]
    [InlineData("source")]
    [InlineData("recovery")]
    [InlineData("metadata")]
    public async Task ProfileOriginalEditorRefusesForeignAndUnsupportedCapturedSourcesWithoutRewriting(string state)
    {
        await using var f = new Fixture();
        var files = new CustomizationFiles(scope: CustomizationScope.User) { Registered = true };
        var workflow = ProfileWorkflow(f, files);
        var registration = workflow.RegisteredInstruction!;
        if (state is "kind") { files.Registration = registration with { Source = files.Source with { Kind = CustomizationKind.Agent } }; }
        if (state is "scope") { files.Registration = registration with { Source = files.Source with { Scope = CustomizationScope.Kora } }; }
        if (state is "source") { files.Current = new(files.Source with { Id = "foreign" }, "original", files.Current.Files); }
        if (state is "recovery") { files.Current = new(files.Source, "original", files.Current.Files, "not original recovery"); }
        if (state is "metadata")
        { files.Current = new(files.Source, "original", [files.Current.Files.Single().WithText("---\napplyTo: **\n---\nUnknown original")]); }
        await ((Func<Task>)(() => workflow.Open(workflow.RegisteredInstruction!, f.Eligible, TestContext.Current.CancellationToken)))
            .Should().ThrowAsync<InvalidDataException>();
        files.Writes.Should().Be(0);
    }

    [Theory]
    [InlineData("applyTo: **\nkora-format: 1")]
    [InlineData("hooks: run\nkora-format: 1")]
    [InlineData("kora-format: 2")]
    public async Task ProfileOriginalDraftCannotDropUnknownMetadataOrUpgradeAnUnsupportedFormat(string header)
    {
        await using var f = new Fixture();
        var files = new CustomizationFiles(scope: CustomizationScope.User) { Registered = true };
        var workflow = ProfileWorkflow(f, files);
        var draft = files.Current.Files.Single().Text.Replace("kora-format: 1", header, StringComparison.Ordinal);
        var review = new ProfileOriginalReview();
        await ((Func<Task>)(() => workflow.Save(workflow.RegisteredInstruction!, files.Current, draft, review,
            f.Eligible, TestContext.Current.CancellationToken))).Should().ThrowAsync<InvalidDataException>();
        review.Originals.Should().BeEmpty();
        files.Reads.Should().Be(0);
        files.Writes.Should().Be(0);
    }

    [Fact]
    public async Task ProfileOriginalPreparationRejectsMissingHostSourceWithoutReadingOrWriting()
    {
        await using var f = new Fixture();
        var files = new CustomizationFiles(scope: CustomizationScope.User);
        var registration = new InstructionOriginalRegistration(null!, "invalid");
        ((Action)(() => ProfileInstructionEditorWorkflow.PrepareSave(registration, files.Current, files.Current.Files.Single().Text)))
            .Should().Throw<InvalidDataException>();
        files.Reads.Should().Be(0);
        files.Writes.Should().Be(0);
    }

    private static ProfileInstructionEditorWorkflow ProfileWorkflow(Fixture f, CustomizationFiles files) =>
        new(new(files, f.Admission, new ArtifactUsePreferences(new LocalSharedSkillPreferencesTests.Store()), f.Catalogue, f.Audit));

    private sealed class ProfileOriginalReview : IProfileInstructionMutationReview
    {
        internal bool Confirmed = true;
        internal InstructionConflictChoice Choice = InstructionConflictChoice.Cancel;
        internal Action? BeforeConfirm;
        internal Action? BeforeConflict;
        internal int Conflicts;
        internal List<CustomizationSnapshot> Originals { get; } = [];
        internal List<ArtifactFileDocument> Proposals { get; } = [];
        public Task<bool> Confirm(CustomizationSnapshot expected, ArtifactFileDocument proposed, CancellationToken token)
        { Originals.Add(expected); Proposals.Add(proposed); BeforeConfirm?.Invoke(); return Task.FromResult(Confirmed); }
        public Task<InstructionConflictChoice> Conflict(CustomizationSnapshot observed, ArtifactFileDocument proposed, CancellationToken token)
        { Conflicts++; BeforeConflict?.Invoke(); return Task.FromResult(Choice); }
    }
}
