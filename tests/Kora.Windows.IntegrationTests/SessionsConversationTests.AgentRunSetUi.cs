using Avalonia.Controls;
using Avalonia.Interactivity;

using AwesomeAssertions;

using Kora.Application.Agents;
using Kora.Application.Dependencies;
using Kora.Application.Hosting;
using Kora.Core.Hosting;
using Kora.NativeUxFixture;
using Kora.Windows.IntegrationTests.Storage;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeIndependentRunSelectionCommandsRetainedResultAndCloseShareOneHost(bool capacityAvailable)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            var first = await f.Store.ReadMetadataAsync(f.Request.SessionId, f.Token);
            f.Request = InteractionStorageFixture.NewRequest();
            await f.AdmitAsync(newSession: true);
            await WindowsSqliteConversationTests.FinishInitial(f);
            var second = await f.Store.ReadMetadataAsync(f.Request.SessionId, f.Token);
            var adapter = new RunSetAdapter();
            await using var composed = new Composition(f, adapter: adapter);
            var parent = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".net-test-artifacts"));
            Directory.CreateDirectory(parent);
            using var native = new NativeUxFixtureSession(parent);
            await native.InitializeAsync();
            var readers = new List<NativeAgentReader>();
            ILocalAgentConcurrencyQualification qualification = capacityAvailable
                ? new RunSetQualification(f.Time.GetUtcNow(), adapter) : new App.UnavailableLocalAgentConcurrencyQualification();
            var factory = new AgentRunSetFactory(composed.Turns, qualification, lane =>
            {
                var reader = new NativeAgentReader();
                readers.Add(reader);
                var conversation = new SessionConversationService(f.Store, f.Store, composed.Workspace, composed.Access,
                    lane, f.Time, NullLogger<SessionConversationService>.Instance, lane, composed.Audit);
                return new(reader, f.Store, composed.Access, conversation, lane, composed.Audit, f.Time);
            });
            var controller = new AgentRunSetController(native.Main, factory, composed.Workspace,
                () => true, () => 1, NullLogger<AgentRunSetController>.Instance);
            controller.Bind();
            AgentRunSetWindow? opened = null;
            using var observation = Window.WindowOpenedEvent.AddClassHandler<AgentRunSetWindow>((window, _) => opened = window);
            async Task Command(string text)
            {
                native.Main.CommandText = text;
                await native.Main.RunTypedCommand.ExecuteAsync();
            }
            try
            {
                await Command("independent agents");
                var window = opened ?? throw new InvalidOperationException("No admitted independent runs window.");
                for (var index = 0; index < 2; index++)
                {
                    await Command($"agent run {index + 1} targets");
                    var panel = window.Runs[index];
                    panel.Targets.SelectedItem = ((AgentRunSetWindow.Target[])panel.Targets.ItemsSource!)
                        .Single(item => item.Entry.Authority.SessionId == (index == 0 ? first : second).Authority.SessionId);
                    await Command($"agent run {index + 1} source existing.agent.md");
                    await Command($"agent run {index + 1} request native original {index + 1}");
                    await Command($"agent run {index + 1} review");
                    panel.Confirm.IsEnabled.Should().BeTrue();
                    panel.Review.Text.Should().Contain($"native original {index + 1}").And.Contain("NO TOOLS");
                }
                adapter.Active.Should().Be(0);
                window.Runs[0].Confirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await RequireRunSetStarted(controller.ObserveOperation(1), adapter.Started(first).Task);
                await Command("agent run 2 select");
                window.Selection.SelectedIndex.Should().Be(1);
                await Command("agent run 2 confirm");
                if (capacityAvailable)
                {
                    await RequireRunSetStarted(controller.ObserveOperation(2), adapter.Started(second).Task);
                    adapter.Active.Should().Be(2);
                }
                else
                {
                    await controller.ObserveOperation(2);
                    adapter.Active.Should().Be(1);
                    window.Runs[1].Status.Text.Should().Contain("resource-blocked");
                    window.Runs[1].Result.Text.Should().BeEmpty();
                }
                adapter.Finish(first, "native first genuine answer");
                await controller.ObserveOperation(1);
                await Command("agent run 1 status");
                window.Runs[0].Result.Text.Should().Be("native first genuine answer");
                if (capacityAvailable)
                {
                    adapter.Finish(second, "native second genuine answer");
                    await controller.ObserveOperation(2);
                    window.Runs[1].Result.Text.Should().Be("native second genuine answer");
                    await Command("agent run 1 stop");
                    window.Runs[0].Status.Text.Should().Contain("joined");
                    window.Runs[1].Result.Text.Should().Be("native second genuine answer");
                    await Command("independent agents close");
                }
                else
                {
                    // Closing reports the failed second invocation, but still joins and clears both source owners.
                    await Command("independent agents close");
                }
                readers.Should().OnlyContain(item => item.Released);
                window.Runs.Should().OnlyContain(item => item.Source.Text == string.Empty
                    && item.Input.Text == string.Empty && item.Review.Text == string.Empty && item.Result.Text == string.Empty);
                composed.Turns.IsQuiescent.Should().BeTrue();
            }
            finally
            {
                adapter.FinishAll();
                // A prior failed closure remains truthful at exit, rather than being turned into success.
                if (capacityAvailable) { await native.Main.ExitAsync(); }
                else { await native.Main.Invoking(value => value.ExitAsync()).Should().ThrowAsync<Exception>(); }
            }
        });
    }
}
