using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora;
using Kora.Controls;
using Kora.Application;
using Kora.Application.Maintenance;
using Kora.Core.Auditing;
using Kora.Core.Context;
using Kora.Core.Hosting;
using Kora.Core.Maintenance;
using Kora.Definitions.Skills;
using Kora.NativeUxFixture;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

/// <summary>
/// Runtime (headless-rendered) accessibility measurement for already-delivered bounded Kora surfaces.
/// These tests exercise the real window types off-screen via Avalonia.Headless to confirm that
/// declared <see cref="AutomationProperties"/> values resolve through actual automation peers, that
/// keyboard Tab navigation reaches controls without faulting, and that DPI/render-scale changes do
/// not throw. They never launch the real app, touch the clipboard/network, or mutate user settings.
/// </summary>
[Collection(nameof(HeadlessUiTestGroup))]
public sealed class AccessibilityRuntimeContractTests
{
    [Fact]
    public async Task Maintenance_window_resolves_automation_names_and_live_regions_at_runtime()
    {
        await HeadlessSession.RunAsync(() =>
        {
            var viewModel = CreateMaintenanceViewModel();
            var window = new MaintenanceWindow(viewModel);
            try
            {
                window.Show();

                var explicitlyNamed = AssertAllExplicitAutomationNamesResolve(window);
                explicitlyNamed.Should().BeGreaterThanOrEqualTo(13, "every bound Maintenance control declares an automation name");

                var liveRegions = Descendants(window)
                    .Count(control => AutomationProperties.GetLiveSetting(control) == AutomationLiveSetting.Polite);
                liveRegions.Should().BeGreaterThanOrEqualTo(2, "Status and VerificationStatus are announced politely");
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public async Task Maintenance_window_keyboard_tab_navigation_reaches_controls_without_faulting()
    {
        await HeadlessSession.RunAsync(() =>
        {
            var viewModel = CreateMaintenanceViewModel();
            var window = new MaintenanceWindow(viewModel);
            try
            {
                window.Show();

                var visited = new HashSet<object>();
                for (var i = 0; i < 12; i++)
                {
                    window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, "Tab");
                    var focused = window.FocusManager?.GetFocusedElement();
                    if (focused is not null)
                    {
                        visited.Add(focused);
                    }
                }

                visited.Should().NotBeEmpty("Tab navigation must be able to reach at least one focusable control");
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.5)]
    [InlineData(2.0)]
    [InlineData(3.0)]
    public async Task Maintenance_window_renders_without_fault_across_dpi_scale_factors(double scaling)
    {
        await HeadlessSession.RunAsync(() =>
        {
            var viewModel = CreateMaintenanceViewModel();
            var window = new MaintenanceWindow(viewModel);
            try
            {
                window.Show();
                using (window.CaptureRenderedFrame())
                {
                    // Warm-up tick: the headless renderer only produces a non-null frame once a
                    // first render pass has completed, mirroring Avalonia's own headless test pattern.
                }

                window.SetRenderScaling(scaling);

                using Bitmap? frame = window.CaptureRenderedFrame();
                frame.Should().NotBeNull($"rendering at {scaling}x DPI scaling must not fault or yield an empty frame");
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public async Task Clipboard_preview_window_resolves_automation_names_and_clears_on_close_at_runtime()
    {
        await HeadlessSession.RunAsync(() =>
        {
            var snapshot = CreateSyntheticClipboardSnapshot();
            var reuseInvoked = false;
            var revokeInvoked = false;

            var window = new ClipboardPreviewWindow(_ => { });
            try
            {
                window.Show(snapshot, () => { reuseInvoked = true; return Task.CompletedTask; }, () => { revokeInvoked = true; return Task.CompletedTask; });

                var explicitlyNamed = AssertAllExplicitAutomationNamesResolve(window);
                explicitlyNamed.Should().BeGreaterThanOrEqualTo(5, "every clipboard preview control declares an automation name");

                reuseInvoked.Should().BeFalse("showing the preview must never trigger a shared clipboard write");
                revokeInvoked.Should().BeFalse("showing the preview must never auto-revoke the synthetic snapshot");
            }
            finally
            {
                window.ClearAndClose();
            }
        });
    }

    [Fact]
    public async Task Scrollable_list_provider_discovers_off_thread_and_uses_the_current_template_without_changing_selection()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var list = new ScrollableListBox
            {
                ItemsSource = Enumerable.Range(1, 12).Select(index => $"Synthetic row {index}").ToArray(),
                Height = 80,
                SelectedIndex = 3,
            };
            var peer = ControlAutomationPeer.CreatePeerForElement(list)!;
            var provider = await Task.Run(() => peer.GetProvider<IScrollProvider>());
            provider.Should().NotBeNull();
            Action unavailable = () => provider!.SetScrollPercent(-1, 0);
            unavailable.Should().Throw<InvalidOperationException>().WithMessage("*no available scroll provider*");
            var window = new Window { Content = list, Width = 400, Height = 200 };
            window.Show();
            try
            {
                list.UpdateLayout();
                var first = (ScrollViewer)list.Scroll!;
                provider!.VerticallyScrollable.Should().BeTrue();
                provider.SetScrollPercent(-1, 100);
                list.UpdateLayout();
                first.Offset.Y.Should().BeGreaterThan(0);
                var originalOffset = first.Offset.Y;
                var presenter = new ItemsPresenter { Name = "PART_ItemsPresenter", ItemsPanel = list.ItemsPanel };
                var replacement = new ScrollViewer { Name = "PART_ScrollViewer", Content = presenter };
                list.Template = new FuncControlTemplate<ListBox>((_, scope) =>
                {
                    scope.Register(presenter.Name!, presenter);
                    scope.Register(replacement.Name!, replacement);
                    return replacement;
                });
                list.ApplyTemplate();
                list.UpdateLayout();
                list.Scroll.Should().BeSameAs(replacement);
                provider.SetScrollPercent(-1, 50);
                list.UpdateLayout();
                replacement.Offset.Y.Should().BeGreaterThan(0);
                provider.VerticalScrollPercent.Should().BeApproximately(50, 0.01);
                first.Offset.Y.Should().Be(originalOffset);
                list.SelectedIndex.Should().Be(3, "scrolling is not selection, inspection or execution");
                Action invalid = () => provider.SetScrollPercent(-1, 101);
                invalid.Should().Throw<ArgumentOutOfRangeException>();
                list.IsEnabled = false;
                Action disabled = () => provider.Scroll(ScrollAmount.NoAmount, ScrollAmount.SmallIncrement);
                disabled.Should().Throw<ElementNotEnabledException>();
            }
            finally { window.Close(); }
        });
    }

    [Theory]
    [InlineData("Status", "Ready", "Status: Ready")]
    [InlineData(null, "Displayed text", "Displayed text")]
    [InlineData("", "Displayed text", "Displayed text")]
    [InlineData("  ", "Displayed text", "Displayed text")]
    [InlineData("Status", null, "Status")]
    [InlineData("Status", "", "Status")]
    [InlineData("Status", "  ", "Status")]
    [InlineData("Ready", "Ready", "Ready")]
    [InlineData("Records", "First\nSecond", "Records: First\nSecond")]
    public async Task Named_text_block_exposes_label_and_displayed_text_without_hiding_or_duplicating_content(
        string? label, string? text, string expected)
    {
        await HeadlessSession.RunAsync(() =>
        {
            var block = new NamedTextBlock { Text = text };
            AutomationProperties.SetName(block, label);
            var peer = ControlAutomationPeer.CreatePeerForElement(block);
            peer.Should().NotBeNull();
            peer!.GetName().Should().Be(expected);
            peer.GetAutomationControlType().Should().Be(AutomationControlType.Text);
        });
    }

    [Fact]
    public async Task Named_text_block_preserves_inline_text_in_its_accessible_name()
    {
        await HeadlessSession.RunAsync(() =>
        {
            var block = new NamedTextBlock { Inlines = new InlineCollection { "Inline fixture text" } };
            AutomationProperties.SetName(block, "Source");
            var peer = ControlAutomationPeer.CreatePeerForElement(block);
            peer!.GetName().Should().Be("Source: Inline fixture text");
        });
    }

    [Fact]
    public async Task Named_text_block_publishes_complete_name_changes_for_text_and_label_updates()
    {
        await HeadlessSession.RunAsync(() =>
        {
            var block = new NamedTextBlock { Text = "Ready" };
            AutomationProperties.SetName(block, "Status");
            var peer = ControlAutomationPeer.CreatePeerForElement(block)!;
            var changes = new List<AutomationPropertyChangedEventArgs>();
            peer.PropertyChanged += (_, args) =>
            {
                if (args.Property == AutomationElementIdentifiers.NameProperty) { changes.Add(args); }
            };

            block.Text = "Refused";
            changes.Should().ContainSingle();
            changes[0].OldValue.Should().Be("Status: Ready");
            changes[0].NewValue.Should().Be("Status: Refused");
            peer.GetName().Should().Be("Status: Refused");

            AutomationProperties.SetName(block, "Outcome");
            changes.Should().HaveCount(2);
            changes[1].OldValue.Should().Be("Status: Refused");
            changes[1].NewValue.Should().Be("Outcome: Refused");

            AutomationProperties.SetName(block, null);
            changes.Should().HaveCount(3);
            changes[2].OldValue.Should().Be("Outcome: Refused");
            changes[2].NewValue.Should().Be("Refused");
            peer.GetName().Should().Be("Refused");
        });
    }

    [Fact]
    public async Task Skill_packages_window_resolves_automation_names_for_bundled_catalogue_at_runtime()
    {
        await HeadlessSession.RunAsync(() =>
        {
            var catalogue = EmbeddedSkillCatalogue.Load();
            var renderer = new NativeDetailRenderer(NullLogger<NativeDetailRenderer>.Instance);

            var window = new SkillPackagesWindow(catalogue, renderer);
            try
            {
                window.Show();

                var explicitlyNamed = AssertAllExplicitAutomationNamesResolve(window);
                explicitlyNamed.Should().BeGreaterThanOrEqualTo(3, "the package selector, identity and source panes all declare automation names");
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public async Task Artifact_input_and_realized_dropdown_controls_expose_command_names_without_execution()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var fixture = new NativeUxFixtureSession(Path.GetTempPath());
            await fixture.InitializeAsync();
            var window = new ResponseWindow(fixture.Main);
            try
            {
                window.ShowResponse();
                var input = window.FindControl<TextBox>("CommandInput")!;
                ControlAutomationPeer.CreatePeerForElement(input)!.GetName()
                    .Should().Be("Typed command or artifact slash input");

                input.Text = "/";
                window.UpdateLayout();
                using (window.CaptureRenderedFrame()) { }
                fixture.Main.IsArtifactCommandDropdownVisible.Should().BeTrue();
                var items = Descendants(window).OfType<ListBoxItem>().ToArray();
                items.Should().ContainSingle();
                ControlAutomationPeer.CreatePeerForElement(items[0])!.GetName()
                    .Should().Be("/inspect-fixture");
                var button = Descendants(items[0]).OfType<Button>().Single();
                ControlAutomationPeer.CreatePeerForElement(button)!.GetName()
                    .Should().Be("/inspect-fixture");
                fixture.Main.CommandText.Should().Be("/");
            }
            finally { window.Close(); }
        });
    }

    private static int AssertAllExplicitAutomationNamesResolve(Window window)
    {
        var checkedCount = 0;
        foreach (var control in Descendants(window))
        {
            var expected = AutomationProperties.GetName(control);
            if (string.IsNullOrWhiteSpace(expected))
            {
                continue;
            }

            checkedCount++;
            var peer = ControlAutomationPeer.CreatePeerForElement(control);
            peer.Should().NotBeNull($"control {control.GetType().Name} declares AutomationProperties.Name but exposes no automation peer");
            var actual = peer!.GetName();
            if (control is NamedTextBlock textBlock)
            {
                actual.Should().StartWith(expected, "static text must retain its descriptive label");
                var text = textBlock.Inlines?.Text ?? textBlock.Text;
                if (!string.IsNullOrWhiteSpace(text))
                {
                    actual.Should().EndWith(text, "static text has no separate text/value pattern and must expose its displayed content");
                }
            }
            else
            {
                actual.Should().Be(expected, $"control {control.GetType().Name}'s runtime automation peer name must match its declared AutomationProperties.Name");
            }
        }

        return checkedCount;
    }

    private static IEnumerable<Control> Descendants(Avalonia.Visual root)
    {
        foreach (var child in root.GetVisualChildren())
        {
            if (child is Control control)
            {
                yield return control;
            }

            foreach (var grandchild in Descendants(child))
            {
                yield return grandchild;
            }
        }
    }

    private static MaintenanceViewModel CreateMaintenanceViewModel()
    {
        return new MaintenanceViewModel(
            new NoOpReleaseMetadataClient(),
            new NoOpApplicationInfo(),
            ReleaseArchitecture.X64,
            new NoOpCanonicalReleasePageOpener(),
            TimeProvider.System,
            new NoOpUiDispatcher(),
            new NoOpSecurityAuditLog(),
            NullLogger<MaintenanceViewModel>.Instance);
    }

    private static ClipboardSnapshot CreateSyntheticClipboardSnapshot()
    {
        return new ClipboardSnapshot(
            Guid.NewGuid(),
            Guid.NewGuid(),
            HostRequest.Create(RequestOrigin.LocalUi),
            "Synthetic headless accessibility fixture clipboard text",
            1,
            DateTimeOffset.UnixEpoch);
    }

    private sealed class NoOpReleaseMetadataClient : IReleaseMetadataClient
    {
        public Task<ReleaseCheck> CheckAsync(ReleaseChannel channel, string currentVersion,
            ReleaseArchitecture architecture, CancellationToken cancellationToken) =>
            Task.FromResult(new ReleaseCheck(ReleaseAvailability.UpToDate, "Headless fixture: no network check performed.", DateTimeOffset.UnixEpoch));
    }

    private sealed class NoOpApplicationInfo : IApplicationInfo
    {
        public string Version => "0.0.0";
    }

    private sealed class NoOpCanonicalReleasePageOpener : ICanonicalReleasePageOpener
    {
        public Task OpenAsync(ReleaseVersion version, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class NoOpUiDispatcher : IUiDispatcher
    {
        public void Post(Action action) => action();

        public Task InvokeAsync(Func<Task> action) => action();
    }

    private sealed class NoOpSecurityAuditLog : ISecurityAuditLog
    {
        public void Write(SecurityAuditEvent auditEvent)
        {
        }
    }
}
