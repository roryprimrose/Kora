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
            LocalContextSessionTarget? actualTarget = null;
            var window = new LocalFilePreviewWindow(_ => { });
            window.ShowRevision(source, (_, _, _) => Task.FromResult(LocalFileSearchResult.Empty(
                LocalFileSearchOutcome.NoMatch, DateTimeOffset.UnixEpoch)), () => true, () => Task.CompletedTask,
                (question, target, gate, token) =>
                {
                    actualTarget = target;
                    target.Request.SessionId.Should().NotBe(source.Review.Request.SessionId);
                    target.Request.TaskId.Should().NotBe(source.Review.Request.TaskId);
                    target.SourceRequest.SessionId.Should().Be(source.Review.Request.SessionId);
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
                    && block.Text.Contains("Reviewed session:", StringComparison.Ordinal));
                window.FocusAnswer();
                var question = Question(window);
                question.IsFocused.Should().BeTrue();
                question.Text = "alpha?";
                calls.Should().Be(0);
                await Press(window, "Use this exact snapshot once to answer locally");
                calls.Should().Be(1);
                window.GetVisualDescendants().OfType<NamedTextBlock>().Should().Contain(block =>
                    block.Text != null && block.Text.Contains(actualTarget!.Request.SessionId.Value.ToString("D"), StringComparison.Ordinal)
                    && block.Text.Contains(actualTarget.Request.TaskId.Value.ToString("D"), StringComparison.Ordinal));
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
                async (_, _, isCurrent, _) =>
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
                if (string.Equals(change, "question", StringComparison.Ordinal))
                {
                    status.Text.Should().Contain("No answer claimed").And.Contain("work has finished");
                }
                else { status.Text.Should().BeNull(); }
            }
            finally { window.ClearAndClose(); }
        });
    }

    [Theory]
    [InlineData(LocalFileAnswerOutcome.SourceUseUnavailable, "source-use risk screening is incomplete, unknown or not composed")]
    [InlineData(LocalFileAnswerOutcome.Denied, "credential-like risk or invalid exact source evidence")]
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
                (_, _, _, _) => Task.FromResult(LocalFileAnswerResult.Empty(outcome) with
                { SourceUseDecision = outcome == LocalFileAnswerOutcome.SourceUseUnavailable
                    ? LocalFileModelUseDecision.SourceUsePolicyMissing
                    : outcome == LocalFileAnswerOutcome.Denied ? LocalFileModelUseDecision.Denied : null }));
            window.Show();
            try
            {
                window.GetVisualDescendants().OfType<TextBlock>().Select(block => block.Text)
                    .Should().Contain(text => text != null && text.Contains(LocalSourceRiskPolicy.Disclosure, StringComparison.Ordinal));
                Question(window).Text = "alpha";
                await Press(window, "Use this exact snapshot once to answer locally");
                Status(window).Text.Should().Contain(disclosure);
                if (outcome == LocalFileAnswerOutcome.SourceUseUnavailable)
                {
                    Status(window).Text.Should().Contain(nameof(LocalFileModelUseDecision.SourceUsePolicyMissing));
                }
                window.GetVisualDescendants().OfType<ListBox>().Should().OnlyContain(rows => rows.ItemsSource == null);
            }
            finally { window.ClearAndClose(); }
        });
    }

    [Theory]
    [InlineData(LocalFileAnswerOutcome.Succeeded)]
    [InlineData(LocalFileAnswerOutcome.Denied)]
    [InlineData(LocalFileAnswerOutcome.Unavailable)]
    public async Task AnotherQuestionRequiresFreshExplicitReviewWithoutRecaptureOrReusingConsumedTarget(LocalFileAnswerOutcome outcome)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var source = Source();
            var citation = new LocalFileLexicalRetrieval().Search(source, source.Reference, "alpha",
                DateTimeOffset.UnixEpoch, CancellationToken.None).Citations.Single();
            var targets = new List<LocalContextSessionTarget>();
            var gates = new List<Func<bool>>();
            var window = new LocalFilePreviewWindow(message => throw new InvalidOperationException(message));
            window.ShowRevision(source, (_, _, _) => throw new NotSupportedException("No search during recovery"),
                () => true, () => throw new NotSupportedException("No recapture during recovery"),
                (_, target, current, _) =>
                {
                    targets.Add(target);
                    gates.Add(current);
                    return Task.FromResult(new LocalFileAnswerResult(outcome,
                        outcome == LocalFileAnswerOutcome.Succeeded ? [new("Old claim", [citation])] : [], 1));
                });
            window.Show();
            try
            {
                Question(window).Text = "alpha";
                await Press(window, "Use this exact snapshot once to answer locally");
                var recovery = FindButton(window, "Review another question on this exact snapshot");
                recovery.IsEnabled.Should().BeTrue();
                if (outcome == LocalFileAnswerOutcome.Succeeded)
                {
                    AnswerRows(window).SelectedItem = citation;
                    await Press(window, "Show exact answer citation");
                }
                Click(recovery);
                targets.Should().ContainSingle();
                gates[0]().Should().BeFalse();
                Status(window).Text.Should().Contain("Fresh review").And.NotContain("Old claim");
                window.GetVisualDescendants().OfType<NamedTextBlock>().Single(block =>
                    string.Equals(AutomationProperties.GetName(block), "Untrusted local file plain text content", StringComparison.Ordinal))
                    .Text.Should().Be(source.Text);
                AnswerRows(window).ItemsSource.Should().BeNull();
                window.GetVisualDescendants().OfType<NamedTextBlock>().Select(block => AutomationProperties.GetName(block))
                    .Should().NotContain(name => name != null && name.StartsWith("Exact cited original source span", StringComparison.Ordinal));
                FindButton(window, "Use this exact snapshot once to answer locally").IsEnabled.Should().BeTrue();
                recovery.IsEnabled.Should().BeFalse();
                await Press(window, "Use this exact snapshot once to answer locally");
                targets.Should().HaveCount(2);
                targets[1].Request.SessionId.Should().NotBe(targets[0].Request.SessionId);
                targets[1].Request.TaskId.Should().NotBe(targets[0].Request.TaskId);
                targets[1].SourceRequest.Should().BeSameAs(targets[0].SourceRequest);
                window.GetVisualDescendants().OfType<NamedTextBlock>().Should().Contain(block =>
                    block.Text != null && block.Text.Contains(targets[1].Request.SessionId.Value.ToString("D"), StringComparison.Ordinal)
                    && block.Text.Contains(targets[1].Request.TaskId.Value.ToString("D"), StringComparison.Ordinal));
            }
            finally { window.ClearAndClose(); }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancelOrQuestionEditKeepsRecoveryClosedUntilActualAnswerWorkJoins(bool edit)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            CancellationToken actualToken = default;
            var calls = 0;
            var window = new LocalFilePreviewWindow(message => throw new InvalidOperationException(message));
            window.ShowRevision(Source(), (_, _, _) => throw new NotSupportedException(), () => true,
                () => throw new NotSupportedException(), async (_, _, _, token) =>
                {
                    calls++;
                    actualToken = token;
                    await release.Task;
                    return new(LocalFileAnswerOutcome.Succeeded, [new("Forbidden late claim", [])], 1);
                });
            window.Show();
            try
            {
                Question(window).Text = "alpha";
                var work = Press(window, "Use this exact snapshot once to answer locally");
                if (edit) { Question(window).Text = "changed question"; }
                else { Click(FindButton(window, "Cancel file answer and clear claims")); }
                actualToken.IsCancellationRequested.Should().BeTrue();
                work.IsCompleted.Should().BeFalse();
                Status(window).Text.Should().Contain("Waiting").And.Contain("No answer claimed");
                var recovery = FindButton(window, "Review another question on this exact snapshot");
                recovery.IsEnabled.Should().BeFalse();
                Click(recovery);
                await Press(window, "Use this exact snapshot once to answer locally");
                calls.Should().Be(1);
                release.SetResult();
                await work;
                Status(window).Text.Should().NotContain("Forbidden late claim").And.NotContain("Waiting");
                AnswerRows(window).ItemsSource.Should().BeNull();
                recovery.IsEnabled.Should().BeTrue();
                Click(recovery);
                calls.Should().Be(1);
                await Press(window, "Use this exact snapshot once to answer locally");
                calls.Should().Be(2);
            }
            finally { release.TrySetResult(); window.ClearAndClose(); }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RetiredOrReplacedSourceCannotUseOldRecoveryOrSubmitControls(bool replace)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var current = true;
            var calls = 0;
            var window = new LocalFilePreviewWindow(message => throw new InvalidOperationException(message));
            Task<LocalFileAnswerResult> Answer(string question, LocalContextSessionTarget target, Func<bool> gate, CancellationToken token)
            {
                calls++;
                return Task.FromResult(LocalFileAnswerResult.Empty(LocalFileAnswerOutcome.Unavailable));
            }
            window.ShowRevision(Source(), (_, _, _) => throw new NotSupportedException(), () => current,
                () => throw new NotSupportedException(), Answer);
            window.Show();
            try
            {
                Question(window).Text = "alpha";
                await Press(window, "Use this exact snapshot once to answer locally");
                var recovery = FindButton(window, "Review another question on this exact snapshot");
                var submit = FindButton(window, "Use this exact snapshot once to answer locally");
                if (replace)
                {
                    window.ShowRevision(Source(), (_, _, _) => throw new NotSupportedException(), () => true,
                        () => throw new NotSupportedException(), Answer);
                }
                else { current = false; }
                window.UpdateLayout();
                Click(recovery);
                await ((AsyncCommand)submit.Command!).ExecuteAsync();
                calls.Should().Be(1);
                if (replace) { Status(window).Text.Should().BeNull(); }
            }
            finally { window.ClearAndClose(); }
        });
    }

    private static Button FindButton(Window window, string caption) => window.GetVisualDescendants().OfType<Button>().Single(button =>
        string.Equals(button.Content as string, caption, StringComparison.Ordinal));
    private static void Click(Button button) =>
        button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
    private static ListBox AnswerRows(Window window) => window.GetVisualDescendants().OfType<ListBox>().Single(list =>
        AutomationProperties.GetName(list)?.StartsWith("Exact source citations", StringComparison.Ordinal) == true);

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
