using System.Text;
using AwesomeAssertions;
using Kora.Application.Mcp;
using Kora.Application.Skills;
using Kora.Core.Mcp;
using Kora.Core.Skills;

namespace Kora.Application.UnitTests.Skills;

public sealed class OwnedMcpSkillGuidanceRefinementTests
{
    private const string Schema = """{"type":"object","additionalProperties":false,"properties":{},"required":[]}""";

    [Theory]
    [InlineData("replace")]
    [InlineData("retire")]
    [InlineData("dispose")]
    [InlineData("current")]
    public void DescriptorAdmissionIsExactNonrenewingAndHasNoAccountOrSourceRead(string change)
    {
        var account = new McpAccountReference(Guid.NewGuid(), new(1), Guid.NewGuid(), new(new("https://example.com/mcp")));
        var source = new McpConnectionDescriptor(new string('a', 64), "Test", "unread-test-source",
            new string('b', 64), account.Destination, account, 1);
        var tool = new McpDiscoveredTool("search", new(Schema));
        using var policy = new McpReadOnlyPolicy([new(account.Destination, account, source.SourceRevision, tool.Name, tool.Schema.Digest)]);
        var live = true;
        var admission = policy.ObservePreparedDescriptor(source, tool, () => live, TimeProvider.System, DateTimeOffset.MaxValue);
        var captured = policy.CaptureGuidanceDescriptor(admission.Reference);
        captured.Should().BeSameAs(admission);
        captured.IsCurrent().Should().BeTrue();
        switch (change)
        {
            case "replace": policy.ObservePreparedDescriptor(source, tool, () => true, TimeProvider.System, DateTimeOffset.MaxValue); break;
            case "retire": admission.Retire(); break;
            case "dispose": policy.Dispose(); break;
            case "current": live = false; break;
        }
        captured.IsCurrent().Should().BeFalse();
        live = true;
        captured.IsCurrent().Should().BeFalse();
        if (change is not "replace")
        { FluentActions.Invoking(() => policy.CaptureGuidanceDescriptor(admission.Reference)).Should().Throw<InvalidOperationException>(); }
        FluentActions.Invoking(() => policy.CaptureGuidanceDescriptor(new(source.ServerId, source.SourceRevision, "other", tool.Schema.Digest)))
            .Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("unmapped")]
    [InlineData("withdrawn")]
    [InlineData("disposed")]
    public void DescriptorCannotBeMadeFromUnknownOrWithdrawnHostPolicy(string failure)
    {
        var account = new McpAccountReference(Guid.NewGuid(), new(1), Guid.NewGuid(), new(new("https://example.com/mcp")));
        var source = new McpConnectionDescriptor(new string('a', 64), "Test", "unread-test-source",
            new string('b', 64), account.Destination, account, 1);
        var tool = new McpDiscoveredTool("search", new(Schema));
        using var policy = new McpReadOnlyPolicy(failure is "unmapped" ? []
            : [new(account.Destination, account, source.SourceRevision, tool.Name, tool.Schema.Digest)]);
        var reference = new DeclarativeMcpToolReference(source.ServerId, source.SourceRevision, tool.Name, tool.Schema.Digest);
        if (failure is "disposed") { policy.Dispose(); }
        if (failure is not "missing")
        {
            FluentActions.Invoking(() => policy.ObservePreparedDescriptor(source, tool,
                () => failure is not "withdrawn", TimeProvider.System, DateTimeOffset.MaxValue)).Should().Throw<Exception>();
        }
        FluentActions.Invoking(() => policy.CaptureGuidanceDescriptor(reference)).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ReopenedMcpNewVersionPreservesOriginalManifestOrderingAndAllOtherBytes()
    {
        var original = OwnedDeclarativeSkillDocument.CreateMcp("example", "1.0.0", "Example", "Description", "OLD",
            new(new string('a', 64), new string('b', 64), "search", new McpToolSchema(Schema).Digest));
        var lines = original.ManifestText.Split('\n');
        (lines[0], lines[1]) = (lines[1], lines[0]);
        original = OwnedDeclarativeSkillDocument.Parse(Encoding.UTF8.GetBytes(string.Join('\n', lines)), original.InstructionBytes.Span);
        var draft = OwnedSkillGuidanceRefinementService.CreateReopenedMcpDraft(new(original, "d", "m", "i"), "2.0.0", "NEW");
        draft.ManifestText.Should().Be(original.ManifestText.Replace("version: 1.0.0\n", "version: 2.0.0\n", StringComparison.Ordinal));
        draft.McpReference.Should().Be(original.McpReference);
        draft.Guidance.Should().Be("NEW");
        var unsupported = OwnedDeclarativeSkillDocument.Create("example", "1.0.0", "Example", "Description", "OLD");
        FluentActions.Invoking(() => OwnedSkillGuidanceRefinementService.CreateReopenedMcpDraft(new(unsupported, "d", "m", "i"), "2.0.0", "NEW"))
            .Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CapturedPolicyAdmissionCannotReviveAfterExpiryOrClockRollback(bool rollback)
    {
        var time = new DescriptorTime();
        var account = new McpAccountReference(Guid.NewGuid(), new(1), Guid.NewGuid(), new(new("https://example.com/mcp")));
        var source = new McpConnectionDescriptor(new string('a', 64), "Test", "unread-test-source",
            new string('b', 64), account.Destination, account, 1);
        var tool = new McpDiscoveredTool("search", new(Schema));
        using var policy = new McpReadOnlyPolicy([new(account.Destination, account, source.SourceRevision, tool.Name, tool.Schema.Digest)]);
        var originalTime = time.Now;
        var admission = policy.ObservePreparedDescriptor(source, tool, static () => true, time, time.Now.AddMinutes(2));
        time.Now += rollback ? TimeSpan.FromMinutes(-1) : TimeSpan.FromMinutes(2);
        admission.IsCurrent().Should().BeFalse();
        time.Now = originalTime;
        admission.IsCurrent().Should().BeFalse();
    }

    private sealed class DescriptorTime : TimeProvider
    {
        internal DateTimeOffset Now = new(2026, 11, 8, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
