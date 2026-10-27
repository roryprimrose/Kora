using System.Text;
using System.Text.Json;

using Avalonia.Controls;
using Avalonia.Interactivity;

using AwesomeAssertions;

using Kora.Application.Dependencies;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Windows.IntegrationTests.Storage;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
    private const string NativeHistoryAdvisory = """{"version":1,"advisory":"HISTORYREVIEW"}""";

    [Theory]
    [InlineData(RequestOrigin.LocalUi)]
    [InlineData(RequestOrigin.ActivatedVoice)]
    public async Task NativeModelHistoryAdvisoryCompletesOriginalQuestionThroughRealSharedFlowWithoutTouchingUnsentText(RequestOrigin origin)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            var transport = new HistoryProposalTransport();
            await using var adapter = new BoundedModelTurnAdapter(transport, f.Time, NullLogger<BoundedModelTurnAdapter>.Instance);
            await using var composed = new Composition(f, adapter: adapter);
            var model = composed.Model;
            var parent = new SessionsWindow(model);
            parent.Show();
            try
            {
                await model.RefreshAsync();
                await model.SelectAsync(model.Sessions.Single());
                model.ComposerBody = "earlier original private message";
                await model.SubmitConversationAsync();
                var target = (await f.Store.ReadMetadataAsync(f.Request.SessionId, f.Token));
                const string question = " \r\n unchanged original question e\u0301? \t";
                composed.Audit.ExpectedOrigin = origin;
                var original = await composed.Conversation.SubmitAsync(target, question, static () => true, f.Token, origin: origin);
                original.Message.AssistantResponse!.Body.Should().Be(NativeHistoryAdvisory);
                transport.Requests.Should().HaveCount(2);
                transport.Requests[1].Context.SystemPolicy.Should().Contain(ModelHistoryContextProposal.ModelPolicy);
                transport.Requests[1].Context.Evidence.Should().BeEmpty();
                await model.ReadConversationAsync();
                model.ComposerBody = " \r\n unrelated unsent composer e\u0301 \t ";
                var unsent = model.ComposerBody;
                var source = model.ConversationMessages.Single(message => message.Request == original.Message.Request);
                parent.FindControl<ListBox>("AnswerSourceRecords")!.SelectedItem = source;
                model.CanOpenModelHistoryContext.Should().BeTrue(model.ConversationStatus);
                parent.FindControl<Button>("ReviewModelHistoryContext")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await parent.ModelHistoryContextOperation;
                model.HasModelHistoryContext.Should().BeTrue(model.ConversationStatus);
                var window = parent.OwnedWindows.OfType<ModelHistoryContextWindow>().Single();
                try
                {
                    var sources = window.FindControl<ListBox>("Sources")!;
                    sources.SelectedItems!.Count.Should().Be(0);
                    model.HistoryContextCandidates.Should().HaveCount(2);
                    model.HistoryContextCandidates.Should().OnlyContain(candidate => candidate.Sequence < source.Sequence);
                    model.ModelHistoryContextQuestion.Should().Contain(question).And.Contain(origin.ToString());
                    transport.Requests.Should().HaveCount(2, "opening is not completion or model consent");
                    f.Count("conversation_messages").Should().Be(2);
                    model.ComposerBody.Should().Be(unsent);
                    var chosen = model.HistoryContextCandidates.Single(candidate => candidate.Selection.Role == ConversationHistoryRole.Assistant);
                    sources.SelectedItems!.Add(chosen);
                    window.FindControl<Button>("Review")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await window.Operation;
                    model.HistoryContextReviewText.Should().Contain(chosen.Body).And.Contain(question)
                        .And.NotContain("earlier original private message").And.NotContain(NativeHistoryAdvisory).And.NotContain(unsent);
                    transport.Requests.Should().HaveCount(2);
                    f.Count("conversation_messages").Should().Be(2);
                    window.FindControl<Button>("Confirm")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await window.Operation;
                    transport.Requests.Should().HaveCount(3);
                    var request = transport.Requests[2];
                    request.Context.UserRequest.Should().Be(question);
                    request.Provenance.Request.Origin.Should().Be(origin);
                    request.Provenance.Request.SessionId.Should().Be(source.Request.SessionId);
                    request.Context.ToolMode.Should().Be(ModelToolMode.None);
                    request.Context.Tools.Should().BeEmpty();
                    request.Context.Evidence.Should().ContainSingle();
                    request.Context.Evidence[0].Disclosure.Should().Be(ModelEvidenceDisclosure.LocalOnly);
                    request.Context.Evidence[0].Content.Should().Contain(chosen.Body).And.NotContain(NativeHistoryAdvisory);
                    var completed = model.ConversationMessages.Single();
                    completed.Body.Should().Be(question);
                    completed.AssistantResponse!.Body.Should().Be("genuine answer using selected context");
                    var stored = await f.Store.ReadConversationMessageAsync(f.Request.SessionId, completed.Request.RequestId, f.Token);
                    stored.Should().Be(completed);
                    model.ComposerBody.Should().Be(unsent);
                    model.HistoryContextReviewText.Should().BeEmpty();
                    await model.ConfirmHistoryContextAsync();
                    transport.Requests.Should().HaveCount(3);
                    f.Count("conversation_messages").Should().Be(3);
                    f.Count("scoped_grants").Should().Be(0);
                    f.Count("host_questions").Should().Be(0);
                }
                finally { window.Close(); }
            }
            finally { parent.ClosePrivate(); }
        });
    }

    [Theory]
    [InlineData("composer")]
    [InlineData("answer")]
    [InlineData("page")]
    [InlineData("session")]
    [InlineData("private")]
    [InlineData("control")]
    [InlineData("expiry")]
    [InlineData("close")]
    public async Task NativeModelHistoryPinsRetireWithoutDispatchOrComposerReplacement(string change)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            var transport = new HistoryProposalTransport();
            await using var adapter = new BoundedModelTurnAdapter(transport, f.Time, NullLogger<BoundedModelTurnAdapter>.Instance);
            await using var composed = new Composition(f, adapter: adapter);
            var model = composed.Model;
            await model.RefreshAsync();
            await model.SelectAsync(model.Sessions.Single());
            model.ComposerBody = "earlier";
            await model.SubmitConversationAsync();
            model.ComposerBody = "original question";
            await model.SubmitConversationAsync();
            await model.ReadConversationAsync();
            model.ComposerBody = "untouched unrelated draft";
            model.SelectedAnswerRecord = model.ConversationMessages.Single(message => message.AssistantResponse!.Body is NativeHistoryAdvisory);
            await model.OpenModelHistoryContextAsync();
            model.SelectHistoryContext([model.HistoryContextCandidates[0]]);
            await model.ReviewHistoryContextAsync();
            model.CanConfirmHistoryContext.Should().BeTrue();
            if (change is "composer") { model.ComposerBody = "user changed unrelated draft"; }
            if (change is "answer") { model.SelectedAnswerRecord = null; }
            if (change is "page") { await model.ReadConversationAsync(); }
            if (change is "session") { await model.SelectAsync(null); }
            if (change is "private") { model.Close(); }
            if (change is "control") { composed.Access.ControlRevision++; }
            if (change is "expiry") { f.Time.Now = f.Time.Now.AddSeconds(60); }
            if (change is "close") { model.ClearHistoryContext(); }
            model.RefreshHistoryContextReview();
            model.CanConfirmHistoryContext.Should().BeFalse();
            model.HistoryContextReviewText.Should().BeEmpty();
            await model.ConfirmHistoryContextAsync();
            transport.Requests.Should().HaveCount(2);
            f.Count("conversation_messages").Should().Be(2);
            if (change is "composer") { model.ComposerBody.Should().Be("user changed unrelated draft"); }
            if (change is "answer" or "page" or "expiry" or "close") { model.ComposerBody.Should().Be("untouched unrelated draft"); }
            model.Close();
        });
    }

    private sealed class HistoryProposalTransport : IModelToolTransport
    {
        internal List<ModelToolRequest> Requests { get; } = [];
        public Task<ModelToolTransportReply> CompleteAsync(ModelToolRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request);
            var answer = Requests.Count switch
            {
                1 => "earlier ASSISTANT private context",
                2 => NativeHistoryAdvisory,
                3 => "genuine answer using selected context",
                _ => throw new InvalidOperationException("Unexpected duplicate model dispatch."),
            };
            var wire = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { kind = "answer", answer }));
            var message = ModelToolProtocol.ParseAssistant(wire, request.ClarificationAllowed);
            return Task.FromResult(new ModelToolTransportReply(ModelTurnOutcome.Succeeded, request.Provenance, message,
                new(ModelToolProtocol.SerializeRequest(request).Length, Math.Max(wire.Length, ModelToolProtocol.SerializeMessage(message).Length))));
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
