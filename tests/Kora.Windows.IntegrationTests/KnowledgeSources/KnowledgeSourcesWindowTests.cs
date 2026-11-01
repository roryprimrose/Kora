using System.Diagnostics;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.VisualTree;
using AwesomeAssertions;
using Kora.Application.Hosting;
using Kora.Application.Infrastructure;
using Kora.Application.KnowledgeSources;
using Kora.Core.Auditing;
using Kora.Core.Context;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.KnowledgeSources;
using Kora.KnowledgeSources;
using Kora.NativeUxFixture;
using Kora.Windows.Context;
using Kora.Windows.IntegrationTests.Storage;
using Kora.Windows.KnowledgeSources;

namespace Kora.Windows.IntegrationTests.KnowledgeSources;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class KnowledgeSourcesWindowTests
{
    [Fact]
    public Task ExplicitNativeDescendantAddReadSaveRestartSearchAndCitationSelectExactRetainedItem() =>
        HeadlessSession.RunAsync(async () =>
        {
            using var listener = new ActivityListener
            {
                ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            };
            ActivitySource.AddActivityListener(listener);
            using var fixture = new NativeUxFixtureSession(OwnedStorageFixture.ScratchParent());
            await fixture.InitializeAsync();
            var root = Path.Combine(OwnedStorageFixture.ScratchParent(), ".knowledge-recursive-ui-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                Directory.CreateDirectory(Path.Combine(root, "A"));
                Directory.CreateDirectory(Path.Combine(root, "B"));
                Directory.CreateDirectory(Path.Combine(root, "Empty"));
                await File.WriteAllTextAsync(Path.Combine(root, "A", "notes.md"), "alpha first saved text", fixture.Token);
                var second = Path.Combine(root, "B", "notes.md");
                await File.WriteAllTextAsync(second, "alpha second saved text", fixture.Token);
                var store = new WindowsKnowledgeSourceStore(fixture);
                var picker = new Picker(root);
                var manager = new KnowledgeSourceManager(new WindowsLocalFileInspector(fixture), store, new Audit(), TimeProvider.System);
                var window = new KnowledgeSourcesWindow(fixture.Main, manager, picker);
                window.Show();
                window.UpdateLayout();
                try
                {
                    var name = window.GetVisualDescendants().OfType<TextBox>().Single(item =>
                        string.Equals(AutomationProperties.GetName(item), "Knowledge source name", StringComparison.Ordinal));
                    name.Text = "Nested";
                    var recursion = window.GetVisualDescendants().OfType<CheckBox>().Single();
                    recursion.IsChecked.Should().NotBeTrue();
                    await Press(window, "Choose folder and review metadata");
                    manager.Review.Should().BeNull();
                    (await store.ListAsync(fixture.Token)).Should().BeEmpty();
                    recursion.IsChecked = true;
                    await Press(window, "Choose folder and review metadata");
                    picker.Selections.Should().Be(2);
                    manager.Review!.Metadata.Scope.Should().Be(KnowledgeSourceScope.BoundedDescendants);
                    window.GetVisualDescendants().OfType<TextBlock>().Should().Contain(item =>
                        item.Text != null && item.Text.Contains("Directory: Empty", StringComparison.Ordinal)
                        && item.Text.Contains(@"A\notes.md", StringComparison.Ordinal)
                        && item.Text.Contains(@"B\notes.md", StringComparison.Ordinal));
                    var preview = Preview(window);
                    preview.Text.Should().BeNull();
                    await Press(window, "Confirm read of this exact reviewed membership");
                    preview.Text.Should().Be("alpha first saved text");
                    (await store.ListAsync(fixture.Token)).Should().BeEmpty();
                    await Press(window, "Confirm save of this exact complete device-local snapshot");
                    (await store.ListAsync(fixture.Token)).Should().ContainSingle();
                }
                finally { await window.StopAsync(); }

                var reopened = new KnowledgeSourcesWindow(fixture.Main,
                    new(new WindowsLocalFileInspector(fixture), new WindowsKnowledgeSourceStore(fixture), new Audit(), TimeProvider.System));
                reopened.Show();
                reopened.UpdateLayout();
                try
                {
                    await Press(reopened, "Reload source inventory");
                    var list = reopened.GetVisualDescendants().OfType<ListBox>().Single();
                    list.SelectedIndex = 0;
                    Preview(reopened).Text.Should().BeNull();
                    await Press(reopened, "Inspect selected exact stored snapshot");
                    var query = reopened.GetVisualDescendants().OfType<TextBox>().Single(item =>
                        string.Equals(AutomationProperties.GetName(item), "Search only the explicitly selected registered source", StringComparison.Ordinal));
                    query.Text = "alpha";
                    await Press(reopened, "Search this exact inspected source locally");
                    await Press(reopened, @"Inspect cited saved item: B\notes.md, line 1");
                    var preview = Preview(reopened);
                    preview.Text.Should().Be("alpha second saved text");
                    preview.SelectionStart.Should().Be(0);
                    preview.SelectionEnd.Should().Be(preview.Text!.Length);
                    list.SelectedItem = null;
                    preview.Text.Should().BeNull();
                    list.SelectedIndex = 0;
                    await Press(reopened, "Refresh selected source (new review)");
                    (await store.ListAsync(fixture.Token)).Single().State.Should().Be(KnowledgeSourceState.Unavailable);
                    await Press(reopened, "Confirm read of this exact reviewed membership");
                    await Press(reopened, "Confirm save of this exact complete device-local snapshot");
                    list.SelectedIndex = 0;
                    await Press(reopened, "Review removal of Kora copies");
                    await Press(reopened, "Confirm remove this inventoried source and Kora copies");
                    (await store.ListAsync(fixture.Token)).Should().BeEmpty();
                    (await File.ReadAllTextAsync(second, fixture.Token)).Should().Be("alpha second saved text");
                }
                finally { await reopened.StopAsync(); }
            }
            finally { Directory.Delete(root, recursive: true); }
        });

