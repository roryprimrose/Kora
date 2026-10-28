using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

using AwesomeAssertions;

using Kora.Application.Agents;
using Kora.Application.Dependencies;
using Kora.Application.Hosting;
using Kora.Core.Agents;
using Kora.Core.Communication;
using Kora.Core.Context;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Providers;
using Kora.Core.Storage;
using Kora.Core.Tools;
using Kora.NativeUxFixture;
using Kora.Windows.IntegrationTests.Storage;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
    [Theory]
    [InlineData("target", true)]
    [InlineData("source", true)]
    [InlineData("task", true)]
    [InlineData("target", false)]
    [InlineData("source", false)]
    [InlineData("task", false)]
    public async Task IndependentRunStatusCannotRestoreRetiredAnswerOrTerminalLabel(string edit, bool answer)
    {
        await WithRunSetAffinity(async ui =>
        {
            await ui.Review(1, ui.First);
            await ui.Invoke(1, ui.First);
            if (answer) { ui.Adapter.Inner.Finish(ui.First, "answer owned only by A"); }
            else { ui.Adapter.Inner.FinishAll(); }
            await ui.Controller.ObserveOperation(1);
            await ui.Command("agent run 1 status");
            var panel = ui.Window.Runs[0];
            panel.Result.Text.Should().Be(answer ? "answer owned only by A" : string.Empty);
            panel.Status.Text.Should().Contain("Result belongs only to the selected conversation");
            var terminalStatus = panel.Status.Text;
            var original = (await ui.Storage.Store.ReadConversationPageAsync(ui.First.Authority.SessionId, null, 25, ui.Storage.Token))
                .Messages.Single();

            await ui.Review(2, ui.Other);
            await ui.Invoke(2, ui.Other);
            ui.Adapter.Inner.Finish(ui.Other, "independent run 2 answer");
            await ui.Controller.ObserveOperation(2);
            var otherStatus = ui.Window.Runs[1].Status.Text;
            var beforeRevoke = false;
            var restored = new List<string>();
            panel.Result.PropertyChanged += (_, args) =>
            {
                if (args.Property != TextBox.TextProperty) { return; }
                if (!string.IsNullOrEmpty(panel.Result.Text)) { restored.Add(panel.Result.Text); }
                else if (!beforeRevoke)
                {
                    beforeRevoke = true;
                    ui.StatusButton(panel).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }
            };
            ui.Edit(edit);
            var audit = WindowsSqliteConversationTests.AuditText(ui.Storage);
            var applicationAudit = ui.Composed.Audit.Events.Count;
            var reads = ui.Readers.Sum(reader => reader.Reads);
            var calls = ui.Adapter.Calls;
            for (var index = 0; index < 3; index++)
            {
                ui.StatusButton(panel).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await ui.Command("agent run 1 status");
                panel.Result.Text.Should().BeEmpty();
                panel.Status.Text.Should().Contain("retired").And.NotBe(terminalStatus);
            }
            restored.Should().BeEmpty("neither reentrant Status before revoke nor repeated Status after join may republish A");
            ui.Adapter.Calls.Should().Be(calls);
            ui.Readers.Sum(reader => reader.Reads).Should().Be(reads);
            WindowsSqliteConversationTests.AuditText(ui.Storage).Should().Be(audit);
            ui.Composed.Audit.Events.Count.Should().Be(applicationAudit);
            ui.Storage.Count("conversation_messages").Should().Be(2);
            ui.Storage.Count("host_questions").Should().Be(0);
            ui.Storage.Count("scoped_grants").Should().Be(0);
            ui.Window.Runs[1].Status.Text.Should().Be(otherStatus);
            ui.Window.Runs[1].Result.Text.Should().Be("independent run 2 answer");

            await ui.Review(1, ui.Second);
            await ui.Invoke(1, ui.Second);
            ui.Adapter.Inner.Finish(ui.Second, "fresh answer owned only by B");
            await ui.Controller.ObserveOperation(1);
            await ui.Command("agent run 1 status");
            panel.Result.Text.Should().Be("fresh answer owned only by B");
            var second = (await ui.Storage.Store.ReadConversationPageAsync(ui.Second.Authority.SessionId, null, 25, ui.Storage.Token))
                .Messages.Single();
            second.AssistantResponse!.Body.Should().Be("fresh answer owned only by B");
            (await ui.Storage.Store.ReadConversationPageAsync(ui.First.Authority.SessionId, null, 25, ui.Storage.Token))
                .Messages.Should().ContainSingle().Which.Should().Be(original);
            await ui.Composed.Model.RefreshAsync();
            await ui.Composed.Model.SelectAsync(ui.Composed.Model.Sessions.Single(entry =>
                entry.Authority.SessionId == ui.First.Authority.SessionId));
            await ui.Composed.Model.ReadConversationAsync();
            ui.Composed.Model.ConversationMessages.Should().ContainSingle().Which.Should().Be(original);
            await ui.Command("agent run 1 stop");
            await ui.Command("agent run 2 status");
            ui.Window.Runs[1].Result.Text.Should().Be("independent run 2 answer");
        });
    }

    [Theory]
    [InlineData("target")]
    [InlineData("source")]
    [InlineData("task")]
    public async Task IndependentRunTerminalNotificationCannotRestoreAnswerAfterReentrantRetirement(string edit)
    {
        await WithRunSetAffinity(async ui =>
        {
            await ui.Review(1, ui.First);
            await ui.Invoke(1, ui.First);
            var panel = ui.Window.Runs[0];
            var retired = false;
            var publications = new List<string>();
            panel.Status.PropertyChanged += (_, args) =>
            {
                if (!retired && args.Property == TextBlock.TextProperty
                    && panel.Status.Text?.Contains("Succeeded", StringComparison.Ordinal) == true)
                {
                    retired = true;
                    ui.Edit(edit);
                }
            };
            panel.Result.PropertyChanged += (_, args) =>
            {
                if (args.Property == TextBox.TextProperty && !string.IsNullOrEmpty(panel.Result.Text))
                { publications.Add(panel.Result.Text); }
            };
            ui.Adapter.Inner.Finish(ui.First, "A answer retired during terminal notification");
            await ui.Controller.ObserveOperation(1);
            retired.Should().BeTrue();
            panel.Result.Text.Should().BeEmpty();
            publications.Should().BeEmpty("the final answer write must revalidate after terminal status observers run");
            await ui.Command("agent run 1 status");
            panel.Result.Text.Should().BeEmpty();
            panel.Status.Text.Should().Contain("retired");
            var retained = (await ui.Storage.Store.ReadConversationPageAsync(ui.First.Authority.SessionId, null, 25, ui.Storage.Token))
                .Messages.Single();
            retained.AssistantResponse!.Body.Should().Be("A answer retired during terminal notification");
            ui.Adapter.Calls.Should().Be(1);
        });
    }

    [Theory]
    [InlineData("target")]
    [InlineData("source")]
    [InlineData("task")]
    public async Task IndependentRunRetirementSuppressesLateCompletionWithoutReleasingIgnoringProvider(string edit)
    {
        await WithRunSetAffinity(async ui =>
        {
            await ui.Review(1, ui.First);
            await ui.Invoke(1, ui.First);
            var panel = ui.Window.Runs[0];
            var publications = new List<string>();
            panel.Result.PropertyChanged += (_, args) =>
            {
                if (args.Property == TextBox.TextProperty && !string.IsNullOrEmpty(panel.Result.Text))
                { publications.Add(panel.Result.Text); }
            };
            ui.Edit(edit);
            await ui.Adapter.Cancelled.Task.WaitAsync(ui.Storage.Token);
            await ui.Command("agent run 1 status");
            panel.Result.Text.Should().BeEmpty();
            panel.Status.Text.Should().Contain("retired");
            ui.Controller.ObserveOperation(1).IsCompleted.Should().BeFalse();
            ui.Readers[0].Released.Should().BeFalse();
            ui.Adapter.Inner.Active.Should().Be(1);
            ui.Composed.Turns.IsQuiescent.Should().BeFalse();
            var stop = ui.Command("agent run 1 stop");
            stop.IsCompleted.Should().BeFalse("presentation retirement is neither cancellation success nor resource join");
            panel.Input.Text = "edited while Stop was joining";
            var retiredStatus = panel.Status.Text;
            ui.Adapter.Inner.Finish(ui.First, "late forbidden A answer");
            await ui.Controller.ObserveOperation(1);
            await stop;
            panel.Status.Text.Should().Be(retiredStatus, "an older Stop completion cannot replace newer panel status");
            await ui.Command("agent run 1 status");
            publications.Should().BeEmpty();
            panel.Result.Text.Should().BeEmpty();
            ui.Readers[0].Released.Should().BeTrue();
            ui.Composed.Turns.IsQuiescent.Should().BeTrue();
            var retained = (await ui.Storage.Store.ReadConversationPageAsync(ui.First.Authority.SessionId, null, 25, ui.Storage.Token))
                .Messages.Single();
            retained.AssistantResponse.Should().BeNull();
            retained.Outcome.Should().NotBe(ModelTurnOutcome.Succeeded);
            ui.Adapter.Calls.Should().Be(1);
        });
    }

    [Theory]
    [InlineData("privacy")]
    [InlineData("call")]
    [InlineData("owner")]
    [InlineData("close")]
    [InlineData("replace")]
    public async Task IndependentRunClosedOwnerAndQueuedOldWindowCallbacksCannotPublishIntoReplacement(string ending)
    {
        await WithRunSetAffinity(async ui =>
        {
            await ui.Review(1, ui.First);
            await ui.Invoke(1, ui.First);
            ui.Adapter.Inner.Finish(ui.First, "closed owner's original A answer");
            await ui.Controller.ObserveOperation(1);
            ui.Window.Runs[0].Result.Text.Should().Be("closed owner's original A answer");
            var oldWindow = ui.Window;
            var oldPanel = oldWindow.Runs[0];
            var oldStatus = ui.StatusButton(oldPanel);
            Task? closing = null;
            if (ending is "privacy") { ui.Native.Access.SetOpen(false); }
            else if (ending is "call") { ui.Calls.Set(CallState.Active); }
            else if (ending is "owner") { ui.OwnerRevision++; await ui.Command("agent run 1 status"); }
            else { closing = ui.Command(ending is "close" ? "independent agents close" : "independent agents"); }
            oldStatus.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await ui.Controller.ObserveOperation(1);
            if (closing is not null) { await closing; }
            oldPanel.Result.Text.Should().BeEmpty();
            oldPanel.Status.Text.Should().BeEmpty();
            ui.Readers[0].Released.Should().BeTrue();
            ui.Composed.Turns.IsQuiescent.Should().BeTrue();
            if (ending is "privacy")
            {
                await ui.Native.Main.PrivacyClosureTask;
                ui.Native.Access.SetOpen(true);
                await ui.Command("independent agents");
                ui.Native.Main.CanRevealPrivatePresentation.Should().BeFalse("unlock alone cannot recover unavailable voice readiness");
                ui.Window.Should().BeSameAs(oldWindow);
                oldStatus.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                oldPanel.Result.Text.Should().BeEmpty();
                ui.Adapter.Calls.Should().Be(1);
                return;
            }
            if (ending is "call") { ui.Calls.Set(CallState.Clear); }
            if (ending is not "replace") { await ui.Command("independent agents"); }
            ui.Window.Should().NotBeSameAs(oldWindow);
            await ui.Review(1, ui.Second);
            var currentReview = ui.Window.Runs[0].Review.Text;
            oldPanel.Source.Text = "late source edit from retired window";
            Dispatcher.UIThread.Post(() => oldStatus.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)), DispatcherPriority.Normal);
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            ui.Window.Runs[0].Review.Text.Should().Be(currentReview);
            await ui.Invoke(1, ui.Second);
            ui.Adapter.Inner.Finish(ui.Second, "new panel's genuine B answer");
            await ui.Controller.ObserveOperation(1);
            var currentStatus = ui.Window.Runs[0].Status.Text;
            oldStatus.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            ui.Window.Runs[0].Status.Text.Should().Be(currentStatus);
            ui.Window.Runs[0].Result.Text.Should().Be("new panel's genuine B answer");
            oldPanel.Result.Text.Should().BeEmpty();
            ui.Adapter.Calls.Should().Be(2);
        });
    }

    private static Task WithRunSetAffinity(Func<RunSetAffinityUi, Task> test) => HeadlessSession.RunAsync(async () =>
    {
        using var storage = new InteractionStorageFixture();
        await storage.InitializeAsync();
        var targets = new List<SessionWorkspaceEntry>();
        for (var index = 0; index < 3; index++)
        {
            if (index != 0) { storage.Request = InteractionStorageFixture.NewRequest(); await storage.AdmitAsync(newSession: true); }
            await WindowsSqliteConversationTests.FinishInitial(storage);
            targets.Add(await storage.Store.ReadMetadataAsync(storage.Request.SessionId, storage.Token));
        }
        var adapter = new AffinityAdapter();
        await using var composed = new Composition(storage, adapter: adapter);
        var parent = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".net-test-artifacts"));
        Directory.CreateDirectory(parent);
        using var native = new NativeUxFixtureSession(parent);
        var calls = new AffinityCallState();
        await native.InitializeAsync(null, callState: calls);
        var readers = new List<AffinityReader>();
        var factory = new AgentRunSetFactory(composed.Turns, new RunSetQualification(storage.Time.GetUtcNow(), adapter), lane =>
        {
            var reader = new AffinityReader();
            readers.Add(reader);
            var conversation = new SessionConversationService(storage.Store, storage.Store, composed.Workspace, composed.Access,
                lane, storage.Time, NullLogger<SessionConversationService>.Instance, lane, composed.Audit);
            return new(reader, storage.Store, composed.Access, conversation, lane, composed.Audit, storage.Time);
        });
        var ui = new RunSetAffinityUi(storage, composed, native, adapter, readers, calls, targets);
        var controller = new AgentRunSetController(native.Main, factory, composed.Workspace,
            () => true, () => ui.OwnerRevision, NullLogger<AgentRunSetController>.Instance);
        ui.Controller = controller;
        controller.Bind();
        using var observation = Window.WindowOpenedEvent.AddClassHandler<AgentRunSetWindow>((window, _) => ui.Opened = window);
        try { await ui.Command("independent agents"); await test(ui); }
        finally { adapter.Inner.FinishAll(); await native.Main.ExitAsync(); }
    });

    private sealed class RunSetAffinityUi(InteractionStorageFixture storage, Composition composed, NativeUxFixtureSession native,
        AffinityAdapter adapter, List<AffinityReader> readers, AffinityCallState calls, List<SessionWorkspaceEntry> targets)
    {
        internal InteractionStorageFixture Storage => storage;
        internal Composition Composed => composed;
        internal NativeUxFixtureSession Native => native;
        internal AffinityAdapter Adapter => adapter;
        internal List<AffinityReader> Readers => readers;
        internal AffinityCallState Calls => calls;
        internal SessionWorkspaceEntry First => targets[0];
        internal SessionWorkspaceEntry Second => targets[1];
        internal SessionWorkspaceEntry Other => targets[2];
        internal long OwnerRevision { get; set; } = 1;
        internal AgentRunSetController Controller { get; set; } = null!;
        internal AgentRunSetWindow? Opened { get; set; }
        internal AgentRunSetWindow Window => Opened ?? throw new InvalidOperationException("No independent runs window.");
        internal async Task Command(string command)
        {
            native.Main.CommandText = command;
            await native.Main.RunTypedCommand.ExecuteAsync();
        }
        internal async Task Review(int number, SessionWorkspaceEntry target)
        {
            await Command($"agent run {number} targets");
            var panel = Window.Runs[number - 1];
            panel.Targets.SelectedItem = ((AgentRunSetWindow.Target[])panel.Targets.ItemsSource!)
                .Single(item => item.Entry.Authority.SessionId == target.Authority.SessionId);
            await Command($"agent run {number} source existing.agent.md");
            await Command($"agent run {number} request original {number} for {target.Authority.SessionId}");
            await Command($"agent run {number} review");
            panel.Confirm.IsEnabled.Should().BeTrue();
        }
        internal async Task Invoke(int number, SessionWorkspaceEntry target)
        {
            await Command($"agent run {number} confirm");
            await RequireRunSetStarted(Controller.ObserveOperation(number), adapter.Inner.Started(target).Task);
        }
        internal Button StatusButton(AgentRunSetWindow.RunPanel panel)
        {
            var index = Array.IndexOf(Window.Runs, panel);
            var tab = ((IEnumerable<TabItem>)Window.Selection.ItemsSource!).ElementAt(index);
            return ((StackPanel)((ScrollViewer)tab.Content!).Content!).Children.OfType<Button>()
                .Single(button => Equals(button.Content, "Status"));
        }
        internal void Edit(string edit)
        {
            var panel = Window.Runs[0];
            if (edit is "source") { panel.Source.Text = "different.agent.md"; }
            else if (edit is "task") { panel.Input.Text = "different original task"; }
            else
            {
                panel.Targets.SelectedItem = ((AgentRunSetWindow.Target[])panel.Targets.ItemsSource!)
                    .Single(item => item.Entry.Authority.SessionId == Second.Authority.SessionId);
            }
        }
    }

    private sealed class AffinityReader : IAgentProfileReader, IAgentProfileSelection
    {
        private readonly NativeAgentReader inner = new();
        internal int Reads { get; private set; }
        internal bool Released => inner.Released;
        public LocalFileMetadata Metadata => inner.Metadata;
        public Task<IAgentProfileSelection> OpenAsync(string path, CancellationToken token) => Task.FromResult<IAgentProfileSelection>(this);
        public Task<byte[]> ReadAsync(CancellationToken token) { Reads++; return inner.ReadAsync(token); }
        public void Dispose() => inner.Dispose();
    }

    private sealed class AffinityAdapter : IModelTurnAdapter
    {
        internal RunSetAdapter Inner { get; } = new();
        internal int Calls { get; private set; }
        internal TaskCompletionSource Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool IsQuiescent => Inner.IsQuiescent;
        public async Task<ModelProviderReply> RunAsync(ModelTurnProvenance provenance, ProviderProfileTuple? profile,
            ReadOnlyMemory<byte> context, Func<string, string, CancellationToken, ValueTask<CapabilityReply>> invokeTool, CancellationToken token)
        {
            Calls++;
            using var registration = token.Register(() => Cancelled.TrySetResult());
            return await Inner.RunAsync(provenance, profile, context, invokeTool, token);
        }
    }

    private sealed class AffinityCallState : ICallStateService
    {
        public event EventHandler<CallStateChangedEventArgs>? StateChanged;
        public CallState CurrentState { get; private set; } = CallState.Clear;
        internal void Set(CallState state)
        {
            CurrentState = state;
            StateChanged?.Invoke(this, new(state));
        }
    }
}
