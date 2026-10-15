using System.Collections.Immutable;

using Avalonia.Automation;
using Avalonia.Controls;

using AwesomeAssertions;

using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Windows.IntegrationTests.Storage;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
    [Fact]
    public async Task EveryNativeTerminalChoiceIsAccessibleSharedExactAndRevokesNavigationWithoutReading()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            await WindowsSqliteConversationTests.Submit(f, f.Request.SessionId, "marker Denied CallerCancelled");
            var snapshot = await f.Store.ReadConversationPageAsync(f.Request.SessionId, null, 50, f.Token);
            var messages = Enumerable.Range(1, 26).Select(index => snapshot.Messages[0] with { Sequence = index }).ToImmutableArray();
            var reader = new SearchReader((_, cursor, limit, _) => ValueTask.FromResult(snapshot with
            {
                Messages = messages.Where(message => message.Sequence > (cursor?.After ?? 0)).Take(limit).ToImmutableArray(),
                Ceiling = 26, HeadRevision = 52,
            }));
            await using var composed = new Composition(f, conversationReader: reader);
            var model = composed.Model;
            var owner = new SessionsPresentationOwner(model);
            var workspace = new SessionsWindow(model, owner);
            var compact = new CompactSessionWindow(model, owner);
            workspace.Show();
            compact.Show();
            try
            {
                await model.RefreshAsync();
                await model.SelectAsync(model.Sessions.Single());
                model.ComposerBody = "independent dirty";
                model.ConversationQuery = "marker";
                await CheckChoices("ConversationOutcome", model.ConversationOutcomes,
                    value => value is null or ModelTurnOutcome.Unavailable, () => model.ConversationOutcome = null);
                await CheckChoices("ConversationReason", model.ConversationReasons,
                    value => value is null or ModelTurnReason.UnregisteredModel, () => model.ConversationReason = null);
                await CheckChoices("ConversationTaskState", model.ConversationTaskStates,
                    value => value is null or HostTaskState.Unavailable, () => model.ConversationTaskState = null);

                model.ConversationRole = ConversationSearchRole.USER;
                model.ConversationOutcome = ModelTurnOutcome.Unavailable;
                await model.SearchConversationAsync();
                model.ConversationSearchMessages.Should().BeEmpty();
                model.ConversationSearchStatus.Should().Contain("Observed 0 exact USER").And.Contain("scanned 26");
                model.ConversationRole.Should().Be(ConversationSearchRole.USER);
                model.ConversationOutcome.Should().Be(ModelTurnOutcome.Unavailable);
                foreach (var edit in new Action[] { () => model.ConversationOutcome = (ModelTurnOutcome)99,
                    () => model.ConversationReason = (ModelTurnReason)99, () => model.ConversationTaskState = (HostTaskState)99 })
                {
                    model.ClearConversationSearch();
                    model.ConversationQuery = "marker";
                    edit();
                    var reads = reader.Reads;
                    await model.SearchConversationAsync();
                    reader.Reads.Should().Be(reads);
                    model.ConversationSearchStatus.Should().Contain("InvalidTerminalFilter");
                }
                Click(compact, "ClearConversationSearch");
                model.ConversationOutcome.Should().BeNull();
                model.ConversationReason.Should().BeNull();
                model.ConversationTaskState.Should().BeNull();
                model.ComposerBody.Should().Be("independent dirty");

                async Task CheckChoices<T>(string name, IReadOnlyList<SessionsViewModel.ConversationTerminalOption<T>> choices,
                    Func<T?, bool> matches, Action reset) where T : struct, Enum
                {
                    var control = workspace.FindControl<ComboBox>(name)!;
                    var shared = compact.FindControl<ComboBox>(name)!;
                    control.ItemsSource!.Cast<SessionsViewModel.ConversationTerminalOption<T>>().Should().Equal(choices);
                    choices.Select(choice => choice.Value).Should().Equal(new T?[] { null }.Concat(Enum.GetValues<T>().Select(value => (T?)value)));
                    choices[0].ToString().Should().Be("Any");
                    AutomationProperties.GetName(control).Should().Contain("Exact terminal HOST").And.Contain(typeof(T).Name).And.Contain("editing does not read");
                    AutomationProperties.GetName(shared).Should().Contain("Shared exact terminal HOST");
                    control.Focusable.Should().BeTrue();
                    shared.Focusable.Should().BeTrue();
                    foreach (var choice in choices)
                    {
                        reset();
                        await model.SearchConversationAsync();
                        model.CanNextConversationSearch.Should().BeTrue();
                        var results = workspace.FindControl<ListBox>("ConversationSearchResults")!;
                        var sharedResults = compact.FindControl<ListBox>("ConversationSearchResults")!;
                        results.SelectedIndex = 0;
                        sharedResults.SelectedIndex = 0;
                        var reads = reader.Reads;
                        // Start from a different exact value so even the Any choice is a real edit.
                        shared.SelectedItem = choices.First(other => other != choice);
                        shared.SelectedItem = choice;
                        control.SelectedItem.Should().Be(choice);
                        reader.Reads.Should().Be(reads);
                        model.ConversationSearchMessages.Should().BeEmpty();
                        results.SelectedItem.Should().BeNull();
                        sharedResults.SelectedItem.Should().BeNull();
                        model.CanNextConversationSearch.Should().BeFalse();
                        Click(compact, "SearchConversation");
                        await compact.ConversationOperation;
                        model.ConversationSearchMessages.Should().HaveCount(matches(choice.Value) ? 25 : 0);
                    }
                    reset();
                }
            }
            finally { owner.Revoke(); }
        });
    }
}
