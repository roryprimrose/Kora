using AwesomeAssertions;
using Kora.Core.Mcp;

namespace Kora.Core.UnitTests.Mcp;

public sealed class McpResourceSelectionTests
{
    [Fact]
    public void TemplateChoiceDoesNotEnableAllResourcesButRetainsExactExplicitUriExclusions()
    {
        var server = new string('a', 64);
        var initial = McpCapabilitySelection.Default;
        initial.IsExplicitlyDisabled(server, McpCapabilityKind.Resource, "data://item").Should().BeFalse();
        var templateOnly = initial.SetCategory(server, McpCapabilityCategory.Resources, false)
            .SetItem(server, McpCapabilityKind.ResourceTemplate, "data://{value}", true);
        templateOnly.IsEnabled(server, McpCapabilityKind.ResourceTemplate, "data://{value}").Should().BeTrue();
        templateOnly.IsEnabled(server, McpCapabilityKind.Resource, "data://item").Should().BeFalse();
        templateOnly.IsExplicitlyDisabled(server, McpCapabilityKind.Resource, "data://item").Should().BeFalse();
        var excluded = templateOnly.SetItem(server, McpCapabilityKind.Resource, "data://item", false);
        excluded.IsExplicitlyDisabled(server, McpCapabilityKind.Resource, "data://item").Should().BeTrue();
        excluded.IsExplicitlyDisabled(server, McpCapabilityKind.Resource, "data://other").Should().BeFalse();
        excluded.IsExplicitlyDisabled(server, McpCapabilityKind.Prompt, "review").Should().BeFalse();
        excluded.IsExplicitlyDisabled(KoraMcpCatalog.ServerId, McpCapabilityKind.Resource, "data://item").Should().BeFalse();
        excluded.IsExplicitlyDisabled(server, McpCapabilityKind.ResourceTemplate, "data://{value}").Should().BeFalse();
        excluded.SetItem(server, McpCapabilityKind.Resource, "data://item", true)
            .IsExplicitlyDisabled(server, McpCapabilityKind.Resource, "data://item").Should().BeFalse();
        excluded.SetCategory(server, McpCapabilityCategory.Resources, true).Categories.Single().Items.Should().BeEmpty();
    }
}
