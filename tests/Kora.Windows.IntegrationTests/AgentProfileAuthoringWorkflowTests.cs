using Avalonia.Controls;

using AwesomeAssertions;

using Kora.Application.Agents;
using Kora.Application.Definitions;
using Kora.Application.Hosting;
using Kora.Core.Agents;
using Kora.NativeUxFixture;
using Kora.Windows.Artifacts;
using Kora.Windows.IntegrationTests.ArtifactDiscovery;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class AgentProfileAuthoringWorkflowTests
{
    [Fact]
    public async Task PrivacyClearsPresentationButRetainsCancellationIgnoringReadUntilRealQuiescence()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var parent = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".net-test-artifacts"));
            Directory.CreateDirectory(parent);
            using var f = new NativeUxFixtureSession(parent);
            await f.InitializeAsync();
            await using var admission = new InstructionControlAdmission(f.Interactions, f.Interactions, new HostTaskCoordinator(f.Tasks));
            var store = new DelayedStore(new WindowsOwnedAgentProfileStore(f));
            var controller = new AgentProfileEditorController(f.Main, new(store, admission, f.Audit),
                () => true, () => 1, NullLogger<AgentProfileEditorController>.Instance);
            AgentProfileEditorWindow? editor = null;
            using var opened = Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) =>
            { if (window is AgentProfileEditorWindow selected) { editor = selected; } });
            controller.Bind();
            async Task Run(string command)
            { f.Main.CommandText = command; await f.Main.RunTypedCommand.ExecuteAsync(); }
            await Run("agent author create example");
            editor!.Source.Text = WindowsOwnedAgentProfileStoreTests.Source;
            var listing = Run("agent author list");
            try
            {
                (await Task.WhenAny(store.Entered.Task, listing)).Should().BeSameAs(store.Entered.Task,
                    "the owned read must start; status: {0}", editor.Status.Text);
                var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                editor.Closed += (_, _) => closed.TrySetResult();
                f.Main.CloseForObservedPrivacyEvent("Synthetic privacy closure", hidePresentation: true);
                await f.Main.PrivacyClosureTask;
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { });
                editor.Source.Text.Should().BeEmpty();
                listing.IsCompleted.Should().BeFalse();
                controller.ActiveOperation.IsCompleted.Should().BeFalse();
                closed.Task.IsCompleted.Should().BeFalse();
                store.Release.SetResult();
                await listing;
                await closed.Task.WaitAsync(TestContext.Current.CancellationToken);
                editor.IsVisible.Should().BeFalse();
                editor.Inventory.ItemsSource.Should().BeNull();
                Directory.Exists(Path.Combine(f.RoamingRoot, "OwnedAgents-v1")).Should().BeFalse();
            }
            finally { store.Release.TrySetResult(); await listing; }
        });
    }

    private sealed class DelayedStore(IOwnedAgentProfileStore inner) : IOwnedAgentProfileStore
    {
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<IReadOnlyList<string>> List(CancellationToken token)
        {
            Entered.TrySetResult();
#pragma warning disable VSTHRD003 // Deliberately cancellation-ignoring storage; controller must retain ownership.
            await Release.Task;
#pragma warning restore VSTHRD003
            return ["late-private-result"];
        }
        public Task<OwnedAgentProfileSnapshot?> Read(string name, CancellationToken token) => inner.Read(name, token);
        public Task<OwnedAgentProfileDestination> PrepareNew(string name, CancellationToken token) => inner.PrepareNew(name, token);
        public Task<AgentProfileWriteResult> Create(OwnedAgentProfileDestination destination, OwnedAgentProfileDocument document,
            Func<bool> eligible, Func<Func<AgentProfileWriteResult>, Task<AgentProfileWriteResult>> commit, CancellationToken token) =>
            inner.Create(destination, document, eligible, commit, token);
    }
}
