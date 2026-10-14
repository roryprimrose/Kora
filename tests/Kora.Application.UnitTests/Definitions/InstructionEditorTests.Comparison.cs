using System.Text;

using AwesomeAssertions;

using Kora.Core.Artifacts;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.Definitions;

public sealed partial class InstructionEditorTests
{
    [Theory]
    [InlineData(ArtifactKind.Instruction, false, "same", "\n")]
    [InlineData(ArtifactKind.Instruction, true, "whitespace", "\r\n")]
    [InlineData(ArtifactKind.Instruction, true, "legacy", "\r")]
    [InlineData(ArtifactKind.Prompt, false, "v2", "\n")]
    [InlineData(ArtifactKind.Prompt, true, "metadata", "\r\n")]
    [InlineData(ArtifactKind.Instruction, false, "new", "\n")]
    [InlineData(ArtifactKind.Prompt, false, "new", "\r\n")]
    [InlineData(ArtifactKind.Instruction, false, "invalid", "\n")]
    [InlineData(ArtifactKind.Instruction, false, "empty", "\n")]
    [InlineData(ArtifactKind.Prompt, true, "unsupported", "\n")]
    public async Task CompareKeepsFrozenRawSourceSeparateFromCanonicalSaveAndHasNoStoreOrUseSideEffects(ArtifactKind kind, bool bom, string state, string newline)
    {
        await using var f = new Fixture(kind);
        var text = string.Join(newline, "---", state is "legacy" ? "name: example" : state is "v2" ? "kora-format: 2" : "kora-format: 1",
            "description: Test", "attribution: Exact attribution", "name: example",
            "---", state is "v2" ? "Exact ${input:topic}" : "  Exact body  ", "");
        if (state is "legacy") { text = text.Replace("name: example" + newline, "", StringComparison.Ordinal); }
        var original = new OwnedInstructionSnapshot("example", new string('a', 48), new string('b', 48),
            ArtifactFileDocument.Parse(ArtifactFileDocument.Encode(text, bom), kind, "kora-profile", "example"));
        var draft = state switch
        {
            "whitespace" or "legacy" => text + " ",
            "metadata" => text.Replace("Exact attribution", "Changed attribution", StringComparison.Ordinal),
            "invalid" => "PRIVATE_INVALID_NO_HEADER",
            "empty" => string.Empty,
            "unsupported" => text.Replace("kora-format: 1", "kora-format: 99", StringComparison.Ordinal),
            _ => text,
        };
        var baseline = state is "new" ? null : original;
        var catalogue = f.Catalogue.Candidates;
        var preferences = f.State.Text;
        var writes = f.State.Writes;
        var result = await f.Actions.CompareDraft("example", baseline, draft, NullLogger.Instance, f.Eligible, CancellationToken.None);
        result.Original.Should().BeSameAs(baseline);
        result.Draft.Should().Be(draft);
        result.RawByteCount.Should().Be(Encoding.UTF8.GetByteCount(draft) + (baseline?.Document.HasBom == true ? 3 : 0));
        result.RawDigest.Should().Be(ArtifactSourceComparison.Digest(ArtifactFileDocument.Encode(draft, baseline?.Document.HasBom == true)));
        result.ExactBytes.Should().Be(state is "same" or "v2");
        result.Validation.Supported.Should().Be(state is not ("invalid" or "empty" or "unsupported"));
        result.Summary.Should().Contain("historical").And.Contain("NOT canonical Save bytes").And.Contain("No disk read/reload");
        if (state is "new") { result.Summary.Should().Contain("missing/new baseline"); }
        if (state is "legacy") { result.RawDigest.Should().NotBe(result.Validation.Digest); }
        if (state is "metadata") { result.ChangedFields.Should().Contain("attribution"); }
        if (state is "invalid" or "empty") { result.ChangedFields.Should().Contain("unavailable"); }
        if (result.Validation.Supported)
        { result.Validation.Digest.Should().Be(f.Actions.PrepareSave("example", baseline, draft).Definition.Digest); }
        f.Actions.IsDraftValidationCurrent(result.Validation).Should().BeTrue();
        f.Files.Reads.Should().Be(0); f.Files.Writes.Should().Be(0);
        f.Audit.Events.Should().BeEmpty();
        f.Catalogue.Candidates.Should().BeSameAs(catalogue);
        f.State.Text.Should().Be(preferences); f.State.Writes.Should().Be(writes);
    }

    [Theory]
    [InlineData("characters")]
    [InlineData("utf8")]
    [InlineData("unicode")]
    [InlineData("kind")]
    [InlineData("name")]
    [InlineData("filename")]
    [InlineData("cancel")]
    [InlineData("host")]
    [InlineData("catalogue")]
    [InlineData("origin")]
    public async Task CompareRejectsOverBudgetInvalidEncodingWrongIdentityAndStaleAdmission(string failure)
    {
        await using var f = new Fixture();
        using var cancellation = new CancellationTokenSource();
        var original = f.Files.Current!;
        var draft = original.Document.Text;
        if (failure is "characters") { draft = new string('a', 65537); }
        if (failure is "utf8") { draft = new string('\u4e00', 30000); }
        if (failure is "unicode") { draft = "\ud800"; }
        if (failure is "kind")
        {
            original = new("example", "aaa", "bbb", ArtifactFileDocument.Parse(ArtifactFileDocument.Encode(
                "---\nname: example\ndescription: Test\n---\nBody", false), ArtifactKind.Prompt, "kora-profile", "example"));
        }
        if (failure is "name") { original = original with { Name = "other" }; }
        if (failure is "filename") { original = original with { CanonicalFileName = "Other.instructions.md" }; }
        f.Control.BeforeOperation = () =>
        {
            if (failure is "cancel") { cancellation.Cancel(); }
            if (failure is "host") { f.Open = false; }
            if (failure is "catalogue") { f.Catalogue.Publish(f.Catalogue.Candidates); }
        };
        using var incoming = failure is "origin"
            ? HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.HostSystem), HostActivityLayer.Application, HostOperation.Request) : null;
        var compare = () => f.Actions.CompareDraft("example", original, draft, NullLogger.Instance, f.Eligible, cancellation.Token);
        await compare.Should().ThrowAsync<Exception>();
        f.Files.Reads.Should().Be(0); f.Files.Writes.Should().Be(0);
        f.Audit.Events.Should().BeEmpty();
    }
}
