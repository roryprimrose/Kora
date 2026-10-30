using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Skills;

namespace Kora.Core.UnitTests.Skills;

public sealed class OwnedSkillMarkdownPackageTests
{
    private static OwnedDeclarativeSkillDocument Package(string guidance = @"Read [notes](references\notes.md).") =>
        OwnedDeclarativeSkillDocument.CreatePackage("example", "1.0.0", "Example", "Description", guidance,
            new Dictionary<string, byte[]>(StringComparer.Ordinal) { [@"references\notes.md"] = "Exact reference\r\n${input:literal}\n"u8.ToArray() });

    [Fact]
    public void ObservationAndMarkdownFormatsRetainIndependentDeclarationsWithoutAuthorityTransfer()
    {
        const string source = """{"steps":[{"tool":"application.get_version","input":{}}]}""";
        var workflow = OwnedDeclarativeSkillDocument.CreateObservationWorkflow("inspection", "1.0.0",
            "Inspection", "Description", source);
        var reopened = OwnedDeclarativeSkillDocument.Parse(workflow.ManifestBytes.Span, workflow.InstructionBytes.Span);
        reopened.ObservationWorkflow!.Steps.Should().ContainSingle();
        reopened.Tool.Should().Be(OwnedSkillToolSelection.ReadOnlyObservations);
        reopened.ReferenceDeclaration.Should().BeNull();
        reopened.References.Should().BeEmpty();
        reopened.IsComplete.Should().BeTrue();
        reopened.RevisionDigest.Should().Be(workflow.RevisionDigest);
        var document = Package();
        document.ObservationWorkflow.Should().BeNull();
        document.Tool.Should().Be(OwnedSkillToolSelection.None);
        document.InputTemplate.Should().BeNull();
        var references = document.References.ToDictionary(item => item.RelativePath, item => item.Bytes.ToArray(), StringComparer.Ordinal);
        var attach = () => OwnedDeclarativeSkillDocument.Parse(workflow.ManifestBytes.Span, workflow.InstructionBytes.Span, references);
        attach.Should().Throw<InvalidDataException>();
        var promote = () => OwnedDeclarativeSkillDocument.Parse(Encoding.UTF8.GetBytes(document.ManifestText.Replace(
            "tools: none", "tools: application.get_version", StringComparison.Ordinal)), document.InstructionBytes.Span, references);
        promote.Should().Throw<InvalidDataException>();
        reopened.IsAvailableForInvocation.Should().BeFalse();
        document.IsAvailableForInvocation.Should().BeFalse();
    }

    [Fact]
    public void CompleteRoundtripPinsEveryPathAndByteWhileDeclarationAloneCannotBeSavedOrUsed()
    {
        var document = Package();
        document.ManifestText.Should().StartWith("kora-skill-format: 5\n");
        document.InputTemplate.Should().BeNull();
        document.Tool.Should().Be(OwnedSkillToolSelection.None);
        document.IsAvailableForInvocation.Should().BeFalse();
        document.TotalBytes.Should().Be(document.ManifestBytes.Length + document.InstructionBytes.Length + document.References[0].Bytes.Length);
        var declaration = OwnedDeclarativeSkillDocument.Parse(document.ManifestBytes.Span, document.InstructionBytes.Span);
        declaration.IsComplete.Should().BeFalse();
        declaration.References.Should().BeEmpty();
        declaration.CompleteText.Should().NotContain("Exact reference");
        declaration.Invoking(item => item.RequireComplete()).Should().Throw<InvalidOperationException>();
        declaration.Invoking(item => OwnedSkillGuidanceContext.RequirePackageLimit(item)).Should().Throw<InvalidOperationException>();
        var complete = OwnedDeclarativeSkillDocument.Parse(declaration.ManifestBytes.Span, declaration.InstructionBytes.Span,
            document.References.ToDictionary(item => item.RelativePath, item => item.Bytes.ToArray(), StringComparer.Ordinal));
        complete.RevisionDigest.Should().Be(document.RevisionDigest);
        complete.CompleteText.Should().Contain("Exact reference\r\n${input:literal}\n");
        complete.References[0].RelativePath.Should().Be(@"references\notes.md");
        complete.References[0].Bytes.ToArray().Should().Equal(document.References[0].Bytes.ToArray());
        var changed = OwnedDeclarativeSkillDocument.Parse(document.ManifestBytes.Span, document.InstructionBytes.Span,
            new Dictionary<string, byte[]>(StringComparer.Ordinal) { [@"references\notes.md"] = "Changed"u8.ToArray() });
        changed.RevisionDigest.Should().NotBe(complete.RevisionDigest);
    }

