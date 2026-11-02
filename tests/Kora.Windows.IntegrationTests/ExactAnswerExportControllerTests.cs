using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using AwesomeAssertions;
using Kora.Application.Presentation;
using Kora.Core.Auditing;
using Kora.Core.Diagnostics;
using Kora.Core.Presentation;
using Kora.Windows.IntegrationTests.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class ExactAnswerExportControllerTests
{
    [Theory]
    [InlineData("picker-cancel")]
    [InlineData("picker-private")]
    [InlineData("picker-source")]
    [InlineData("review-cancel")]
    [InlineData("review-source")]
    [InlineData("review-confirm")]
    public async Task SelectionAndReviewNeverBorrowWriteAuthority(string scenario)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var storage = new InteractionStorageFixture();
            var source = ExactAnswerExportWriterTests.Source();
            using var host = HostActivity.BeginRoot(source.ConversationMessage!.Request, HostActivityLayer.Desktop, HostOperation.Request);
            var writer = new Writer();
            var live = true;
            var current = source;
            var audit = new Audit();
            var controller = new ExactAnswerExportWindowController(writer, audit, NullLogger<ExactAnswerExportReview>.Instance, (_, _) =>
            {
                if (scenario is "picker-private") { live = false; }
                if (scenario is "picker-source") { current = ExactAnswerExportWriterTests.Source(); }
                return Task.FromResult<string?>(scenario is "picker-cancel" ? null : writer.Destination.Path);
            });
            var owner = new Window();
            owner.Show();
            try
            {
                var operation = controller.ShowAsync(owner, source, () => live, _ => Task.FromResult(current), storage.Token);
                if (scenario is "picker-private" or "picker-source")
                {
                    var rejected = () => operation;
                    await rejected.Should().ThrowAsync<InvalidOperationException>();
                    writer.Destination.Calls.Should().Be(0);
                    owner.OwnedWindows.Should().BeEmpty();
                    return;
                }
                if (scenario is not "picker-cancel")
                {
                    var review = owner.OwnedWindows.OfType<ExactAnswerExportWindow>().Single();
                    review.GetVisualDescendants().OfType<TextBox>().Single().Text.Should().Be(source.Source);
                    writer.Destination.Calls.Should().Be(0);
                    audit.Events.Should().BeEmpty();
                    if (scenario is "review-cancel") { review.Close(); }
                    else
                    {
                        if (scenario is "review-source") { current = ExactAnswerExportWriterTests.Source(); }
                        review.GetVisualDescendants().OfType<Button>().Single(button =>
                            string.Equals(button.Name, "ConfirmExactAnswerExport", StringComparison.Ordinal))
                            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        await review.Operation;
                        review.Outcome.Should().Be(scenario is "review-confirm"
                            ? ExactAnswerExportOutcome.Saved : ExactAnswerExportOutcome.NotWritten);
                        review.Close();
                    }
                }
                var result = await operation;
                result.Should().Contain(scenario is "review-confirm" ? "verified" : scenario is "review-source" ? "failed" : "cancelled");
                writer.Destination.Calls.Should().Be(scenario is "review-confirm" ? 1 : 0);
                writer.Destination.Disposals.Should().Be(scenario is "picker-cancel" ? 0 : 1);
            }
            finally { owner.Close(); }
        });
    }

    [Fact]
    public async Task ClosingAStagingReviewRetiresDisclosureAndJoinsTheWriterBeforeReleasingItsDestination()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var storage = new InteractionStorageFixture();
            var source = ExactAnswerExportWriterTests.Source();
            using var host = HostActivity.BeginRoot(source.ConversationMessage!.Request, HostActivityLayer.Desktop, HostOperation.Request);
            var writer = new Writer();
            writer.Destination.Hold = true;
            var controller = new ExactAnswerExportWindowController(writer, new Audit(), NullLogger<ExactAnswerExportReview>.Instance,
                (_, _) => Task.FromResult<string?>(writer.Destination.Path));
            var owner = new Window();
            owner.Show();
            try
            {
                var operation = controller.ShowAsync(owner, source, () => true, _ => Task.FromResult(source), storage.Token);
                var review = owner.OwnedWindows.OfType<ExactAnswerExportWindow>().Single();
                var text = review.GetVisualDescendants().OfType<TextBox>().Single();
                review.GetVisualDescendants().OfType<Button>().Single(button =>
                    string.Equals(button.Name, "ConfirmExactAnswerExport", StringComparison.Ordinal))
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await writer.Destination.Started.Task;
                review.Close();
                text.Text.Should().BeEmpty();
                operation.IsCompleted.Should().BeFalse("native close is not writer quiescence");
                writer.Destination.Disposals.Should().Be(0);
                writer.Destination.Release.SetResult();
                (await operation).Should().Contain("cancelled");
                string.Join("\n", review.GetVisualDescendants().OfType<TextBlock>().Select(label => label.Text))
                    .Should().NotContain(writer.Destination.Path).And.NotContain(source.Reference.ItemId.Value.ToString("D"));
                writer.Destination.Disposals.Should().Be(1);
                writer.Destination.Calls.Should().Be(1);
            }
            finally { writer.Destination.Release.TrySetResult(); owner.Close(); }
        });
    }

    private sealed class Writer : IExactAnswerExportWriter
    {
        internal Destination Destination { get; } = new();
        public Task<IExactAnswerExportDestination> SelectAsync(string path, CancellationToken token) =>
            Task.FromResult<IExactAnswerExportDestination>(Destination);
    }

    private sealed class Destination : IExactAnswerExportDestination
    {
        public string Path => System.IO.Path.Combine(OwnedStorageFixture.ScratchParent(), ".scratch", "orchestration",
            "exact-answer-export", "synthetic-not-written.md");
        internal bool Hold { get; set; }
        internal int Calls { get; private set; }
        internal int Disposals { get; private set; }
        internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<ExactAnswerExportOutcome> PublishAsync(AdmittedDetailContent source,
            Func<CancellationToken, Task> revalidate, Func<bool> admitted, CancellationToken token)
        {
            Calls++;
            await revalidate(token);
            Started.SetResult();
            if (Hold)
            {
                await Release.Task;
                token.IsCancellationRequested.Should().BeTrue();
                return ExactAnswerExportOutcome.Cancelled;
            }
            return ExactAnswerExportOutcome.Saved;
        }
        public ValueTask DisposeAsync() { Disposals++; return ValueTask.CompletedTask; }
    }

    private sealed class Audit : ISecurityAuditLog
    {
        internal List<SecurityAuditEvent> Events { get; } = [];
        public void Write(SecurityAuditEvent auditEvent) => Events.Add(auditEvent);
    }
}
