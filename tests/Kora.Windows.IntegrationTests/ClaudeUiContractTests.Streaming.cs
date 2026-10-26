using System.Text;
using Avalonia.Controls;
using Avalonia.Interactivity;
using AwesomeAssertions;
using Kora.Application.Diagnostics;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Windows.IntegrationTests.Dependencies;
using Kora.Windows.IntegrationTests.Storage;
using Kora.Windows.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

public sealed partial class ClaudeUiContractTests
{
    [Theory]
    [InlineData("complete")]
    [InlineData("cancel")]
    [InlineData("privacy")]
    [InlineData("unqualified")]
    public async Task NativeClaudeStreamButtonUsesCanonicalProvisionalUiAndFinalOnlyRetention(string terminal)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            await using var c = new HostedClaudeSessionRequestTests.Composition(f, streamAnswer: terminal is not "unqualified");
            await c.InitializeAsync();
            var first = ClaudeMessagesRuntimeTests.StreamStart + ClaudeMessagesRuntimeTests.StreamText("safe preview");
            var stream = new ClaudeMessagesRuntimeTests.ProgressiveStream(
                Encoding.UTF8.GetBytes(first + ClaudeMessagesRuntimeTests.StreamEnd), 1, Encoding.UTF8.GetByteCount(first));
            c.Http.Respond = request => request.RequestUri!.AbsolutePath.EndsWith("count_tokens", StringComparison.Ordinal)
                ? ClaudeAccountBrokerTests.Fixture.Json("{\"input_tokens\":25}") : ClaudeMessagesRuntimeTests.StreamingResponse(stream);
            c.Reviews.ClaudeOffered += review =>
            {
                using var presentation = review.Presentation();
                c.Reviews.Confirm(review, true, RequestOrigin.LocalUi).Should().BeTrue();
                presentation.Complete(HostOperationOutcome.Completed);
            };
            var sink = new WindowsSqliteEvidenceSink(f.Paths);
            sink.Initialize();
            var model = new SessionsViewModel(c.Workspace,
                new DurableEvidenceQuery(new WindowsSqliteEvidenceReader(sink), c.Access, f.Time,
                    NullLogger<DurableEvidenceQuery>.Instance), c.Access, NullLogger<SessionsViewModel>.Instance,
                conversation: c.Conversation);
            var owner = new SessionsPresentationOwner(model);
            var window = new SessionsWindow(model, owner);
            window.Show();
            Task operation = Task.CompletedTask;
            try
            {
                await model.RefreshAsync();
                await model.SelectAsync(model.Sessions.Single());
                await model.ReadConversationAsync();
                model.ComposerBody = "original native stream";
                window.FindControl<Button>("SubmitClaudeAnswerStream")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                operation = window.ConversationOperation;
                if (terminal is "unqualified")
                {
                    await operation;
                    model.ConversationStatus.Should().Contain("Unavailable");
                    c.Http.Requests.Should().OnlyContain(item => string.Equals(item.Method, "GET", StringComparison.Ordinal));
                    return;
                }
                var ready = await Task.WhenAny(stream.Paused.Task, operation).WaitAsync(f.Token);
                ready.Should().BeSameAs(stream.Paused.Task, model.ConversationStatus);
                model.ProvisionalAnswer.Should().Be("safe preview");
                model.HasProvisionalAnswer.Should().BeTrue();
                operation.IsCompleted.Should().BeFalse();
                if (terminal is "cancel") { await model.CancelRunningConversationAsync(); }
                if (terminal is "privacy")
                {
                    c.Access.CanControl = false;
                    c.Access.CanInspect = false;
                    owner.Revoke();
                }
                if (terminal is not "complete")
                {
                    model.ProvisionalAnswer.Should().BeEmpty();
                    operation.IsCompleted.Should().BeFalse("the physical reader deliberately ignores cancellation");
                }
                stream.Release.TrySetResult();
                await operation;
                model.ProvisionalAnswer.Should().BeEmpty();
                var stored = (await f.Store.ReadConversationPageAsync(f.Request.SessionId, null, 25, f.Token)).Messages.Single();
                stored.Body.Should().Be("original native stream");
                stored.AssistantResponse?.Body.Should().Be(terminal is "complete" ? "safe preview" : null);
            }
            finally
            {
                stream.Release.TrySetResult();
                await operation;
                owner.Revoke();
                window.Close();
            }
        });
    }
}
