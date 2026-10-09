using System.Text;

using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Core.Skills;
using Kora.Windows.IntegrationTests.Audio;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class SharedSkillWithdrawalWindowTests
{
    [WindowsFact]
    public async Task Exact_native_confirmation_is_separate_from_selection_and_names_all_metadata_and_effects()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var source = Source();
            var window = Window();
            window.Show();
            try
            {
                window.SetSources([source]);
                window.FindControl<ComboBox>("SourceSelector")!.SelectedIndex = 0;
                var pending = window.ConfirmWithdrawalAsync(source, TestContext.Current.CancellationToken);
                pending.IsCompleted.Should().BeFalse();
                var dialog = window.OwnedWindows.Single();
                var identity = dialog.GetVisualDescendants().OfType<TextBlock>().Single(block =>
                    block.Text?.Contains("Source ID:", StringComparison.Ordinal) == true);
                identity.Text.Should().Contain(source.Id.ToString("N")).And.Contain(source.DirectoryIdentity)
                    .And.Contain("shared skill files are untouched").And.Contain("No enablement, execution or grants change");
                var confirm = dialog.GetVisualDescendants().OfType<Button>().Single(button =>
                    button.Content is string text && text.StartsWith("Unregister source:", StringComparison.Ordinal));
                confirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                (await pending).Should().BeTrue();
                identity.Text.Should().BeEmpty();
                dialog.Content.Should().BeNull();
                window.SelectedSource.Should().Be(source);
            }
            finally { window.Close(); }
        });
    }

    [WindowsFact]
    public async Task Cancellation_and_window_close_clear_confirmation_and_all_catalogue_text_hex_and_metadata()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var source = Source();
            var window = Window();
            window.Show();
            window.SetSources([source]);
            window.FindControl<ComboBox>("SourceSelector")!.SelectedIndex = 0;
            var snapshot = SharedSkillSnapshot.Parse(source.Id, "test\\SKILL.md", [0xff], false, ["test\\private.ps1"]);
            window.SetCatalogue(new(source, [snapshot]));
            window.FindControl<TextBox>("SharedSource")!.Text.Should().Contain("FF");
            window.SelectedPackage.Should().Be(snapshot);
            using var cancelled = new CancellationTokenSource();
            var pending = window.ConfirmWithdrawalAsync(source, cancelled.Token);
            await cancelled.CancelAsync();
            (await pending).Should().BeFalse();
            window.OwnedWindows.Should().BeEmpty();
            window.ClearCatalogue();
            AssertCleared(window);
            window.SetCatalogue(new(source, [snapshot]));
            window.Close();
            AssertCleared(window);
            window.SelectedSource.Should().BeNull();
            window.RegisteredSources.Should().BeEmpty();
            ((Action)(() => window.SetCatalogue(new(source, [snapshot])))).Should().Throw<InvalidOperationException>();
            ((Action)(() => window.SetSources([source]))).Should().Throw<InvalidOperationException>();
        });
    }

    [WindowsFact]
    public async Task Selection_change_and_removed_source_cannot_publish_a_late_catalogue()
    {
        await HeadlessSession.RunAsync(() =>
        {
            var source = Source();
            var other = new SharedSkillSource(Guid.NewGuid(), ".other\\skills", new string('b', 48));
            var window = Window();
            window.Show();
            try
            {
                window.SetSources([source, other]);
                var selector = window.FindControl<ComboBox>("SourceSelector")!;
                selector.SelectedIndex = 0;
                var snapshot = SharedSkillSnapshot.Parse(source.Id, "test\\SKILL.md", Encoding.UTF8.GetBytes("Private instructions"), false);
                var catalogue = new SharedSkillCatalogue(source, [snapshot]);
                window.SetCatalogue(catalogue);
                selector.SelectedIndex = 1;
                AssertCleared(window);
                ((Action)(() => window.SetCatalogue(catalogue))).Should().Throw<InvalidOperationException>();
                window.SetSources([other]);
                ((Action)(() => window.SetCatalogue(catalogue))).Should().Throw<InvalidOperationException>();
                AssertCleared(window);
            }
            finally { window.Close(); }
        });
    }

    private static void AssertCleared(SharedSkillSourcesWindow window)
    {
        window.SelectedPackage.Should().BeNull();
        window.FindControl<TextBox>("SharedSource")!.Text.Should().BeEmpty();
        window.FindControl<TextBox>("UninspectedEntries")!.Text.Should().BeEmpty();
        window.FindControl<TextBlock>("SharedIdentity")!.Text.Should().BeEmpty();
        window.FindControl<Expander>("UninspectedDisclosure")!.IsVisible.Should().BeFalse();
    }

    private static SharedSkillSourcesWindow Window() => new(
        static () => Task.CompletedTask, static () => Task.CompletedTask,
        static () => Task.CompletedTask, static () => Task.CompletedTask, static () => Task.CompletedTask);
    private static SharedSkillSource Source() => new(Guid.NewGuid(), ".agents\\skills", new string('a', 48));
}
