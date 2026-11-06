using System.Text;
using AwesomeAssertions;
using Kora.Core.Mcp;
using Kora.Core.Skills;

namespace Kora.Core.UnitTests.Skills;

public sealed class DeclarativeMcpToolReferenceTests
{
    private static readonly string Digest = new('a', 64);
    private static string Text => "---\nname: reference-review\nversion: 1.0.0\ndescription: Review one reference\n"
        + $"format: {DeclarativeMcpToolReference.Format}\nallowed-tools: mcp.Search\nmcp-server: {Digest}\n"
        + $"mcp-source-revision: {Digest}\nmcp-schema-digest: {Digest}\n---\nSummarize the observed reference.";
    private static SharedSkillSnapshot Parse(string text, bool other = false) =>
        SharedSkillSnapshot.Parse(Guid.NewGuid(), "reference-review\\SKILL.md", Encoding.UTF8.GetBytes(text), other);

    [Fact]
    public void ExactReferenceIsDataOnlyAndPreservedByHostSnapshotProjections()
    {
        var snapshot = Parse(Text);
        var reference = snapshot.McpReference!;
        reference.ServerId.Should().Be(Digest);
        reference.SourceRevision.Should().Be(Digest);
        reference.ToolName.Should().Be("Search");
        reference.SchemaDigest.Should().Be(Digest);
        reference.ProtocolVersion.Should().Be(McpProtocol.Version);
        snapshot.DeclaredTools.Should().Equal("mcp.Search");
        snapshot.UnavailableReasons.Should().Equal("declared-tools-unavailable");
        snapshot.IsInstructionCompatible.Should().BeFalse();
        snapshot.IsAvailableForInvocation.Should().BeFalse();
        snapshot.WithUnavailableReason("stale").McpReference.Should().Be(reference);
        snapshot.WithPackageDirectoryIdentity(new string('b', 48)).McpReference.Should().Be(reference);
    }

    [Theory]
    [InlineData("server")]
    [InlineData("source")]
    [InlineData("schema")]
    [InlineData("tool")]
    [InlineData("long-tool")]
    public void UnknownOrNoncanonicalBindingsAreRejected(string field)
    {
        var create = () => new DeclarativeMcpToolReference(field is "server" ? "reference" : Digest,
            field is "source" ? new string('A', 64) : Digest,
            field is "tool" ? "search other" : field is "long-tool" ? new string('s', 65) : "search",
            field is "schema" ? new string('z', 64) : Digest);
        create.Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData("format")]
    [InlineData("tool")]
    [InlineData("tool-prefix")]
    [InlineData("server")]
    [InlineData("source")]
    [InlineData("schema")]
    [InlineData("unknown")]
    [InlineData("duplicate")]
    [InlineData("references")]
    [InlineData("bad-digest")]
    [InlineData("many-tools")]
    [InlineData("unversioned")]
    public void MissingUnknownAmbiguousAndAlternateDeclarationsNeverBecomeCompatible(string fault)
    {
        var text = Text;
        if (fault is "format") { text = text.Replace(DeclarativeMcpToolReference.Format, "future", StringComparison.Ordinal); }
        if (fault is "tool") { text = text.Replace("allowed-tools: mcp.Search\n", string.Empty, StringComparison.Ordinal); }
        if (fault is "tool-prefix") { text = text.Replace("mcp.Search", "network.get_web_page", StringComparison.Ordinal); }
        if (fault is "server") { text = text.Replace($"mcp-server: {Digest}\n", string.Empty, StringComparison.Ordinal); }
        if (fault is "source") { text = text.Replace($"mcp-source-revision: {Digest}\n", string.Empty, StringComparison.Ordinal); }
        if (fault is "schema") { text = text.Replace($"mcp-schema-digest: {Digest}\n", string.Empty, StringComparison.Ordinal); }
        if (fault is "unknown" or "duplicate" or "references")
        {
            text = text.Replace("description: Review one reference\n", fault switch
            {
                "unknown" => "description: Review one reference\nendpoint: selected\n",
                "duplicate" => "description: Review one reference\nmcp-server: duplicate\n",
                _ => "description: Review one reference\nreferences: reference.md\n",
            }, StringComparison.Ordinal);
        }
        if (fault is "bad-digest") { text = text.Replace($"mcp-schema-digest: {Digest}", "mcp-schema-digest: newest", StringComparison.Ordinal); }
        if (fault is "many-tools") { text = text.Replace("mcp.Search", "mcp.Search mcp.other", StringComparison.Ordinal); }
        if (fault is "unversioned") { text = text.Replace($"format: {DeclarativeMcpToolReference.Format}\n", string.Empty, StringComparison.Ordinal); }
        var snapshot = Parse(text);
        snapshot.IsInstructionCompatible.Should().BeFalse();
        snapshot.UnavailableReasons.Should().NotEqual(["declared-tools-unavailable"]);
    }

    [Fact]
    public void AdditionalFilesAndWebToolsNeverAcquireMcpCompatibility()
    {
        Parse(Text, other: true).UnavailableReasons.Should().Contain("additional-package-files-not-supported");
        var web = Parse(Text.Replace($"format: {DeclarativeMcpToolReference.Format}\n", string.Empty, StringComparison.Ordinal)
            .Replace("allowed-tools: mcp.Search", "allowed-tools: network.get_web_page", StringComparison.Ordinal));
        web.McpReference.Should().BeNull();
        web.UnavailableReasons.Should().Contain("unsupported-mcp-reference-format");
    }

    [Theory]
    [InlineData("allowed-tools: mcp.Search")]
    [InlineData("mcp-server: ")]
    [InlineData("mcp-source-revision: ")]
    [InlineData("mcp-schema-digest: ")]
    public void CorrectFieldCountCannotHideAMissingRequiredMcpBinding(string field)
    {
        var old = field.EndsWith(' ') ? field + Digest : field;
        Parse(Text.Replace(old, "references: none", StringComparison.Ordinal))
            .UnavailableReasons.Should().Contain("invalid-mcp-reference");
    }
}
