using AwesomeAssertions;

using Kora.Core.Artifacts;
using Kora.Core.Configuration;
using Kora.Core.Dependencies;
using Kora.Windows.Artifacts;

namespace Kora.Windows.IntegrationTests.ArtifactDiscovery;

public sealed class WindowsDiskArtifactDiscoveryTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        "Kora.Windows.IntegrationTests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void Supported_profile_discovery_admits_dotted_directories_but_not_deeper_or_unrelated_sources()
    {
        var profile = Path.Combine(root, "profile");
        var profiles = Path.Combine(profile, "Code", "User", "profiles");
        var nested = Path.Combine(profiles, "profile.one", "prompts.v1");
        Directory.CreateDirectory(nested);
        const string text = "---\nname: Dotted\n---\nSupported source.";
        File.WriteAllText(Path.Combine(nested, "dotted.prompt.md"), text);
        var tooDeep = Path.Combine(nested, "too.deep");
        Directory.CreateDirectory(tooDeep);
        File.WriteAllText(Path.Combine(tooDeep, "invalid.prompt.md"), "Must not be parsed.");
        var unrelated = Path.Combine(profile, "unrelated");
        Directory.CreateDirectory(unrelated);
        File.WriteAllText(Path.Combine(unrelated, "invalid.prompt.md"), "Must not be parsed.");
        var discovery = new WindowsDiskArtifactDiscovery(new Paths(Path.Combine(root, "kora")),
            ArtifactDirectorySettings.Empty, profile);
        var found = discovery.Load().Should().ContainSingle().Subject;
        found.CommandName.Should().Be("dotted");
        found.Source.Should().Be("vscode-profile");
        var renamed = Path.Combine(profiles, "renamed.profile");
        Directory.Move(Path.GetDirectoryName(nested)!, renamed);
        var moved = discovery.Load().Should().ContainSingle().Subject;
        moved.Id.Should().Be(found.Id);
        moved.UseControlId.Should().NotBe(found.UseControlId);
        Directory.Delete(renamed, recursive: true);
        discovery.Load().Should().BeEmpty();
    }

    [Fact]
    public void Supported_profile_discovery_preserves_reparse_exclusion_and_file_limit()
    {
        var profile = Path.Combine(root, "profile");
        var profiles = Path.Combine(profile, "Code", "User", "profiles");
        Directory.CreateDirectory(profiles);
        var outside = Path.Combine(root, "outside");
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "untrusted.prompt.md"), "Must not be parsed.");
        var junction = Path.Combine(profiles, "linked.profile");
        var start = new System.Diagnostics.ProcessStartInfo("cmd.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            Arguments = $"/d /c mklink /J \"{junction}\" \"{outside}\"",
        };
        using (var process = System.Diagnostics.Process.Start(start)!)
        {
            process.WaitForExit();
            process.ExitCode.Should().Be(0);
        }
        try
        {
            (File.GetAttributes(junction) & FileAttributes.ReparsePoint).Should().Be(FileAttributes.ReparsePoint);
            var discovery = new WindowsDiskArtifactDiscovery(new Paths(Path.Combine(root, "kora")),
                ArtifactDirectorySettings.Empty, profile);
            discovery.Load().Should().BeEmpty();
            for (var index = 0; index < 129; index++)
            {
                File.WriteAllText(Path.Combine(profiles, $"prompt-{index}.prompt.md"), "Limit checked before parsing.");
            }
            var load = () => discovery.Load();
            load.Should().Throw<InvalidDataException>().WithMessage("*128-file limit*");
        }
        finally
        {
            Directory.Delete(junction);
        }
    }

    [Fact]
    public void NamedAgentLookupUsesOnlyRegisteredAgentRootsAndNeverReadsOrRepairsProfileContent()
    {
        var agents = Path.Combine(root, "agents");
        Directory.CreateDirectory(agents);
        var path = Path.Combine(agents, "role.agent.md");
        File.WriteAllText(path, "---\nname: Readable role\ndescription: Role\n---\nInert role.");
        var discovery = new WindowsDiskArtifactDiscovery(
            [new(agents, ArtifactKind.Agent, "additional", "*.agent.md", 0)]);
        var selected = discovery.Load().Single();
        discovery.ResolveAgentPath(selected, TestContext.Current.CancellationToken).Should().Be(path);
        File.WriteAllText(path, "Malformed changed bytes are not a new registration or approved profile.");
        discovery.ResolveAgentPath(selected, TestContext.Current.CancellationToken).Should().Be(path);
        var cancelled = () => discovery.ResolveAgentPath(selected, new CancellationToken(true));
        cancelled.Should().Throw<OperationCanceledException>();
        var unbound = () => discovery.ResolveAgentPath(selected with { UseControlId = null }, TestContext.Current.CancellationToken);
        unbound.Should().Throw<InvalidOperationException>();
        var wrongKind = ArtifactFileDocument.Parse(ArtifactFileDocument.Encode("---\nname: Prompt\n---\nInert", false),
            ArtifactKind.Prompt, "additional", "prompt").Definition;
        var wrong = () => discovery.ResolveAgentPath(wrongKind, TestContext.Current.CancellationToken);
        wrong.Should().Throw<InvalidOperationException>();
        File.Delete(path);
        var missing = () => discovery.ResolveAgentPath(selected, TestContext.Current.CancellationToken);
        missing.Should().Throw<InvalidOperationException>();
        for (var i = 0; i < 129; i++) { File.WriteAllText(Path.Combine(agents, $"role-{i}.agent.md"), "No content read."); }
        var overflow = () => discovery.ResolveAgentPath(selected, TestContext.Current.CancellationToken);
        overflow.Should().Throw<InvalidDataException>();
    }

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

    [Fact]
    public void Load_applies_existing_processing_rules_to_additional_directories_and_agents()
    {
        var skills = Path.Combine(root, "custom-skills");
        var instructions = Path.Combine(root, "custom-instructions");
        var prompts = Path.Combine(root, "custom-prompts");
        var agents = Path.Combine(root, "custom-agents");
        Directory.CreateDirectory(Path.Combine(skills, "deploy-extra"));
        Directory.CreateDirectory(instructions);
        Directory.CreateDirectory(prompts);
        Directory.CreateDirectory(agents);
        File.WriteAllText(Path.Combine(skills, "deploy-extra", "SKILL.md"), """
            ---
            name: deploy-extra
            description: "Deploy through an additional root."
            ---
            Follow the reviewed deployment workflow.
            """);
        File.WriteAllText(Path.Combine(instructions, "standards-extra.instructions.md"), """
            ---
            name: "Standards Extra"
            description: "Apply additional standards."
            ---
            Apply the additional standards.
            """);
        File.WriteAllText(Path.Combine(prompts, "explain-extra.prompt.md"), """
            ---
            name: "Explain Extra"
            ---
            Explain the supplied result.
            """);
        File.WriteAllText(Path.Combine(agents, "review.agent.md"), """
            ---
            name: "Review Changes"
            description: "Review a proposed change."
            tools: ["read"]
            ---
            Review the supplied change using the available read-only context.
            """);
        var settings = new ArtifactDirectorySettings([skills], [instructions], [prompts], [agents], [], []);
        var paths = new Paths(Path.Combine(root, "kora"));

        var artifacts = new WindowsDiskArtifactDiscovery(
            paths,
            settings,
            Path.Combine(root, "empty-profile")).Load();

        artifacts.Select(artifact => artifact.Kind).Should().Equal(
            ArtifactKind.Skill,
            ArtifactKind.Instruction,
            ArtifactKind.Prompt,
            ArtifactKind.Agent);
        artifacts.Should().OnlyContain(artifact => artifact.Source == "additional");
        var agent = artifacts.Single(artifact => artifact.Kind == ArtifactKind.Agent);
        agent.Kind.Should().Be(ArtifactKind.Agent);
        agent.CommandName.Should().Be("review-changes");
        agent.Source.Should().Be("additional");
        agent.Id.Should().Be("disk.additional.agent.review-changes");
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

    private sealed record Paths(string LocalRoot) : IApplicationDataPaths
    {
        public string RoamingRoot => LocalRoot;
    }
}
