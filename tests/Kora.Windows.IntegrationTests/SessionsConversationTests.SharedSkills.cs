using System.Text;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Interactivity;
using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Application.Hosting;
using Kora.Application.Skills;
using Kora.Core.Hosting;
using Kora.Core.Skills;
using Kora.NativeUxFixture;
using Kora.Windows.IntegrationTests.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
    [Fact]
    public async Task NativeSharedSkillReviewEnableExactTargetAndConfirmationRetainGenuineAnswerAndClearPrivateBuffers()
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
            var reader = new InvocationReader();
            var preferences = new LocalSharedSkillPreferences(native);
            var discovery = new SharedSkillDiscoveryService(preferences, reader, native.SharedAdmission, native.Audit,
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
                opened.Instructions.Text.Should().Contain(reader.Text);
                adapter.Calls.Should().Be(0);
                var buttons = ((StackPanel)((ScrollViewer)opened.Content!).Content!).Children.OfType<Button>().ToArray();
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
                wire.RootElement.GetProperty("UserRequest").GetString().Should().Be(body);
                wire.RootElement.GetProperty("Tools").GetArrayLength().Should().Be(0);
            }
            finally { opened.Close(); }
            opened.Instructions.Text.Should().BeEmpty();
            opened.Request.Text.Should().BeEmpty();
            opened.Confirmation.Text.Should().BeEmpty();
            opened.Status.Text.Should().BeEmpty();
        });
    }

    private sealed class InvocationReader : ISharedSkillSourceReader
    {
        internal SharedSkillSource Source { get; } = new(Guid.NewGuid(), ".agents\\skills", new string('a', 48));
        internal string Text => "---\nname: concise-review\nversion: 1.0.0\ndescription: Review prose\n---\nReview user prose concisely.";
        public ValueTask<SharedSkillSource> SelectAsync(string selectedRoot, CancellationToken cancellationToken) => ValueTask.FromResult(Source);
        public ValueTask<SharedSkillCatalogue> DiscoverAsync(SharedSkillSource source, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new SharedSkillCatalogue(Source,
                [SharedSkillSnapshot.Parse(Source.Id, "concise-review\\SKILL.md", Encoding.UTF8.GetBytes(Text), false)]));
    }
}
