using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Core.Artifacts;

namespace Kora.Windows.IntegrationTests.ArtifactDiscovery;

public sealed partial class OwnedDefinitionEditorWindowTests
{
    [Theory]
    [InlineData(ArtifactKind.Instruction, "save", 1120)]
    [InlineData(ArtifactKind.Prompt, "save", 640)]
    [InlineData(ArtifactKind.Instruction, "hide", 640)]
    [InlineData(ArtifactKind.Prompt, "minimize", 1120)]
    [InlineData(ArtifactKind.Instruction, "leave", 1120)]
    public async Task HostedInstructionReviewRequiresCurrentCompleteSourceAndPreservesDraft(ArtifactKind kind, string action, int width)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var windows = new List<Window>();
            using var opened = Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) => windows.Add(window));
            await using var f = await DraftValidationFixture.Create(kind, false, hosted: true);
            var shell = f.Shell;
            shell.Width = width;
            var view = f.View;
            var original = view.Source.Text!;
            var proposed = original + "\r\nRetained literal draft";
            view.Source.Text = proposed;
            var saving = f.Run(f.Prefix + " save");
            var confirm = await FindHostedInstructionDecision(shell, "Confirm save", saving);
            var review = shell.FindControl<ContentControl>("NativeReviewContent")!;
            var bodies = review.GetVisualDescendants().OfType<TextBox>().ToArray();
            bodies.Should().HaveCount(2).And.OnlyContain(source => source.IsReadOnly && !source.IsUndoEnabled);
            bodies[0].Text.Should().Be(original);
            bodies[1].Text.Should().Be(proposed);
            File.ReadAllText(f.SourcePath).Should().Be(original);
            switch (action)
            {
                case "save": confirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); break;
                case "hide": shell.Hide(); shell.Show(); shell.Activate(); break;
                case "minimize":
                    shell.WindowState = WindowState.Minimized;
                    shell.WindowState = WindowState.Normal;
                    break;
                case "leave":
                    shell.ShowHostedView(WorkbenchActivity.Help, new TextBlock { Text = "Another view" });
                    shell.ShowHostedView(WorkbenchActivity.InstructionEditor, view);
                    break;
            }
            await saving.WaitAsync(TimeSpan.FromSeconds(5), f.Session.Token);
            view.Source.Text.Should().Be(proposed);
            File.ReadAllText(f.SourcePath).Should().Be(action is "save" ? proposed : original);
            bodies.Should().OnlyContain(source => string.IsNullOrEmpty(source.Text));
            if (action is not "save")
            {
                confirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                File.ReadAllText(f.SourcePath).Should().Be(original);
                f.Store.Writes.Should().Be(0);
            }
            windows.Distinct().Should().Equal(shell);
        });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task HostedRebaseRetainsRawDraftAndRequiresFreshSeparateSave(bool adopt)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            await using var f = await DraftValidationFixture.Create(ArtifactKind.Instruction, false, hosted: true);
            var view = f.View;
            var original = f.Source!;
            var latest = original with { Document = Document(ArtifactKind.Instruction, "PRIVATE_LATEST_BODY") };
            f.Store.ReadOverride = (_, _) => Task.FromResult<OwnedInstructionSnapshot?>(latest);
            var draft = view.Source.Text + "\r\n PRIVATE_RAW_DRAFT ";
            view.Source.Text = draft;
            var rebasing = f.Run("instructions rebase");
            var confirm = await FindHostedInstructionDecision(f.Shell, "Adopt latest baseline only", rebasing);
            var review = f.Shell.FindControl<ContentControl>("NativeReviewContent")!;
            var sources = review.GetVisualDescendants().OfType<TextBox>().ToArray();
            sources.Should().HaveCount(3);
            sources.Select(source => source.Text).Should().Equal(original.Document.Text, latest.Document.Text, draft);
            if (adopt) { confirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
            else
            {
                f.Shell.Hide();
                f.Shell.Show();
                f.Shell.Activate();
            }
            await rebasing.WaitAsync(TimeSpan.FromSeconds(5), f.Session.Token);
            view.Source.Text.Should().Be(draft);
            f.Store.Writes.Should().Be(0);
            File.ReadAllText(f.SourcePath).Should().Be(original.Document.Text);
            sources.Should().OnlyContain(source => string.IsNullOrEmpty(source.Text));
        });
    }

    [Theory]
    [InlineData("hide")]
    [InlineData("leave")]
    public async Task HostedFindReattachesWithoutRestoringOldMatchesOrReadingAnotherSource(string action)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            await using var f = await DraftValidationFixture.Create(ArtifactKind.Instruction, false, hosted: true);
            var view = f.View;
            var raw = view.Source.Text!;
            view.BufferFindQuery.Text = "PRIVATE";
            view.BufferFindFirst.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            view.Source.SelectedText.Should().Be("PRIVATE");
            if (action is "hide")
            {
                f.Shell.Hide();
                f.Shell.Show();
                f.Shell.Activate();
            }
            else
            {
                f.Shell.ShowHostedView(WorkbenchActivity.Help, new TextBlock { Text = "Another view" });
                f.Shell.ShowHostedView(WorkbenchActivity.InstructionEditor, view);
            }
            view.BufferFindQuery.Text.Should().BeEmpty();
            view.Source.SelectedText.Should().BeEmpty();
            view.Source.Text.Should().Be(raw);
            view.BufferFindQuery.Text = "ORIGINAL";
            view.BufferFindFirst.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            view.Source.SelectedText.Should().Be("ORIGINAL");
            f.Store.Reads.Should().Be(0);
            f.Store.Writes.Should().Be(0);
        });
    }

    [Theory]
    [InlineData("hide")]
    [InlineData("minimize")]
    [InlineData("leave")]
    public async Task HostedLateRebaseAdmissionCannotReadOrReopenReviewAfterReturn(string change)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            await using var f = await DraftValidationFixture.Create(ArtifactKind.Instruction, false, hosted: true);
            var view = f.View;
            view.Source.Text += "\r\nRETAINED_DRAFT";
            var draft = view.Source.Text;
            f.Sessions.PauseNext = true;
            var rebasing = f.Run("instructions rebase");
            try
            {
                await f.Sessions.Started.Task.WaitAsync(TimeSpan.FromSeconds(5), f.Session.Token);
                switch (change)
                {
                    case "hide": f.Shell.Hide(); f.Shell.Show(); f.Shell.Activate(); break;
                    case "minimize":
                        f.Shell.WindowState = WindowState.Minimized;
                        f.Shell.WindowState = WindowState.Normal;
                        break;
                    case "leave":
                        f.Shell.ShowHostedView(WorkbenchActivity.Help, new TextBlock { Text = "Another view" });
                        f.Shell.ShowHostedView(WorkbenchActivity.InstructionEditor, view);
                        break;
                }
            }
            finally { f.Sessions.Release.TrySetResult(); }
            await rebasing.WaitAsync(TimeSpan.FromSeconds(5), f.Session.Token);
            view.Source.Text.Should().Be(draft);
            f.Store.Reads.Should().Be(0);
            f.Store.Writes.Should().Be(0);
            f.Shell.FindControl<ContentControl>("NativeReviewContent")!.GetVisualDescendants()
                .OfType<OwnedInstructionRebaseReviewView>().Should().BeEmpty();
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HostedInstructionDirtyCloseKeepsDraftOnCancelOrCompletesOwnerClose(bool discard)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            await using var f = await DraftValidationFixture.Create(ArtifactKind.Instruction, false, hosted: true);
            var view = f.View;
            var draft = view.Source.Text + "\r\nUNSAVED_DRAFT";
            view.Source.Text = draft;
            f.Shell.Close();
            var closing = f.WaitForEditorOperation();
            var choice = await FindHostedInstructionDecision(f.Shell, discard ? "Discard" : "Cancel", closing);
            choice.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await closing;
            Dispatcher.UIThread.RunJobs();
            if (discard)
            {
                f.Shell.IsVisible.Should().BeFalse();
                view.Source.Text.Should().BeEmpty();
            }
            else
            {
                f.Shell.IsVisible.Should().BeTrue();
                view.Source.Text.Should().Be(draft);
            }
            f.Store.Writes.Should().Be(0);
        });
    }

    private static async Task<Button> FindHostedInstructionDecision(ResponseWindow shell, string caption, Task operation)
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
            var next = await Task.WhenAny(ready.Task, operation).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            if (ReferenceEquals(next, operation))
            { throw new InvalidOperationException("The instruction operation ended before its native decision: " + caption); }
            return await ready.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        }
        finally { shell.LayoutUpdated -= Check; }
    }
}
