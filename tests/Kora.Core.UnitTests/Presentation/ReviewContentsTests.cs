using System.Security.Cryptography;
using System.Text;

using AwesomeAssertions;

using Kora.Core.Agents;
using Kora.Core.Artifacts;
using Kora.Core.Presentation;
using Kora.Core.Skills;

namespace Kora.Core.UnitTests.Presentation;

public sealed class ReviewContentsTests
{
    [Fact]
    public void DocumentsKeepExactTextAndDigestOfTheirBytes()
    {
        var bytes = Encoding.UTF8.GetBytes("Write-Output 'é'\r\n");

        var document = ReviewDocument.FromBytes(@"scripts\Run.ps1", bytes);

        document.Path.Should().Be("scripts/Run.ps1");
        document.Segments.Should().Equal("scripts", "Run.ps1");
        document.Text.Should().Be("Write-Output 'é'\r\n");
        document.Digest.Should().Be(Convert.ToHexStringLower(SHA256.HashData(bytes)));
    }

    [Theory]
    [InlineData("../secret.txt")]
    [InlineData("scripts/../../x")]
    [InlineData("C:/Windows/x")]
    [InlineData(@"\\server\share\x")]
    [InlineData("//server/share/x")]
    [InlineData(@"\x")]
    [InlineData("/x")]
    [InlineData(@"\")]
    [InlineData("/")]
    [InlineData("./")]
    [InlineData(" ")]
    public void PathsMustStayRelativeAndContained(string path) =>
        FluentActions.Invoking(() => ReviewDocument.FromText(path, "x")).Should().Throw<ArgumentException>();

    [Fact]
    public void SetsRejectEmptyAndDuplicatePathsAndKeepUnavailableEntries()
    {
        FluentActions.Invoking(() => new ReviewContents("Skill", "rev", [])).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => new ReviewContents("Skill", "rev",
            [ReviewDocument.FromText("a/B.md", "1"), ReviewDocument.FromText("a/b.md", "2")])).Should().Throw<ArgumentException>();

        var contents = new ReviewContents("Skill", "rev", [ReviewDocument.FromText("SKILL.md", "x")], ["refs/a.md (reference not read)", " "]);

        contents.Unavailable.Should().Equal("refs/a.md (reference not read)");
    }

    [Fact]
    public void SingleTextContentsUseTheTextDigestUnlessARevisionIsSupplied()
    {
        var plain = ReviewContents.FromText("Prompt", "request.md", "hello");
        var pinned = ReviewContents.FromText("Prompt", "request.md", "hello", "expansion-digest");

        plain.Revision.Should().Be(plain.Documents.Single().Digest);
        pinned.Revision.Should().Be("expansion-digest");
    }

    [Fact]
    public void PackageContentsListEveryDeclaredFileByteExact()
    {
        var resources = new[]
        {
            new SkillResourceSnapshot("manifest.json", "Kora.Manifest", """
                {"schemaVersion":1,"id":"test","version":"1","name":"Test","description":"Test",
                "action":"test","entryPoint":"entry.ps1","parameterContract":"test","runtime":"powershell7",
                "dependencies":["test"],"helperLoadOrder":[],"files":[
                {"name":"skill.md","resourceId":"Kora.Instructions","kind":"instructions"},
                {"name":"entry.ps1","resourceId":"Kora.Entry","kind":"entry"}]}
                """u8.ToArray()),
            new SkillResourceSnapshot("skill.md", "Kora.Instructions", "# Test"u8.ToArray()),
            new SkillResourceSnapshot("entry.ps1", "Kora.Entry", "throw 'test only'"u8.ToArray()),
        };
        var package = new SkillPackageCatalogue(["Kora.Manifest"], resources).Packages[0];

        var contents = ReviewContents.FromPackage("Test", package);

        contents.Revision.Should().Be(package.DefinitionDigest);
        contents.Documents.Select(document => document.Path).Should().BeEquivalentTo(package.Files.Select(file => file.Name));
        contents.Documents.Single(document => string.Equals(document.Path, "entry.ps1", StringComparison.Ordinal)).Digest
            .Should().Be(package.Files.Single(file => string.Equals(file.Name, "entry.ps1", StringComparison.Ordinal)).Digest);
    }

    [Fact]
    public void SharedSkillContentsIncludeReadReferencesAndFlagUnreadOnes()
    {
        const string header = "---\nname: reference-example\nversion: 1.0.0\ndescription: Explain prose\nformat: skill-md-instructions-v2\nreferences: notes.md\n---\nRead [notes](notes.md).";
        var candidate = SharedSkillSnapshot.Parse(Guid.NewGuid(), "example\\SKILL.md", Encoding.UTF8.GetBytes(header), true, ["example\\notes.md"])
            .WithPackageDirectoryIdentity(new string('a', 48));

        var unread = ReviewContents.FromSharedSkill("Example", candidate);
        unread.Documents.Select(document => document.Path).Should().Equal("SKILL.md");
        unread.Unavailable.Should().ContainSingle(item => item.StartsWith("notes.md", StringComparison.Ordinal));

        var bundle = SharedSkillReferenceBundle.Create(candidate,
            new Dictionary<string, byte[]>(StringComparer.Ordinal) { ["notes.md"] = Encoding.UTF8.GetBytes("# Notes") });
        var complete = ReviewContents.FromSharedSkill("Example", candidate.WithReferenceBundle(bundle));
        complete.Documents.Select(document => document.Path).Should().Equal("SKILL.md", "notes.md");
        complete.Documents[1].Text.Should().Be("# Notes");
        complete.Revision.Should().Be(bundle.RevisionDigest);
        complete.Unavailable.Should().BeEmpty();
    }

    [Fact]
    public void OwnedSkillContentsSplitManifestInstructionsAndReferences()
    {
        var simple = OwnedDeclarativeSkillDocument.Create("daily-review", "1.0.0", "Daily review", "Review the day", "Guidance.");
        var plain = ReviewContents.FromOwnedSkill("Daily review", new(simple, "d", "m", "i"));
        plain.Documents.Select(document => document.Path).Should().Equal("manifest.yaml", "instructions.md");
        plain.Documents[1].Text.Should().Be(simple.Guidance);
        plain.Revision.Should().Be(simple.RevisionDigest);
        plain.Unavailable.Should().BeEmpty();

        var package = OwnedDeclarativeSkillDocument.CreatePackage("guide", "1.0.0", "Guide", "Guide", "Read [a](a.md) and [b](references\\b.md).",
            new Dictionary<string, byte[]>(StringComparer.Ordinal) { ["a.md"] = "A"u8.ToArray(), ["references\\b.md"] = "B"u8.ToArray() });
        var packaged = ReviewContents.FromOwnedSkill("Guide", new(package, "d", "m", "i"));
        packaged.Documents.Select(document => document.Path).Should().Equal("manifest.yaml", "instructions.md", "a.md", "references/b.md");
        packaged.Unavailable.Should().BeEmpty();
    }

    [Fact]
    public void AgentContentsIncludeResolvedInstructionsOrFlagUnresolvedReferences()
    {
        var profile = BoundedAgentProfile.Parse(ArtifactFileDocument.Parse(ArtifactFileDocument.Encode("""
            ---
            name: agent
            description: Test
            kora-agent-format: 2
            runtime: local
            input: text
            output: text
            references: disk.kora-profile.instruction.example
            tools: none
            maximum-calls: 0
            maximum-rounds: 1
            maximum-input-bytes: 32768
            maximum-output-bytes: 4096
            deadline-ms: 15000
            ---
            Original role.
            """, false), ArtifactKind.Agent, "selected-user-source", "agent"));
        var instruction = new OwnedInstructionSnapshot("example", new string('a', 48), new string('b', 48),
            ArtifactFileDocument.Parse(ArtifactFileDocument.Encode("---\nkora-format: 1\nname: example\ndescription: Test\n---\nBody", false),
                ArtifactKind.Instruction, "kora-profile", "example"));

        var unresolved = ReviewContents.FromAgent("agent", profile, null);
        unresolved.Documents.Should().ContainSingle();
        unresolved.Unavailable.Should().ContainSingle(item => item.Contains("disk.kora-profile.instruction.example", StringComparison.Ordinal));

        var resolved = ReviewContents.FromAgent("agent", profile, new AgentInstructionBundle(profile, [instruction]));
        resolved.Documents.Select(document => document.Path).Should().Equal(
            profile.Document.Definition.Id + ".agent.md", "instructions/" + instruction.CanonicalFileName);
        resolved.Documents[0].Text.Should().Be(profile.Document.Text);
        resolved.Revision.Should().Be(profile.Document.Definition.Digest);
        resolved.Unavailable.Should().BeEmpty();
    }
}
