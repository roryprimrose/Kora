using AwesomeAssertions;

using Kora.Core.Conversation;
using Kora.Windows.IntegrationTests.Storage;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ConversationReadKeepsConcurrentCompletionUntilExactReturnedPageIncludesIt(bool latest, bool includesCompletion)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            await SeedReadingMessages(f, f.Request.SessionId, 1);
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var hold = false;
            var reader = new SearchReader(async (session, cursor, limit, token) =>
            {
                var captured = await f.Store.ReadConversationPageAsync(session, cursor, limit, token);
                if (!hold) { return captured; }
                entered.TrySetResult();
                await release.Task.WaitAsync(token);
                return includesCompletion
                    ? await f.Store.ReadConversationPageAsync(session, cursor, limit, token)
                    : captured;
            }, readMetadata: f.Store.ReadMetadataAsync, readMessage: f.Store.ReadConversationMessageAsync);
            var adapter = new MemoryAnswerAdapter();
            var opened = new List<Kora.Core.Presentation.AdmittedDetailContent>();
            await using var composition = new Composition(f, (content, live, _, _) =>
            {
                live().Should().BeTrue();
                opened.Add(content);
                return Task.FromResult("Exact concurrent answer opened");
            }, adapter: adapter, conversationReader: reader);
            var model = composition.Model;
            await model.RefreshAsync();
            await model.SelectAsync(model.Sessions.Single());
            await model.ReadConversationAsync();
            var original = model.ConversationMessages.Single();
            model.ComposerBody = "independent unsent input";
            var published = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void Published(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
            {
                if (string.Equals(args.PropertyName, nameof(SessionsViewModel.ConversationUpdates), StringComparison.Ordinal)
                    && model.ConversationUpdates.Count == 1)
                { published.TrySetResult(); }
            }
            model.PropertyChanged += Published;
            hold = true;
            var pending = latest ? model.ReadLatestConversationAsync() : model.ReadConversationAsync();
            try
            {
                await entered.Task.WaitAsync(f.Token);
                var result = await composition.Conversation.SubmitAsync(model.SelectedSessionRecord!,
                    "Independent original finishes during an awaited history read.", static () => true, f.Token);
                await published.Task.WaitAsync(f.Token);
                model.ConversationUpdates.Should().ContainSingle().Which.Should().Be(result.Message);
                pending.IsCompleted.Should().BeFalse();
                var audit = WindowsSqliteConversationTests.AuditText(f);
                var retention = await f.Store.ReadRetentionAsync(f.Request.SessionId, f.Token);
                release.SetResult();
                await pending;
                if (includesCompletion)
                {
                    model.ConversationMessages.Should().Equal(original, result.Message);
                    model.ConversationUpdates.Should().BeEmpty("the exact completion is already in the returned page");
                    model.HasConversationUpdates.Should().BeFalse();
                }
                else
                {
                    model.ConversationMessages.Should().Equal([original], "a read cannot silently expand its snapshot");
                    model.ConversationUpdates.Should().ContainSingle().Which.Should().BeSameAs(result.Message);
                    model.HasConversationUpdates.Should().BeTrue("the newer answer still requires deliberate latest-page admission");
                    await model.OpenConversationDetailAsync(model.ConversationUpdates.Single(), assistant: true);
                    opened.Should().ContainSingle().Which.Source.Should().Be(result.Message.AssistantResponse!.Body);
                    hold = false;
                    await model.ReadLatestConversationAsync();
                    model.ConversationMessages.Should().Equal(original, result.Message);
                    model.ConversationUpdates.Should().BeEmpty();
                    model.HasConversationUpdates.Should().BeFalse();
                }
                model.ComposerBody.Should().Be("independent unsent input");
                model.CanCompose.Should().BeTrue();
                adapter.Calls.Should().Be(1);
                f.Count("conversation_messages").Should().Be(2);
                f.Count("conversation_drafts").Should().Be(0);
                f.Count("host_questions").Should().Be(0);
                f.Count("scoped_grants").Should().Be(0);
                WindowsSqliteConversationTests.AuditText(f).Should().Be(audit);
                (await f.Store.ReadRetentionAsync(f.Request.SessionId, f.Token)).Should().Be(retention);
            }
            finally
            {
                release.TrySetResult();
                await pending;
                model.PropertyChanged -= Published;
                model.Close();
            }
        });
    }
}
