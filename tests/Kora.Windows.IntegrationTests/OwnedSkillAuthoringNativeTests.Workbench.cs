using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Application.Skills;
using Kora.NativeUxFixture;
using Kora.Windows.Skills;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

public sealed partial class OwnedSkillAuthoringNativeTests
{
    [Theory]
    [InlineData("save", 1120)]
    [InlineData("save", 640)]
    [InlineData("hide-review", 1120)]
    [InlineData("leave-confirm", 640)]
    [InlineData("minimize-confirm", 1120)]
    public async Task HostedSkillEditorPreservesDraftAndRequiresSeparateCurrentCompletePublicationReview(string scenario, int width)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var parent = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".net-test-artifacts"));
            Directory.CreateDirectory(parent);
            using var f = new NativeUxFixtureSession(parent);
            await f.InitializeAsync();
            await using var admission = new OwnedSkillControlAdmission(f.Interactions, f.Interactions, new(f.Tasks));
            var store = new WindowsOwnedDeclarativeSkillStore(f);
            await using var controller = new OwnedSkillEditorController(f.Main, new(store, admission, f.Audit),
                admission, static () => true, static () => 1, NullLogger<OwnedSkillEditorController>.Instance);
            using var responses = new ResponseWindowController(f.Main, NullLogger<ResponseWindowController>.Instance);
            controller.BindWorkbench(responses.PrepareWorkbenchOwner,
                view => responses.ShowHostedView(WorkbenchActivity.SkillEditor, view));
            controller.Bind();
            async Task Run(string text)
            {
                f.Main.CommandText = text;
                await f.Main.RunTypedCommand.ExecuteAsync().WaitAsync(TimeSpan.FromSeconds(5), f.Token);
            }
            var windows = new List<Window>();
            using var opened = Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) => windows.Add(window));
            await Run("owned skill create example 1.0.0");
            await Run("owned skill name: Example");
            await Run("owned skill description: Description");
            await Run("owned skill guidance: Complete literal guidance");
            await Run("owned skill validate");
            var view = controller.View!;
            var document = view.Document();
            var shell = responses.PrepareWorkbenchOwner();
            shell.Width = width;
            shell.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            var saving = Run("owned skill save");
            var accept = await FindHostedSkillDecision(shell, "Continue to separate native publication confirmation", saving, f.Token);
            var review = shell.FindControl<ContentControl>("NativeReviewContent")!;
            var sources = review.GetVisualDescendants().OfType<TextBox>().ToArray();
            sources.Should().HaveCount(2).And.OnlyContain(source => source.IsReadOnly);
            sources[0].Text.Should().Be(document.ManifestText);
            sources[1].Text.Should().Be(document.Guidance);
            accept.IsEffectivelyVisible.Should().BeTrue();
            (await store.Read("example", "1.0.0", f.Token)).Should().BeNull();
            if (scenario is not "hide-review")
            {
                accept.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                accept = await FindHostedSkillDecision(shell, "Publish exact new version", saving, f.Token);
                sources = review.GetVisualDescendants().OfType<TextBox>().ToArray();
                sources.Should().HaveCount(2);
                sources[0].Text.Should().Be(document.ManifestText);
                sources[1].Text.Should().Be(document.Guidance);
                (await store.Read("example", "1.0.0", f.Token)).Should().BeNull("source review alone is not publication permission");
            }
            switch (scenario)
            {
                case "save": accept.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); break;
                case "hide-review": shell.Hide(); shell.Show(); shell.Activate(); break;
                case "leave-confirm":
                    shell.ShowHostedView(WorkbenchActivity.Help, new TextBlock { Text = "Another view" });
                    shell.ShowHostedView(WorkbenchActivity.SkillEditor, view);
                    break;
                case "minimize-confirm":
                    shell.WindowState = WindowState.Minimized;
                    shell.WindowState = WindowState.Normal;
                    break;
            }
            await saving;
            var saved = await store.Read("example", "1.0.0", f.Token);
            if (scenario is "save")
            {
                saved.Should().NotBeNull();
                saved!.Document.CompleteText.Should().Be(document.CompleteText);
                await Run("owned skill list");
                await Run("owned skill reopen example 1.0.0");
                view.Document().CompleteText.Should().Be(document.CompleteText);
            }
            else
            {
                saved.Should().BeNull();
                view.Document().CompleteText.Should().Be(document.CompleteText);
                sources.Should().OnlyContain(source => string.IsNullOrEmpty(source.Text), "expired review bodies must be released");
                accept.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                (await store.Read("example", "1.0.0", f.Token)).Should().BeNull("an old control cannot readmit a retired review");
            }
            windows.Distinct().Should().Equal(shell);
            var exit = controller.PrepareClose();
            if (scenario is not "save")
            {
                var discard = await FindHostedSkillDecision(shell, "Discard", exit, f.Token);
                discard.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
            (await exit).Should().BeTrue();
            controller.View.Should().BeNull();
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HostedSkillDirtyOwnerCloseHonorsCancelOrCompletesOriginalClose(bool discard)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var parent = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".net-test-artifacts"));
            Directory.CreateDirectory(parent);
            using var f = new NativeUxFixtureSession(parent);
            await f.InitializeAsync();
            await using var admission = new OwnedSkillControlAdmission(f.Interactions, f.Interactions, new(f.Tasks));
            await using var controller = new OwnedSkillEditorController(f.Main,
                new(new WindowsOwnedDeclarativeSkillStore(f), admission, f.Audit), admission,
                static () => true, static () => 1, NullLogger<OwnedSkillEditorController>.Instance);
            using var responses = new ResponseWindowController(f.Main, NullLogger<ResponseWindowController>.Instance);
            controller.BindWorkbench(responses.PrepareWorkbenchOwner,
                view => responses.ShowHostedView(WorkbenchActivity.SkillEditor, view));
            controller.Bind();
            f.Main.CommandText = "owned skill create example 1.0.0";
            await f.Main.RunTypedCommand.ExecuteAsync();
            var view = controller.View!;
            view.Guidance.Text = "Unsaved exact draft";
            var shell = responses.PrepareWorkbenchOwner();
            shell.Close();
            var closing = controller.ActiveOperation;
            var choice = await FindHostedSkillDecision(shell, discard ? "Discard" : "Cancel", closing, f.Token);
            choice.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await closing.WaitAsync(TimeSpan.FromSeconds(5), f.Token);
            shell.IsVisible.Should().Be(!discard);
            if (discard) { controller.View.Should().BeNull(); }
            else
            {
                controller.View.Should().BeSameAs(view);
                view.Guidance.Text.Should().Be("Unsaved exact draft");
                var exit = controller.PrepareClose();
                choice = await FindHostedSkillDecision(shell, "Discard", exit, f.Token);
                choice.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                (await exit).Should().BeTrue();
            }
        });
    }

    private static async Task<Button> FindHostedSkillDecision(ResponseWindow shell, string caption, Task operation, CancellationToken token)
    {
        var ready = new TaskCompletionSource<Button>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Check(object? sender, EventArgs args)
        {
            var button = shell.FindControl<ContentControl>("NativeReviewContent")!
                .GetVisualDescendants().OfType<Button>().FirstOrDefault(button => string.Equals(button.Content as string, caption, StringComparison.Ordinal));
            if (button is not null) { ready.TrySetResult(button); }
        }
        shell.LayoutUpdated += Check;
        try
        {
            shell.UpdateLayout();
            Check(null, EventArgs.Empty);
            var next = await Task.WhenAny(ready.Task, operation).WaitAsync(TimeSpan.FromSeconds(5), token);
            if (ReferenceEquals(next, operation))
            { throw new InvalidOperationException("The owned skill operation ended before its native decision: " + caption); }
            return await ready.Task;
        }
        finally { shell.LayoutUpdated -= Check; }
    }
}
