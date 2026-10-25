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
    public async Task NativeSessionsRunsOneQualifiedFakeThroughActualHostRegistryAndPrivateSqliteThenPresentsSeparateAssistant()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            var adapter = new AnswerAdapter();
            await using var composed = new Composition(f, adapter: adapter);
            var model = composed.Model;
            var window = new SessionsWindow(model);
            window.Show();
            try
            {
                await model.RefreshAsync();
                await model.SelectAsync(model.Sessions.Single());
                await model.ReadConversationAsync();
                model.ComposerBody = "What version did this host actually report?";
                window.FindControl<Button>("SubmitConversation")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await window.ConversationOperation;
                adapter.Calls.Should().Be(1);
                adapter.ToolCalls.Should().Be(1);
                var message = model.ConversationMessages.Single();
                message.Body.Should().Be("What version did this host actually report?");
                message.AssistantResponse!.Body.Should().Be("Host version: 1.0.0");
                model.ConversationStatus.Should().Contain("Succeeded").And.Contain("ASSISTANT").And.NotContain("No assistant answer");
                var stored = await f.Store.ReadConversationMessageAsync(f.Request.SessionId, message.Request.RequestId, f.Token);
                stored.Should().Be(message);
                model.ComposerBody.Should().BeEmpty();
                model.ComposerDirty.Should().BeFalse();
                f.Count("host_questions").Should().Be(0);
                f.Count("scoped_grants").Should().Be(0);
                model.ConversationQuery = "1.0.0";
                await model.SearchConversationAsync();
                model.ConversationSearchMessages.Should().BeEmpty("All searches original USER / typed HOST fields and must not claim assistant-body search");
                model.ConversationRole = ConversationSearchRole.USER;
                await model.SearchConversationAsync();
                model.ConversationSearchMessages.Should().BeEmpty("USER search must not claim assistant-body search");
                model.ConversationRole = ConversationSearchRole.HOST;
                await model.SearchConversationAsync();
                model.ConversationSearchMessages.Should().BeEmpty("typed HOST search must not claim assistant-body search");
                model.ConversationRole = ConversationSearchRole.ASSISTANT;
                await model.SearchConversationAsync();
                model.ConversationSearchMessages.Should().ContainSingle("only explicit ASSISTANT search reads the retained answer body")
                    .Which.AssistantResponse!.Body.Should().Be("Host version: 1.0.0");
                window.ClosePrivate();
                model.ConversationMessages.Should().BeEmpty("private closure retires the immediate terminal projection as well as pages");
            }
            finally { window.ClosePrivate(); }
        });
    }

    [Fact]
    public async Task NewRequestUsesSameActualExecutionAndKeepsGenuineAnswerDistinctUntilPrivateClose()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        var adapter = new AnswerAdapter();
        await using var composed = new Composition(f, adapter: adapter);
        using var composer = composed.NewComposer();
        composer.Body = "New original request";
        await composer.Create();
        composer.Review();
        await composer.Submit();
        composer.AssistantResponse!.Body.Should().Be("Host version: 1.0.0");
        composer.Body.Should().BeEmpty();
        composer.Submission.Should().NotBeNull();
        var retained = await f.Store.ReadConversationMessageAsync(composer.Target!.Authority.SessionId,
            composer.Submission!.RequestId, f.Token);
        retained.Body.Should().Be("New original request");
        retained.AssistantResponse.Should().Be(composer.AssistantResponse);
        adapter.Calls.Should().Be(1);
        composer.Dispose();
        composer.AssistantResponse.Should().BeNull();
        composer.CanSubmit.Should().BeFalse();
    }

    private sealed class AnswerAdapter : IModelTurnAdapter
    {
        internal int Calls { get; private set; }
        internal int ToolCalls { get; private set; }
        public async Task<ModelProviderReply> RunAsync(ModelTurnProvenance provenance, ProviderProfileTuple? profile,
            ReadOnlyMemory<byte> context, Func<string, string, CancellationToken, ValueTask<CapabilityReply>> invokeTool,
            CancellationToken token)
        {
            Calls++;
            var result = await invokeTool(ReadOnlyCapabilityCatalog.Version, "{}", token);
            result.Outcome.Should().Be(CapabilityOutcome.Succeeded);
            ToolCalls++;
            return new(ModelTurnOutcome.Succeeded, provenance.Selection, new("Host version: " + result.Version!.Version, null));
        }
    }
}
