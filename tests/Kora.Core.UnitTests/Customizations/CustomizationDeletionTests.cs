using AwesomeAssertions;

using Kora.Core.Artifacts;
using Kora.Core.Customizations;

namespace Kora.Core.UnitTests.Customizations;

public sealed class CustomizationDeletionTests
{
    [Theory]
    [InlineData(CustomizationKind.Instruction)]
    [InlineData(CustomizationKind.Prompt)]
    [InlineData(CustomizationKind.Agent)]
    public void SupportedOwnedSingleFileDeletionValidatesTheSameCompleteDocument(CustomizationKind kind)
    {
        var source = new CustomizationSource("file.example", kind, CustomizationScope.Kora, "example",
            "host-source", CustomizationLayout.PrimaryFile(kind, "example"));
        var reviewed = new CustomizationSnapshot(source, "revision",
            [new(source.PrimaryFile, ArtifactFileDocument.Encode(CustomizationLayout.Template(kind, "example"), true))]);
        CustomizationDeletion.Supports(source).Should().BeTrue();
        CustomizationDeletion.Validate(reviewed);
    }

    [Theory]
    [InlineData("skill")]
    [InlineData("mcp")]
    [InlineData("user")]
    [InlineData("builtin")]
    [InlineData("readonly")]
    [InlineData("recovery")]
    [InlineData("alias")]
    [InlineData("name")]
    [InlineData("unsupported")]
    [InlineData("malformed")]
    public void DeletionRejectsUnownedIncompatibleAliasedAndUnrecoveredSources(string failure)
    {
        var kind = failure is "skill" ? CustomizationKind.Skill : failure is "mcp" ? CustomizationKind.McpServer : CustomizationKind.Instruction;
        var source = new CustomizationSource("file.example", kind,
            failure is "user" ? CustomizationScope.User : failure is "builtin" ? CustomizationScope.BuiltIn : CustomizationScope.Kora,
            failure is "name" ? "Example" : "example", "host-source",
            failure is "alias" ? "alias.instructions.md" : CustomizationLayout.PrimaryFile(kind, "example"),
            failure is "readonly");
        var text = CustomizationLayout.Template(kind, "example");
        if (failure is "unsupported") { text = text.Replace("kora-format: 1", "kora-format: 99", StringComparison.Ordinal); }
        if (failure is "malformed") { text = "Not a supported definition"; }
        var reviewed = new CustomizationSnapshot(source, "revision",
            [new(source.PrimaryFile, ArtifactFileDocument.Encode(text, false))], failure is "recovery" ? "Recovery required" : null);
        ((Action)(() => CustomizationDeletion.Validate(reviewed))).Should().Throw<InvalidDataException>();
    }
}
