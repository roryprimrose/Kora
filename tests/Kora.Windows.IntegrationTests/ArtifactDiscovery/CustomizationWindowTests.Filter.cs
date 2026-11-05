using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;

using AwesomeAssertions;

using Kora.Core.Customizations;
using Kora.NativeUxFixture;

namespace Kora.Windows.IntegrationTests.ArtifactDiscovery;

public sealed partial class CustomizationWindowTests
{
    [Theory]
    [InlineData(CustomizationKind.Instruction)]
    [InlineData(CustomizationKind.Skill)]
    [InlineData(CustomizationKind.Agent)]
    [InlineData(CustomizationKind.McpServer)]
    [InlineData(CustomizationKind.Prompt)]
    public Task ArtifactFilterUpdatesEveryCategoryFromDisplayedMetadataWithoutSelectionOrDraftChanges(CustomizationKind kind) =>
        HeadlessSession.RunAsync(async () =>
        {
            using var f = new NativeUxFixtureSession(Path.GetTempPath());
            await f.InitializeAsync();
            var sources = Enum.GetValues<CustomizationKind>().SelectMany(sourceKind =>
                Enum.GetValues<CustomizationScope>().SelectMany(scope => new[]
                {
                    new CustomizationSource(sourceKind + "-" + scope + "-lookup", sourceKind, scope,
                        "Lookup records", @"C:\Profiles\lookup", "source.md"),
                    new CustomizationSource(sourceKind + "-" + scope + "-archive", sourceKind, scope,
                        "Archive records", @"C:\Definitions\archive", "source.md"),
                })).ToArray();
            var selected = new List<CustomizationSource>();
            var actions = new List<string>();
            CustomizationWindow? window = null;
            void Render() => window!.ShowSources(sources,
                source => source.Name.StartsWith("Archive", StringComparison.Ordinal), selected.Add);
            window = new(action =>
            {
                actions.Add(action);
                if (action is "Filter") { Render(); }
            }, f.Main);
            try
            {
                window.Show();
                window.Kind = kind;
                Render();
                Button[] Rows() => window.GetLogicalDescendants().OfType<Expander>()
                    .SelectMany(group => ((StackPanel)group.Content!).Children.OfType<Button>()).ToArray();
                string[] Names() => Rows().Select(row => ((StackPanel)row.Content!).Children
                    .OfType<TextBlock>().First().Text!).ToArray();
                void Expect(string? filter, int count)
                {
                    window.Search.Text = filter;
                    Rows().Should().HaveCount(count);
                    window.FilterSummary.Text.Should().Be(count + " of 6 artifacts");
                }

                AutomationProperties.GetName(window.Search).Should().Contain("Filter artifacts");
                AutomationProperties.GetLabeledBy(window.Search).Should().BeOfType<TextBlock>()
                    .Which.Text.Should().Be("Filter artifacts");
                Rows().Should().HaveCount(6);
                window.ClearFilter.IsEnabled.Should().BeFalse();
                Rows()[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var original = selected.Should().ContainSingle().Which;
                original.Kind.Should().Be(kind);
                window.Editor.Text = "Unsaved definition text";
                window.Request.Text = "Unsent authoring request";
                actions.Clear();

                Expect("look", 3);
                Names().Should().OnlyContain(name => name == "Lookup records");
                Expect("  LOOKUP RECORDS  ", 3);
                Expect(@"c:\PROFILES", 3);
                Expect("Built-IN", 2);
                Expect("kora", 2);
                Expect("user", 2);
                Expect("DISABLED", 3);
                Names().Should().OnlyContain(name => name == "Archive records (Disabled)");
                Expect("no such artifact", 0);
                window.GetLogicalDescendants().OfType<Expander>().Should().OnlyContain(group =>
                    ((StackPanel)group.Content!).Children.OfType<TextBlock>()
                        .Single().Text == "No matching artifacts");
                window.ClearFilter.IsEnabled.Should().BeTrue();
                window.ClearFilter.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                window.Search.Text.Should().BeEmpty();
                Rows().Should().HaveCount(6);
                window.FilterSummary.Text.Should().Be("6 of 6 artifacts");
                window.ClearFilter.IsEnabled.Should().BeFalse();
                Expect(" \t ", 6);
                Expect(null, 6);
                actions.Should().OnlyContain(action => action == "Filter",
                    "typing must not select, read, save, enable or invoke an artifact");
                selected.Should().ContainSingle().Which.Should().BeSameAs(original);
                window.SelectedSource.Should().BeSameAs(original);
                window.Editor.Text.Should().Be("Unsaved definition text");
                window.Request.Text.Should().Be("Unsent authoring request");

                window.Search.Text = "archive";
                foreach (var next in Enum.GetValues<CustomizationKind>())
                {
                    window.Kind = next;
                    Names().Should().HaveCount(3).And.OnlyContain(name => name == "Archive records (Disabled)");
                    window.Search.Text.Should().Be("archive");
                    Rows()[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    selected[^1].Kind.Should().Be(next);
                }
                window.ClearPrivateText();
                window.Search.Text.Should().BeEmpty();
                window.FilterSummary.Text.Should().BeEmpty();
                Rows().Should().BeEmpty();
            }
            finally { window.Close(); }
        });
}
