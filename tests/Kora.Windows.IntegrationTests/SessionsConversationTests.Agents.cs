using System.Text;
using System.Text.Json;

using Avalonia.Controls;
using Avalonia.Interactivity;

using AwesomeAssertions;

using Kora.Application.Agents;
using Kora.Core.Agents;
using Kora.Core.Context;
using Kora.NativeUxFixture;
using Kora.Windows.IntegrationTests.Storage;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeAgentReviewSeparateConsentRetainsRealAnswerAndClearsPrivateBuffersOnStop(bool invoke)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            var adapter = new HistoryAdapter();
            await using var composed = new Composition(f, adapter: adapter);
            var parent = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".net-test-artifacts"));
            Directory.CreateDirectory(parent);
            using var native = new NativeUxFixtureSession(parent);
            await native.InitializeAsync();
            var reader = new NativeAgentReader();
            await using var workflow = new BoundedAgentWorkflow(reader, f.Store, composed.Access,
                composed.Conversation, composed.Turns, composed.Audit, f.Time);
            var controller = new AgentRunController(native.Main, workflow, composed.Workspace,
                () => true, () => 1, NullLogger<AgentRunController>.Instance);
            controller.Bind();
            AgentRunWindow? opened = null;
            using var observation = Window.WindowOpenedEvent.AddClassHandler<AgentRunWindow>((window, _) => opened = window);
            native.Main.CommandText = "review agent";
            await native.Main.RunTypedCommand.ExecuteAsync();
            var window = opened ?? throw new InvalidOperationException("No admitted bounded agent window.");
            try
            {
                var buttons = ((StackPanel)((ScrollViewer)window.Content!).Content!).Children.OfType<Button>().ToArray();
                buttons.Single(button => Equals(button.Content, "Refresh existing Active conversations"))
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await controller.ActiveOperation;
                window.Targets.SelectedIndex = 0;
                window.Source.Text = @"C:\Users\owner\existing.agent.md";
                window.Input.Text = "Original native request.";
                buttons.Single(button => Equals(button.Content, "Read source and review exact invocation"))
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await controller.ActiveOperation;
                window.Confirm.IsEnabled.Should().BeTrue();
                window.Review.Text.Should().Contain("Original native request.").And.Contain("LOCAL TEXT -> TEXT");
                adapter.Calls.Should().Be(0);
                if (invoke)
                {
                    window.Confirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await controller.ActiveOperation;
                    adapter.Calls.Should().Be(1);
                    window.Status.Text.Should().Contain("Succeeded");
                    window.Result.Text.Should().Be("earlier ASSISTANT");
                    var message = (await f.Store.ReadConversationPageAsync(f.Request.SessionId, null, 25, f.Token)).Messages.Single();
                    message.Body.Should().Be("Original native request.");
                    message.AssistantResponse!.Body.Should().Be("earlier ASSISTANT");
                    using var wire = JsonDocument.Parse(adapter.Wire!);
                    wire.RootElement.GetProperty("Tools").GetArrayLength().Should().Be(0);
                    wire.RootElement.GetProperty("Constraints").GetProperty("MaximumCalls").GetInt32().Should().Be(0);
                }
                native.Main.CommandText = "agent stop";
                await native.Main.RunTypedCommand.ExecuteAsync();
                adapter.Calls.Should().Be(invoke ? 1 : 0);
                reader.Released.Should().BeTrue();
                window.Source.Text.Should().BeEmpty();
                window.Input.Text.Should().BeEmpty();
                window.Review.Text.Should().BeEmpty();
                window.Result.Text.Should().BeEmpty();
            }
            finally { await native.Main.ExitAsync(); }
        });
    }

    private sealed class NativeAgentReader : IAgentProfileReader, IAgentProfileSelection
    {
        public bool Released { get; private set; }
        public LocalFileMetadata Metadata { get; } = new(@"C:\Users\owner\existing.agent.md", "native-source", 512, DateTimeOffset.UnixEpoch);
        public Task<IAgentProfileSelection> OpenAsync(string path, CancellationToken token) => Task.FromResult<IAgentProfileSelection>(this);
        public Task<byte[]> ReadAsync(CancellationToken token) => Task.FromResult(Encoding.UTF8.GetBytes("""
            ---
            name: existing-profile
            description: User role
            kora-agent-format: 1
            runtime: local
            input: text
            output: text
            references: none
            tools: none
            maximum-calls: 0
            maximum-rounds: 1
            maximum-input-bytes: 32768
            maximum-output-bytes: 4096
            deadline-ms: 15000
            ---
            Explain the original request. No scripts or imported references.
            """));
        public void Dispose() => Released = true;
    }
}
