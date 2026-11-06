using AwesomeAssertions;
using Kora.Core.Mcp;
using Kora.Core.Configuration;

namespace Kora.Core.UnitTests.Mcp;

public sealed class McpCapabilitySelectionTests
{
    private static readonly string Server = new('a', 64);
    private static readonly string Other = new('b', 64);

    [Theory]
    [InlineData(McpCapabilityKind.Tool, McpCapabilityCategory.Tools)]
    [InlineData(McpCapabilityKind.Resource, McpCapabilityCategory.Resources)]
    [InlineData(McpCapabilityKind.ResourceTemplate, McpCapabilityCategory.Resources)]
    [InlineData(McpCapabilityKind.Prompt, McpCapabilityCategory.Prompts)]
    public void DefaultsEnableEveryKindAndBulkChangesReplaceExceptions(McpCapabilityKind kind, McpCapabilityCategory category)
    {
        var defaults = McpCapabilitySelection.Default;
        defaults.IsEnabled(Server, kind, "first").Should().BeTrue();
        var excluded = defaults.SetItem(Server, kind, "first", false);
        excluded.IsEnabled(Server, kind, "first").Should().BeFalse();
        excluded.IsEnabled(Other, kind, "first").Should().BeTrue();
        excluded.IsEnabled(Server, kind, "new-item").Should().BeTrue();
        var off = excluded.SetCategory(Server, category, false);
        off.IsEnabled(Server, kind, "first").Should().BeFalse();
        off.IsEnabled(Server, kind, "new-item").Should().BeFalse();
        off.Categories.Single().Items.Should().BeEmpty();
        var mixed = off.SetItem(Server, kind, "first", true);
        mixed.IsEnabled(Server, kind, "first").Should().BeTrue();
        mixed.IsEnabled(Server, kind, "new-item").Should().BeFalse();
        mixed.SetItem(Server, kind, "first", false).Categories.Single().Items.Should().ContainSingle(item => !item.Enabled);
        mixed.SetCategory(Server, category, true).IsEnabled(Server, kind, "new-item").Should().BeTrue();
        excluded.Fingerprint(Server).Should().NotBe(defaults.Fingerprint(Server));
        excluded.Fingerprint(Other).Should().Be(defaults.Fingerprint(Other));
        defaults.IsEnabled(Server, kind, "first").Should().BeTrue();
    }

    [Fact]
    public void ResourcesAndTemplatesShareDefaultsButNotIndividualIdentities()
    {
        var state = McpCapabilitySelection.Default.SetItem(Server, McpCapabilityKind.Resource, "data://item", false);
        state.IsEnabled(Server, McpCapabilityKind.ResourceTemplate, "data://item").Should().BeTrue();
        state.SetCategory(Server, McpCapabilityCategory.Resources, false)
            .IsEnabled(Server, McpCapabilityKind.ResourceTemplate, "data://new").Should().BeFalse();
        var two = state.SetCategory(Other, McpCapabilityCategory.Prompts, false);
        two.Fingerprint(Server).Should().Be(state.Fingerprint(Server));
        new McpCapabilitySelection(two.Categories.Reverse()).Fingerprint(Server).Should().Be(two.Fingerprint(Server));
    }

