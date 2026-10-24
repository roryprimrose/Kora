using System.Text;
using AwesomeAssertions;
using Kora.Core.Skills;

namespace Kora.Core.UnitTests.Skills;

public sealed class SharedSkillReferenceBundleTests
{
    private static readonly Guid Source = Guid.NewGuid();
    private const string Header = "---\nname: reference-example\nversion: 1.0.0\ndescription: Explain prose\nformat: skill-md-instructions-v2\nreferences: ";

    private static SharedSkillSnapshot Candidate(string paths = "notes.md", string body = "Explain the supplied prose.",
        string? primary = null) =>
        SharedSkillSnapshot.Parse(Source, "example\\SKILL.md", Encoding.UTF8.GetBytes(primary ?? Header + paths + "\n---\n" + body),
            true, SharedSkillReferenceDeclaration.Parse(paths).Paths.Select(path => "example\\" + path)
                .Concat(paths.Contains('\\') ? ["example\\references"] : []).ToArray())
            .WithPackageDirectoryIdentity(new string('a', 48));

    [Fact]
    public void DiscoveryIsInertAndExactBundlePreservesAllBytesWithAggregateAttribution()
    {
        var candidate = Candidate("notes.md, references\\guide.md", "Read [notes](notes.md) and [guide](references\\guide.md).");
        candidate.IsReferenceCandidate.Should().BeTrue();
        candidate.IsInstructionCompatible.Should().BeFalse();
        candidate.ReferenceBundle.Should().BeNull();
        var notes = Encoding.UTF8.GetBytes("\uFEFF# Notes\r\nUse exact prose e\u0301.");
        var contents = new Dictionary<string, byte[]>(StringComparer.Ordinal) { ["notes.md"] = notes, ["references\\guide.md"] = Encoding.UTF8.GetBytes("Guide.") };
        var bundle = SharedSkillReferenceBundle.Create(candidate, contents);
        var complete = candidate.WithReferenceBundle(bundle);
        var digest = complete.RevisionDigest;
        notes[0] = 0;
        contents.Clear();
        complete.IsInstructionCompatible.Should().BeTrue();
        complete.ReferenceBundle!.References.Select(item => item.RelativePath).Should().Equal("notes.md", "references\\guide.md");
        complete.ReferenceBundle.References[0].Text.Should().Be("\uFEFF# Notes\r\nUse exact prose e\u0301.");
        complete.ReferenceBundle.References[0].Bytes.ToArray().Should().Equal(Encoding.UTF8.GetBytes(complete.ReferenceBundle.References[0].Text));
        digest.Should().HaveLength(64).And.NotBe(candidate.RevisionDigest);
        complete.WithUnavailableReason("retired").ReferenceBundle.Should().BeSameAs(bundle);
        var foreign = Candidate("notes.md, references\\guide.md", primary: Header + "notes.md, references\\guide.md\n---\nDifferent primary.");
        ((Action)(() => foreign.WithReferenceBundle(bundle))).Should().Throw<InvalidDataException>();
        ((Action)(() => complete.WithReferenceBundle(bundle))).Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData("format: unknown\nreferences: notes.md", "unsupported-reference-format")]
    [InlineData("format: skill-md-instructions-v2", "unsupported-reference-format")]
    [InlineData("references: notes.md", "unsupported-reference-format")]
    [InlineData("format: skill-md-instructions-v2\nreferences: ../notes.md", "invalid-reference-declaration")]
    public void InvalidVersionedDeclarationsStayUnavailable(string fields, string reason)
    {
        var text = "---\nname: example\nversion: 1.0.0\ndescription: Prose\n" + fields + "\n---\nProse.";
        SharedSkillSnapshot.Parse(Source, "SKILL.md", Encoding.UTF8.GetBytes(text), false)
            .UnavailableReasons.Should().Contain(reason);
    }

