using System.Text.Json;

using Avalonia.Controls;
using Avalonia.Interactivity;

using AwesomeAssertions;

using Kora.Application.Dependencies;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Providers;
using Kora.Core.Tools;
using Kora.Windows.IntegrationTests.Storage;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
    [Fact]
    public async Task NativeHistorySelectionReviewAndConfirmationRetainRealAnswerThroughExactPrivateStore()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            var adapter = new HistoryAdapter();
            await using var composed = new Composition(f, adapter: adapter);
            var model = composed.Model;
            var parent = new SessionsWindow(model);
            parent.Show();
            try
            {
                await model.RefreshAsync();
                await model.SelectAsync(model.Sessions.Single());
                model.ComposerBody = "earlier original";
                await model.SubmitConversationAsync();
                await model.ReadConversationAsync();
                var original = model.ConversationMessages.Single();
                model.ComposerBody = "What follows from those messages?";
                parent.FindControl<Button>("ReviewHistoryContext")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var window = parent.OwnedWindows.OfType<ConversationHistoryWindow>().Single();
                try
                {
                    var sources = window.FindControl<ListBox>("Sources")!;
                    sources.SelectedItems!.Add(model.HistoryContextCandidates.Single(candidate => candidate.Selection.Role == ConversationHistoryRole.User));
                    sources.SelectedItems.Add(model.HistoryContextCandidates.Single(candidate => candidate.Selection.Role == ConversationHistoryRole.Assistant));
                    window.FindControl<Button>("Review")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await window.Operation;
                    model.HistoryContextReviewText.Should().Contain(original.Body).And.Contain(original.AssistantResponse!.Body)
                        .And.Contain("What follows from those messages?");
                    adapter.Calls.Should().Be(1, "review alone cannot dispatch");
                    window.FindControl<Button>("Confirm")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await window.Operation;
                    adapter.Calls.Should().Be(2);
                    var completed = model.ConversationMessages.Single();
                    completed.Body.Should().Be("What follows from those messages?");
                    completed.AssistantResponse!.Body.Should().Be("genuine retained follow-up");
                    completed.Request.SessionId.Should().Be(original.Request.SessionId);
                    model.ComposerBody.Should().BeEmpty();
                    model.HistoryContextReviewText.Should().BeEmpty();
                    var stored = await f.Store.ReadConversationMessageAsync(f.Request.SessionId, completed.Request.RequestId, f.Token);
                    stored.Should().Be(completed);
                    using var wire = JsonDocument.Parse(adapter.Wire!);
                    wire.RootElement.GetProperty("ToolMode").GetInt32().Should().Be((int)ModelToolMode.None);
                    wire.RootElement.GetProperty("Tools").GetArrayLength().Should().Be(0);
                    wire.RootElement.GetProperty("Evidence").GetArrayLength().Should().Be(2);
                    f.Count("conversation_messages").Should().Be(2);
                    f.Count("host_questions").Should().Be(0);
                    f.Count("scoped_grants").Should().Be(0);
                }
                finally { window.Close(); }
            }
            finally { parent.ClosePrivate(); }
        });
    }

    [Theory]
    [InlineData("edit")]
    [InlineData("selection")]
    [InlineData("close")]
    [InlineData("privacy")]
    [InlineData("expiry")]
    [InlineData("control")]
    [InlineData("session")]
    public async Task NativeHistoryReviewRetiresOnOriginalInputSourcePrivacyCloseOrExpiryWithoutSubmission(string change)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            await using var composed = new Composition(f, adapter: new HistoryAdapter());
            var model = composed.Model;
            await model.RefreshAsync();
            await model.SelectAsync(model.Sessions.Single());
            model.ComposerBody = "earlier";
            await model.SubmitConversationAsync();
            await model.ReadConversationAsync();
            model.OpenHistoryContext();
            model.SelectHistoryContext([model.HistoryContextCandidates[0]]);
            model.ComposerBody = "new request";
            await model.ReviewHistoryContextAsync();
            model.CanConfirmHistoryContext.Should().BeTrue();
            if (change is "edit") { model.ComposerBody = "changed request"; }
            if (change is "selection") { model.SelectHistoryContext([]); }
            if (change is "close") { model.ClearHistoryContext(); }
            if (change is "privacy") { model.Close(); }
            if (change is "expiry") { f.Time.Now = f.Time.Now.AddSeconds(60); model.RefreshHistoryContextReview(); }
            if (change is "control") { composed.Access.ControlRevision++; model.RefreshHistoryContextReview(); }
            if (change is "session") { await model.SelectAsync(null); }
            model.CanConfirmHistoryContext.Should().BeFalse();
            model.HistoryContextReviewText.Should().BeEmpty();
            if (change is "close" or "privacy" or "expiry" or "control" or "session")
            { model.HistoryContextCandidates.Should().BeEmpty(); }
            await model.ConfirmHistoryContextAsync();
            f.Count("conversation_messages").Should().Be(1);
            model.Close();
        });
    }

    private sealed class HistoryAdapter : IModelTurnAdapter
    {
        internal int Calls { get; private set; }
        internal byte[]? Wire { get; private set; }
        internal Task<ModelProviderReply>? Pending { get; init; }
        internal TaskCompletionSource? Entered { get; init; }
        public Task<ModelProviderReply> RunAsync(ModelTurnProvenance provenance, ProviderProfileTuple? profile,
            ReadOnlyMemory<byte> context, Func<string, string, CancellationToken, ValueTask<CapabilityReply>> invokeTool,
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Calls++;
            Wire = context.ToArray();
            Entered?.TrySetResult();
            return Pending ?? Task.FromResult(new ModelProviderReply(ModelTurnOutcome.Succeeded, provenance.Selection,
                new(Calls == 1 ? "earlier ASSISTANT" : "genuine retained follow-up", null)));
        }
    }
}
