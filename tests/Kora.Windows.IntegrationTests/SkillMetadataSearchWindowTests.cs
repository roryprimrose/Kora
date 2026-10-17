using System.Globalization;
using System.Text;
using System.Text.Json;

using Avalonia.Controls;
using Avalonia.Automation;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Core.Skills;
using Kora.Application.Configuration;
using Kora.Application.Skills;
using Kora.Definitions.Skills;
using Kora.NativeUxFixture;
using Kora.Windows.IntegrationTests.Audio;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class SkillMetadataSearchWindowTests
{
    private static readonly string[] BundledDigestFields = ["PackageDefinitionDigest", "PackageDeclaredResourceDigest"];
    private static readonly string[] SharedButtons = ["RegisterSource", "RefreshSources", "DiscoverSource", "VerifyRevision",
        "UnregisterSource", "SearchSharedPackages", "ClearSharedSearch"];

    [WindowsFact]
    public async Task BundledExactDigestFieldsAreLabelledReadFreeAndRetireEveryOriginalSourceImmediately()
    {
        await HeadlessSession.RunAsync(() =>
        {
            var catalogue = EmbeddedSkillCatalogue.Load();
            var callbacks = 0;
            var window = new SkillPackagesWindow(catalogue, new(NullLogger<NativeDetailRenderer>.Instance), () => callbacks++);
            window.Show();
            try
            {
                var selector = window.FindControl<ComboBox>("PackageSelector")!;
                var package = catalogue.Packages[^1];
                foreach (var name in BundledDigestFields)
                {
                    Click(window, "ClearPackageSearch");
                    selector.SelectedIndex = catalogue.Packages.Count - 1;
                    var oldTabs = window.FindControl<TabControl>("SourceTabs")!.Items.OfType<TabItem>().ToArray();
                    var oldReaders = oldTabs.SelectMany(tab => ((DockPanel)tab.Content!).Children.OfType<TextBox>()).ToArray();
                    var input = window.FindControl<TextBox>(name)!;
                    AssertDigestField(window, input);
                    input.Text = string.Equals(name, "PackageDefinitionDigest", StringComparison.Ordinal)
                        ? package.DefinitionDigest : package.DeclaredResourceDigest;
                    window.SelectedPackage.Should().BeNull();
                    selector.Items.Should().BeEmpty();
                    oldTabs.Should().OnlyContain(tab => tab.Content == null);
                    oldReaders.Should().OnlyContain(reader => reader.Text == string.Empty);
                    window.FindControl<TextBlock>("PackageIdentity")!.Text.Should().BeEmpty();
                    Click(window, "SearchPackages");
                    selector.Items.Should().ContainSingle();
                    window.SelectedPackage.Should().BeNull();
                    selector.SelectedIndex = 0;
                    window.SelectedPackage.Should().BeSameAs(package);
                    input.Text = new string('0', 64);
                    Click(window, "SearchPackages");
                    selector.Items.Should().BeEmpty();
                    input.Text = package.DefinitionDigest.ToUpperInvariant();
                    Click(window, "SearchPackages");
                    window.FindControl<TextBlock>("PackageSearchStatus")!.Text.Should().StartWith("Search rejected:");
                    input.Text.Should().Be(package.DefinitionDigest.ToUpperInvariant());
                    selector.Items.Should().BeEmpty();
                }
                Click(window, "ClearPackageSearch");
                window.FindControl<TextBox>("PackageDefinitionDigest")!.Text.Should().BeEmpty();
                window.FindControl<TextBox>("PackageDeclaredResourceDigest")!.Text.Should().BeEmpty();
                selector.Items.Should().HaveCount(catalogue.Packages.Count);
                selector.SelectedIndex.Should().Be(-1);
                window.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
                window.IsVisible.Should().BeFalse();
                Click(window, "SearchPackages");
                Click(window, "SharedSources");
                callbacks.Should().Be(0);
                selector.Items.Should().BeEmpty();
            }
            finally { window.Close(); }
        });
    }

    [WindowsFact]
    public async Task SharedRevisionEditsAndClearRetireGenerationAndPrivateHexWithoutManagementCalls()
    {
        await HeadlessSession.RunAsync(() =>
        {
            var calls = 0;
            Task Callback() { calls++; return Task.CompletedTask; }
            var window = new SharedSkillSourcesWindow(Callback, Callback, Callback, Callback, Callback);
            window.Show();
            try
            {
                var source = Source();
                var snapshot = SharedSkillSnapshot.Parse(source.Id, "private\\SKILL.md", [0xff], true, ["private\\hidden.ps1"]);
                window.SetSources([source]);
                window.FindControl<ComboBox>("SourceSelector")!.SelectedIndex = 0;
                window.SetCatalogue(new(source, [snapshot]));
                var selector = window.FindControl<ComboBox>("SharedPackageSelector")!;
                var input = window.FindControl<TextBox>("SharedRevisionDigest")!;
                AssertDigestField(window, input);
                Click(window, "ClearSharedSearch");
                selector.SelectedIndex.Should().Be(-1);
                selector.SelectedIndex = 0;
                window.FindControl<TextBox>("SharedSource")!.Text.Should().Contain("FF");
                var generation = window.CatalogueGeneration;
                input.Text = snapshot.RevisionDigest;
                window.CatalogueGeneration.Should().BeGreaterThan(generation);
                window.SelectedPackage.Should().BeNull();
                window.SelectedSource.Should().BeSameAs(source);
                selector.Items.Should().BeEmpty();
                AssertPrivatePanesEmpty(window);
                Click(window, "SearchSharedPackages");
                selector.Items.Should().ContainSingle();
                selector.SelectedIndex = 0;
                window.SelectedPackage.Should().BeSameAs(snapshot);
                window.FindControl<ComboBox>("SharedFilter")!.SelectedIndex = 1;
                AssertPrivatePanesEmpty(window);
                Click(window, "SearchSharedPackages");
                selector.Items.Should().BeEmpty();
                window.FindControl<ComboBox>("SharedFilter")!.SelectedIndex = 2;
                foreach (var invalid in new[] { snapshot.RevisionDigest.ToUpperInvariant(), " " + snapshot.RevisionDigest,
                    snapshot.RevisionDigest[..63], snapshot.RevisionDigest + "0", new string('a', 257), "\u202e" })
                {
                    input.Text = invalid;
                    Click(window, "SearchSharedPackages");
                    selector.Items.Should().BeEmpty();
                    input.Text.Should().Be(invalid);
                    window.FindControl<TextBlock>("SharedSearchStatus")!.Text.Should().StartWith("Search rejected:");
                }
                input.Text = new string('0', 64);
                Click(window, "SearchSharedPackages");
                selector.Items.Should().BeEmpty();
                window.SetBusy(true);
                input.IsEnabled.Should().BeFalse();
                window.SetBusy(false);
                input.IsEnabled.Should().BeTrue();
                Click(window, "ClearSharedSearch");
                input.Text.Should().BeEmpty();
                selector.Items.Should().ContainSingle();
                selector.SelectedIndex.Should().Be(-1);
                AssertPrivatePanesEmpty(window);
                input.Text = snapshot.RevisionDigest;
                window.ClearCatalogue();
                AssertReset(window);
                window.SetCatalogue(new(source, [snapshot]));
                input.Text = snapshot.RevisionDigest;
                window.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
                input.Text.Should().BeEmpty();
                window.SelectedSource.Should().BeNull();
                window.RegisteredSources.Should().BeEmpty();
                AssertPrivatePanesEmpty(window);
                foreach (var name in SharedButtons) { Click(window, name); }
                calls.Should().Be(0);
                selector.Items.Should().BeEmpty();
            }
            finally { window.Close(); }
        });
    }

    private static void AssertDigestField(Window window, TextBox input)
    {
        AutomationProperties.GetName(input).Should().Contain("empty means Any").And.Contain("lowercase SHA-256");
        input.IsUndoEnabled.Should().BeFalse();
        window.GetVisualDescendants().OfType<Label>().Should().ContainSingle(label => ReferenceEquals(label.Target, input));
    }

    [Theory]
    [InlineData("discover")]
    [InlineData("verify")]
    [InlineData("discover-failure")]
    [InlineData("verify-failure")]
    [InlineData("close")]
    [InlineData("privacy")]
    public async Task PendingSharedReadsCannotPublishAfterDigestEditReselectionOrPrivateClosure(string operation)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var parent = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".net-test-artifacts"));
            Directory.CreateDirectory(parent);
            using var fixture = new NativeUxFixtureSession(parent);
            await fixture.InitializeAsync();
            var source = Source();
            var snapshot = Snapshot(source, "one", "one", "Description");
            var catalogue = new SharedSkillCatalogue(source, [snapshot]);
            var preferences = new LocalSharedSkillPreferences(fixture);
            preferences.Save([source]);
            var reader = new PendingSharedReader();
            var service = new SharedSkillDiscoveryService(preferences, reader, fixture.SharedAdmission, fixture.Audit,
                NullLogger<SharedSkillDiscoveryService>.Instance);
            using var controller = new SharedSkillSourcesWindowController(fixture.Main, service, () => true,
                NullLogger<SharedSkillSourcesWindowController>.Instance);
            SharedSkillSourcesWindow? window = null;
            using var opened = Window.WindowOpenedEvent.AddClassHandler<SharedSkillSourcesWindow>((view, _) => window = view);
            controller.Open();
            var view = window ?? throw new InvalidOperationException("The admitted shared window did not open.");
            try
            {
                var search = view.FindControl<Button>("SearchSharedPackages")!;
                await WhenEnabled(search).WaitAsync(fixture.Token);
                view.FindControl<ComboBox>("SourceSelector")!.SelectedIndex = 0;
                view.SetCatalogue(catalogue);
                Click(view, operation is "verify" or "verify-failure" ? "VerifyRevision" : "DiscoverSource");
                await reader.Started.Task.WaitAsync(fixture.Token);
                search.IsEnabled.Should().BeFalse();
                var finished = WhenEnabled(search);
                var generation = view.CatalogueGeneration;
                var input = view.FindControl<TextBox>("SharedRevisionDigest")!;
                input.Text = snapshot.RevisionDigest;
                view.CatalogueGeneration.Should().BeGreaterThan(generation);
                AssertPrivatePanesEmpty(view);
                if (operation is "verify" or "verify-failure")
                {
                    Click(view, "SearchSharedPackages");
                    view.FindControl<ComboBox>("SharedPackageSelector")!.SelectedIndex = 0;
                    view.SelectedPackage.Should().BeSameAs(snapshot);
                }
                if (operation is "close") { view.Close(); }
                if (operation is "privacy")
                {
                    fixture.Access.SetOpen(false);
                    fixture.Main.CloseForObservedPrivacyEvent("Synthetic skill digest privacy closure", hidePresentation: true);
                    await fixture.Main.PrivacyClosureTask;
                    view.IsVisible.Should().BeFalse();
                }
                if (operation is "discover-failure" or "verify-failure")
                { reader.Completion.SetException(new IOException("Synthetic pending read failed.")); }
                else { reader.Completion.SetResult(catalogue); }
                if (operation is "close" or "privacy")
                {
                    await reader.Cancelled.Task.WaitAsync(fixture.Token);
                    input.Text.Should().BeEmpty();
                    view.SelectedSource.Should().BeNull();
                    view.SelectedPackage.Should().BeNull();
                    AssertPrivatePanesEmpty(view);
                    view.FindControl<TextBlock>("SharedStatus")!.Text.Should().BeEmpty();
                }
                else
                {
                    await finished.WaitAsync(fixture.Token);
                    input.Text.Should().Be(snapshot.RevisionDigest);
                    view.FindControl<TextBlock>("SharedStatus")!.Text.Should().Be("Filters edited. No source read or revision recheck performed.");
                    if (operation is "discover" or "discover-failure")
                    {
                        view.SelectedPackage.Should().BeNull();
                        view.FindControl<ComboBox>("SharedPackageSelector")!.Items.Should().BeEmpty();
                        AssertPrivatePanesEmpty(view);
                    }
                }
                reader.Reads.Should().Be(1);
            }
            finally
            {
                reader.Completion.TrySetResult(catalogue);
                view.Close();
            }
        });
    }

    private static Task WhenEnabled(Button button)
    {
        if (button.IsEnabled) { return Task.CompletedTask; }
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Changed(object? sender, Avalonia.AvaloniaPropertyChangedEventArgs args)
        {
            if (button.IsEnabled)
            {
                button.PropertyChanged -= Changed;
                completion.SetResult();
            }
        }
        button.PropertyChanged += Changed;
        return completion.Task;
    }

    private sealed class PendingSharedReader : ISharedSkillSourceReader
    {
        internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource<SharedSkillCatalogue> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int Reads { get; private set; }

        public ValueTask<SharedSkillSource> SelectAsync(string selectedRoot, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Selection is not admitted in this fixture.");

        public async ValueTask<SharedSkillCatalogue> DiscoverAsync(SharedSkillSource source, CancellationToken cancellationToken)
        {
            Reads++;
            using var registration = cancellationToken.Register(() => Cancelled.TrySetResult());
            Started.SetResult();
            var result = await Completion.Task;
            cancellationToken.ThrowIfCancellationRequested();
            return result;
        }
    }

    [WindowsFact]
    public async Task BundledEditsRetireSelectionAndNeverAutoOpenFilteredResults()
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
                window.SelectedPackage.Should().BeNull();
                original.Should().OnlyContain(tab => tab.Content == null);
                Click(window, "SearchPackages");
                window.SelectedPackage.Should().BeNull();
                selector.SelectedIndex = 0;
                window.SelectedPackage.Should().BeSameAs(catalogue.Packages[0]);
                query.Text = catalogue.Packages[^1].Manifest.Id.ToUpperInvariant();
                window.SelectedPackage.Should().BeNull();
                window.FindControl<TextBlock>("PackageSearchStatus")!.Text.Should().Contain("Filters edited");
                Click(window, "SearchPackages");
                window.SelectedPackage.Should().BeNull();
                tabs.Items.Should().BeEmpty();
                original.Should().OnlyContain(tab => tab.Content == null);
                window.FindControl<TextBlock>("PackageIdentity")!.Text.Should().BeEmpty();
                selector.Items.Should().ContainSingle();
                selector.SelectedIndex = 0;
                window.SelectedPackage.Should().BeSameAs(catalogue.Packages[^1]);
                window.FindControl<TextBlock>("PackageIdentity")!.Text.Should().Contain(catalogue.Packages[^1].Manifest.Id);
                query.Text = "";
                Click(window, "SearchPackages");
                selector.SelectedIndex.Should().Be(-1);
                window.SelectedPackage.Should().BeNull();
                selector.SelectedIndex = catalogue.Packages.Count - 1;
                Click(window, "SearchPackages");
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
                window.SelectedPackage.Should().BeNull();
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
                selector.SelectedIndex.Should().Be(-1);
                selector.SelectedIndex = 1;
                Click(window, "SearchSharedPackages");
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
            first.FindControl<TextBox>("PackageDefinitionDigest")!.Text = new string('a', 64);
            await fixture.Main.SetAllowVoiceActivationDuringCallsAsync(false);
            first.IsVisible.Should().BeFalse();
            first.FindControl<TextBox>("PackageQuery")!.Text.Should().BeEmpty();
            first.FindControl<TextBox>("PackageDefinitionDigest")!.Text.Should().BeEmpty();
            first.SelectedPackage.Should().BeNull();
            controller.Open();
            var second = window ?? throw new InvalidOperationException("The admitted bundled window did not reopen.");
            second.Should().NotBeSameAs(first);
            second.FindControl<TextBox>("PackageQuery")!.Text = "another private query";
            second.FindControl<TextBox>("PackageDeclaredResourceDigest")!.Text = new string('b', 64);
            var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            second.Closed += (_, _) => closed.TrySetResult();
            fixture.Access.SetOpen(false);
            fixture.Main.CloseForObservedPrivacyEvent("Synthetic search privacy closure", hidePresentation: true);
            await closed.Task.WaitAsync(fixture.Token);
            await fixture.Main.PrivacyClosureTask;
            second.IsVisible.Should().BeFalse();
            second.FindControl<TextBox>("PackageQuery")!.Text.Should().BeEmpty();
            second.FindControl<TextBox>("PackageDeclaredResourceDigest")!.Text.Should().BeEmpty();
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
        window.FindControl<TextBox>("SharedRevisionDigest")!.Text.Should().BeEmpty();
        window.FindControl<ComboBox>("SharedFilter")!.SelectedIndex.Should().Be(0);
        window.FindControl<ComboBox>("SharedPackageSelector")!.Items.Should().BeEmpty();
        AssertPrivatePanesEmpty(window);
    }
}
