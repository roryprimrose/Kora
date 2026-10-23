using System.Text;
using Avalonia.Controls;
using Avalonia.Interactivity;
using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Application.Hosting;
using Kora.Application.Skills;
using Kora.Core.Network;
using Kora.Core.Skills;
using Kora.NativeUxFixture;
using Kora.Windows.IntegrationTests.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
    [Fact]
    public async Task NativeToolRevisionReviewRequiresSeparateEnablementAndComposedActionBeforeRetention()
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
            var reader = new ToolInvocationReader();
            var discovery = new SharedSkillDiscoveryService(new LocalSharedSkillPreferences(native), reader, native.SharedAdmission, native.Audit,
                NullLogger<SharedSkillDiscoveryService>.Instance);
            await discovery.LoadSourcesAsync(static () => true, native.Token);
            await discovery.RegisterAsync("native selection", static () => true, native.Token);
            await using var use = new SharedSkillUseAdmission(native.Interactions, native.Interactions, new(native.Tasks));
            var instructionWorkflow = new SharedSkillInvocationWorkflow(discovery, use, composed.Access, f.Store, composed.Workspace,
                composed.Conversation, composed.Audit, f.Time);
            var toolWorkflow = new ReviewedToolSkillInvocationWorkflow(discovery, use, composed.Access, f.Store,
                composed.Conversation, composed.Audit, f.Time);
            using var packages = new SkillPackagesWindowController(native.Main, static () => true,
                NullLogger<SkillPackagesWindowController>.Instance, new(NullLogger<NativeDetailRenderer>.Instance),
                discovery, NullLogger<SharedSkillSourcesWindowController>.Instance);
            using var controller = new SharedSkillInvocationController(native.Main, instructionWorkflow, packages,
                static () => true, static () => 1, NullLogger<SharedSkillInvocationController>.Instance, toolWorkflow);
            controller.Bind();
            SharedSkillSourcesWindow? inspector = null;
            SharedSkillInvocationWindow? invocation = null;
            var useOpened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var observed = Window.WindowOpenedEvent.AddClassHandler<SharedSkillSourcesWindow>((window, _) => inspector = window);
            using var reviewed = Window.WindowOpenedEvent.AddClassHandler<SharedSkillInvocationWindow>((window, _) =>
            { invocation = window; useOpened.SetResult(); });
            packages.TryOpen(SkillReviewSurface.SharedSources, static () => true).Should().BeTrue();
            var sources = inspector ?? throw new InvalidOperationException("No source inspector.");
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
            sources.FindControl<Button>("ReviewToolUse")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await useOpened.Task.WaitAsync(native.Token);
            var opened = invocation ?? throw new InvalidOperationException("No tool skill review.");
            try
            {
                opened.Title.Should().Be("Reviewed one-tool shared skill");
                opened.Instructions.Text.Should().Contain(reader.Text);
                var buttons = ((StackPanel)((ScrollViewer)opened.Content!).Content!).Children.OfType<Button>().ToArray();
                async Task Click(string label)
                {
                    buttons.Single(button => Equals(button.Content, label)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await controller.ActiveOperation;
                }
                await Click("Refresh existing Active conversations");
                opened.Targets.SelectedIndex = 0;
                await Click("Review exact target and tool revision");
                adapter.Calls.Should().Be(0);
                await Click("Enable exact reviewed revision");
                opened.Status.Text.Should().Contain("authorization");
                const string body = "  original tool request e\u0301\r\n unchanged ";
                opened.Request.Text = body;
                opened.Address.Text = "https://example.com/";
                await Click("Review exact invocation");
                opened.Confirm.IsEnabled.Should().BeTrue();
                opened.Confirmation.Text.Should().Contain(body).And.Contain("ONE host WebPageGet call");
                opened.Confirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await controller.ActiveOperation;
                opened.Status.Text.Should().Contain("Unavailable");
                adapter.Calls.Should().Be(0);
                (await f.Store.ReadConversationPageAsync(f.Request.SessionId, null, 25, f.Token)).Messages.Should().BeEmpty();
            }
            finally { opened.Close(); }
            opened.Instructions.Text.Should().BeEmpty();
            opened.Request.Text.Should().BeEmpty();
            opened.Confirmation.Text.Should().BeEmpty();
        });
    }

    private sealed class ToolInvocationReader : ISharedSkillSourceReader
    {
        internal SharedSkillSource Source { get; } = new(Guid.NewGuid(), ".agents\\skills", new string('a', 48));
        internal string Text => $"---\nname: page-guide\nversion: 1.0.0\ndescription: Review a page\nallowed-tools: {WebPageCapability.Id}\n---\nUse the host-approved page observation to answer the original request.";
        public ValueTask<SharedSkillSource> SelectAsync(string selectedRoot, CancellationToken cancellationToken) => ValueTask.FromResult(Source);
        public ValueTask<SharedSkillCatalogue> DiscoverAsync(SharedSkillSource source, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new SharedSkillCatalogue(Source,
                [SharedSkillSnapshot.Parse(Source.Id, "page-guide\\SKILL.md", Encoding.UTF8.GetBytes(Text), false)]));
    }
}
