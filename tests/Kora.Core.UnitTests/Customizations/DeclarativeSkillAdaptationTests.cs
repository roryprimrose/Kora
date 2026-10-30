using AwesomeAssertions;

using Kora.Core.Artifacts;
using Kora.Core.Commands;
using Kora.Core.Customizations;

namespace Kora.Core.UnitTests.Customizations;

public sealed class DeclarativeSkillAdaptationTests
{
    private const string SourceText = "---\r\nname: original\r\nversion: 1.2.3\r\ndescription: Review prose\r\n---\r\n\r\nOriginal author credit.\r\nPlain guidance.\r\n";
    private static readonly CustomizationSource Original = new("source.original", CustomizationKind.Skill, CustomizationScope.User,
        "original", @"Q:\profile\.copilot\skills\original\SKILL.md", "SKILL.md");
    private static readonly CustomizationSource Destination = new("owned.adapted", CustomizationKind.Skill, CustomizationScope.Kora,
        "adapted", @"Q:\owned\Skills\adapted\SKILL.md", "SKILL.md");
    private static CustomizationSnapshot Snapshot(string text = SourceText) =>
        new(Original, "exact-revision", [new("SKILL.md", ArtifactFileDocument.Encode(text, true))]);

    [Fact]
    public void AdaptationPreservesAllPlainGuidanceAndHonestLineageWithoutAuthority()
    {
        var source = Snapshot();
        var adaptation = DeclarativeSkillAdaptation.Create(source, Destination);
        adaptation.Original.Should().BeSameAs(source);
        adaptation.Destination.Should().BeSameAs(Destination);
        var document = adaptation.ValidateDraft([adaptation.Proposed]);
        document.HasBom.Should().BeTrue();
        document.Body.Should().Be("\r\nOriginal author credit.\r\nPlain guidance.\r\n");
        document.Metadata["name"].Should().Be("adapted");
        document.Metadata["version"].Should().Be("1.2.3");
        document.Metadata["description"].Should().Be("Review prose");
        document.Metadata["attribution"].Should().Be(adaptation.Attribution)
            .And.Contain("source.original").And.Contain("exact-revision").And.Contain("original@1.2.3");
        source.Files.Single().Text.Should().Be(SourceText);
        var edited = adaptation.Proposed.WithText(adaptation.Proposed.Text + "Optional plain edit.");
        adaptation.ValidateDraft([edited]).Body.Should().EndWith("Optional plain edit.");
    }

    [Theory]
    [InlineData("allowed-tools: shell\n")]
    [InlineData("format: skill-md-instructions-v2\nreferences: notes.md\n")]
    [InlineData("author: Unknown metadata attribution cannot be discarded\n")]
    [InlineData("dependencies: required-action\n")]
    [InlineData("hooks: run-on-save\n")]
    public void UnsupportedDeclarationsAreRefusedInsteadOfDropped(string field)
    {
        var text = SourceText.Replace("---\r\n\r\n", field + "---\r\n\r\n", StringComparison.Ordinal);
        ((Action)(() => DeclarativeSkillAdaptation.Create(Snapshot(text), Destination))).Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData("```powershell\nGet-Process\n```")]
    [InlineData("Use required.ps1")]
    [InlineData("Use [required](notes.md)")]
    [InlineData("https://example.com")]
    public void ExecutableAndUnresolvedGuidanceIsNotManufacturedIntoACompatibleSkill(string body)
    {
        ((Action)(() => DeclarativeSkillAdaptation.Create(Snapshot(SourceText + body), Destination))).Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData("built-in")]
    [InlineData("owned")]
    [InlineData("kind")]
    [InlineData("recovery")]
    [InlineData("companion")]
    [InlineData("alias")]
    [InlineData("same")]
    [InlineData("destination")]
    [InlineData("destination-kind")]
    [InlineData("destination-readonly")]
    [InlineData("destination-primary")]
    [InlineData("primary")]
    [InlineData("mismatch")]
    [InlineData("lineage")]
    public void OnlyOneExactSharedPrimaryAndDistinctCanonicalOwnedDestinationAreAccepted(string state)
    {
        var snapshot = Snapshot();
        var source = snapshot.Source;
        var destination = Destination;
        IReadOnlyList<CustomizationFile> files = snapshot.Files;
        switch (state)
        {
            case "built-in": source = source with { Scope = CustomizationScope.BuiltIn, IsReadOnly = true }; break;
            case "owned": source = source with { Scope = CustomizationScope.Kora }; break;
            case "kind": source = source with { Kind = CustomizationKind.Agent }; break;
            case "companion": files = [.. files, new("notes.md", "Notes"u8)]; break;
            case "alias": destination = destination with { Name = "Adapted" }; break;
            case "same": destination = destination with { Name = "original" }; break;
            case "destination": destination = destination with { Scope = CustomizationScope.User }; break;
            case "destination-kind": destination = destination with { Kind = CustomizationKind.Agent }; break;
            case "destination-readonly": destination = destination with { IsReadOnly = true }; break;
            case "destination-primary": destination = destination with { PrimaryFile = "skill.md" }; break;
            case "primary": source = source with { PrimaryFile = "skill.md" }; files = [new("skill.md", files[0].CopyBytes())]; break;
            case "mismatch": source = source with { Name = "different" }; break;
            case "lineage": source = source with { Location = "source\nlocation" }; break;
        }
        snapshot = new(source, snapshot.Revision, files, state is "recovery" ? "recover first" : null);
        ((Action)(() => DeclarativeSkillAdaptation.Create(snapshot, destination))).Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void MissingSourceOrDestinationIsNotAnAdaptation(bool source)
    {
        ((Action)(() => DeclarativeSkillAdaptation.Create(source ? null! : Snapshot(), source ? Destination : null!)))
            .Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData("metadata")]
    [InlineData("attribution")]
    [InlineData("name")]
    [InlineData("version")]
    [InlineData("description")]
    [InlineData("companion")]
    [InlineData("primary")]
    [InlineData("script")]
    public void DraftEditingCannotRemoveLineageOrAddUnrepresentableRequirements(string state)
    {
        var adaptation = DeclarativeSkillAdaptation.Create(Snapshot(), Destination);
        var text = adaptation.Proposed.Text;
        text = state switch
        {
            "metadata" => text.Replace("version:", "tools: required\nversion:", StringComparison.Ordinal),
            "attribution" => text.Replace(adaptation.Attribution, "invented author", StringComparison.Ordinal),
            "name" => text.Replace("name: adapted", "name: different", StringComparison.Ordinal),
            "version" => text.Replace("version: 1.2.3", "version: unknown", StringComparison.Ordinal),
            "description" => text.Replace("Review prose", "", StringComparison.Ordinal),
            "script" => text + "Run required.py",
            _ => text,
        };
        CustomizationFile[] files = [new(state is "primary" ? "other.md" : "SKILL.md", ArtifactFileDocument.Encode(text, false))];
        if (state is "companion") { files = [.. files, new("notes.md", "Notes"u8)]; }
        ((Action)(() => adaptation.ValidateDraft(files))).Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData("settings")]
    [InlineData("skills")]
    [InlineData("help")]
    public void ExistingPublicationNamespaceValidationRejectsReservedAdaptationIds(string name)
    {
        var adaptation = DeclarativeSkillAdaptation.Create(Snapshot(), Destination with { Name = name });
        var proposed = adaptation.ValidateDraft([adaptation.Proposed]).Definition;
        ((Action)(() => ArtifactPublicationValidation.RequireUnshadowed(proposed, [],
            new(new BuiltInCommandCatalog())))).Should().Throw<InvalidDataException>();
    }
}
