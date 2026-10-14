using System.Globalization;
using System.Text;
using System.Text.Json;

using Avalonia.Controls;
using Avalonia.Interactivity;

using AwesomeAssertions;

using Kora.Core.Skills;
using Kora.Definitions.Skills;
using Kora.NativeUxFixture;
using Kora.Windows.IntegrationTests.Audio;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class SkillMetadataSearchWindowTests
{
    [WindowsFact]
    public async Task BundledSearchPreservesOnlyExactSurvivingSelectionAndNeverAutoOpensFilteredResults()
    {
        await HeadlessSession.RunAsync(() =>
        {
            var catalogue = EmbeddedSkillCatalogue.Load();
            var window = new SkillPackagesWindow(catalogue, new(NullLogger<NativeDetailRenderer>.Instance));
            window.Show();
            try
            {
                var query = window.FindControl<TextBox>("PackageQuery")!;
                var selector = window.FindControl<ComboBox>("PackageSelector")!;
                var tabs = window.FindControl<TabControl>("SourceTabs")!;
                window.SelectedPackage.Should().BeSameAs(catalogue.Packages[0]);
                var original = tabs.Items.OfType<TabItem>().ToArray();
                query.Text = catalogue.Packages[0].Manifest.Id.ToUpperInvariant();
                Click(window, "SearchPackages");
                window.SelectedPackage.Should().BeSameAs(catalogue.Packages[0]);
                tabs.Items.OfType<TabItem>().Should().Equal(original);
                query.Text = catalogue.Packages[^1].Manifest.Id.ToUpperInvariant();
                window.SelectedPackage.Should().BeSameAs(catalogue.Packages[0]);
                Click(window, "SearchPackages");
                window.SelectedPackage.Should().BeNull();
                tabs.Items.Should().BeEmpty();
                original.Should().OnlyContain(tab => tab.Content == null);
                window.FindControl<TextBlock>("PackageIdentity")!.Text.Should().BeEmpty();
                window.FindControl<TextBlock>("PackageSearchStatus")!.Text.Should().Contain("Selection excluded");
                selector.Items.Should().ContainSingle();
                selector.SelectedIndex = 0;
                window.SelectedPackage.Should().BeSameAs(catalogue.Packages[^1]);
                window.FindControl<TextBlock>("PackageIdentity")!.Text.Should().Contain(catalogue.Packages[^1].Manifest.Id);
                query.Text = "";
                Click(window, "SearchPackages");
                selector.SelectedIndex.Should().Be(catalogue.Packages.Count - 1);
                window.SelectedPackage.Should().BeSameAs(catalogue.Packages[^1]);
                window.FindControl<ComboBox>("PackageFilter")!.SelectedIndex = 2;
                Click(window, "SearchPackages");
                window.SelectedPackage.Should().BeNull();
                selector.Items.Should().BeEmpty();
                Click(window, "ClearPackageSearch");
                selector.Items.Should().HaveCount(catalogue.Packages.Count);
                selector.SelectedIndex.Should().Be(-1);
                tabs.Items.Should().BeEmpty();
                window.FindControl<ComboBox>("PackageFilter")!.SelectedIndex.Should().Be(0);
            }
            finally { window.Close(); }
        });
    }

    [WindowsFact]
    public async Task SharedSearchMapsFilteredRowsToExactObjectsWithoutReaderOrManagementCallbacks()
    {
        await HeadlessSession.RunAsync(() =>
        {
            var calls = 0;
            SharedSkillSnapshot? verified = null;
            SharedSkillSourcesWindow? window = null;
            Task Callback() { calls++; return Task.CompletedTask; }
            Task Verify() { calls++; verified = window!.SelectedPackage; return Task.CompletedTask; }
            window = new(Callback, Callback, Callback, Verify, Callback);
            window.Show();
            try
            {
                var source = Source();
                var first = Snapshot(source, "first", "same", "first description");
                var second = Snapshot(source, "second", "same", "second description");
                var catalogue = new SharedSkillCatalogue(source, [first, second]);
                window.SetSources([source]);
                window.FindControl<ComboBox>("SourceSelector")!.SelectedIndex = 0;
                window.SetCatalogue(catalogue);
                var query = window.FindControl<TextBox>("SharedQuery")!;
                var selector = window.FindControl<ComboBox>("SharedPackageSelector")!;
                window.SelectedPackage.Should().BeSameAs(catalogue.Packages[0]);
                query.Text = "SECOND DESCRIPTION";
                window.FindControl<ComboBox>("SharedFilter")!.SelectedIndex = 2;
                window.SelectedPackage.Should().BeSameAs(catalogue.Packages[0]);
                Click(window, "SearchSharedPackages");
                calls.Should().Be(0);
                selector.Items.Should().ContainSingle();
                selector.Items.Cast<string>().Single().Should().Contain("second\\\\SKILL.md");
                window.SelectedPackage.Should().BeNull();
                AssertPrivatePanesEmpty(window);
                selector.SelectedIndex = 0;
                window.SelectedPackage.Should().BeSameAs(catalogue.Packages[1]);
                window.FindControl<TextBox>("SharedSource")!.Text.Should().Be(second.Text);
                window.FindControl<TextBlock>("SharedIdentity")!.Text.Should().Contain("second\\\\SKILL.md");
                Click(window, "VerifyRevision");
                calls.Should().Be(1);
                verified.Should().BeSameAs(catalogue.Packages[1]);
                query.Text = "same";
                Click(window, "SearchSharedPackages");
                selector.SelectedIndex.Should().Be(1);
                window.SelectedPackage.Should().BeSameAs(catalogue.Packages[1]);
                window.FindControl<ComboBox>("SharedFilter")!.SelectedIndex = 1;
                Click(window, "SearchSharedPackages");
                window.SelectedPackage.Should().BeNull();
                AssertPrivatePanesEmpty(window);
                Click(window, "ClearSharedSearch");
                window.SelectedPackage.Should().BeNull();
                selector.Items.Should().HaveCount(2);
                calls.Should().Be(1);
                window.FindControl<ComboBox>("SharedFilter")!.SelectedIndex.Should().Be(0);
            }
            finally { window.Close(); }
        });
    }

    [WindowsFact]
    public async Task SharedDefaultShowsAllCompatibilityStatesAndCompleteBoundedLabelsWithBeyondFirstMatches()
    {
        await HeadlessSession.RunAsync(() =>
        {
            var source = Source();
            var packages = Enumerable.Range(0, SharedSkillCatalogue.MaximumPackages - 1).Select(index =>
                Snapshot(source, "path" + index.ToString(CultureInfo.InvariantCulture),
                    "name" + index.ToString(CultureInfo.InvariantCulture) + new string('a', 58),
                    new string('界', 1024), index == 0 ? "run.exe" : "Private body")).ToList();
            packages.Add(SharedSkillSnapshot.Parse(source.Id, "unknown\\SKILL.md", [0xff], false));
            var catalogue = new SharedSkillCatalogue(source, packages);
            var window = SharedWindow();
            window.Show();
            try
            {
                window.SetSources([source]);
                window.FindControl<ComboBox>("SourceSelector")!.SelectedIndex = 0;
                window.SetCatalogue(catalogue);
                var selector = window.FindControl<ComboBox>("SharedPackageSelector")!;
                selector.Items.Should().HaveCount(SharedSkillCatalogue.MaximumPackages);
                var labels = selector.Items.Cast<string>().ToArray();
                labels[0].Should().Contain("instruction-incompatible; invocation unavailable");
                labels[1].Should().Contain("instruction-compatible; invocation unavailable");
                labels[^1].Should().Contain("declared name unavailable").And.Contain("instruction-incompatible");
                for (var index = 0; index < packages.Count; index++)
                {
                    labels[index].Should().Contain(JsonSerializer.Serialize(packages[index].SourceQualifiedIdentity));
                    labels[index].Should().Contain(JsonSerializer.Serialize(packages[index].RelativeFile));
                    if (packages[index].Name is { } name) { labels[index].Should().Contain(name); }
                }
                window.FindControl<TextBox>("SharedQuery")!.Text = packages[^2].Name!.ToUpperInvariant();
                Click(window, "SearchSharedPackages");
                selector.Items.Should().ContainSingle();
                window.SelectedPackage.Should().BeNull();
                selector.SelectedIndex = 0;
                window.SelectedPackage.Should().BeSameAs(packages[^2]);
                window.FindControl<TextBox>("SharedSource")!.Text.Should().Be(packages[^2].Text);
            }
            finally { window.Close(); }
        });
    }

    [WindowsFact]
    public async Task InvalidQueriesClearResultsAndNeverDefaultToAllAndCloseRetiresEveryPrivateField()
    {
        await HeadlessSession.RunAsync(() =>
        {
            var source = Source();
            var shared = SharedWindow();
            var bundled = new SkillPackagesWindow(EmbeddedSkillCatalogue.Load(), new(NullLogger<NativeDetailRenderer>.Instance));
            shared.Show();
            bundled.Show();
            try
            {
                shared.SetSources([source]);
                shared.FindControl<ComboBox>("SourceSelector")!.SelectedIndex = 0;
                var catalogue = new SharedSkillCatalogue(source, [Snapshot(source, "one", "one", "Description")]);
                foreach (var query in new[] { new string('a', 257), new string('界', 171), "\u202e", new string('\ud800', 1), "\n" })
                {
                    shared.SetCatalogue(catalogue);
                    shared.FindControl<TextBox>("SharedQuery")!.Text = query;
                    bundled.FindControl<TextBox>("PackageQuery")!.Text = query;
                    Click(shared, "SearchSharedPackages");
                    Click(bundled, "SearchPackages");
                    shared.FindControl<ComboBox>("SharedPackageSelector")!.Items.Should().BeEmpty();
                    bundled.FindControl<ComboBox>("PackageSelector")!.Items.Should().BeEmpty();
                    shared.FindControl<TextBox>("SharedQuery")!.Text.Should().Be(query);
                    bundled.FindControl<TextBox>("PackageQuery")!.Text.Should().Be(query);
                    shared.FindControl<TextBlock>("SharedSearchStatus")!.Text.Should().StartWith("Search rejected:");
                    bundled.FindControl<TextBlock>("PackageSearchStatus")!.Text.Should().StartWith("Search rejected:");
                    AssertPrivatePanesEmpty(shared);
                    bundled.SelectedPackage.Should().BeNull();
                    bundled.FindControl<TabControl>("SourceTabs")!.Items.Should().BeEmpty();
                }
            }
            finally { shared.Close(); bundled.Close(); }
            shared.FindControl<TextBox>("SharedQuery")!.Text.Should().BeEmpty();
            bundled.FindControl<TextBox>("PackageQuery")!.Text.Should().BeEmpty();
            shared.SelectedSource.Should().BeNull();
            shared.SelectedPackage.Should().BeNull();
            bundled.SelectedPackage.Should().BeNull();
            Click(shared, "SearchSharedPackages");
            Click(bundled, "SearchPackages");
            shared.FindControl<TextBlock>("SharedSearchStatus")!.Text.Should().BeEmpty();
            bundled.FindControl<TextBlock>("PackageSearchStatus")!.Text.Should().BeEmpty();
        });
    }

    [WindowsFact]
    public async Task SourceReplacementRefreshAndWithdrawalClearQueryFilterResultsAndSelection()
    {
        await HeadlessSession.RunAsync(() =>
        {
            var first = Source();
            var replacement = new SharedSkillSource(Guid.NewGuid(), first.ProfileRelativeRoot, first.DirectoryIdentity);
            var window = SharedWindow();
            window.Show();
            try
            {
                window.SetSources([first]);
                var sourceSelector = window.FindControl<ComboBox>("SourceSelector")!;
                sourceSelector.SelectedIndex = 0;
                var old = new SharedSkillCatalogue(first, [Snapshot(first, "one", "one", "Description")]);
                window.SetCatalogue(old);
                window.FindControl<TextBox>("SharedQuery")!.Text = "one";
                window.FindControl<ComboBox>("SharedFilter")!.SelectedIndex = 1;
                Click(window, "SearchSharedPackages");
                var generation = window.CatalogueGeneration;
                sourceSelector.SelectedIndex = -1;
                sourceSelector.SelectedIndex = 0;
                window.CatalogueGeneration.Should().BeGreaterThan(generation);
                AssertReset(window);
                window.SetCatalogue(old);
                window.SetSources([replacement]);
                sourceSelector.SelectedIndex = 0;
                AssertReset(window);
                ((Action)(() => window.SetCatalogue(old))).Should().Throw<InvalidOperationException>();
                var newCatalogue = new SharedSkillCatalogue(replacement, [Snapshot(replacement, "one", "one", "New description")]);
                window.SetCatalogue(newCatalogue);
                window.SelectedPackage!.SourceId.Should().Be(replacement.Id);
                window.ClearCatalogue();
                AssertReset(window);
                window.SetSources([]);
                window.SelectedSource.Should().BeNull();
                ((Action)(() => window.SetCatalogue(newCatalogue))).Should().Throw<InvalidOperationException>();
                Click(window, "SearchSharedPackages");
                window.FindControl<TextBlock>("SharedSearchStatus")!.Text.Should().Contain("No admitted catalogue");
                AssertReset(window);
            }
            finally { window.Close(); }
        });
    }

    [WindowsFact]
    public async Task OwningControllerPrivacyAndCallInvalidationCloseAndClearBundledSearch()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var parent = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".net-test-artifacts"));
            Directory.CreateDirectory(parent);
            using var fixture = new NativeUxFixtureSession(parent);
            await fixture.InitializeAsync();
            using var controller = new SkillPackagesWindowController(fixture.Main, () => true,
                NullLogger<SkillPackagesWindowController>.Instance, new(NullLogger<NativeDetailRenderer>.Instance),
                fixture.SharedSkills, NullLogger<SharedSkillSourcesWindowController>.Instance);
            SkillPackagesWindow? window = null;
            using var opened = Window.WindowOpenedEvent.AddClassHandler<SkillPackagesWindow>((view, _) => window = view);
            controller.Open();
            var first = window ?? throw new InvalidOperationException("The admitted bundled window did not open.");
            first.FindControl<TextBox>("PackageQuery")!.Text = "private query";
            await fixture.Main.SetAllowVoiceActivationDuringCallsAsync(false);
            first.IsVisible.Should().BeFalse();
            first.FindControl<TextBox>("PackageQuery")!.Text.Should().BeEmpty();
            first.SelectedPackage.Should().BeNull();
            controller.Open();
            var second = window ?? throw new InvalidOperationException("The admitted bundled window did not reopen.");
            second.Should().NotBeSameAs(first);
            second.FindControl<TextBox>("PackageQuery")!.Text = "another private query";
            var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            second.Closed += (_, _) => closed.TrySetResult();
            fixture.Access.SetOpen(false);
            fixture.Main.CloseForObservedPrivacyEvent("Synthetic search privacy closure", hidePresentation: true);
            await closed.Task.WaitAsync(fixture.Token);
            await fixture.Main.PrivacyClosureTask;
            second.IsVisible.Should().BeFalse();
            second.FindControl<TextBox>("PackageQuery")!.Text.Should().BeEmpty();
            second.SelectedPackage.Should().BeNull();
        });
    }

    private static void Click(Window window, string name) =>
        window.FindControl<Button>(name)!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static SharedSkillSourcesWindow SharedWindow() => new(
        static () => Task.CompletedTask, static () => Task.CompletedTask, static () => Task.CompletedTask,
        static () => Task.CompletedTask, static () => Task.CompletedTask);

    private static SharedSkillSource Source() => new(Guid.NewGuid(), ".agents\\skills", new string('a', 48));

    private static SharedSkillSnapshot Snapshot(SharedSkillSource source, string path, string name, string description, string body = "Private body") =>
        SharedSkillSnapshot.Parse(source.Id, path + "\\SKILL.md",
            Encoding.UTF8.GetBytes($"---\nname: {name}\nversion: 1.0.0\ndescription: {description}\n---\n{body}"), false);

    private static void AssertPrivatePanesEmpty(SharedSkillSourcesWindow window)
    {
        window.FindControl<TextBox>("SharedSource")!.Text.Should().BeEmpty();
        window.FindControl<TextBox>("UninspectedEntries")!.Text.Should().BeEmpty();
        window.FindControl<TextBlock>("SharedIdentity")!.Text.Should().BeEmpty();
        window.FindControl<Expander>("UninspectedDisclosure")!.IsVisible.Should().BeFalse();
    }

    private static void AssertReset(SharedSkillSourcesWindow window)
    {
        window.SelectedPackage.Should().BeNull();
        window.FindControl<TextBox>("SharedQuery")!.Text.Should().BeEmpty();
        window.FindControl<ComboBox>("SharedFilter")!.SelectedIndex.Should().Be(0);
        window.FindControl<ComboBox>("SharedPackageSelector")!.Items.Should().BeEmpty();
        AssertPrivatePanesEmpty(window);
    }
}
