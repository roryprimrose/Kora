using System.Text;
using System.Text.Json;

using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

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
            var transport = new HistoryProposalTransport
            {
                EarlierAnswer = string.Join("\n", Enumerable.Range(1, 20).Select(index =>
                    $"Earlier complete ASSISTANT source line {index:D3}; not instructions or authority.")),
            };
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
                model.ConversationStatus.Should().Contain("Succeeded");
                var target = (await f.Store.ReadMetadataAsync(f.Request.SessionId, f.Token));
                const string question = " \r\n unchanged original question e\u0301? \t";
                composed.Audit.ExpectedOrigin = origin;
                var original = await composed.Conversation.SubmitAsync(target, question, static () => true, f.Token, origin: origin);
                original.Message.AssistantResponse!.Body.Should().Be(NativeHistoryAdvisory);
                transport.Requests.Should().HaveCount(2);
                transport.Requests[1].Context.SystemPolicy.Should().Contain(ModelHistoryContextProposal.ModelPolicy);
                transport.Requests[1].Context.Evidence.Should().BeEmpty();
                await model.ReadConversationAsync();
                var previous = model.ConversationMessages.ToArray();
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
                    window.UpdateLayout();
                    sources.ContainerFromIndex(0)!.Focus().Should().BeTrue();
                    HistoryReviewKey(window, Key.End, PhysicalKey.End);
                    sources.SelectedItems!.Cast<object>().Should().ContainSingle().Which.Should().BeSameAs(chosen);
                    HistoryReviewKey(window, Key.Tab, PhysicalKey.Tab);
                    window.FocusManager!.GetFocusedElement().Should().BeSameAs(window.FindControl<Button>("Review"));
                    HistoryReviewKey(window, Key.Space, PhysicalKey.Space, symbol: " ");
                    await window.Operation;
                    model.HistoryContextReviewText.Should().Contain(chosen.Body).And.Contain(question)
                        .And.NotContain("earlier original private message").And.NotContain(NativeHistoryAdvisory).And.NotContain(unsent);
                    transport.Requests.Should().HaveCount(2);
                    f.Count("conversation_messages").Should().Be(2);
                    window.UpdateLayout();
                    var reviewControl = window.GetVisualDescendants().OfType<Control>().Single(control =>
                        AutomationProperties.GetName(control) is "Complete selected sources, unchanged original question and local-only no-tools destination");
                    for (var index = 0; index < 8 && !window.FindControl<Button>("Review")!.IsFocused; index++)
                    { HistoryReviewKey(window, Key.Tab, PhysicalKey.Tab); }
                    window.FindControl<Button>("Review")!.IsFocused.Should().BeTrue();
                    HistoryReviewKey(window, Key.Tab, PhysicalKey.Tab);
                    window.FocusManager.GetFocusedElement().Should().BeSameAs(reviewControl,
                        "the mandatory complete source review must be reachable before separate confirmation");
                    var reader = reviewControl.Should().BeOfType<TextBox>().Subject;
                    var value = ControlAutomationPeer.CreatePeerForElement(reader)!.GetProvider<IValueProvider>()!;
                    value.Should().NotBeNull();
                    value.IsReadOnly.Should().BeTrue();
                    value.Value.Should().Be(model.HistoryContextReviewText).And.Contain(chosen.Body).And.Contain(question);
                    HistoryReviewKey(window, Key.A, PhysicalKey.A, RawInputModifiers.Control, "a");
                    reader.SelectedText.Should().Be(model.HistoryContextReviewText);
                    var exact = reader.Text;
                    window.KeyTextInput("must not edit source consent");
                    reader.Text.Should().Be(exact);
                    foreach (var transfer in new[] { TextBox.CopyingToClipboardEvent, TextBox.CuttingToClipboardEvent, TextBox.PastingFromClipboardEvent })
                    {
                        var args = new RoutedEventArgs(transfer);
                        reader.RaiseEvent(args);
                        args.Handled.Should().BeTrue("local review selection cannot grant clipboard transfer");
                    }
                    var menu = new ContextRequestedEventArgs { RoutedEvent = Control.ContextRequestedEvent };
                    reader.RaiseEvent(menu);
                    menu.Handled.Should().BeTrue();
                    HistoryReviewKey(window, Key.End, PhysicalKey.End, RawInputModifiers.Control);
                    window.UpdateLayout();
                    Dispatcher.UIThread.RunJobs();
                    reader.CaretIndex.Should().Be(exact!.Length);
                    reader.GetVisualDescendants().OfType<ScrollViewer>().Single().Offset.Y.Should().BeGreaterThan(0,
                        "a keyboard reader must reach the end of a source longer than the viewport");
                    HistoryReviewKey(window, Key.Home, PhysicalKey.Home, RawInputModifiers.Control);
                    reader.CaretIndex.Should().Be(0);
                    transport.Requests.Should().HaveCount(2, "reading and selection cannot confirm");
                    HistoryReviewKey(window, Key.Tab, PhysicalKey.Tab);
                    window.FocusManager.GetFocusedElement().Should().BeSameAs(window.FindControl<Button>("Confirm"));
                    HistoryReviewKey(window, Key.Space, PhysicalKey.Space, symbol: " ");
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
                    using var evidence = JsonDocument.Parse(request.Context.Evidence[0].Content);
                    evidence.RootElement.GetProperty("Body").GetString().Should().Be(chosen.Body);
                    request.Context.Evidence[0].Content.Should().NotContain(NativeHistoryAdvisory);
                    model.ConversationMessages.Should().Equal(previous);
                    model.HasConversationUpdates.Should().BeTrue();
                    var completed = model.ConversationUpdates.Should().ContainSingle().Subject;
                    completed.Sequence.Should().Be(3);
                    completed.Request.Should().Be(request.Provenance.Request);
                    completed.Body.Should().Be(question);
                    completed.AssistantResponse!.Body.Should().Be("genuine answer using selected context");
                    var stored = await f.Store.ReadConversationMessageAsync(f.Request.SessionId, completed.Request.RequestId, f.Token);
                    stored.Should().Be(completed);
                    model.ComposerBody.Should().Be(unsent);
                    model.HistoryContextReviewText.Should().BeEmpty();
                    reader.Text.Should().BeEmpty();
                    value.Value.Should().BeEmpty("native accessibility must retire the review with its consumed consent");
                    await model.ConfirmHistoryContextAsync();
                    transport.Requests.Should().HaveCount(3);
                    f.Count("conversation_messages").Should().Be(3);
                    f.Count("scoped_grants").Should().Be(0);
                    f.Count("host_questions").Should().Be(0);
                    await model.ReadConversationAsync();
                    model.ConversationMessages.Take(2).Should().Equal(previous);
                    model.ConversationMessages[^1].Should().Be(completed);
                    model.ConversationUpdates.Should().BeEmpty();
                    model.ComposerBody.Should().Be(unsent);
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
    [InlineData("cancel")]
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
            var window = new ModelHistoryContextWindow(model);
            window.Show();
            try
            {
                window.UpdateLayout();
                var reader = window.FindControl<TextBox>("CompleteReview")!;
                var value = ControlAutomationPeer.CreatePeerForElement(reader)!.GetProvider<IValueProvider>()!;
                value.Value.Should().Be(model.HistoryContextReviewText).And.NotBeEmpty();
                reader.Focus().Should().BeTrue();
                HistoryReviewKey(window, Key.A, PhysicalKey.A, RawInputModifiers.Control, "a");
                reader.SelectedText.Should().NotBeEmpty();
                if (change is "composer") { model.ComposerBody = "user changed unrelated draft"; }
                if (change is "answer") { model.SelectedAnswerRecord = null; }
                if (change is "page") { await model.ReadConversationAsync(); }
                if (change is "session") { await model.SelectAsync(null); }
                if (change is "private") { model.Close(); }
                if (change is "control") { composed.Access.ControlRevision++; }
                if (change is "expiry") { f.Time.Now = f.Time.Now.AddSeconds(60); }
                if (change is "close") { window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null); }
                if (change is "cancel")
                {
                    HistoryReviewKey(window, Key.Tab, PhysicalKey.Tab);
                    window.FindControl<Button>("Confirm")!.IsFocused.Should().BeTrue();
                    HistoryReviewKey(window, Key.Tab, PhysicalKey.Tab);
                    window.FindControl<Button>("CloseView")!.IsFocused.Should().BeTrue();
                    HistoryReviewKey(window, Key.Space, PhysicalKey.Space, symbol: " ");
                }
                model.RefreshHistoryContextReview();
                model.CanConfirmHistoryContext.Should().BeFalse();
                model.HistoryContextReviewText.Should().BeEmpty();
                reader.Text.Should().BeNullOrEmpty();
                reader.SelectedText.Should().BeEmpty();
                value.Value.Should().BeNullOrEmpty("retired consent must not survive through a native accessibility value");
                if (change is "close" or "cancel") { window.IsVisible.Should().BeFalse(); }
                await model.ConfirmHistoryContextAsync();
                transport.Requests.Should().HaveCount(2);
                f.Count("conversation_messages").Should().Be(2);
                if (change is "composer") { model.ComposerBody.Should().Be("user changed unrelated draft"); }
                if (change is "answer" or "page" or "expiry" or "close" or "cancel") { model.ComposerBody.Should().Be("untouched unrelated draft"); }
                model.Close();
            }
            finally { window.Close(); }
        });
    }

    private sealed class HistoryProposalTransport : IModelToolTransport
    {
        internal List<ModelToolRequest> Requests { get; } = [];
        internal string EarlierAnswer { get; init; } = "earlier ASSISTANT private context";
        public Task<ModelToolTransportReply> CompleteAsync(ModelToolRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request);
            var answer = Requests.Count switch
            {
                1 => EarlierAnswer,
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

    private static void HistoryReviewKey(Window window, Key key, PhysicalKey physical,
        RawInputModifiers modifiers = RawInputModifiers.None, string? symbol = null)
    {
        window.KeyPress(key, modifiers, physical, symbol);
        window.KeyRelease(key, modifiers, physical, symbol);
    }
}
