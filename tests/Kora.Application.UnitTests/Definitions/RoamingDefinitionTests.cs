using System.Text.Json;

using AwesomeAssertions;

using Kora.Application.Configuration;
using Kora.Application.UnitTests.Configuration;
using Kora.Core.Artifacts;

namespace Kora.Application.UnitTests.Definitions;

public sealed class RoamingDefinitionTests
{
    [Theory]
    [InlineData(ArtifactKind.Skill)]
    [InlineData(ArtifactKind.Agent)]
    [InlineData(ArtifactKind.Instruction)]
    [InlineData(ArtifactKind.Prompt)]
    public void FileContentNeedsExactLocalReviewAndNeverCarriesApprovalToAnotherMachine(ArtifactKind kind)
    {
        var state = new LocalSharedSkillPreferencesTests.Store();
        var use = new ArtifactUsePreferences(state);
        var definition = Definition(kind);
        using var catalogue = new ArtifactCatalogue([definition], use);
        catalogue.Artifacts.Should().BeEmpty();
        catalogue.Candidates.Single().IsUseDisabled.Should().BeFalse("unreviewed is not an explicit exclusion");
        catalogue.SourceCandidates.Single().Should().BeSameAs(definition);
        use.SetDisabled(definition.UseControlId!, false, definition.Digest);
        catalogue.IsCurrent(definition.Id, definition.Digest).Should().BeTrue();
        use.IsRevisionAllowed("another", definition.Digest).Should().BeFalse();
        new ArtifactUsePreferences(state).IsRevisionAllowed(definition.UseControlId!, definition.Digest).Should().BeTrue();
        using var otherMachine = new ArtifactCatalogue([definition],
            new ArtifactUsePreferences(new LocalSharedSkillPreferencesTests.Store()));
        otherMachine.Artifacts.Should().BeEmpty();
        use.Reconcile([definition]);
        state.Writes.Should().Be(1, "an unchanged reload must not rewrite local approval");
        var changed = new ArtifactDefinition(definition.Id, kind, definition.Name, definition.Description,
            definition.CommandName, definition.SpokenNames, definition.Source, "2", new string('b', 64), "Changed")
            { UseControlId = definition.UseControlId };
        catalogue.Publish([changed]);
        catalogue.Artifacts.Should().BeEmpty();
        use.Reconcile([changed]);
        catalogue.Publish([definition]);
        catalogue.Artifacts.Should().BeEmpty("restored bytes cannot revive a retired review");
        new ArtifactUsePreferences(state).IsRevisionAllowed(definition.UseControlId!, definition.Digest).Should().BeFalse();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DeletionOrUncertainPublicationRetiresPersistedReview(bool deleted)
    {
        var state = new LocalSharedSkillPreferencesTests.Store();
        var use = new ArtifactUsePreferences(state);
        var definition = Definition(ArtifactKind.Skill);
        use.SetDisabled(definition.UseControlId!, false, definition.Digest);
        if (!deleted) { state.FailWrite = true; }
        Action reconcile = () => use.Reconcile([]);
        if (deleted) { reconcile(); }
        else
        {
            reconcile.Should().Throw<IOException>();
            use.IsDisabled("unrelated").Should().BeTrue();
        }
        use.IsRevisionAllowed(definition.UseControlId!, definition.Digest).Should().BeFalse();
    }

    [Fact]
    public void VersionOneExclusionsMigrateWithoutInventingReviewsAndDisableRequiresFreshReviewToRestore()
    {
        var state = new LocalSharedSkillPreferencesTests.Store { Text = "{\"Version\":1,\"Disabled\":[\"bundled.other\"]}" };
        var use = new ArtifactUsePreferences(state);
        var definition = Definition(ArtifactKind.Agent);
        use.IsDisabled("bundled.other").Should().BeTrue();
        use.IsRevisionAllowed(definition.UseControlId!, definition.Digest).Should().BeFalse();
        use.SetDisabled(definition.UseControlId!, false, definition.Digest);
        use.SetDisabled(definition.UseControlId!, true);
        use.SetDisabled(definition.UseControlId!, false);
        use.IsRevisionAllowed(definition.UseControlId!, definition.Digest).Should().BeFalse();
        using var saved = JsonDocument.Parse(state.Text!);
        saved.RootElement.GetProperty("Version").GetInt32().Should().Be(2);
        use.IsDisabled("bundled.other").Should().BeTrue();
        ((Action)(() => use.IsRevisionAllowed("bad/id", definition.Digest))).Should().Throw<ArgumentException>();
        ((Action)(() => use.IsRevisionAllowed("valid", "bad"))).Should().Throw<ArgumentException>();
        ((Action)(() => use.SetDisabled("valid", false, "bad"))).Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("{\"Version\":2,\"Disabled\":[],\"Reviewed\":[]}")]
    [InlineData("{\"Version\":3,\"Disabled\":[]}")]
    [InlineData("{\"Version\":2,\"Disabled\":[],\"Reviewed\":{\"file.test\":false}}")]
    [InlineData("{\"Version\":2,\"Disabled\":[],\"Reviewed\":{\"file.test\":\"bad\"}}")]
    [InlineData("{\"Version\":2,\"Disabled\":[],\"Reviewed\":{\"bad/id\":\"DIGEST\"}}")]
    [InlineData("{\"Version\":2,\"Disabled\":[],\"Reviewed\":{\"file.test\":\"DIGEST\",\"file.test\":\"DIGEST\"}}")]
    [InlineData("{\"Version\":2,\"Disabled\":[\"file.test\"],\"Reviewed\":{\"file.test\":\"DIGEST\"}}")]
    public void CorruptRevisionApprovalsFailClosedWithoutRewritingState(string text)
    {
        var state = new LocalSharedSkillPreferencesTests.Store
        { Text = text.Replace("DIGEST", new string('a', 64), StringComparison.Ordinal) };
        ((Action)(() => _ = new ArtifactUsePreferences(state))).Should().Throw<InvalidDataException>();
        state.Writes.Should().Be(0);
    }

    private static ArtifactDefinition Definition(ArtifactKind kind) =>
        new("disk.example", kind, "Example", "Test", "example", ["example"], "user-profile", "1",
            new string('a', 64), "Original") { UseControlId = "file.example" };
}