    [Theory]
    [InlineData("[guide](references\\guide.md) then [notes](notes.md).", true)]
    [InlineData("[notes](notes.md) and [notes](notes.md).", true)]
    [InlineData("[unknown](other.md)", false)]
    [InlineData("[unfinished", false)]
    [InlineData("[notes](notes.md", false)]
    [InlineData("![image](notes.md)", false)]
    [InlineData("[[nested]](notes.md)", false)]
    public void OnlyExactDeclaredInlineLinksAreAllowedIndependentlyOfDeclarationOrder(string body, bool compatible) =>
        Candidate("notes.md, references\\guide.md", body).IsReferenceCandidate.Should().Be(compatible);

    [Fact]
    public async Task DeclarationAndDefaultReaderRejectIncompleteUnsupportedAndAliasedInputs()
    {
        var declaration = SharedSkillReferenceDeclaration.Parse("notes.md");
        declaration.MatchesInventory("SKILL.md", ["notes.md"]).Should().BeTrue();
        declaration.MatchesInventory("SKILL.md", ["notes.md", "notes.md"]).Should().BeFalse();
        var noEntries = SharedSkillSnapshot.Parse(Source, "SKILL.md", Encoding.UTF8.GetBytes(Header + "notes.md\n---\nProse."), false);
        noEntries.IsReferenceCandidate.Should().BeFalse();
        var primary = SharedSkillSnapshot.Parse(Source, "SKILL.md", Encoding.UTF8.GetBytes("---\nname: example\nversion: 1.0.0\ndescription: Prose\n---\nProse."), false);
        ((Action)(() => SharedSkillReferenceBundle.Create(primary, new Dictionary<string, byte[]>(StringComparer.Ordinal))))
            .Should().Throw<InvalidDataException>();
        var candidate = Candidate();
        ((Action)(() => SharedSkillReferenceBundle.Create(candidate.WithUnavailableReason("unavailable"),
            new Dictionary<string, byte[]>(StringComparer.Ordinal) { ["notes.md"] = [65] }))).Should().Throw<InvalidDataException>();
        ((Action)(() => SharedSkillReferenceBundle.Create(candidate,
            new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase) { ["NOTES.MD"] = [65] }))).Should().Throw<InvalidDataException>();
        ISharedSkillSourceReader reader = new UnsupportedReader();
        await ((Func<Task>)(async () => await reader.ReadReferencesAsync(new(Source, ".agents\\skills", new string('a', 48)),
            candidate, TestContext.Current.CancellationToken))).Should().ThrowAsync<InvalidOperationException>();
        ((Action)(() => SharedSkillReferenceDeclaration.Parse(new string('a', 241) + ".md"))).Should().Throw<InvalidDataException>();
    }

    private sealed class UnsupportedReader : ISharedSkillSourceReader
    {
        public ValueTask<SharedSkillSource> SelectAsync(string selectedRoot, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<SharedSkillCatalogue> DiscoverAsync(SharedSkillSource source, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    [Theory]
    [InlineData("")]
    [InlineData("notes.md,notes.md")]
    [InlineData("notes.md, notes.md")]
    [InlineData("Notes.md")]
    [InlineData("notes.MD")]
    [InlineData("SKILL.md")]
    [InlineData("skill.md")]
    [InlineData("..\\notes.md")]
    [InlineData("a/notes.md")]
    [InlineData("\\\\host\\notes.md")]
    [InlineData("c:\\notes.md")]
    [InlineData("notes.md:stream")]
    [InlineData("notes.md ")]
    [InlineData("notes.txt")]
    [InlineData("con.md")]
    [InlineData("com1.md")]
    [InlineData("notes\u200b.md")]
    [InlineData("not\u00e9s.md")]
    [InlineData("a.md, b.md, c.md, d.md, e.md")]
    public void DeclarationRejectsAliasesDuplicatesUnsupportedKindsAndUnboundedSets(string value) =>
        ((Action)(() => SharedSkillReferenceDeclaration.Parse(value))).Should().Throw<InvalidDataException>();

    [Theory]
    [InlineData("[nested](other.md)")]
    [InlineData("[nested](notes.md)")]
    [InlineData("[id]: notes.md")]
    [InlineData("https://example.invalid")]
    [InlineData("<img src=x>")]
    [InlineData("```csharp\nrun\n```")]
    [InlineData("run.ps1")]
    [InlineData("run.sh")]
    [InlineData("run.exe")]
    [InlineData("run.py")]
    [InlineData("\0")]
    [InlineData("\u200b")]
    public void ReferencesNeverRecursivelyResolveOrAdmitActiveContent(string text)
    {
        var candidate = Candidate();
        ((Action)(() => SharedSkillReferenceBundle.Create(candidate,
            new Dictionary<string, byte[]>(StringComparer.Ordinal) { ["notes.md"] = Encoding.UTF8.GetBytes(text) }))).Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void ExactInclusiveByteAndCountBoundsNeverTruncate()
    {
        var candidate = Candidate("a.md, b.md, c.md, d.md");
        var contents = candidate.ReferenceDeclaration!.Paths.ToDictionary(path => path,
            _ => Encoding.UTF8.GetBytes(new string('x', 16 * 1024)), StringComparer.Ordinal);
        ((Action)(() => SharedSkillReferenceBundle.Create(candidate, contents))).Should().Throw<InvalidDataException>();
        contents["d.md"] = Encoding.UTF8.GetBytes(new string('x', 16 * 1024 - candidate.Bytes.Length));
        var bundle = SharedSkillReferenceBundle.Create(candidate, contents);
        bundle.References.Sum(item => item.Bytes.Length).Should().Be(64 * 1024 - candidate.Bytes.Length);
        contents["a.md"] = Encoding.UTF8.GetBytes(new string('x', 16 * 1024 + 1));
        ((Action)(() => SharedSkillReferenceBundle.Create(candidate, contents))).Should().Throw<InvalidDataException>();
        contents["a.md"] = [];
        ((Action)(() => SharedSkillReferenceBundle.Create(candidate, contents))).Should().Throw<InvalidDataException>();
        contents["a.md"] = [0xff];
        ((Action)(() => SharedSkillReferenceBundle.Create(candidate, contents))).Should().Throw<InvalidDataException>();
        contents.Remove("a.md");
        ((Action)(() => SharedSkillReferenceBundle.Create(candidate, contents))).Should().Throw<InvalidDataException>();
        contents["extra.md"] = [65];
        ((Action)(() => SharedSkillReferenceBundle.Create(candidate, contents))).Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void DigestIncludesExactPrimaryPathsReferencePathsOrderAndBytes()
    {
        var candidate = Candidate("a.md, b.md");
        var contents = new Dictionary<string, byte[]>(StringComparer.Ordinal) { ["b.md"] = [66], ["a.md"] = [65] };
        var digest = SharedSkillReferenceBundle.Create(candidate, contents).RevisionDigest;
        SharedSkillReferenceBundle.Create(candidate, new Dictionary<string, byte[]>(StringComparer.Ordinal) { ["a.md"] = [65], ["b.md"] = [66] })
            .RevisionDigest.Should().Be(digest);
        SharedSkillReferenceBundle.Create(Candidate("b.md, a.md"), contents).RevisionDigest.Should().NotBe(digest);
        contents["a.md"] = [65, 10];
        SharedSkillReferenceBundle.Create(candidate, contents).RevisionDigest.Should().NotBe(digest);
        var withoutPin = SharedSkillSnapshot.Parse(Source, candidate.RelativeFile, candidate.Bytes, true, candidate.UninspectedFiles);
        ((Action)(() => withoutPin.WithReferenceBundle(SharedSkillReferenceBundle.Create(withoutPin, contents))))
            .Should().Throw<InvalidOperationException>();
        ((Action)(() => candidate.WithPackageDirectoryIdentity("unknown"))).Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData("notes.md", "example\\extra.md")]
    [InlineData("notes.md", "example\\Notes.md")]
    [InlineData("notes.md", "example\\run.ps1")]
    public void ExtraMissingOrAliasedEntriesNeverBecomeCompatibleSubsets(string paths, string entry)
    {
        var snapshot = SharedSkillSnapshot.Parse(Source, "example\\SKILL.md",
            Encoding.UTF8.GetBytes(Header + paths + "\n---\nProse."), true, [entry]);
        snapshot.IsReferenceCandidate.Should().BeFalse();
        snapshot.UnavailableReasons.Should().Contain("undeclared-or-missing-package-entries");
    }
}
