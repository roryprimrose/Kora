using System.Text;

using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Application.Hosting;
using Kora.Application.Dependencies;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Windows.IntegrationTests.Storage;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task NativeManagementAnswerRequiresSeparateDisclosureConfirmationAndRetainsOnlyInExplicitDestination(bool compact, bool qualified)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            var execution = new PendingTurnAdapter();
            var manager = new ManagementAnswerAdapter();
            await using var composition = new Composition(f, adapter: execution, management: true,
                managementAdapter: qualified ? manager : null);
            var target = await f.Store.ReadMetadataAsync(f.Request.SessionId, f.Token);
            var destination = await composition.Workspace.CreateAsync(new("Explicit management answers"), RequestOrigin.LocalUi, f.Token);
            var running = await composition.Conversation.SubmitQueuedAsync(target, "private running original", () => true, f.Token);
            await execution.NextAsync();
            await composition.Conversation.SubmitQueuedAsync(target, "private pending original", () => true, f.Token);
            var model = composition.Model;
            var owner = new SessionsPresentationOwner(model);
            Window window = compact ? new CompactSessionWindow(model, owner) : new SessionsWindow(model, owner);
            window.Show();
            try
            {
                await model.RefreshAsync();
                await model.SelectAsync(model.Sessions.Single(row => row.Authority.SessionId == target.Authority.SessionId));
                model.ManagementDestination = model.ManagementDestinations.Single(row => row.Authority == destination.Authority);
                model.ManagementQuestion = "What current and remaining work is actually observed?";
                model.CanReviewManagementAnswer.Should().BeFalse();
                model.ManagementEnabled = true;
                model.ComposerBody = "private unsent original remains independent";
                var tasks = f.Count("host_task_events");
                var messages = f.Count("conversation_messages");
                var pending = await composition.Conversation.ReadPendingConversationAsync(target, () => true, f.Token);
                var panel = window.GetVisualDescendants().OfType<ManagementAnswerPanel>().Single();
                panel.FindControl<Button>("ReviewManagement")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await panel.Operation;
                model.ManagementText.Should().Contain("Complete body-free ledger evidence").And.Contain(model.ManagementQuestion)
                    .And.Contain("Explicit management answers").And.NotContain("private running").And.NotContain("private pending");
                model.CanConfirmManagementAnswer.Should().BeTrue();
                manager.Calls.Should().Be(0);
                f.Count("host_task_events").Should().Be(tasks);
                f.Count("conversation_messages").Should().Be(messages);
                model.ManagementQuestion += " Please distinguish unknown work.";
                model.CanConfirmManagementAnswer.Should().BeFalse();
                model.ManagementText.Should().BeEmpty();
                await model.ConfirmManagementAnswerAsync();
                manager.Calls.Should().Be(0);
                panel.FindControl<Button>("ReviewManagement")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await panel.Operation;
                model.ManagementEnabled = false;
                model.CanConfirmManagementAnswer.Should().BeFalse();
                model.ManagementText.Should().BeEmpty();
                model.ManagementEnabled = true;
                panel.FindControl<Button>("ReviewManagement")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await panel.Operation;
                panel.FindControl<Button>("ConfirmManagement")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await panel.Operation;
                model.CanConfirmManagementAnswer.Should().BeFalse();
                var retained = await f.Store.ReadConversationPageAsync(destination.Authority.SessionId, null, 25, f.Token);
                var answer = retained.Messages.Should().ContainSingle().Subject;
                answer.Body.Should().Be(model.ManagementQuestion);
                if (qualified)
                {
                    manager.Calls.Should().Be(1);
                    answer.AssistantResponse!.Body.Should().Be(ManagementAnswerAdapter.Answer);
                    model.ManagementText.Should().Contain(ManagementAnswerAdapter.Answer).And.Contain("Succeeded");
                    Encoding.UTF8.GetString(manager.Wire!).Should().Contain(model.ManagementQuestion)
                        .And.NotContain("private running").And.NotContain("private pending").And.NotContain("private unsent");
                }
                else
                {
                    manager.Calls.Should().Be(0);
                    answer.AssistantResponse.Should().BeNull();
                    answer.Outcome.Should().Be(ModelTurnOutcome.Unavailable);
                    model.ManagementText.Should().Contain("Unavailable").And.Contain("No retained ASSISTANT");
                }
                (await composition.Conversation.ReadPendingConversationAsync(target, () => true, f.Token)).Should().BeEquivalentTo(pending);
                running.Completion.IsCompleted.Should().BeFalse();
                execution.Calls.Should().Be(1);
                model.ComposerBody.Should().Be("private unsent original remains independent");
                f.Count("scoped_grants").Should().Be(0);
                WindowsSqliteConversationTests.AuditText(f).Should().NotContain(model.ManagementQuestion)
                    .And.NotContain("private running").And.NotContain("private pending").And.NotContain(ManagementAnswerAdapter.Answer);
            }
            finally
            {
                execution.ReleaseAll();
                await running.Completion;
                owner.Revoke();
            }
        });
    }

    [Fact]
    public async Task NativeManagementAnswerRefusesBusyDestinationWithoutBorrowingItsExecutionOrRetainingAnotherMessage()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            var execution = new PendingTurnAdapter();
            var manager = new ManagementAnswerAdapter();
            await using var composition = new Composition(f, adapter: execution, management: true, managementAdapter: manager);
            var source = await f.Store.ReadMetadataAsync(f.Request.SessionId, f.Token);
            var destination = await composition.Workspace.CreateAsync(new("Busy answer destination"), RequestOrigin.LocalUi, f.Token);
            var busy = await composition.Conversation.SubmitQueuedAsync(destination, "Original destination work", () => true, f.Token);
            await execution.NextAsync();
            try
            {
                var model = composition.Model;
                await model.RefreshAsync();
                await model.SelectAsync(model.Sessions.Single(row => row.Authority.SessionId == source.Authority.SessionId));
                model.ManagementDestination = model.ManagementDestinations.Single(row => row.Authority == destination.Authority);
                model.ManagementEnabled = true;
                await model.ReviewManagementAnswerAsync();
                model.CanConfirmManagementAnswer.Should().BeTrue();
                var messages = f.Count("conversation_messages");
                await model.ConfirmManagementAnswerAsync();
                model.ManagementText.Should().Contain("unconfirmed");
                manager.Calls.Should().Be(0);
                execution.Calls.Should().Be(1);
                f.Count("conversation_messages").Should().Be(messages);
                busy.Completion.IsCompleted.Should().BeFalse();
                await model.ReviewManagementAnswerAsync();
                model.ManagementText.Should().NotBeEmpty();
                composition.Access.ControlRevision++;
                model.ManagementText.Should().BeEmpty();
                await model.ConfirmManagementAnswerAsync();
                manager.Calls.Should().Be(0);
            }
            finally { execution.ReleaseAll(); await busy.Completion; }
        });
    }

    private sealed class ManagementAnswerAdapter : IModelTurnAdapter
    {
        internal const string Answer = "The host observed running work and a pending request; no future plan or completion is inferred.";
        internal int Calls { get; private set; }
        internal byte[]? Wire { get; private set; }
        public Task<ModelProviderReply> RunAsync(ModelTurnProvenance provenance, Kora.Core.Providers.ProviderProfileTuple? profile,
            ReadOnlyMemory<byte> context, Func<string, string, CancellationToken, ValueTask<Kora.Core.Tools.CapabilityReply>> invokeTool,
            CancellationToken token)
        {
            Calls++;
            Wire = context.ToArray();
            return Task.FromResult(new ModelProviderReply(ModelTurnOutcome.Succeeded, provenance.Selection, new(Answer, null)));
        }
    }
}