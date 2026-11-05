using AwesomeAssertions;

using Kora.Core.Mcp;

namespace Kora.Windows.IntegrationTests;

public sealed class McpCapabilityRowsTests
{
    [Theory]
    [InlineData("search", "search")]
    [InlineData(" SEARCH ", "search")]
    [InlineData("external-source.json", "search")]
    [InlineData("User", "search")]
    [InlineData("builtin.kora", null)]
    [InlineData("private schema marker", null)]
    public void LiveMatchingUsesDisplayedMetadataNotSchemasOrAuthority(string query, string? expected)
    {
        var rows = new McpCapabilityRows();
        rows.SetSnapshot([External(), BuiltIn()]);
        rows.ApplyFilter(query, McpCapabilityCategory.Tools);
        if (expected is not null) { rows.Visible.Single().Capability.Id.Should().Be(expected); }
        else { rows.Visible.Should().BeEmpty(); }
        rows.Total.Should().Be(2);
    }

    [Fact]
    public void ClearingAndCategoryChangesPreserveSelectionAndAllOriginalChoiceValues()
    {
        var tool = External();
        var builtin = BuiltIn();
        var resource = tool with { Capability = new(McpCapabilityKind.Resource, "data://reference", "Reference", "{}") };
        var template = tool with { Capability = new(McpCapabilityKind.ResourceTemplate, "data://{value}", "Template", "{}") };
        var rows = new McpCapabilityRows();
        rows.SetSnapshot([tool, builtin, resource, template]);
        rows.ApplyFilter("search", McpCapabilityCategory.Tools);
        rows.CountText.Should().Be("1 of 2 capabilities");
        rows.Select(tool);
        rows.ApplyFilter("no match", McpCapabilityCategory.Tools);
        rows.Visible.Should().BeEmpty();
        rows.Selected.Should().BeSameAs(tool);
        rows.ClearFilter();
        rows.CountText.Should().Be("2 of 2 capabilities");
        rows.Category.Should().Be(McpCapabilityCategory.Tools);
        rows.Visible.Should().Equal(tool, builtin);
        rows.ApplyFilter(null, McpCapabilityCategory.Resources);
        rows.Visible.Should().Equal(resource, template);
        rows.Selected.Should().BeSameAs(tool);
        tool.IsSelected.Should().BeFalse();
        builtin.IsSelected.Should().BeTrue();
        builtin.IsEffectiveConsideration.Should().BeFalse();
        builtin.Label.Should().Contain("Not currently available").And.NotContain("Disabled");
    }

    [Fact]
    public void FilterDoesNotRefreshMetadataOrRewriteDraftChoicesAndPrivateClearDropsTheSnapshot()
    {
        var reads = 0;
        IEnumerable<McpCapabilityRow> Snapshot()
        {
            reads++;
            yield return External();
            yield return BuiltIn();
        }
        var rows = new McpCapabilityRows();
        rows.SetSnapshot(Snapshot());
        var selected = rows.Visible[0];
        rows.Select(selected);
        foreach (var query in new[] { "disabled", "built-in", "missing", "", "Search" })
        { rows.ApplyFilter(query, null); }
        rows.ClearFilter();
        reads.Should().Be(1);
        rows.Selected.Should().BeSameAs(selected);
        rows.Visible[0].IsSelected.Should().BeFalse();
        rows.ClearPrivate();
        rows.Filter.Should().BeEmpty();
        rows.Visible.Should().BeEmpty();
        rows.Selected.Should().BeNull();
        rows.Category.Should().BeNull();
        rows.CountText.Should().Be("0 of 0 capabilities");
    }

    [Fact]
    public void EqualNamesOnDifferentServersAreDistinctAndInvisibleRowsCannotRetargetSelection()
    {
        var first = External();
        var second = first with { ServerId = new string('b', 64), ServerName = "Other" };
        var rows = new McpCapabilityRows();
        rows.SetSnapshot([first, second]);
        rows.Select(first);
        rows.ApplyFilter("Other", null);
        FluentActions.Invoking(() => rows.Select(first)).Should().Throw<InvalidOperationException>();
        rows.Selected.Should().BeSameAs(first);
        rows.SetSnapshot([second]);
        rows.Selected.Should().BeNull();
        FluentActions.Invoking(() => rows.SetSnapshot([second, second])).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => rows.ApplyFilter("", (McpCapabilityCategory)99)).Should().Throw<ArgumentOutOfRangeException>();
        second.ToString().Should().Be(nameof(McpCapabilityRow));
    }

    private static McpCapabilityRow External() => new(new string('a', 64), "Reference server", "external-source.json", "User",
        new(McpCapabilityKind.Tool, "search", "Search", """{"private":"private schema marker"}"""), false, false);

    private static McpCapabilityRow BuiltIn() => new("builtin.kora", "Kora", "Host catalogue", "Built-in",
        new(McpCapabilityKind.Tool, "sessions.list", "List sessions", "{}"), true, false, "Server disabled");
}