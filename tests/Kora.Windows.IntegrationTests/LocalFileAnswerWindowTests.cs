using System.Text;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.VisualTree;
using AwesomeAssertions;
using Kora.Application.Infrastructure;
using Kora.Controls;
using Kora.Core.Context;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class LocalFileAnswerWindowTests
{
    [Fact]
    public async Task SeparateExplicitNativeRequestDisplaysClaimsAndExactCitationWithoutPassiveDispatchOrFocus()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var source = Source();
            var citation = new LocalFileLexicalRetrieval().Search(source, source.Reference, "alpha",
                DateTimeOffset.UnixEpoch, CancellationToken.None).Citations.Single();
            var response = new LocalFileAnswerResult(LocalFileAnswerOutcome.Succeeded, [new("Exact marker claim", [citation])], 1)
            {
                ExecutionReason = LocalContextTurnReason.Completed,
                ExecutionState = HostTaskState.Succeeded,
            };
            var calls = 0;
            var window = new LocalFilePreviewWindow(_ => { });
            window.ShowRevision(source, (_, _, _) => Task.FromResult(LocalFileSearchResult.Empty(
                LocalFileSearchOutcome.NoMatch, DateTimeOffset.UnixEpoch)), () => true, () => Task.CompletedTask,
                (question, gate, token) =>
                {
                    question.Should().Be("alpha?");
                    gate().Should().BeTrue();
                    token.IsCancellationRequested.Should().BeFalse();
                    calls++;
                    return Task.FromResult(response);
                });
            window.Show();
            window.UpdateLayout();
            try
            {
                calls.Should().Be(0);
                window.GetVisualDescendants().OfType<NamedTextBlock>().Should().Contain(block =>
                    block.Text != null && block.Text.Contains("Only host session/task receipts are retained", StringComparison.Ordinal)
                    && block.Text.Contains(source.Review.Request.SessionId.Value.ToString("D"), StringComparison.Ordinal)
                    && block.Text.Contains(source.Review.Request.TaskId.Value.ToString("D"), StringComparison.Ordinal));
                window.FocusAnswer();
                var question = Question(window);
                question.IsFocused.Should().BeTrue();
                question.Text = "alpha?";
                calls.Should().Be(0);
                await Press(window, "Use this exact snapshot once to answer locally");
                calls.Should().Be(1);
                window.GetVisualDescendants().OfType<Button>().Single(button => string.Equals(button.Content as string,
                    "Use this exact snapshot once to answer locally", StringComparison.Ordinal)).IsEnabled.Should().BeFalse();
                question.IsFocused.Should().BeTrue();
                Status(window).Text.Should().Contain("Succeeded").And.Contain("Exact marker claim")
                    .And.Contain(source.RevisionId.ToString("D")).And.Contain(citation.Excerpt);
                var rows = window.GetVisualDescendants().OfType<ListBox>().Single(list =>
                    AutomationProperties.GetName(list)?.StartsWith("Exact source citations", StringComparison.Ordinal) == true);
                rows.SelectedItem = citation;
                await Press(window, "Show exact answer citation");
                window.UpdateLayout();
                var span = window.GetVisualDescendants().OfType<NamedTextBlock>().Single(block =>
                    AutomationProperties.GetName(block)?.StartsWith("Exact cited original source span", StringComparison.Ordinal) == true);
                span.Text.Should().Be(citation.Excerpt);
                span.IsFocused.Should().BeTrue();
                span.Bounds.Height.Should().BeGreaterThan(0);
                question.Text = "changed question";
                Status(window).Text.Should().BeNull();
                rows.ItemsSource.Should().BeNull();
                span.Text.Should().BeNull();
            }
            finally { window.ClearAndClose(); }
        });
    }

    [Theory]
    [InlineData("question")]
    [InlineData("source")]
    [InlineData("close")]
    public async Task NativeRequestChangesFenceLateModelClaimsAndCitationNavigation(string change)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var source = Source();
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var current = true;
            Func<bool>? gate = null;
            var window = new LocalFilePreviewWindow(_ => { });
            window.ShowRevision(source, (_, _, _) => throw new NotSupportedException(), () => current, () => Task.CompletedTask,
                async (_, isCurrent, _) =>
                {
                    gate = isCurrent;
                    await release.Task;
                    return LocalFileAnswerResult.Empty(LocalFileAnswerOutcome.Succeeded);
                });
            window.Show();
            try
            {
                Question(window).Text = "alpha";
                var status = Status(window);
                var run = Press(window, "Use this exact snapshot once to answer locally");
                window.GetVisualDescendants().OfType<Button>().Single(button => string.Equals(button.Content as string,
                    "Use this exact snapshot once to answer locally", StringComparison.Ordinal)).IsEnabled.Should().BeFalse();
                gate!().Should().BeTrue();
                if (string.Equals(change, "source", StringComparison.Ordinal)) { current = false; }
                else if (string.Equals(change, "close", StringComparison.Ordinal)) { window.ClearAndClose(); }
                else { Question(window).Text = "changed"; }
                gate().Should().BeFalse();
                release.SetResult();
                await run;
                status.Text.Should().BeNull();
            }
            finally { window.ClearAndClose(); }
        });
    }

    [Theory]
    [InlineData(LocalFileAnswerOutcome.SourceUseUnavailable, "source-use admission service")]
    [InlineData(LocalFileAnswerOutcome.NoEvidence, "No model was invoked")]
    [InlineData(LocalFileAnswerOutcome.Unsupported, "no supported answer")]
    [InlineData(LocalFileAnswerOutcome.ContextLimit, "Nothing was silently dropped")]
    [InlineData(LocalFileAnswerOutcome.Unavailable, "No answer claimed")]
    public async Task NativeFailuresAndNoEvidenceAreExplicitWithoutAnswerRows(LocalFileAnswerOutcome outcome, string disclosure)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var window = new LocalFilePreviewWindow(_ => { });
            window.ShowRevision(Source(), (_, _, _) => throw new NotSupportedException(), () => true, () => Task.CompletedTask,
                (_, _, _) => Task.FromResult(LocalFileAnswerResult.Empty(outcome)));
            window.Show();
            try
            {
                Question(window).Text = "alpha";
                await Press(window, "Use this exact snapshot once to answer locally");
                Status(window).Text.Should().Contain(disclosure);
                window.GetVisualDescendants().OfType<ListBox>().Should().OnlyContain(rows => rows.ItemsSource == null);
            }
            finally { window.ClearAndClose(); }
        });
    }

    private static LocalFileRevision Source()
    {
        var bytes = Encoding.UTF8.GetBytes("# Heading\r\nalpha exact marker");
        return new(new(Guid.NewGuid(), Guid.NewGuid(), HostRequest.Create(RequestOrigin.LocalUi),
            new(@"C:\Team\guide.md", "identity", bytes.Length, DateTimeOffset.UnixEpoch)), bytes, DateTimeOffset.UnixEpoch);
    }

    private static TextBox Question(Window window) => window.GetVisualDescendants().OfType<TextBox>().Single(box =>
        string.Equals(AutomationProperties.GetName(box), "Question using only this exact immutable file snapshot", StringComparison.Ordinal));
    private static NamedTextBlock Status(Window window) => window.GetVisualDescendants().OfType<NamedTextBlock>().Single(block =>
        string.Equals(AutomationProperties.GetName(block), "Local file answer outcome and cited model claims", StringComparison.Ordinal));
    private static Task Press(Window window, string caption)
    {
        window.UpdateLayout();
        return ((AsyncCommand)window.GetVisualDescendants().OfType<Button>().Single(button =>
            string.Equals(button.Content as string, caption, StringComparison.Ordinal)).Command!).ExecuteAsync();
    }
}
