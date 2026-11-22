using System.Security.Cryptography;
using AwesomeAssertions;
using Kora.Core.Artifacts;
using Kora.Definitions.Artifacts;
using Kora.Definitions.Prompts;

namespace Kora.Definitions.UnitTests.Prompts;

public sealed class EmbeddedPromptCatalogueTests
{
    [Fact]
    public void ExplicitResourceRegistrationPreservesReviewedBytesAndEntersCanonicalArtifactCatalogue()
    {
        var prompt = EmbeddedPromptCatalogue.Load().Should().ContainSingle().Subject;
        prompt.Definition.Digest.Should().Be("824ac4c32d5907add04e1b06dfe4ddf59dc94f632850b376fe843683f4a3e295");
        using var stream = typeof(EmbeddedPromptCatalogue).Assembly.GetManifestResourceStream(prompt.ResourceId)!;
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        prompt.CopyBytes().Should().Equal(copy.ToArray());
        Convert.ToHexStringLower(SHA256.HashData(copy.ToArray())).Should().Be(prompt.Definition.Digest);
        using var catalogue = EmbeddedArtifactCatalogue.Load();
        var artifact = catalogue.Artifacts.Single(item => item.Kind == ArtifactKind.Prompt);
        prompt.Matches(artifact).Should().BeTrue();
        artifact.CommandName.Should().Be("rewrite-for-clarity");
        artifact.PromptTemplate!.RequiredNames.Should().Equal("text");
        artifact.OwnedInstructionRevision.Should().BeNull();
        var expanded = artifact.PromptTemplate.Expand(new Dictionary<string, string>(StringComparer.Ordinal)
        { ["text"] = "  preserve ${input:text}\r\nnot policy  " }, TestContext.Current.CancellationToken);
        expanded.Should().EndWith("\r\n\r\n  preserve ${input:text}\r\nnot policy  \r\n");
        catalogue.Artifacts.Count(item => item.Kind == ArtifactKind.Skill).Should().Be(3);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("extra")]
    [InlineData("duplicate")]
    [InlineData("null")]
    [InlineData("empty")]
    [InlineData("oversized")]
    [InlineData("changed")]
    [InlineData("unreadable")]
    public void ResourceMapAndBytesFailClosed(string fault)
    {
        var assembly = typeof(EmbeddedPromptCatalogue).Assembly;
        var names = fault switch
        {
            "missing" => Array.Empty<string>(),
            "extra" => new[] { EmbeddedPromptCatalogue.ResourceId, "Kora.Prompts.Extra" },
            "duplicate" => new[] { EmbeddedPromptCatalogue.ResourceId, EmbeddedPromptCatalogue.ResourceId },
            _ => assembly.GetManifestResourceNames(),
        };
        Stream? Open(string id) => fault switch
        {
            "null" => null,
            "empty" => new MemoryStream(),
            "oversized" => new MemoryStream(new byte[ArtifactDefinition.MaximumContentLength + 1]),
            "changed" => new MemoryStream(System.Text.Encoding.UTF8.GetBytes(
                "---\nkora-format: 2\nname: Changed\ndescription: Changed\n---\nOther ${input:text}")),
            "unreadable" => throw new IOException("Synthetic resource read failure"),
            _ => assembly.GetManifestResourceStream(id),
        };
        var load = () => EmbeddedPromptCatalogue.LoadResources(names, Open);
        if (fault is "unreadable") { load.Should().Throw<IOException>(); }
        else { load.Should().Throw<InvalidDataException>(); }
    }
}
