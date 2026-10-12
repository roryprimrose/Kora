using AwesomeAssertions;
using Kora.Core.Artifacts;

namespace Kora.Windows.IntegrationTests.ArtifactDiscovery;

public sealed partial class WindowsOwnedInstructionStoreTests
{
    [Fact]
    public async Task NativeV2PromptPublicationAndAbsentCopyPreserveTemplateBytesAndNeverSaveFieldDrafts()
    {
        var store = Create(ArtifactKind.Prompt);
        var document = ArtifactFileDocument.Parse(ArtifactFileDocument.Encode(
            "---\r\nkora-format: 2\r\nname: example\r\ndescription: Test\r\nattribution: Exact\r\n---\r\nRequest ${input:topic}\r\n", true),
            ArtifactKind.Prompt, "kora-profile", "example");
        var published = await store.Write("example", null, document, () => true, TestContext.Current.CancellationToken);
        published.Current!.Document.Definition.PromptTemplate!.RequiredNames.Should().Equal("topic");
        published.Current.Document.CopyBytes().Should().Equal(document.CopyBytes());
        var before = published.Current.Document.Definition.Digest;
        published.Current.Document.Definition.PromptTemplate.Expand(new Dictionary<string, string>(StringComparer.Ordinal)
            { ["topic"] = "private-field-value" }, TestContext.Current.CancellationToken).Should().Be("Request private-field-value\r\n");
        var copy = document.WithOwnedCopyName("separate");
        var copied = await store.Write("separate", null, copy, () => true, TestContext.Current.CancellationToken);
        copied.Current!.Document.HasBom.Should().BeTrue();
        copied.Current.Document.Metadata["kora-format"].Should().Be("2");
        (await store.Read("example", TestContext.Current.CancellationToken))!.Document.Definition.Digest.Should().Be(before);
        copied.Current.Document.Text.Should().NotContain("private-field-value");
        var duplicate = await store.Write("separate", null, copy, () => true, TestContext.Current.CancellationToken);
        duplicate.Conflict.Should().BeTrue();
        (await store.Read("separate", TestContext.Current.CancellationToken))!.Document.CopyBytes().Should().Equal(copy.CopyBytes());
    }
}
