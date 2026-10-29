using System.Text;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Interactivity;
using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Application.Hosting;
using Kora.Application.Skills;
using Kora.Core.Auditing;
using Kora.Core.Hosting;
using Kora.Core.Skills;
using Kora.NativeUxFixture;
using Kora.Windows.IntegrationTests.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
    [Theory]
    [InlineData(false, null)]
    [InlineData(true, null)]
    [InlineData(true, "declaration")]
    [InlineData(true, "capture")]
    public async Task NativeSharedSkillReviewRetainsGenuineAnswerOrJoinsExplicitReferenceStop(bool references, string? stopAt)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            f.Time.UseFrozenTimers = true;
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            var adapter = new HistoryAdapter();
            await using var composed = new Composition(f, adapter: adapter);
            var parent = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".net-test-artifacts"));
            Directory.CreateDirectory(parent);
            using var native = new NativeUxFixtureSession(parent);
            await native.InitializeAsync();
            var reader = new InvocationReader(references);
            var preferences = new LocalSharedSkillPreferences(native);
            var discovery = new SharedSkillDiscoveryService(preferences, reader, native.SharedAdmission, composed.Audit,
                NullLogger<SharedSkillDiscoveryService>.Instance);
            await discovery.LoadSourcesAsync(static () => true, native.Token);
            await discovery.RegisterAsync("native selection", static () => true, native.Token);
            await using var use = new SharedSkillUseAdmission(native.Interactions, native.Interactions, new(native.Tasks));
            var workflow = new SharedSkillInvocationWorkflow(discovery, use, composed.Access, f.Store, composed.Workspace,
                composed.Conversation, composed.Audit, f.Time);
            using var packages = new SkillPackagesWindowController(native.Main, static () => true,
                NullLogger<SkillPackagesWindowController>.Instance, new(NullLogger<NativeDetailRenderer>.Instance),
                discovery, NullLogger<SharedSkillSourcesWindowController>.Instance);
            using var controller = new SharedSkillInvocationController(native.Main, workflow, packages,
                static () => true, static () => 1, NullLogger<SharedSkillInvocationController>.Instance);
            controller.Bind();
            SharedSkillSourcesWindow? inspector = null;
            SharedSkillInvocationWindow? invocation = null;
            var useOpened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var observed = Window.WindowOpenedEvent.AddClassHandler<SharedSkillSourcesWindow>((window, _) => inspector = window);
            using var reviewed = Window.WindowOpenedEvent.AddClassHandler<SharedSkillInvocationWindow>((window, _) =>
            { invocation = window; useOpened.SetResult(); });
            packages.TryOpen(SkillReviewSurface.SharedSources, static () => true).Should().BeTrue();
            var sources = inspector ?? throw new InvalidOperationException("No admitted source inspector.");
            var refresh = sources.FindControl<Button>("SearchSharedPackages")!;
            if (!refresh.IsEnabled)
            {
                var refreshed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                refresh.PropertyChanged += (_, args) =>
                { if (args.Property == Avalonia.Input.InputElement.IsEnabledProperty && refresh.IsEnabled) { refreshed.TrySetResult(); } };
                await refreshed.Task.WaitAsync(native.Token);
            }
            sources.FindControl<ComboBox>("SourceSelector")!.SelectedIndex = 0;
            sources.SetCatalogue(await discovery.DiscoverAsync(reader.Source, static () => true, native.Token));
            sources.FindControl<Button>("ReviewInstructionUse")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await useOpened.Task.WaitAsync(native.Token);
            var opened = invocation ?? throw new InvalidOperationException("No reviewed-use window.");
            try
            {
                var buttons = ((StackPanel)((ScrollViewer)opened.Content!).Content!).Children.OfType<Button>().ToArray();
                var stop = buttons.Single(button => Equals(button.Content, "Stop and close reviewed use"));
                if (references)
                {
                    reader.ReferenceReads.Should().Be(0);
                    opened.Instructions.Text.Should().Contain("notes.md").And.NotContain(reader.Reference);
                    if (stopAt is "declaration")
                    {
                        stop.IsEnabled.Should().BeTrue();
                        stop.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        await controller.ActiveOperation;
                        reader.ReferenceReads.Should().Be(0);
                        adapter.Calls.Should().Be(0);
                        (await f.Store.ReadConversationPageAsync(f.Request.SessionId, null, 25, f.Token)).Messages.Should().BeEmpty();
                        return;
                    }
                    reader.PauseReferences = stopAt is "capture";
                    opened.ReadReferences.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    if (stopAt is "capture")
                    {
                        var reading = controller.ActiveOperation;
                        try
                        {
                            await reader.ReferenceReadEntered.Task.WaitAsync(native.Token);
                            stop.IsEnabled.Should().BeTrue();
                            stop.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                            reader.ReferenceReadToken.IsCancellationRequested.Should().BeTrue();
                            reading.IsCompleted.Should().BeFalse();
                            reader.ReferenceReadJoined.Should().BeFalse();
                            opened.IsVisible.Should().BeFalse();
                            opened.Instructions.Text.Should().BeEmpty();
                            opened.Confirm.IsEnabled.Should().BeFalse();
                        }
                        finally
                        {
                            reader.ReleaseReferences.TrySetResult();
                            await reading;
                        }
                        reader.ReferenceReadJoined.Should().BeTrue();
                        reader.ReferenceReads.Should().Be(1);
                        adapter.Calls.Should().Be(0);
                        (await f.Store.ReadConversationPageAsync(f.Request.SessionId, null, 25, f.Token)).Messages.Should().BeEmpty();
                        composed.Audit.Events.Where(item => string.Equals(item.ActionId, "skills.references.read", StringComparison.Ordinal))
                            .Select(item => item.Outcome).Should().Equal(
                                SecurityAuditOutcome.Requested, SecurityAuditOutcome.Failed);
                        return;
                    }
                    await controller.ActiveOperation;
                    opened.Status.Text.Should().Contain("separately enable");
                    opened.Instructions.Text.Should().Contain(reader.Reference);
                }
                opened.Instructions.Text.Should().Contain(reader.Text);
                adapter.Calls.Should().Be(0);
                buttons.Single(button => Equals(button.Content, "Enable exact reviewed revision")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await controller.ActiveOperation;
                opened.Status.Text.Should().Contain("enabled");
                buttons.Single(button => Equals(button.Content, "Refresh existing Active conversations")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await controller.ActiveOperation;
                opened.Targets.SelectedIndex = 0;
                const string body = "  original native prose e\u0301\r\n unchanged  ";
                opened.Request.Text = body;
                buttons.Single(button => Equals(button.Content, "Review exact invocation")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await controller.ActiveOperation;
                opened.Confirm.IsEnabled.Should().BeTrue();
                opened.Confirmation.Text.Should().Contain(body);
                adapter.Calls.Should().Be(0);
                opened.Confirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await controller.ActiveOperation;
                adapter.Calls.Should().Be(1);
                opened.Status.Text.Should().Contain("Succeeded").And.Contain("earlier ASSISTANT");
                var message = (await f.Store.ReadConversationPageAsync(f.Request.SessionId, null, 25, f.Token)).Messages.Single();
                message.Body.Should().Be(body);
                message.AssistantResponse!.Body.Should().Be("earlier ASSISTANT");
                using var wire = JsonDocument.Parse(adapter.Wire!);
                wire.RootElement.GetProperty("Evidence")[0].GetProperty("Content").GetString().Should().Be(reader.Text);
                wire.RootElement.GetProperty("Evidence").GetArrayLength().Should().Be(references ? 2 : 1);
                if (references)
                { wire.RootElement.GetProperty("Evidence")[1].GetProperty("Content").GetString().Should().Contain(reader.Reference); }
                wire.RootElement.GetProperty("UserRequest").GetString().Should().Be(body);
                wire.RootElement.GetProperty("Tools").GetArrayLength().Should().Be(0);
            }
            finally
            {
                opened.Close();
                opened.Instructions.Text.Should().BeEmpty();
                opened.Request.Text.Should().BeEmpty();
                opened.Confirmation.Text.Should().BeEmpty();
                opened.Status.Text.Should().BeEmpty();
            }
        });
    }

    private sealed class InvocationReader(bool references = false) : ISharedSkillSourceReader
    {
        internal SharedSkillSource Source { get; } = new(Guid.NewGuid(), ".agents\\skills", new string('a', 48));
        internal string Text => "---\nname: concise-review\nversion: 1.0.0\ndescription: Review prose\n"
            + (references ? "format: skill-md-instructions-v2\nreferences: notes.md\n" : "")
            + "---\nReview user prose concisely.";
        internal string Reference => "# Notes\nExplain the exact original prose.";
        internal int ReferenceReads { get; private set; }
        internal bool PauseReferences { get; set; }
        internal TaskCompletionSource ReferenceReadEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource ReleaseReferences { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal CancellationToken ReferenceReadToken { get; private set; }
        internal bool ReferenceReadJoined { get; private set; }
        private SharedSkillSnapshot Snapshot()
        {
            var snapshot = SharedSkillSnapshot.Parse(Source.Id, "concise-review\\SKILL.md", Encoding.UTF8.GetBytes(Text),
                references, references ? ["concise-review\\notes.md"] : []);
            return references ? snapshot.WithPackageDirectoryIdentity(new string('b', 48)) : snapshot;
        }
        public ValueTask<SharedSkillSource> SelectAsync(string selectedRoot, CancellationToken cancellationToken) => ValueTask.FromResult(Source);
        public ValueTask<SharedSkillCatalogue> DiscoverAsync(SharedSkillSource source, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new SharedSkillCatalogue(Source,
                [Snapshot()]));
        public async ValueTask<SharedSkillSnapshot> ReadReferencesAsync(SharedSkillSource source, SharedSkillSnapshot primary, CancellationToken cancellationToken)
        {
            ReferenceReads++;
            ReferenceReadToken = cancellationToken;
            ReferenceReadEntered.TrySetResult();
            if (PauseReferences) { await ReleaseReferences.Task; }
            var snapshot = Snapshot();
            ReferenceReadJoined = true;
            return snapshot.WithReferenceBundle(SharedSkillReferenceBundle.Create(snapshot,
                new Dictionary<string, byte[]>(StringComparer.Ordinal) { ["notes.md"] = Encoding.UTF8.GetBytes(Reference) }));
        }
    }
}
