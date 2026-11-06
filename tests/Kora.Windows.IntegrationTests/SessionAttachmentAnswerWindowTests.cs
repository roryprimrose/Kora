using System.Text;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using AwesomeAssertions;
using Kora.Application.Hosting;
using Kora.Application.Infrastructure;
using Kora.Core.Context;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class SessionAttachmentAnswerWindowTests
{
    [Theory]
    [InlineData(480)]
    [InlineData(800)]
    public async Task KeyboardReadsCompleteEvidenceAndAnswerWithoutImplicitConsentAndRetiresOldReaders(int width)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var source = Source(string.Join("\n", Enumerable.Range(0, 100).Select(index => $"alpha exact line {index}")));
            var submissions = 0;
            var answer = string.Join("\n", Enumerable.Range(0, 90).Select(index => $"Claim alpha {index}"))
                + "\n[Retained source reference: exact final marker]";
            var window = new LocalFilePreviewWindow(_ => throw new InvalidOperationException("No failure expected."));
            window.ShowAttachment(source, (_, _, _) => throw new InvalidOperationException("No search requested"),
                static () => true, null);
            window.ShowAttachmentAnswer(source, (question, admitted, _) =>
            {
                var evidence = new LocalFileLexicalRetrieval().Search(source.File, source.File.Reference, question,
                    DateTimeOffset.UtcNow, CancellationToken.None);
                return Task.FromResult(new SessionAttachmentAnswerReview(Guid.NewGuid(),
                    new(new(source.Session, new(1), true), null), source, evidence, question,
                    RequestOrigin.LocalUi, 0, DateTimeOffset.UtcNow.AddMinutes(2), admitted));
            }, (review, admitted, _) =>
            {
                admitted().Should().BeTrue();
                submissions++;
                var request = new HostRequest(new(Guid.NewGuid()), source.Session, new(Guid.NewGuid()), RequestOrigin.LocalUi);
                var provenance = new ModelTurnProvenance(new(Guid.NewGuid()), request, ModelProviderSelection.OllamaCandidate);
                return Task.FromResult(new ConversationSubmissionReceipt(
                    new(request, new(1), 1, 3, DateTimeOffset.UtcNow, review.Question,
                        ModelTurnOutcome.Succeeded, ModelTurnReason.ProviderOutcome)
                    {
                        Version = 2,
                        Execution = provenance,
                        ExecutionMetadataRevision = 0,
                        AssistantResponse = new(provenance, answer),
                    }, new(request, new(3), HostTaskState.Succeeded)));
            }, static () => true, CancellationToken.None);
            window.Width = width;
            window.Height = 800;
            window.Show();
            try
            {
                var question = window.GetVisualDescendants().OfType<TextBox>()
                    .Single(control => control.PlaceholderText?.StartsWith("Question about", StringComparison.Ordinal) == true);
                question.Text = "alpha";
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                await ((AsyncCommand)FindButton(window, "Review exact evidence").Command!).ExecuteAsync();
                window.UpdateLayout();
                var evidence = window.GetVisualDescendants().OfType<TextBlock>()
                    .Single(block => block.Text?.StartsWith("Question:\nalpha", StringComparison.Ordinal) == true);
                var viewport = evidence.GetVisualAncestors().OfType<ScrollViewer>().First();
                viewport.Focusable.Should().BeTrue();
                viewport.IsTabStop.Should().BeTrue();
                var confirm = FindButton(window, "Consent once:");
                confirm.Focus().Should().BeTrue();
                confirm.IsFocused.Should().BeTrue();
                FocusWithKeyboard(window, viewport, RawInputModifiers.Shift);
                ReadToEnd(window, viewport);
                evidence.Text.Should().Contain("alpha exact line 99");
                PressKey(window, Key.Space, PhysicalKey.Space);
                PressKey(window, Key.Enter, PhysicalKey.Enter);
                submissions.Should().Be(0);
                confirm.IsEnabled.Should().BeTrue();
                FocusWithKeyboard(window, confirm);
                await ((AsyncCommand)confirm.Command!).ExecuteAsync();
                submissions.Should().Be(1);
                confirm.IsEnabled.Should().BeFalse();
                window.UpdateLayout();
                var result = window.GetVisualDescendants().OfType<TextBlock>()
                    .Single(block => block.Text?.StartsWith(answer, StringComparison.Ordinal) == true);
                var resultViewport = result.GetVisualAncestors().OfType<ScrollViewer>().First();
                resultViewport.Focusable.Should().BeTrue();
                resultViewport.IsTabStop.Should().BeTrue();
                FocusWithKeyboard(window, resultViewport);
                ReadToEnd(window, resultViewport);
                result.Text.Should().Contain("[Retained source reference: exact final marker]");
                result.Text.Should().StartWith(answer);
                submissions.Should().Be(1);
                window.ClearAndClose();
                evidence.Text.Should().BeNull();
                result.Text.Should().BeNull();
                viewport.IsEnabled.Should().BeFalse();
                resultViewport.IsEnabled.Should().BeFalse();
                viewport.Focusable.Should().BeFalse();
                resultViewport.Focusable.Should().BeFalse();
            }
            finally { window.ClearAndClose(); }
        });
    }

    [Fact]
    public async Task NativeRetainedSourceRequiresEvidenceReviewThenSeparateConsentAndClearsPrivateControlsOnClose()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var source = Source();
            var submissions = 0;
            var prepared = 0;
            SessionAttachmentAnswerReview? review = null;
            var window = new LocalFilePreviewWindow(_ => { });
            window.ShowAttachment(source, (_, _, _) => throw new InvalidOperationException("No search requested"),
                static () => true, null);
            window.ShowAttachmentAnswer(source, (question, admitted, _) =>
            {
                prepared++;
                var evidence = new LocalFileLexicalRetrieval().Search(source.File, source.File.Reference, question,
                    DateTimeOffset.UtcNow, CancellationToken.None);
                review = new(Guid.NewGuid(), new(new(source.Session, new(1), true), null), source, evidence, question,
                    RequestOrigin.LocalUi, 0, DateTimeOffset.UtcNow.AddMinutes(2), admitted);
                return Task.FromResult(review);
            }, (actual, admitted, _) =>
            {
                submissions++;
                actual.Should().BeSameAs(review);
                admitted().Should().BeTrue();
                var request = new HostRequest(new(Guid.NewGuid()), source.Session, new(Guid.NewGuid()), RequestOrigin.LocalUi);
                return Task.FromResult(new ConversationSubmissionReceipt(
                    new(request, new(1), 1, 2, DateTimeOffset.UtcNow, actual.Question,
                        ModelTurnOutcome.Unavailable, ModelTurnReason.QualificationPending),
                    new(request, new(2), HostTaskState.Unavailable)));
            }, static () => true, CancellationToken.None);
            window.Show();
            try
            {
                var question = window.GetVisualDescendants().OfType<TextBox>()
                    .Single(control => control.PlaceholderText?.StartsWith("Question about", StringComparison.Ordinal) == true);
                var confirm = FindButton(window, "Consent once:");
                question.Text = "alpha";
                submissions.Should().Be(0);
                confirm.IsEnabled.Should().BeFalse();
                await ((AsyncCommand)FindButton(window, "Review exact evidence").Command!).ExecuteAsync();
                prepared.Should().Be(1);
                submissions.Should().Be(0);
                Text(window).Should().Contain(source.File.Text).And.Contain("Question:\nalpha")
                    .And.Contain("Stored-copy consent did not authorize");
                confirm.IsEnabled.Should().BeTrue();
                await ((AsyncCommand)confirm.Command!).ExecuteAsync();
                submissions.Should().Be(1);
                confirm.IsEnabled.Should().BeFalse();
                Text(window).Should().Contain("No answer retained. HOST Unavailable / QualificationPending");
                window.ClearAndClose();
                question.Text.Should().BeNull();
                review!.Invoking(value => _ = value.Question).Should().Throw<InvalidOperationException>();
                confirm.IsEnabled.Should().BeFalse();
            }
            finally { window.ClearAndClose(); }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EditingOrCancellingReviewRetiresExactConsentAndPreventsLateEvidenceDisclosure(bool cancel)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var source = Source();
            SessionAttachmentAnswerReview? review = null;
            var pending = new TaskCompletionSource<SessionAttachmentAnswerReview>(TaskCreationOptions.RunContinuationsAsynchronously);
            var window = new LocalFilePreviewWindow(_ => { });
            window.ShowAttachment(source, (_, _, _) => throw new InvalidOperationException(), static () => true, null);
            window.ShowAttachmentAnswer(source, (question, admitted, _) =>
            {
                var evidence = new LocalFileLexicalRetrieval().Search(source.File, source.File.Reference, question,
                    DateTimeOffset.UtcNow, CancellationToken.None);
                review = new(Guid.NewGuid(), new(new(source.Session, new(1), true), null), source, evidence, question,
                    RequestOrigin.LocalUi, 0, DateTimeOffset.UtcNow.AddMinutes(2), admitted);
                return pending.Task;
            }, (_, _, _) => throw new InvalidOperationException("No model disclosure authorized"),
                static () => true, CancellationToken.None);
            window.Show();
            try
            {
                var question = window.GetVisualDescendants().OfType<TextBox>()
                    .Single(control => control.PlaceholderText?.StartsWith("Question about", StringComparison.Ordinal) == true);
                question.Text = "alpha";
                var operation = ((AsyncCommand)FindButton(window, "Review exact evidence").Command!).ExecuteAsync();
                if (cancel) { FindButton(window, "Cancel / clear").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
                else { question.Text = "changed"; }
                pending.SetResult(review!);
                await operation;
                Text(window).Should().NotContain("Question:\nalpha");
                FindButton(window, "Consent once:").IsEnabled.Should().BeFalse();
                review!.Invoking(value => _ = value.Source).Should().Throw<InvalidOperationException>();
            }
            finally { window.ClearAndClose(); }
        });
    }

    [Fact]
    public async Task ReplacingSourceClearsAndDetachesOldEvidenceReadersWithoutSubmittingOrRetargeting()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var source = Source();
            var replacement = Source("replacement immutable marker");
            var submissions = 0;
            var window = new LocalFilePreviewWindow(_ => throw new InvalidOperationException("No failure expected."));
            window.ShowAttachment(source, (_, _, _) => throw new InvalidOperationException("No search requested"),
                static () => true, null);
            window.ShowAttachmentAnswer(source, (question, admitted, _) =>
            {
                var evidence = new LocalFileLexicalRetrieval().Search(source.File, source.File.Reference, question,
                    DateTimeOffset.UtcNow, CancellationToken.None);
                return Task.FromResult(new SessionAttachmentAnswerReview(Guid.NewGuid(),
                    new(new(source.Session, new(1), true), null), source, evidence, question,
                    RequestOrigin.LocalUi, 0, DateTimeOffset.UtcNow.AddMinutes(2), admitted));
            }, (_, _, _) =>
            {
                submissions++;
                throw new InvalidOperationException("No submission authorized.");
            }, static () => true, CancellationToken.None);
            window.Show();
            try
            {
                var question = window.GetVisualDescendants().OfType<TextBox>()
                    .Single(control => control.PlaceholderText?.StartsWith("Question about", StringComparison.Ordinal) == true);
                question.Text = "alpha";
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                await ((AsyncCommand)FindButton(window, "Review exact evidence").Command!).ExecuteAsync();
                window.UpdateLayout();
                var evidence = window.GetVisualDescendants().OfType<TextBlock>()
                    .Single(block => block.Text?.StartsWith("Question:\nalpha", StringComparison.Ordinal) == true);
                var oldViewport = evidence.GetVisualAncestors().OfType<ScrollViewer>().First();
                oldViewport.Focus().Should().BeTrue();
                window.ShowAttachment(replacement, (_, _, _) => throw new InvalidOperationException("No search requested"),
                    static () => true, null);
                window.UpdateLayout();
                evidence.Text.Should().BeNull();
                question.Text.Should().BeNull();
                oldViewport.IsEnabled.Should().BeFalse();
                oldViewport.Focusable.Should().BeFalse();
                oldViewport.IsTabStop.Should().BeFalse();
                var key = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.End };
                oldViewport.RaiseEvent(key);
                key.Handled.Should().BeFalse();
                Text(window).Should().Contain(replacement.File.Text).And.NotContain(source.File.Text)
                    .And.NotContain("Question:\nalpha");
                submissions.Should().Be(0);
            }
            finally { window.ClearAndClose(); }
        });
    }

    private static Button FindButton(LocalFilePreviewWindow window, string prefix) =>
        window.GetVisualDescendants().OfType<Button>().Single(button =>
            (button.Content as string)?.StartsWith(prefix, StringComparison.Ordinal) == true);

    private static string Text(LocalFilePreviewWindow window) =>
        string.Join("\n", window.GetVisualDescendants().OfType<TextBlock>().Select(block => block.Text));

    private static void ReadToEnd(Window window, ScrollViewer viewport)
    {
        PressKey(window, Key.Home, PhysicalKey.Home);
        window.UpdateLayout();
        viewport.Extent.Height.Should().BeGreaterThan(viewport.Viewport.Height);
        viewport.Offset.Y.Should().Be(0);
        PressKey(window, Key.PageDown, PhysicalKey.PageDown);
        window.UpdateLayout();
        viewport.Offset.Y.Should().BeGreaterThan(0);
        PressKey(window, Key.End, PhysicalKey.End);
        window.UpdateLayout();
        viewport.Offset.Y.Should().BeApproximately(viewport.Extent.Height - viewport.Viewport.Height, 1);
    }

    private static void PressKey(Window window, Key key, PhysicalKey physicalKey,
        RawInputModifiers modifiers = RawInputModifiers.None)
    {
        window.KeyPress(key, modifiers, physicalKey, null);
        window.KeyRelease(key, modifiers, physicalKey, null);
    }

    private static void FocusWithKeyboard(Window window, Control target,
        RawInputModifiers modifiers = RawInputModifiers.None)
    {
        var maximumSteps = window.GetVisualDescendants().OfType<Control>().Count(control => control.Focusable && control.IsTabStop);
        for (var index = 0; index < maximumSteps && !target.IsFocused; index++)
        { PressKey(window, Key.Tab, PhysicalKey.Tab, modifiers); }
        target.IsFocused.Should().BeTrue("the complete private reader and separate consent must be reachable through the native Tab sequence");
    }

    private static SessionFileAttachment Source(string text = "alpha immutable retained marker")
    {
        var request = HostRequest.Create(RequestOrigin.LocalUi);
        var bytes = Encoding.UTF8.GetBytes(text);
        return new(request.SessionId, new(1), new(1),
            new(new(Guid.NewGuid(), Guid.NewGuid(), request,
                new(@"C:\Synthetic\retained.md", "identity", bytes.Length, DateTimeOffset.UnixEpoch)), bytes, DateTimeOffset.UnixEpoch));
    }
}