    [Theory]
    [InlineData("instructions.md")]
    [InlineData("skill.md")]
    [InlineData("../outside.md")]
    [InlineData(@"..\outside.md")]
    [InlineData(@"\\server\outside.md")]
    [InlineData(@"C:\outside.md")]
    [InlineData("https://example.com/a.md")]
    [InlineData("references/notes.md")]
    [InlineData("Notes.md")]
    [InlineData("notes.md:stream")]
    [InlineData("notes.exe")]
    [InlineData(@"a\b\c\d\e.md")]
    [InlineData("con.md")]
    public void UnsupportedNamesCannotChoosePathsOrComponentAuthority(string path)
    {
        var create = () => OwnedDeclarativeSkillDocument.CreatePackage("example", "1.0.0", "Example", "Description", "Literal",
            new Dictionary<string, byte[]>(StringComparer.Ordinal) { [path] = "Notes"u8.ToArray() });
        create.Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData("```powershell\nrun\n```")]
    [InlineData("[another](other.md)")]
    [InlineData("https://example.com/")]
    [InlineData("<script>")]
    [InlineData("run worker.ps1")]
    [InlineData("\0binary")]
    [InlineData("\u202Ehidden")]
    public void ReferenceComponentsCannotCarryActiveMarkupNestedReadsOrBinaryContent(string text)
    {
        var document = Package();
        var parse = () => OwnedDeclarativeSkillDocument.Parse(document.ManifestBytes.Span, document.InstructionBytes.Span,
            new Dictionary<string, byte[]>(StringComparer.Ordinal) { [@"references\notes.md"] = Encoding.UTF8.GetBytes(text) });
        parse.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void MissingExtraInvalidAndUnversionedComponentsAreRefusedInsteadOfDroppedOrReinterpreted()
    {
        var document = Package();
        var legacy = OwnedDeclarativeSkillDocument.Create("example", "1.0.0", "Example", "Description", "${input:literal}");
        foreach (var contents in new[]
        {
            new Dictionary<string, byte[]>(StringComparer.Ordinal),
            new Dictionary<string, byte[]>(StringComparer.Ordinal) { ["wrong.md"] = "Wrong"u8.ToArray() },
            new Dictionary<string, byte[]>(StringComparer.Ordinal) { [@"references\notes.md"] = [] },
            new Dictionary<string, byte[]>(StringComparer.Ordinal) { [@"references\notes.md"] = [0xff] },
            new Dictionary<string, byte[]>(StringComparer.Ordinal) { [@"references\notes.md"] = new byte[16385] },
        })
        {
            var parse = () => OwnedDeclarativeSkillDocument.Parse(document.ManifestBytes.Span, document.InstructionBytes.Span, contents);
            parse.Should().Throw<InvalidDataException>();
        }
        var reinterpret = () => OwnedDeclarativeSkillDocument.Parse(legacy.ManifestBytes.Span, legacy.InstructionBytes.Span,
            new Dictionary<string, byte[]>(StringComparer.Ordinal) { ["notes.md"] = "Notes"u8.ToArray() });
        reinterpret.Should().Throw<InvalidDataException>();
        legacy.Guidance.Should().Be("${input:literal}");
        legacy.InputTemplate.Should().BeNull();
        var unknown = () => OwnedDeclarativeSkillDocument.Parse(Encoding.UTF8.GetBytes(
            document.ManifestText.Replace("kora-skill-format: 5", "kora-skill-format: 99", StringComparison.Ordinal)), document.InstructionBytes.Span);
        unknown.Should().Throw<InvalidDataException>();
        var badPrimary = () => Package("[undeclared](other.md)");
        badPrimary.Should().Throw<InvalidDataException>();
        var tooMany = () => OwnedDeclarativeSkillDocument.CreatePackage("example", "1.0.0", "Example", "Description", "Literal",
            Enumerable.Range(1, 5).ToDictionary(index => "notes" + index + ".md", _ => "Notes"u8.ToArray(), StringComparer.Ordinal));
        tooMany.Should().Throw<InvalidDataException>();
        var aggregate = () => OwnedDeclarativeSkillDocument.CreatePackage("example", "1.0.0", "Example", "Description", "Literal",
            Enumerable.Range(1, 4).ToDictionary(index => "notes" + index + ".md", _ => Encoding.UTF8.GetBytes(new string('x', 16384)), StringComparer.Ordinal));
        aggregate.Should().Throw<InvalidDataException>();
        var escaped = () => OwnedDeclarativeSkillDocument.CreatePackage("example", "1.0.0", "Example", "Description", "Literal",
            new Dictionary<string, byte[]>(StringComparer.Ordinal) { ["notes.md"] = Encoding.UTF8.GetBytes(new string('"', 8000)) });
        escaped.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void ExactCompleteEnvelopeIncludesAllComponentsAndEnforces32768SerializedBytesWithoutTruncation()
    {
        var document = Package();
        var request = HostRequest.Create(RequestOrigin.ActivatedVoice);
        var now = DateTimeOffset.UnixEpoch;
        var empty = OwnedSkillGuidanceContext.Create(document, request, "", now, now.AddMinutes(1));
        var body = new string('x', ModelContextEnvelope.MaximumInputUtf8Bytes - empty.Serialize().Length);
        var context = OwnedSkillGuidanceContext.Create(document, request, body, now, now.AddMinutes(1));
        context.Serialize().Length.Should().Be(32768);
        context.ToolMode.Should().Be(ModelToolMode.None);
        context.Tools.Should().BeEmpty();
        context.Evidence.Should().HaveCount(3).And.OnlyContain(item => item.Disclosure == ModelEvidenceDisclosure.LocalOnly);
        using var reference = JsonDocument.Parse(context.Evidence[2].Content);
        reference.RootElement.GetProperty("RelativePath").GetString().Should().Be(@"references\notes.md");
        reference.RootElement.GetProperty("Content").GetString().Should().Be(document.References[0].Text);
        var oversized = () => OwnedSkillGuidanceContext.Create(document, request, body + "x", now, now.AddMinutes(1));
        oversized.Should().Throw<InvalidDataException>();
        var expanded = OwnedSkillGuidanceContext.Create(
            OwnedDeclarativeSkillDocument.Create("example", "1.0.0", "Example", "Description", "Literal"),
            request, "Original", now, now.AddSeconds(5), "Exact input expansion");
        expanded.Evidence.Should().HaveCount(3);
        expanded.Evidence[2].Content.Should().Be("Exact input expansion");
        expanded.ExpiresAt.Should().Be(now.AddSeconds(5));
    }

    [Fact]
    public void ExactComponentFileIdentitiesAndCompleteBundleBytesParticipateInRevisionComparison()
    {
        var snapshot = new OwnedDeclarativeSkillSnapshot(Package(), "directory", "manifest", "instructions")
        { ReferenceIdentities = ImmutableDictionary<string, string>.Empty.Add(@"references\notes.md", "file") };
        snapshot.TotalBytes.Should().Be(snapshot.Document.TotalBytes);
        (snapshot with { StoredBytes = 999 }).TotalBytes.Should().Be(999);
        snapshot.IsSameRevision(snapshot with { }).Should().BeTrue();
        snapshot.IsSameRevision(snapshot with { ReferenceIdentities = snapshot.ReferenceIdentities.SetItem(@"references\notes.md", "replacement") }).Should().BeFalse();
        snapshot.IsSameRevision(snapshot with { ReferenceIdentities = ImmutableDictionary<string, string>.Empty.Add("another.md", "file") }).Should().BeFalse();
        snapshot.IsSameRevision(snapshot with { ReferenceIdentities = ImmutableDictionary<string, string>.Empty }).Should().BeFalse();
        snapshot.IsSameRevision(snapshot with
        { Document = OwnedDeclarativeSkillDocument.Parse(snapshot.Document.ManifestBytes.Span, snapshot.Document.InstructionBytes.Span) }).Should().BeFalse();
    }
}