    private static TextBox Preview(Window window) => window.GetVisualDescendants().OfType<TextBox>().Single(item =>
        string.Equals(AutomationProperties.GetName(item), "Exact admitted untrusted text, never executed", StringComparison.Ordinal));

    private sealed class Picker(string root) : IUserFolderPicker
    {
        internal int Selections;
        public Task<string?> SelectFolderAsync(CancellationToken cancellationToken)
        {
            Selections++;
            return Task.FromResult<string?>(root);
        }
    }

    [Fact]
    public Task NativeSelectionInspectionSearchDisableRefreshAndInventoriedRemovalAreSeparateOperations() =>
        HeadlessSession.RunAsync(async () =>
        {
            using var listener = new ActivityListener
            {
                ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            };
            ActivitySource.AddActivityListener(listener);
            using var fixture = new NativeUxFixtureSession(OwnedStorageFixture.ScratchParent());
            await fixture.InitializeAsync();
            var originalDirectory = Path.Combine(OwnedStorageFixture.ScratchParent(), ".knowledge-ui-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(originalDirectory);
            try
            {
                var original = Path.Combine(originalDirectory, "notes.md");
                await File.WriteAllTextAsync(original, "alpha synthetic notes", TestContext.Current.CancellationToken);
                var store = new WindowsKnowledgeSourceStore(fixture);
                var manager = new KnowledgeSourceManager(new WindowsLocalFileInspector(fixture), store, new Audit(), TimeProvider.System);
                await HostRequestRunner.RunAsync(RequestOrigin.LocalUi, async () =>
                {
                    var review = await manager.ReviewAsync("Team", originalDirectory, null, () => true, fixture.Token);
                    var candidate = await manager.CaptureAsync(review.ReviewId, () => true, fixture.Token);
                    await manager.PublishAsync(candidate, () => true, fixture.Token);
                });
                var window = new KnowledgeSourcesWindow(fixture.Main, manager);
                window.Show();
                window.UpdateLayout();
                try
                {
                    await Press(window, "Reload source inventory");
                    var list = window.GetVisualDescendants().OfType<ListBox>().Single();
                    list.SelectedIndex = 0;
                    var preview = window.GetVisualDescendants().OfType<TextBox>().Single(item =>
                        string.Equals(AutomationProperties.GetName(item), "Exact admitted untrusted text, never executed", StringComparison.Ordinal));
                    preview.Text.Should().BeNull();
                    await Press(window, "Inspect selected exact stored snapshot");
                    preview.Text.Should().Be("alpha synthetic notes");
                    var query = window.GetVisualDescendants().OfType<TextBox>().Single(item =>
                        string.Equals(AutomationProperties.GetName(item), "Search only the explicitly selected registered source", StringComparison.Ordinal));
                    query.Text = "alpha";
                    await Press(window, "Search this exact inspected source locally");
                    window.GetVisualDescendants().OfType<TextBlock>().Should().Contain(item =>
                        item.Text != null && item.Text.Contains("Matched; 1 excerpts", StringComparison.Ordinal)
                        && item.Text.Contains("alpha synthetic notes", StringComparison.Ordinal));
                    await Press(window, "Disable selected source");
                    preview.Text.Should().BeNull();
                    var disabled = (await store.ListAsync(fixture.Token)).Single();
                    disabled.State.Should().Be(KnowledgeSourceState.Disabled);
                    list.SelectedIndex = 0;
                    await Press(window, "Refresh selected source (new review)");
                    (await store.ListAsync(fixture.Token)).Single().State.Should().Be(KnowledgeSourceState.Unavailable);
                    preview.Text.Should().BeNull();
                    await Press(window, "Confirm read of this exact reviewed membership");
                    preview.Text.Should().Be("alpha synthetic notes");
                    (await store.ListAsync(fixture.Token)).Single().State.Should().Be(KnowledgeSourceState.Unavailable);
                    await Press(window, "Confirm save of this exact complete device-local snapshot");
                    (await store.ListAsync(fixture.Token)).Single().State.Should().Be(KnowledgeSourceState.Enabled);
                    list.SelectedIndex = 0;
                    await Press(window, "Review removal of Kora copies");
                    (await store.ListAsync(fixture.Token)).Should().ContainSingle();
                    await Press(window, "Confirm remove this inventoried source and Kora copies");
                    (await store.ListAsync(fixture.Token)).Should().BeEmpty();
                    (await File.ReadAllTextAsync(original, fixture.Token)).Should().Be("alpha synthetic notes");
                    fixture.Main.IsVoiceEnabled.Should().BeFalse();
                }
                finally { await window.StopAsync(); }
            }
            finally { Directory.Delete(originalDirectory, recursive: true); }
        });

    private static Task Press(Window window, string caption) =>
        ((AsyncCommand)window.GetVisualDescendants().OfType<Button>()
            .Single(button => string.Equals(button.Content?.ToString(), caption, StringComparison.Ordinal)).Command!).ExecuteAsync();

    private sealed class Audit : ISecurityAuditLog
    {
        public void Write(SecurityAuditEvent auditEvent) { }
    }
}
