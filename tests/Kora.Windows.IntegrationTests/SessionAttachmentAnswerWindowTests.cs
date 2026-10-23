using System.Text;
using Avalonia.Controls;
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

    private static Button FindButton(LocalFilePreviewWindow window, string prefix) =>
        window.GetVisualDescendants().OfType<Button>().Single(button =>
            (button.Content as string)?.StartsWith(prefix, StringComparison.Ordinal) == true);

    private static string Text(LocalFilePreviewWindow window) =>
        string.Join("\n", window.GetVisualDescendants().OfType<TextBlock>().Select(block => block.Text));

    private static SessionFileAttachment Source()
    {
        var request = HostRequest.Create(RequestOrigin.LocalUi);
        var bytes = Encoding.UTF8.GetBytes("alpha immutable retained marker");
        return new(request.SessionId, new(1), new(1),
            new(new(Guid.NewGuid(), Guid.NewGuid(), request,
                new(@"C:\Synthetic\retained.md", "identity", bytes.Length, DateTimeOffset.UnixEpoch)), bytes, DateTimeOffset.UnixEpoch));
    }
}