    [Theory]
    [InlineData("")]
    [InlineData("invalid")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public void InvalidServerIdentitiesAreRejected(string identity)
    {
        FluentActions.Invoking(() => McpCapabilitySelection.Default.IsEnabled(identity, McpCapabilityKind.Tool, "tool"))
            .Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => new McpItemSelection(identity, false)).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void InvalidKindsIdentitiesDuplicatesAndInventoryBoundsAreRejected()
    {
        FluentActions.Invoking(() => McpCapabilitySelection.Category((McpCapabilityKind)99)).Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => McpCapabilitySelection.Default.SetCategory(Server, (McpCapabilityCategory)99, false))
            .Should().Throw<ArgumentOutOfRangeException>();
        foreach (var identity in new[] { "", " \t", "line\n", new string('a', 2049) })
        { FluentActions.Invoking(() => McpCapabilitySelection.ItemKey(McpCapabilityKind.Tool, identity)).Should().Throw<ArgumentException>(); }
        FluentActions.Invoking(() => new McpCapabilitySelection(null!)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => new McpCategorySelection(Server, McpCapabilityCategory.Tools, true, null!))
            .Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => McpCapabilitySelection.Default.Fingerprint(null!)).Should().Throw<ArgumentNullException>();
        var category = new McpCategorySelection(Server, McpCapabilityCategory.Tools, false, []);
        FluentActions.Invoking(() => new McpCapabilitySelection([category, category])).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => new McpCapabilitySelection([null!])).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => new McpCapabilitySelection(Enumerable.Repeat(category, McpCapabilitySelection.MaximumCategories + 1)))
            .Should().Throw<ArgumentException>();
        var item = new McpItemSelection(new string('c', 64), false);
        FluentActions.Invoking(() => new McpCategorySelection(Server, McpCapabilityCategory.Tools, true, [item, item]))
            .Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => new McpCategorySelection(Server, McpCapabilityCategory.Tools, true, [null!]))
            .Should().Throw<ArgumentException>();
        var items = Enumerable.Range(0, McpCapabilitySelection.MaximumOverrides + 1)
            .Select(index => new McpItemSelection(index.ToString("x64", System.Globalization.CultureInfo.InvariantCulture), false)).ToArray();
        FluentActions.Invoking(() => new McpCategorySelection(Server, McpCapabilityCategory.Tools, true, items))
            .Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => new McpCapabilitySelection([
            new(Server, McpCapabilityCategory.Tools, true, items[..512]),
            new(Server, McpCapabilityCategory.Prompts, true, items[..513])])).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void CategoryAdmissionUsesOverridesAndMutationsCannotRewindOrOverflowItsRevision()
    {
        var defaults = McpCapabilitySelection.Default;
        defaults.MayConsider(Server, McpCapabilityCategory.Tools).Should().BeTrue();
        var off = defaults.SetCategory(Server, McpCapabilityCategory.Tools, false);
        off.MayConsider(Server, McpCapabilityCategory.Tools).Should().BeFalse();
        var mixed = off.SetItem(Server, McpCapabilityKind.Tool, "search", true);
        mixed.MayConsider(Server, McpCapabilityCategory.Tools).Should().BeTrue();
        var enabled = mixed.SetCategory(Server, McpCapabilityCategory.Tools, true);
        enabled.MayConsider(Server, McpCapabilityCategory.Tools).Should().BeTrue();
        var toggled = enabled.SetItem(Server, McpCapabilityKind.Tool, "search", false)
            .SetItem(Server, McpCapabilityKind.Tool, "search", true);
        toggled.Fingerprint(Server).Should().NotBe(enabled.Fingerprint(Server));
        var exhausted = new McpCapabilitySelection([new(Server, McpCapabilityCategory.Tools, true, [], long.MaxValue)]);
        FluentActions.Invoking(() => exhausted.SetCategory(Server, McpCapabilityCategory.Tools, false)).Should().Throw<InvalidOperationException>();
        FluentActions.Invoking(() => exhausted.SetItem(Server, McpCapabilityKind.Tool, "search", false)).Should().Throw<InvalidOperationException>();
        FluentActions.Invoking(() => new McpCategorySelection(Server, McpCapabilityCategory.Tools, true, [], -1))
            .Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Reserved_built_in_slot_preserves_but_does_not_expand_external_capacity()
    {
        var external = Enumerable.Range(0, ArtifactDirectorySettings.MaximumIncludedMcpServers)
            .Select(index => index.ToString("x64", System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        var settings = new ArtifactDirectorySettings([], [], [], [], [], [.. external, KoraMcpCatalog.ServerId]);
        settings.IncludedMcpServerIds.Should().HaveCount(129);
        var tooManyExternal = () => new ArtifactDirectorySettings([], [], [], [], [], [.. external, new string('f', 64)]);
        tooManyExternal.Should().Throw<ArgumentException>();
        var categories = external.SelectMany(server => Enum.GetValues<McpCapabilityCategory>()
            .Select(category => new McpCategorySelection(server, category, true, []))).ToArray();
        var complete = new McpCapabilitySelection([.. categories, .. Enum.GetValues<McpCapabilityCategory>()
            .Select(category => new McpCategorySelection(KoraMcpCatalog.ServerId, category, true, []))]);
        complete.Categories.Should().HaveCount(387);
        var extraExternal = () => new McpCapabilitySelection([.. categories,
            new(new string('f', 64), McpCapabilityCategory.Tools, true, [])]);
        extraExternal.Should().Throw<ArgumentException>();
    }
}
