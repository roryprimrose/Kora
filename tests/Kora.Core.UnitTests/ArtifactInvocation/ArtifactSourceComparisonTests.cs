using AwesomeAssertions;

using Kora.Core.Artifacts;

namespace Kora.Core.UnitTests.Artifacts;

public sealed class ArtifactSourceComparisonTests
{
    [Theory]
    [InlineData("same", "(unchanged bytes)")]
    [InlineData("new", "new file (all fields and body)")]
    [InlineData("delete", "delete entire original")]
    [InlineData("bom", "exact source bytes/formatting")]
    [InlineData("whitespace", "exact source bytes/formatting")]
    [InlineData("newline", "exact source bytes/formatting")]
    [InlineData("body", "body, exact source bytes/formatting")]
    [InlineData("metadata", "description, attribution, exact source bytes/formatting")]
    public void SharedFieldRulesRetainExactByteChangesAndMetadataTruth(string change, string expected)
    {
        const string text = "---\nname: example\ndescription: Test\nattribution: Original\n---\nBody";
        var original = ArtifactFileDocument.Parse(ArtifactFileDocument.Encode(text, false), ArtifactKind.Instruction, "kora-profile", "example");
        var draft = change switch
        {
            "whitespace" => text + " ",
            "newline" => text.Replace("\n", "\r\n", StringComparison.Ordinal),
            "body" => text + " changed",
            "metadata" => text.Replace("description: Test\nattribution: Original", "description: Updated", StringComparison.Ordinal),
            _ => text,
        };
        var proposed = ArtifactFileDocument.Parse(ArtifactFileDocument.Encode(draft, change is "bom"), ArtifactKind.Instruction, "kora-profile", "example");
        ArtifactSourceComparison.ChangedFields(change is "new" ? null : original, change is "delete" ? null : proposed).Should().Be(expected);
        ArtifactSourceComparison.Digest(original.CopyBytes()).Should().Be(original.Definition.Digest);
    }
}
