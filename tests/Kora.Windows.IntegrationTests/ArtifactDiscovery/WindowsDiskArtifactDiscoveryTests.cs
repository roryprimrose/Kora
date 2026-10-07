using AwesomeAssertions;

using Kora.Core.Artifacts;
using Kora.Windows.Artifacts;

namespace Kora.Windows.IntegrationTests.ArtifactDiscovery;

public sealed class WindowsDiskArtifactDiscoveryTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        "Kora.Windows.IntegrationTests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void Load_reads_bounded_skill_prompt_and_instruction_files()
    {
        var skills = Path.Combine(root, "skills");
        var prompts = Path.Combine(root, "prompts");
        Directory.CreateDirectory(Path.Combine(skills, "deploy"));
        Directory.CreateDirectory(prompts);
        File.WriteAllText(Path.Combine(skills, "deploy", "SKILL.md"), """
            ---
            name: deploy
            description: "Deploy an application."
            ---
            Follow the reviewed deployment workflow.
            """);
        File.WriteAllText(Path.Combine(prompts, "explain.prompt.md"), """
            ---
            name: "Explain Result"
            description: "Explain a result."
            agent: agent
            ---
            Explain the supplied result.
            """);
        File.WriteAllText(Path.Combine(prompts, "standards.instructions.md"), """
            ---
            name: "Coding Standards"
            description: "Apply coding standards."
            applyTo: "**/*.cs"
            ---
            Apply the coding standards.
            """);
        var discovery = new WindowsDiskArtifactDiscovery(
        [
            new(skills, ArtifactKind.Skill, "agents-profile", "SKILL.md", 1),
            new(prompts, ArtifactKind.Prompt, "vscode-profile", "*.prompt.md", 0),
            new(prompts, ArtifactKind.Instruction, "vscode-profile", "*.instructions.md", 0),
        ]);

        var artifacts = discovery.Load();

        artifacts.Select(artifact => artifact.CommandName)
            .Should().Equal("deploy", "explain-result", "coding-standards");
        artifacts.Select(artifact => artifact.Kind)
            .Should().Equal(ArtifactKind.Skill, ArtifactKind.Prompt, ArtifactKind.Instruction);
        artifacts.Should().OnlyContain(artifact =>
            artifact.Source.EndsWith("-profile", StringComparison.Ordinal)
            && artifact.Version.StartsWith("disk-", StringComparison.Ordinal)
            && artifact.Content.Length > 0);
    }

    [Fact]
    public void Load_excludes_non_user_invocable_skills()
    {
        var skills = Path.Combine(root, "skills");
        Directory.CreateDirectory(Path.Combine(skills, "hidden"));
        File.WriteAllText(Path.Combine(skills, "hidden", "SKILL.md"), """
            ---
            name: hidden
            description: "Hidden model-only skill."
            user-invocable: false
            ---
            Hidden instructions.
            """);
        var discovery = new WindowsDiskArtifactDiscovery(
            [new(skills, ArtifactKind.Skill, "agents-profile", "SKILL.md", 1)]);

        discovery.Load().Should().BeEmpty();
    }

    [Theory]
    [InlineData("No frontmatter")]
    [InlineData("---\ndescription: missing close")]
    [InlineData("---\ndescription: \"No body\"\n---")]
    public void Load_rejects_malformed_disk_artifacts(string content)
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "broken.prompt.md"), content);
        var discovery = new WindowsDiskArtifactDiscovery(
            [new(root, ArtifactKind.Prompt, "vscode-profile", "*.prompt.md", 0)]);

        ((Action)(() => discovery.Load())).Should().Throw<InvalidDataException>();
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
