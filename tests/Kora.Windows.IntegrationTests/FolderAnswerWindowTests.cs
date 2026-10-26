using System.Collections.Immutable;
using System.Text;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.VisualTree;
using AwesomeAssertions;
using Kora.Application.Infrastructure;
using Kora.Controls;
using Kora.Core.Context;
using Kora.Core.Context.FolderAnswers;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class FolderAnswerWindowTests
{
    [Fact]
    public async Task CompleteExcerptReviewPrecedesSeparateConfirmationAndExactCrossFileNavigation()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var source = Source();
            var prepares = 0;
            var answers = 0;
            FolderAnswerReview? actual = null;
            var window = new LocalFilePreviewWindow(message => throw new InvalidOperationException(message));
            window.ShowFolderRevision(source, (_, _, _) => throw new NotSupportedException(), () => true,
                () => Task.CompletedTask, (question, current, token) =>
                {
                    prepares++;
                    question.Should().Be("alpha?");
                    current().Should().BeTrue();
                    actual = Review(source, question);
                    return Task.FromResult(new FolderAnswerPreparation(LocalFileAnswerOutcome.Succeeded, actual));
                }, (review, current, token) =>
                {
                    answers++;
                    review.Should().BeSameAs(actual);
                    current().Should().BeTrue();
                    return Task.FromResult(new LocalFileAnswerResult(LocalFileAnswerOutcome.Succeeded,
                        [new("A genuine cited model claim", review.Search.Citations.ToImmutableArray())], review.Search.MatchingChunks));
                });
            window.Show();
            try
            {
                Question(window).Text = "alpha?";
                prepares.Should().Be(0);
                answers.Should().Be(0);
                Confirm(window).IsEnabled.Should().BeFalse();
                await Press(window, "Review folder excerpts for this question");
                prepares.Should().Be(1);
                answers.Should().Be(0);
                Status(window).Text.Should().Contain("REVIEW NEEDED").And.Contain("2 selected excerpts")
                    .And.Contain(actual!.ExcerptUtf8Bytes.ToString(System.Globalization.CultureInfo.InvariantCulture))
                    .And.Contain("bounded retrieval omitted other matches: True");
                Rows(window).ItemsSource.Should().BeSameAs(actual.Search.Citations);
                Confirm(window).IsEnabled.Should().BeTrue();
                await Press(window, Confirm(window).Content!.ToString()!);
                answers.Should().Be(1);
                Confirm(window).IsEnabled.Should().BeFalse();
                Status(window).Text.Should().Contain("A genuine cited model claim");
                var citation = actual.Search.Citations[1];
                Rows(window).SelectedItem = citation;
                await Press(window, "Show exact folder answer citation");
                window.UpdateLayout();
                var span = window.GetVisualDescendants().OfType<NamedTextBlock>().Single(block =>
                    AutomationProperties.GetName(block)?.StartsWith("Exact cited original source span", StringComparison.Ordinal) == true);
                span.Text.Should().Be(citation.Excerpt);
                span.IsFocused.Should().BeTrue();
                var files = window.GetVisualDescendants().OfType<ComboBox>().Single(combo =>
                    AutomationProperties.GetName(combo)?.StartsWith("Inspect each exact", StringComparison.Ordinal) == true);
                files.SelectedItem.Should().BeSameAs(source.Files[1]);
                Question(window).Text = "changed";
                Status(window).Text.Should().BeNull();
                Rows(window).ItemsSource.Should().BeNull();
                span.Text.Should().BeNull();
                answers.Should().Be(1);
            }
            finally { window.ClearAndClose(); }
        });
    }

    [Theory]
    [InlineData("question", false)]
    [InlineData("cancel", false)]
    [InlineData("close", false)]
    [InlineData("source", false)]
    [InlineData("question", true)]
    [InlineData("cancel", true)]
    [InlineData("close", true)]
    [InlineData("source", true)]
    public async Task ChangesCancelActualWorkAndSuppressLateReviewOrClaims(string change, bool duringAnswer)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var source = Source();
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var current = true;
            CancellationToken actualToken = default;
            var window = new LocalFilePreviewWindow(message => throw new InvalidOperationException(message));
            window.ShowFolderRevision(source, (_, _, _) => throw new NotSupportedException(), () => current,
                () => Task.CompletedTask, async (question, gate, token) =>
                {
                    if (!duringAnswer) { actualToken = token; await release.Task; }
                    return new(LocalFileAnswerOutcome.Succeeded, Review(source, question));
                }, async (_, gate, token) =>
                {
                    actualToken = token;
                    await release.Task;
                    return LocalFileAnswerResult.Empty(LocalFileAnswerOutcome.Succeeded);
                });
            window.Show();
            try
            {
                Question(window).Text = "alpha";
                Task work;
                if (duringAnswer)
                {
                    await Press(window, "Review folder excerpts for this question");
                    work = Press(window, Confirm(window).Content!.ToString()!);
                }
                else { work = Press(window, "Review folder excerpts for this question"); }
                var status = Status(window);
                switch (change)
                {
                    case "question": Question(window).Text = "other"; break;
                    case "cancel":
                        window.GetVisualDescendants().OfType<Button>().Single(button => string.Equals(button.Content as string,
                            "Cancel folder answer and clear excerpts", StringComparison.Ordinal))
                            .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                        break;
                    case "close": window.ClearAndClose(); break;
                    default: current = false; break;
                }
                if (!string.Equals(change, "source", StringComparison.Ordinal)) { actualToken.IsCancellationRequested.Should().BeTrue(); }
                work.IsCompleted.Should().BeFalse();
                release.SetResult();
                await work;
                status.Text.Should().BeNull();
            }
            finally { window.ClearAndClose(); }
        });
    }

    private static TextBox Question(Window window) => window.GetVisualDescendants().OfType<TextBox>().Single(control =>
        string.Equals(AutomationProperties.GetName(control), "Question across the exact immutable folder snapshot", StringComparison.Ordinal));
    private static NamedTextBlock Status(Window window) => window.GetVisualDescendants().OfType<NamedTextBlock>().Single(control =>
        string.Equals(AutomationProperties.GetName(control), "Folder answer review and local model outcome", StringComparison.Ordinal));
    private static ListBox Rows(Window window) => window.GetVisualDescendants().OfType<ListBox>().Single(control =>
        string.Equals(AutomationProperties.GetName(control), "Complete selected folder excerpts and exact citations", StringComparison.Ordinal));
    private static Button Confirm(Window window) => window.GetVisualDescendants().OfType<Button>().Single(button =>
        string.Equals(button.Content as string, "Confirm: answer locally using only these reviewed folder excerpts", StringComparison.Ordinal));
    private static Task Press(Window window, string label) =>
        ((AsyncCommand)window.GetVisualDescendants().OfType<Button>().Single(button =>
            string.Equals(button.Content as string, label, StringComparison.Ordinal)).Command!).ExecuteAsync();

    private static FolderAnswerReview Review(LocalFolderRevision source, string question)
    {
        var input = new HostRequest(new(Guid.NewGuid()), source.Review.Request.SessionId, source.Review.Request.TaskId, RequestOrigin.LocalUi);
        var search = new LocalFileLexicalRetrieval().Search(source, source.Reference, question, DateTimeOffset.UnixEpoch, CancellationToken.None);
        return new(source, question, search with { MatchingChunks = 3, Truncated = true },
            LocalContextSessionTarget.ReviewedFresh(input, HostRequest.Create(RequestOrigin.LocalUi)));
    }

    private static LocalFolderRevision Source()
    {
        const string text = "alpha marker\r\nuntrusted instruction";
        var request = HostRequest.Create(RequestOrigin.LocalUi);
        var metadata = new LocalFolderMetadata(@"C:\Team", "directory",
            Enumerable.Range(0, 2).Select(index => new LocalFileMetadata($@"C:\Team\{index}.md",
                $"identity-{index}", Encoding.UTF8.GetByteCount(text), DateTimeOffset.UnixEpoch)));
        var review = new LocalFolderReview(Guid.NewGuid(), Guid.NewGuid(), request, metadata);
        return new(review, metadata.Files.Select(file => new LocalFileRevision(new(review.ReviewId, review.SourceId, request, file),
            Encoding.UTF8.GetBytes(text), DateTimeOffset.UnixEpoch)));
    }
}
