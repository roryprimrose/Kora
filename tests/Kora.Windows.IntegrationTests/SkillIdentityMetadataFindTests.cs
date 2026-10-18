using System.Text;

using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

using AwesomeAssertions;

using Kora.Controls;
using Kora.Core.Skills;
using Kora.Definitions.Skills;
using Kora.NativeUxFixture;
using Kora.Windows.IntegrationTests.Audio;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class SkillIdentityMetadataFindTests
{
    private static readonly string[] BundledQueryFields = ["PackageQuery", "PackageDefinitionDigest", "PackageDeclaredResourceDigest"];
    private static readonly string[] BundledSelectors = ["PackageFilter", "PackageOrder"];
    private static readonly string[] SharedAbsentQueries = ["BodyOnlySecret", "HiddenOnlySecret", "missing"];

    [WindowsFact]
    public async Task BundledFindUsesOnlyRenderedIdentityAndRetiresOnSelectionSearchEditsAndClose()
    {
        await HeadlessSession.RunAsync(() =>
        {
            var admitted = true;
            var callbacks = 0;
            var window = new SkillPackagesWindow(EmbeddedSkillCatalogue.Load(),
                new(NullLogger<NativeDetailRenderer>.Instance), () => callbacks++, () => admitted);
            window.Show();
            try
            {
                var pane = window.FindControl<IdentityMetadataFindPane>("IdentityFind")!;
                var identity = window.FindControl<TextBlock>("PackageIdentity")!.Text!;
                var selected = window.SelectedPackage;
                var catalogueQuery = window.FindControl<TextBox>("PackageQuery")!.Text;
                Find(pane, "Bundled");
                Match(pane).Text.Should().Be("Bundled");
                pane.FindControl<TextBlock>("IdentityFindLocation")!.Text.Should().Be("UTF-16 [0, 7); end exclusive.");
                Status(pane).Text.Should().Contain($"[{identity.IndexOf("Bundled", StringComparison.Ordinal)},");
                window.SelectedPackage.Should().BeSameAs(selected);
                window.FindControl<TextBox>("PackageQuery")!.Text.Should().Be(catalogueQuery);
                Find(pane, "BUNDLED FIRST-PARTY SOURCE");
                Status(pane).Text.Should().StartWith("No literal match");
                var sourceLine = selected!.Files.SelectMany(file => file.Text.Split('\n'))
                    .Select(line => line.TrimEnd('\r'))
                    .First(line => line.Length is > 5 and < 100 && !identity.Contains(line, StringComparison.Ordinal));
                Find(pane, sourceLine);
                Status(pane).Text.Should().StartWith("No literal match");
                AssertPrivateInteraction(pane);
                foreach (var name in BundledQueryFields)
                {
                    Click(window, "SearchPackages");
                    window.FindControl<ComboBox>("PackageSelector")!.SelectedIndex = 0;
                    Find(pane, "Bundled");
                    window.FindControl<TextBox>(name)!.Text = "x";
                    AssertRetired(pane);
                    window.FindControl<TextBox>(name)!.Text = string.Empty;
                }
                Click(window, "ClearPackageSearch");
                var selector = window.FindControl<ComboBox>("PackageSelector")!;
                foreach (var name in BundledSelectors)
                {
                    selector.SelectedIndex = 0;
                    Find(pane, "Bundled");
                    window.FindControl<ComboBox>(name)!.SelectedIndex = 1;
                    AssertRetired(pane);
                    Click(window, "ClearPackageSearch");
                }
                selector.SelectedIndex = 0;
                Find(pane, "Bundled");
                selector.SelectedIndex = -1;
                AssertRetired(pane);
                selector.SelectedIndex = 0;
                Find(pane, "Bundled");
                Click(window, "SearchPackages");
                AssertRetired(pane);
                selector.SelectedIndex = -1;
                selector.SelectedIndex = 0;
                Find(pane, "Bundled");
                admitted = false;
                Click(pane, "IdentityFindNext");
                AssertRetired(pane);
                admitted = true;
                AssertRetired(pane);
                selector.SelectedIndex = -1;
                selector.SelectedIndex = 0;
                Find(pane, "Bundled");
                window.Close();
                AssertRetired(pane);
                pane.Admit("retired identity", () => true);
                AssertRetired(pane);
                callbacks.Should().Be(0);
            }
            finally { window.Close(); }
        });
    }

    [WindowsFact]
    public async Task SharedFindExcludesBodyAndUninspectedEntriesAndRetiresAtEveryOperationBoundary()
    {
        await HeadlessSession.RunAsync(() =>
        {
            var calls = 0;
            Task Callback() { calls++; return Task.CompletedTask; }
            var window = new SharedSkillSourcesWindow(Callback, Callback, Callback, Callback, Callback, () => true);
            window.Show();
            try
            {
                var source = new SharedSkillSource(Guid.NewGuid(), ".copilot\\skills", new string('A', 48));
                var bytes = Encoding.UTF8.GetBytes("---\nname: example\nversion: 1.0.0\ndescription: Example\n---\nBodyOnlySecret");
                var snapshot = SharedSkillSnapshot.Parse(source.Id, "example\\SKILL.md", bytes, true, ["HiddenOnlySecret.ps1"]);
                var catalogue = new SharedSkillCatalogue(source, [snapshot]);
                window.SetSources([source]);
                window.FindControl<ComboBox>("SourceSelector")!.SelectedIndex = 0;
                window.SetCatalogue(catalogue);
                var pane = window.FindControl<IdentityMetadataFindPane>("IdentityFind")!;
                var identity = window.FindControl<TextBlock>("SharedIdentity")!.Text!;
                foreach (var query in SharedAbsentQueries)
                {
                    Find(pane, query);
                    Status(pane).Text.Should().StartWith("No literal match");
                }
                Find(pane, "SKILL.md");
                Match(pane).Text.Should().Be("SKILL.md");
                identity.Should().Contain("SKILL.md");
                AssertPrivateInteraction(pane);
                foreach (var retire in new Action[]
                {
                    () => window.SetBusy(true),
                    () => window.SetWithdrawalBusy(true),
                    () => window.ClearCatalogue(),
                    () => window.SetCatalogue(catalogue),
                    () => window.SetSources([source]),
                    () => window.FindControl<TextBox>("SharedQuery")!.Text = "edit",
                    () => window.FindControl<TextBox>("SharedRevisionDigest")!.Text = "edit",
                    () => window.FindControl<ComboBox>("SharedOrder")!.SelectedIndex = 1,
                    () => window.FindControl<ComboBox>("SharedFilter")!.SelectedIndex = 1,
                    () => Click(window, "SearchSharedPackages"),
                    () => window.FindControl<ComboBox>("SourceSelector")!.SelectedIndex = -1,
                })
                {
                    window.SetBusy(false);
                    window.SetSources([source]);
                    window.FindControl<ComboBox>("SourceSelector")!.SelectedIndex = 0;
                    window.SetCatalogue(catalogue);
                    Find(pane, "SKILL.md");
                    retire();
                    // Replacement catalogues may select a new snapshot; old query/match never survives.
                    Query(pane).Text.Should().BeEmpty();
                    Match(pane).Text.Should().BeEmpty();
                    Status(pane).Text.Should().BeEmpty();
                    window.SetBusy(false);
                    window.SetWithdrawalBusy(false);
                }
                window.SetSources([source]);
                window.FindControl<ComboBox>("SourceSelector")!.SelectedIndex = 0;
                window.SetBusy(true);
                window.SetCatalogue(catalogue);
                window.SetBusy(false);
                window.FindControl<ComboBox>("SharedPackageSelector")!.SelectedIndex.Should().Be(-1);
                AssertRetired(pane);
                window.FindControl<ComboBox>("SharedPackageSelector")!.SelectedIndex = 0;
                Find(pane, "SKILL.md");
                Match(pane).Text.Should().Be("SKILL.md");
                window.ClearPrivateContent();
                AssertRetired(pane);
                window.Close();
                AssertRetired(pane);
                calls.Should().Be(0);
            }
            finally { window.Close(); }
        });
    }

    [WindowsFact]
    public async Task IdentityFindReentrantRetirementDuringBringIntoViewCannotRediscloseOrFocus()
    {
        await HeadlessSession.RunAsync(() =>
        {
            var window = new SkillPackagesWindow(EmbeddedSkillCatalogue.Load(),
                new(NullLogger<NativeDetailRenderer>.Instance), current: () => true);
            window.Show();
            try
            {
                var pane = window.FindControl<IdentityMetadataFindPane>("IdentityFind")!;
                Match(pane).AddHandler(Control.RequestBringIntoViewEvent, (_, _) =>
                    window.FindControl<ComboBox>("PackageSelector")!.SelectedIndex = -1);
                Find(pane, "Bundled");
                AssertRetired(pane);
                Match(pane).IsFocused.Should().BeFalse();
            }
            finally { window.Close(); }
        });
    }

    private static void AssertPrivateInteraction(IdentityMetadataFindPane pane)
    {
        foreach (var name in new[] { "IdentityFindQuery", "IdentityFindFirst", "IdentityFindNext",
            "IdentityFindPrevious", "IdentityFindClear", "IdentityFindStatus", "IdentityFindLocation", "IdentityFindMatch" })
        {
            ControlAutomationPeer.CreatePeerForElement(pane.FindControl<Control>(name)!)!.GetName().Should().NotBeNullOrEmpty();
        }
        Query(pane).IsUndoEnabled.Should().BeFalse();
        foreach (var transfer in new[] { TextBox.CopyingToClipboardEvent, TextBox.CuttingToClipboardEvent, TextBox.PastingFromClipboardEvent })
        {
            var args = new RoutedEventArgs(transfer);
            Query(pane).RaiseEvent(args);
            args.Handled.Should().BeTrue();
        }
        foreach (var invalid in new[] { "", "\n", "\u200b", new string('x', 257), new string('界', 171), new string('\ud800', 1) })
        {
            Find(pane, invalid);
            Status(pane).Text.Should().StartWith("Invalid");
            Match(pane).IsVisible.Should().BeFalse();
        }
        Query(pane).Text = " ";
        Query(pane).Focus();
        Query(pane).RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
        Match(pane).Text.Should().Be(" ");
        var first = Status(pane).Text;
        pane.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.F3 });
        Status(pane).Text.Should().NotBe(first);
        pane.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.F3, KeyModifiers = KeyModifiers.Shift });
        Status(pane).Text.Should().Be(first);
        pane.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
        Query(pane).Text.Should().BeEmpty();
        Match(pane).Text.Should().BeEmpty();
        Status(pane).Text.Should().BeEmpty();
        pane.FindControl<TextBlock>("IdentityFindLocation")!.Text.Should().BeEmpty();
        Query(pane).Text = " ";
        Click(pane, "IdentityFindFirst");
        Click(pane, "IdentityFindClear");
        Query(pane).Text.Should().BeEmpty();
    }

    private static void AssertRetired(IdentityMetadataFindPane pane)
    {
        Query(pane).Text = "stale private query";
        Click(pane, "IdentityFindFirst");
        Click(pane, "IdentityFindNext");
        Click(pane, "IdentityFindPrevious");
        Click(pane, "IdentityFindClear");
        Query(pane).Text.Should().BeEmpty();
        Query(pane).IsEnabled.Should().BeFalse();
        Match(pane).Text.Should().BeEmpty();
        Status(pane).Text.Should().BeEmpty();
    }

    private static void Find(IdentityMetadataFindPane pane, string query)
    {
        Query(pane).Text = query;
        Click(pane, "IdentityFindFirst");
    }

    private static TextBox Query(IdentityMetadataFindPane pane) => pane.FindControl<TextBox>("IdentityFindQuery")!;
    private static TextBlock Match(IdentityMetadataFindPane pane) => pane.FindControl<TextBlock>("IdentityFindMatch")!;
    private static TextBlock Status(IdentityMetadataFindPane pane) => pane.FindControl<TextBlock>("IdentityFindStatus")!;
    private static void Click(Control owner, string name) =>
        owner.FindControl<Button>(name)!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
}
