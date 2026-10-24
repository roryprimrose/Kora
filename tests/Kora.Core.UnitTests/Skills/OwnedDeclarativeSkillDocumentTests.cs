using System.Text;
using AwesomeAssertions;
using Kora.Core.Skills;

namespace Kora.Core.UnitTests.Skills;

public sealed class OwnedDeclarativeSkillDocumentTests
{
    private static OwnedDeclarativeSkillDocument Document(string guidance = "Literal workflow.\r\nowned skill save\n```ps1\nnot executed\n```") =>
        OwnedDeclarativeSkillDocument.Create("daily-review", "1.0.0", "Daily review", "Review the day", guidance);

    [Fact]
    public void ExactTwoFileRoundtripPreservesLiteralGuidanceAndIsAlwaysInactive()
    {
        var document = Document();
        var parsed = OwnedDeclarativeSkillDocument.Parse(document.ManifestBytes.Span, document.InstructionBytes.Span);
        parsed.ManifestText.Should().Be("kora-skill-format: 1\nid: daily-review\nversion: 1.0.0\nname: Daily review\ndescription: Review the day\nruntime: local\ntools: none\nreferences: none\n");
        parsed.Guidance.Should().Be(document.Guidance);
        parsed.IsAvailableForInvocation.Should().BeFalse();
        parsed.Id.Should().Be("daily-review");
        parsed.Version.Should().Be("1.0.0");
        parsed.Name.Should().Be("Daily review");
        parsed.Description.Should().Be("Review the day");
    }

    [Theory]
    [InlineData("", "1.0.0")]
    [InlineData("A", "1.0.0")]
    [InlineData("a-", "1.0.0")]
    [InlineData("a--b", "1.0.0")]
    [InlineData("con", "1.0.0")]
    [InlineData("nul", "1.0.0")]
    [InlineData("aux", "1.0.0")]
    [InlineData("prn", "1.0.0")]
    [InlineData("com1", "1.0.0")]
    [InlineData("lpt0", "1.0.0")]
    [InlineData("../a", "1.0.0")]
    [InlineData("a/b", "1.0.0")]
    [InlineData("a\\b", "1.0.0")]
    [InlineData(".owned-skill-test", "1.0.0")]
    [InlineData("a:b", "1.0.0")]
    [InlineData("a", "1.0")]
    [InlineData("a", "01.0.0")]
    [InlineData("a", "1.0.0.0")]
    [InlineData("a", "1.0.x")]
    [InlineData("a", "1.0.0 ")]
    [InlineData("a", "1.0.10000")]
    public void NoncanonicalIdentitiesNeverBecomePaths(string id, string version)
    {
        var action = () => OwnedDeclarativeSkillDocument.RequireIdentity(id, version);
        action.Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData("runtime: local", "runtime: powershell")]
    [InlineData("tools: none", "tools: WebPageGet")]
    [InlineData("references: none", "references: other.md")]
    [InlineData("kora-skill-format: 1", "kora-skill-format: 2")]
    [InlineData("name: Daily review", "name: !execute")]
    [InlineData("name: Daily review", "name: 'Daily review'")]
    [InlineData("name: Daily review", "name: Daily: review")]
    [InlineData("version: 1.0.0", "version: 01.0.0")]
    [InlineData("description: Review the day", "name: duplicate")]
    [InlineData("description: Review the day", "entry: execute.ps1")]
    [InlineData("description: Review the day", "dependencies: script")]
    [InlineData("description: Review the day", "hooks: after-save")]
    [InlineData("description: Review the day", "description: ")]
    [InlineData("description: Review the day", " description: Review")]
    public void ClosedSchemaRejectsExecutableUnknownDuplicateOrMalformedMetadata(string original, string replacement)
    {
        var document = Document();
        var bytes = Encoding.UTF8.GetBytes(document.ManifestText.Replace(original, replacement, StringComparison.Ordinal));
        var parse = () => OwnedDeclarativeSkillDocument.Parse(bytes, document.InstructionBytes.Span);
        parse.Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData("\uFEFFtext")]
    [InlineData("text\0")]
    [InlineData("text\u202Ehidden")]
    [InlineData(" ")]
    public void UnsupportedLiteralTextFailsExplicitly(string guidance)
    {
        var create = () => Document(guidance);
        create.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void StrictEncodingAndAggregateExactByteThresholdsAreEnforced()
    {
        var document = Document("x");
        var count = SharedSkillSnapshot.MaximumBytes - document.ManifestBytes.Length;
        Document(new string('x', count)).InstructionBytes.Length.Should().Be(count);
        var oversized = () => Document(new string('x', count + 1));
        oversized.Should().Throw<InvalidDataException>();
        var empty = () => OwnedDeclarativeSkillDocument.Parse([], "x"u8);
        empty.Should().Throw<InvalidDataException>();
        var invalidManifest = () => OwnedDeclarativeSkillDocument.Parse([0xff], "x"u8);
        invalidManifest.Should().Throw<InvalidDataException>();
        var invalidGuidance = () => OwnedDeclarativeSkillDocument.Parse(document.ManifestBytes.Span, [0xff]);
        invalidGuidance.Should().Throw<InvalidDataException>();
        var invalidUnicode = () => Document("\ud800");
        invalidUnicode.Should().Throw<InvalidDataException>();
        var noGuidance = () => Document("");
        noGuidance.Should().Throw<InvalidDataException>();
        var bom = () => OwnedDeclarativeSkillDocument.Parse(Encoding.UTF8.GetPreamble().Concat(document.ManifestBytes.ToArray()).ToArray(), "x"u8);
        bom.Should().Throw<InvalidDataException>();
        var headerSize = () => OwnedDeclarativeSkillDocument.Parse(new byte[4097], "x"u8);
        headerSize.Should().Throw<InvalidDataException>();
        var lines = () => OwnedDeclarativeSkillDocument.Parse(Encoding.UTF8.GetBytes(document.ManifestText + "\n"), "x"u8);
        lines.Should().Throw<InvalidDataException>();
        var missingSeparator = () => OwnedDeclarativeSkillDocument.Parse(Encoding.UTF8.GetBytes(document.ManifestText.Replace("name: Daily review", "name", StringComparison.Ordinal)), "x"u8);
        missingSeparator.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void IdentityAndScalarExactLengthWhitespaceAndSavedIdentityAreCovered()
    {
        var tooLongId = () => OwnedDeclarativeSkillDocument.RequireIdentity(new string('a', 65), "1.0.0");
        tooLongId.Should().Throw<InvalidDataException>();
        foreach (var value in new[] { new string('a', 129), " Leading", "Trailing ", "?alias", "-alias" })
        {
            var create = () => OwnedDeclarativeSkillDocument.Create("example", "1.0.0", value, "Description", "Guidance");
            create.Should().Throw<InvalidDataException>();
        }
        var description = () => OwnedDeclarativeSkillDocument.Create("example", "1.0.0", "Name", new string('a', 1025), "Guidance");
        description.Should().Throw<InvalidDataException>();
        var document = Document();
        var saved = new OwnedDeclarativeSkillSnapshot(document, "directory", "manifest", "instructions");
        saved.Document.Should().BeSameAs(document);
        saved.DirectoryIdentity.Should().Be("directory");
        saved.ManifestIdentity.Should().Be("manifest");
        saved.InstructionsIdentity.Should().Be("instructions");
        saved.ToString().Should().Be("Daily review (daily-review 1.0.0) - saved, inactive");
        var cause = new IOException("readback");
        new OwnedSkillPublicationUnconfirmedException(cause).InnerException.Should().BeSameAs(cause);
    }
}
