using System.Text.Json;
using System.Text.Json.Nodes;

using AwesomeAssertions;

using Kora.Core.Authorization;
using Kora.Core.Customizations;

namespace Kora.Core.UnitTests.Authorization;

public sealed class PermissionGrantTargetTests
{
    [Theory]
    [InlineData(PermissionGrantArtifactKind.Tool, CustomizationScope.BuiltIn)]
    [InlineData(PermissionGrantArtifactKind.Skill, CustomizationScope.Kora)]
    [InlineData(PermissionGrantArtifactKind.Instruction, CustomizationScope.User)]
    [InlineData(PermissionGrantArtifactKind.Agent, null)]
    [InlineData(PermissionGrantArtifactKind.McpServer, CustomizationScope.User)]
    [InlineData(PermissionGrantArtifactKind.Prompt, CustomizationScope.Kora)]
    [InlineData(PermissionGrantArtifactKind.Uri, null)]
    public void TypedMetadataRoundTripsWithoutInventingUnknownScope(PermissionGrantArtifactKind kind, CustomizationScope? scope)
    {
        var target = new PermissionGrantTarget(kind, scope);
        target.Kind.Should().Be(kind);
        target.Scope.Should().Be(scope);
        JsonSerializer.Deserialize<PermissionGrantTarget>(JsonSerializer.Serialize(target)).Should().Be(target);
    }

    [Fact]
    public void UndefinedMetadataFailsRatherThanBecomingAKnownOrUnknownGroup()
    {
        var kind = () => new PermissionGrantTarget((PermissionGrantArtifactKind)99);
        var scope = () => new PermissionGrantTarget(PermissionGrantArtifactKind.Tool, (CustomizationScope)99);
        kind.Should().Throw<InvalidDataException>();
        scope.Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData("ActionId")]
    [InlineData("SourcePartition")]
    [InlineData("SkillId")]
    [InlineData("DefinitionDigest")]
    [InlineData("DeclaredResourceDigest")]
    [InlineData("TrackedContentDigest")]
    [InlineData("ImplementationDigest")]
    [InlineData("InvocationDigest")]
    [InlineData("ResourceDigest")]
    [InlineData("IdentityDigest")]
    [InlineData("DestinationDigest")]
    [InlineData("TransformationDigest")]
    [InlineData("PolicyRevision")]
    public void PresentationMetadataPreservesLegacyAuthorityEqualityButNeverIgnoresAnAuthorityField(string field)
    {
        var digest = new string('a', 64);
        var original = new ExactOperationBinding("bounded.read", "builtin", "inspect",
            digest, digest, digest, digest, digest, digest, digest, digest, digest, new(1));
        var typed = new ExactOperationBinding("bounded.read", "builtin", "inspect",
            digest, digest, digest, digest, digest, digest, digest, digest, digest, new(1),
            new(PermissionGrantArtifactKind.Tool, CustomizationScope.BuiltIn));
        original.Target.Should().BeNull();
        typed.Target.Should().NotBeNull();
        (original == typed).Should().BeTrue();
        original.GetHashCode().Should().Be(typed.GetHashCode());
        var changed = JsonNode.Parse(JsonSerializer.Serialize(typed))!.AsObject();
        if (string.Equals(field, "PolicyRevision", StringComparison.Ordinal))
        { changed[field]!["Value"] = 2; }
        else { changed[field] = field.EndsWith("Digest", StringComparison.Ordinal) ? new string('b', 64) : "changed"; }
        var other = JsonSerializer.Deserialize<ExactOperationBinding>(changed.ToJsonString())!;
        original.Equals(other).Should().BeFalse();
        original.Equals(null).Should().BeFalse();
    }
}
