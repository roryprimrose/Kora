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
    [InlineData("saved")]
    [InlineData("review-cancel")]
    [InlineData("confirm-cancel")]
    [InlineData("source-review")]
    [InlineData("source-confirm")]
    [InlineData("source-commit")]
    [InlineData("destination")]
    [InlineData("draft")]
    [InlineData("owner")]
    [InlineData("cancel")]
    [InlineData("expired")]
    [InlineData("clock-back")]
    [InlineData("catalogue")]
    [InlineData("collision")]
    [InlineData("uncertain")]
    public async Task SkillAdaptationUsesExistingPublicationWithExactSourceAndSeparateOneUseReview(string state)
    {
        await using var f = new Fixture();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var files = new CustomizationFiles(CustomizationKind.Skill, CustomizationScope.User);
        var destination = files.Source with { Id = "file.adapted", Scope = CustomizationScope.Kora, Name = "adapted", Location = "owned-source" };
        files.CreatedSource = destination;
        files.Current = new(files.Source, "original-revision", [new("SKILL.md",
            ArtifactFileDocument.Encode("---\nname: example\nversion: 1.0.0\ndescription: Review prose\n---\nOriginal author credit.\nPlain guidance.", false))]);
        var original = files.Current;
        var use = new ArtifactUsePreferences(new LocalSharedSkillPreferencesTests.Store());
        var service = new CustomizationManagementService(files, f.Admission, use, f.Catalogue, f.Audit);
        var adaptation = await service.PrepareSkillAdaptation(original, "adapted", f.Eligible, cancellation.Token);
        adaptation.Original.Should().BeSameAs(original);
        var time = new AdaptationTime();
        var reviews = 0;
        var confirmations = 0;
        var draftCurrent = true;
        void ChangeSource() => files.Current = new(files.Source, "changed", original.Files);
        if (state is "source-commit") { files.BeforeCommit = ChangeSource; }
        if (state is "uncertain") { files.FailWrite = true; }
        Task<bool> Review(CancellationToken token)
        {
            reviews++;
            files.Writes.Should().Be(0);
            if (state is "source-review") { ChangeSource(); }
            if (state is "expired") { time.Now = time.Now.AddMinutes(2); }
            if (state is "clock-back") { time.Now = time.Now.AddSeconds(-1); }
            return Task.FromResult(state is not "review-cancel");
        }
        Task<bool> Confirm(CancellationToken token)
        {
            confirmations++;
            reviews.Should().Be(1);
            files.Writes.Should().Be(0);
            switch (state)
            {
                case "source-confirm": ChangeSource(); break;
                case "destination": files.CreatedSource = destination with { Location = "other-destination" }; break;
                case "draft": draftCurrent = false; break;
                case "owner": f.Open = false; break;
                case "cancel": cancellation.Cancel(); break;
                case "catalogue": f.Catalogue.ReportReloadFailure(new IOException("Synthetic reload failure")); break;
                case "collision": f.Catalogue.Publish([adaptation.ValidateDraft([adaptation.Proposed]).Definition]); break;
            }
            return Task.FromResult(state is not "confirm-cancel");
        }
        var operation = () => service.SaveSkillAdaptation(adaptation, [adaptation.Proposed], Review, Confirm,
            () => f.Eligible() && draftCurrent, time, cancellation.Token);
        if (state is "saved")
        {
            var saved = await operation();
            saved!.Files.Single().Text.Should().Be(adaptation.Proposed.Text);
            saved.Source.Should().Be(destination);
            files.CopySource.Should().BeSameAs(original);
            reviews.Should().Be(1);
            confirmations.Should().Be(1);
            use.IsDisabled(destination.Id).Should().BeTrue();
            use.IsRevisionAllowed(destination.Id, adaptation.Proposed.Digest).Should().BeFalse();
            original.Files.Single().Text.Should().Contain("name: example").And.NotContain("attribution:");
            f.Audit.Events.Select(entry => entry.Outcome).Should().Equal(SecurityAuditOutcome.Requested, SecurityAuditOutcome.Succeeded);
        }
        else if (state is "review-cancel" or "confirm-cancel")
        { (await operation()).Should().BeNull(); }
        else { await operation.Should().ThrowAsync<Exception>(); }
        if (state is not "saved")
        {
            files.Writes.Should().Be(0);
            f.Audit.Events.Should().NotContain(entry => entry.Outcome == SecurityAuditOutcome.Succeeded);
        }
        if (state is "uncertain")
        {
            use.IsDisabled(destination.Id).Should().BeTrue();
            f.Audit.Events.Last().Outcome.Should().Be(SecurityAuditOutcome.Unknown);
        }
    }

    [Theory]
    [InlineData("reserved")]
    [InlineData("unavailable")]
    [InlineData("changed")]
    public async Task SkillAdaptationPreparationRefusesUnpublishableOrStaleSources(string state)
    {
        await using var f = new Fixture();
        var files = new CustomizationFiles(CustomizationKind.Skill, CustomizationScope.User);
        files.Current = new(files.Source, "original", [new("SKILL.md",
            ArtifactFileDocument.Encode("---\nname: example\nversion: 1.0.0\ndescription: Review prose\n---\nGuidance.", false))]);
        var original = files.Current;
        files.CreatedSource = files.Source with { Id = "file.adapted", Scope = CustomizationScope.Kora, Name = state is "reserved" ? "help" : "adapted" };
        if (state is "unavailable") { f.Catalogue.ReportReloadFailure(new IOException("Synthetic unavailable catalogue")); }
        if (state is "changed") { files.Current = new(files.Source, "changed", original.Files); }
        var service = new CustomizationManagementService(files, f.Admission,
            new ArtifactUsePreferences(new LocalSharedSkillPreferencesTests.Store()), f.Catalogue, f.Audit);
        var operation = () => service.PrepareSkillAdaptation(original, files.CreatedSource.Name, f.Eligible, TestContext.Current.CancellationToken);
        await operation.Should().ThrowAsync<Exception>();
        files.Writes.Should().Be(0);
        f.Audit.Events.Should().BeEmpty();
    }

    private sealed class AdaptationTime : TimeProvider
    {
        internal DateTimeOffset Now { get; set; } = new(2026, 10, 29, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
