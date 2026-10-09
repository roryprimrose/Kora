using Avalonia.Controls;

using AwesomeAssertions;

using Kora.Definitions.Skills;
using Kora.Core.Skills;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

public sealed class SkillPackageReviewTests
{
    [Fact]
    public void Native_tabs_cover_every_manifest_listed_file_and_use_the_same_original_snapshot()
    {
        var catalogue = EmbeddedSkillCatalogue.Load();
        var renderer = new NativeDetailRenderer(NullLogger<NativeDetailRenderer>.Instance);
        foreach (var package in catalogue.Packages)
        {
            var tabs = SkillPackagesWindow.CreateSourceTabs(catalogue, package, renderer);
            tabs.Should().HaveCount(package.Files.Count);
            for (var i = 0; i < tabs.Length; i++)
            {
                var file = package.Files[i];
                tabs[i].Header.Should().Be(file.Name + (catalogue.Dependents(file.ResourceId).Count > 1 ? " (shared)" : string.Empty));
                var panel = tabs[i].Content.Should().BeOfType<DockPanel>().Subject;
                panel.Children.Should().HaveCount(2);
                var chrome = panel.Children[0].Should().BeOfType<Kora.Controls.NamedTextBlock>().Subject;
                chrome.Text.Should().Contain(file.ResourceId).And.Contain(file.Digest);
                var reader = panel.Children[1].Should().BeOfType<TextBox>().Subject;
                reader.Text.Should().Be(file.Text);
                reader.IsReadOnly.Should().BeTrue();
                reader.IsUndoEnabled.Should().BeFalse();
                reader.ContextMenu.Should().BeNull();
                reader.ContextFlyout.Should().BeNull();
            }
            package.IsAvailableForInvocation.Should().BeFalse();
        }
    }

    [Fact]
    public void Native_review_preserves_BOM_and_line_endings_even_when_rich_renderer_is_unavailable()
    {
        var original = EmbeddedSkillCatalogue.Load();
        var files = original.Packages.SelectMany(package => package.Files).Distinct().ToList();
        var helper = files.Single(file => string.Equals(file.ResourceId, "Kora.Scripts.Shared.SessionControl", StringComparison.Ordinal));
        files[files.IndexOf(helper)] = new(helper.Name, helper.ResourceId, [0xef, 0xbb, 0xbf, 0x61, 13, 10]);
        var catalogue = new SkillPackageCatalogue(original.Packages.Select(package => package.Files[0].ResourceId).ToArray(), files);
        var renderer = new NativeDetailRenderer(NullLogger<NativeDetailRenderer>.Instance, available: false);
        var tabs = SkillPackagesWindow.CreateSourceTabs(catalogue, catalogue.Packages[0], renderer);
        var panel = (DockPanel)tabs[^1].Content!;
        ((TextBox)panel.Children[1]).Text.Should().Be("\uFEFFa\r\n");
        ((TextBlock)panel.Children[0]).Text.Should().Contain("Renderer unavailable");
    }
}
