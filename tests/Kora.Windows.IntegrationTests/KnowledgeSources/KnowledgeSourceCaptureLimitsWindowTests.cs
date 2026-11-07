using System.Diagnostics;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.VisualTree;
using AwesomeAssertions;
using Kora.Application.Configuration;
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
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests.KnowledgeSources;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class KnowledgeSourceCaptureLimitsWindowTests
{
    [Fact]
    public Task NativeAndOriginalCommandsShareExactReviewConfirmCancelResetAndNextRefresh() =>
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
            fixture.Main.BindClipboardOwnershipGate(static () => true);
            var root = Path.Combine(OwnedStorageFixture.ScratchParent(), ".capture-limits-ui-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                await File.WriteAllTextAsync(Path.Combine(root, "a.md"), "alpha", fixture.Token);
                var preferences = new LocalKnowledgeSourceCaptureLimitsPreferences(fixture);
                var audit = new Audit();
                var store = new WindowsKnowledgeSourceStore(fixture, preferences);
                var manager = new KnowledgeSourceManager(new WindowsLocalFileInspector(fixture), store, audit, TimeProvider.System, preferences);
                var service = new KnowledgeSourceCaptureLimitsConfigurationService(store, preferences, audit, TimeProvider.System,
                    NullLogger<KnowledgeSourceCaptureLimitsConfigurationService>.Instance);
                var window = new KnowledgeSourcesWindow(fixture.Main, manager, new Picker(root), captureLimits: service);
                window.Show();
                window.UpdateLayout();
                try
                {
                    Field(window, "Knowledge source name").Text = "Team";
                    await Press(window, "Choose folder and review metadata");
                    await Press(window, "Confirm read of this exact reviewed membership");
                    await Press(window, "Confirm save of this exact complete device-local snapshot");
                    var original = (await store.ListAsync(fixture.Token)).Single();
                    var list = window.GetVisualDescendants().OfType<ListBox>().Single();
                    list.SelectedIndex = 0;
                    await Press(window, "Get selected source capture limits");
                    Field(window, "Source maximum file count").Text.Should().Be("32");
                    Field(window, "Source maximum file count").Text = "2";
                    Field(window, "Source maximum per-file bytes").Text = "16";
                    Field(window, "Source maximum total bytes").Text = "32";
                    await Press(window, "Review selected source lower capture limits");
                    preferences.Load(original.Id).Should().BeNull();
                    await Press(window, "Cancel source capture limits review");
                    preferences.Load(original.Id).Should().BeNull();
                    await Press(window, "Review selected source lower capture limits");
                    Field(window, "Source maximum file count").Text = "1";
                    window.UpdateLayout();
                    window.GetVisualDescendants().OfType<Button>().Should().NotContain(b =>
                        string.Equals(b.Content as string, "Confirm this exact source capture limits preference", StringComparison.Ordinal));
                    await Press(window, "Review selected source lower capture limits");
                    await Press(window, "Confirm this exact source capture limits preference");
                    preferences.Load(original.Id).Should().Be(new KnowledgeSourceCaptureLimits(1, 16, 32));
                    (await store.ListAsync(fixture.Token)).Single().Version.Should().Be(original.Version);
                    await Press(window, "Refresh selected source (new review)");
                    manager.Review!.Metadata.CaptureLimits.Should().Be(new KnowledgeSourceCaptureLimits(1, 16, 32));
                    await Press(window, "Confirm read of this exact reviewed membership");
                    await Press(window, "Confirm save of this exact complete device-local snapshot");
                    var refreshed = (await store.ListAsync(fixture.Token)).Single();
                    refreshed.Folder.CaptureLimits.Should().Be(new KnowledgeSourceCaptureLimits(1, 16, 32));

                    await Command(window, "source capture limits reset \"Team\"");
                    await Command(window, "source capture limits cancel");
                    preferences.Load(original.Id).Should().NotBeNull();
                    await Command(window, "source capture limits reset \"Team\"");
                    await Command(window, "source capture limits confirm");
                    preferences.Load(original.Id).Should().BeNull();
                    refreshed.Folder.CaptureLimits.Should().Be(new KnowledgeSourceCaptureLimits(1, 16, 32));
                    await Command(window, "source capture limits lower \"Team\" 1 1 1");
                    await Command(window, "source capture limits confirm");
                    await Press(window, "Reload source inventory");
                    list.SelectedIndex = 0;
                    await Press(window, "Refresh selected source (new review)");
                    manager.Review.Should().BeNull();
                    manager.Candidate.Should().BeNull();
                    (await store.ListAsync(fixture.Token)).Single().State.Should().Be(KnowledgeSourceState.Unavailable);
                    await Press(window, "Reload source inventory");
                    list.SelectedIndex = 0;
                    await Press(window, "Review removal of Kora copies");
                    await Press(window, "Confirm remove this inventoried source and Kora copies");
                    (await store.ListAsync(fixture.Token)).Should().BeEmpty();
                    preferences.Load(original.Id).Should().BeNull();
                    (await File.ReadAllTextAsync(Path.Combine(root, "a.md"), fixture.Token)).Should().Be("alpha");
                }
                finally { await window.StopAsync(); }
            }
            finally { Directory.Delete(root, recursive: true); }
        });

    private static Task Command(KnowledgeSourcesWindow window, string text) =>
        HostRequestRunner.RunAsync(RequestOrigin.LocalUi, () => window.OpenCaptureLimitsAsync(
            KnowledgeSourceCaptureLimitsCommand.Parse(text, "Kora")!, () => true));
    private static TextBox Field(Window window, string name) => window.GetVisualDescendants().OfType<TextBox>().Single(item =>
        string.Equals(AutomationProperties.GetName(item), name, StringComparison.Ordinal));
    private static async Task Press(Window window, string name)
    {
        window.UpdateLayout();
        var button = window.GetVisualDescendants().OfType<Button>().Single(item =>
            string.Equals(item.Content as string, name, StringComparison.Ordinal));
        await ((AsyncCommand)button.Command!).ExecuteAsync();
        window.UpdateLayout();
    }
    private sealed class Picker(string root) : IUserFolderPicker
    {
        public Task<string?> SelectFolderAsync(CancellationToken cancellationToken) => Task.FromResult<string?>(root);
    }
    private sealed class Audit : ISecurityAuditLog
    {
        public void Write(SecurityAuditEvent auditEvent) { }
    }
}
