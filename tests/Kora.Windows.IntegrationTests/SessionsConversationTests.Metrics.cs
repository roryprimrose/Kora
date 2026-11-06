using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using AwesomeAssertions;
using Kora.Application.Dependencies;
using Kora.Core.Dependencies;
using Kora.Core.Providers;
using Kora.Core.Tools;
using Kora.Windows.IntegrationTests.Storage;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeSessionMetricsShowReportedTokensAndContextWithoutAdditionalModelOrDurableWrites(bool compact)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            var adapter = new MetricsAdapter();
            await using var composition = new Composition(f, adapter: adapter);
            var model = composition.Model;
            var owner = new SessionsPresentationOwner(model);
            Window window = compact ? new CompactSessionWindow(model, owner) : new SessionsWindow(model, owner);
            window.Show();
            try
            {
                await model.RefreshAsync();
                await model.SelectAsync(model.Sessions.Single());
                await composition.Conversation.SubmitAsync(model.SelectedSessionRecord!, "original user text", () => true, f.Token);
                model.ComposerBody = "unsent private text";
                var audits = WindowsSqliteConversationTests.AuditText(f);
                var messages = f.Count("conversation_messages");
                var tasks = f.Count("host_task_events");
                var retention = await f.Store.ReadRetentionAsync(f.Request.SessionId, f.Token);
                var panel = window.GetVisualDescendants().OfType<SessionMetricsPanel>().Single();
                model.CanReadMetrics.Should().BeTrue(model.Status);
                panel.FindControl<Expander>("MetricsExpander")!.IsExpanded = true;
                await panel.Operation;
                model.MetricsStatus.Should().Contain("Input tokens: 100", model.Status).And.Contain("Output tokens: 20")
                    .And.Contain("100 / 1,000 tokens (10.0%)").And.NotContain("original user text")
                    .And.NotContain("unsent private text");
                model.MetricsModels.Should().ContainSingle();
                window.UpdateLayout();
                panel.GetVisualDescendants().OfType<ProgressBar>().Single().Value.Should().Be(10);
                panel.FindControl<Button>("RefreshSessionMetrics")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await panel.Operation;
                adapter.Calls.Should().Be(1);
                model.ComposerBody.Should().Be("unsent private text");
                f.Count("conversation_messages").Should().Be(messages);
                f.Count("host_task_events").Should().Be(tasks);
                (await f.Store.ReadRetentionAsync(f.Request.SessionId, f.Token)).Should().Be(retention);
                WindowsSqliteConversationTests.AuditText(f).Should().Be(audits);
                composition.Access.CanInspect = false;
                model.MetricsModels.Should().BeEmpty();
                model.MetricsStatus.Should().NotContain("100 /");
            }
            finally { owner.Revoke(); }
        });
    }

    [Fact]
    public async Task NativeMetricsRequireSelectionAndRejectLatePresenter()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            await using var composition = new Composition(f);
            var model = composition.Model;
            model.CanReadMetrics.Should().BeFalse();
            await model.RefreshAsync();
            await model.SelectAsync(model.Sessions.Single());
            await model.ReadMetricsAsync(() => false);
            model.MetricsModels.Should().BeEmpty();
            model.MetricsStatus.Should().NotContain("Finished model executions");
        });
    }

    private sealed class MetricsAdapter : IModelTurnAdapter
    {
        internal int Calls { get; private set; }
        public Task<ModelProviderReply> RunAsync(ModelTurnProvenance provenance, ProviderProfileTuple? profile,
            ReadOnlyMemory<byte> context, Func<string, string, CancellationToken, ValueTask<CapabilityReply>> invokeTool,
            CancellationToken token)
        {
            Calls++;
            return Task.FromResult(new ModelProviderReply(ModelTurnOutcome.Succeeded, provenance.Selection, new("answer", null))
            { TokenUsage = new(100, 20, 100, 1000) });
        }
    }
}
