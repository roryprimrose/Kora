using System.Text;
using Avalonia.Controls;
using Avalonia.Interactivity;
using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Application.Dependencies;
using Kora.Application.Hosting;
using Kora.Application.Skills;
using Kora.Core.Authorization;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Network;
using Kora.Core.Skills;
using Kora.NativeUxFixture;
using Kora.Windows.IntegrationTests.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task NativeToolRevisionReviewRequiresSeparateEnablementAndExactActionApprovalBeforeRetention(bool available, bool deny)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            var adapter = new HistoryAdapter();
            await using var transport = new PageModelTransport();
            await using var bounded = new BoundedModelTurnAdapter(transport, f.Time, NullLogger<BoundedModelTurnAdapter>.Instance);
            var network = new PageNetworkTransport();
            await using var composed = new Composition(f, adapter: available ? bounded : adapter,
                pageTransport: available ? network : null);
            var parent = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".net-test-artifacts"));
            Directory.CreateDirectory(parent);
            using var native = new NativeUxFixtureSession(parent);
            await native.InitializeAsync();
            using var questionHost = new NativeQuestionHost(f.Store, f.Time, NullLogger<NativeQuestionViewModel>.Instance);
            using var questions = new QuestionWindowController(native.Main, questionHost, native.VersionQuery, new HostBoundary(),
                new(NullLogger<NativeDetailRenderer>.Instance), static () => true,
                NullLogger<QuestionWindowController>.Instance, static () => 1);
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
                static () => true, static () => 1, NullLogger<SharedSkillInvocationController>.Instance, toolWorkflow, questions);
            controller.Bind();
            SharedSkillSourcesWindow? inspector = null;
            SharedSkillInvocationWindow? invocation = null;
            var useOpened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var approvalOpened = new TaskCompletionSource<QuestionWindow>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var approvalObserved = Window.WindowOpenedEvent.AddClassHandler<QuestionWindow>((window, _) =>
                approvalOpened.TrySetResult(window));
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
                if (available)
                {
                    await Task.WhenAny(approvalOpened.Task, controller.ActiveOperation).WaitAsync(TimeSpan.FromSeconds(60), native.Token);
                    approvalOpened.Task.IsCompleted.Should().BeTrue(opened.Status.Text);
                    var approval = await approvalOpened.Task;
                    approval.FindControl<TextBlock>("PromptLabel")!.Text.Should().Contain("https://example.com/");
                    approval.FindControl<TextBlock>("TargetLabel")!.Text.Should().Contain(f.Request.SessionId.Value.ToString("D"));
                    if (deny) { approval.FindControl<Button>("CancelQuestion")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
                    else
                    {
                        var review = approval.FindControl<Button>("ReviewQuestion")!;
                        review.IsEnabled.Should().BeTrue(approval.FindControl<TextBlock>("QuestionStatus")!.Text);
                        var exactReview = approval.FindControl<TextBox>("ExactReview")!;
                        var reviewedOperation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                        void Reviewed(object? sender, Avalonia.AvaloniaPropertyChangedEventArgs args)
                        {
                            if (args.Property == TextBox.TextProperty && !string.IsNullOrEmpty(exactReview.Text))
                            { reviewedOperation.TrySetResult(); }
                        }
                        exactReview.PropertyChanged += Reviewed;
                        try
                        {
                            review.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                            await Task.WhenAny(reviewedOperation.Task, controller.ActiveOperation).WaitAsync(TimeSpan.FromSeconds(60), native.Token);
                            reviewedOperation.Task.IsCompleted.Should().BeTrue(
                                $"invocation: {opened.Status.Text}; question: {approval.FindControl<TextBlock>("QuestionStatus")!.Text}");
                        }
                        finally { exactReview.PropertyChanged -= Reviewed; }
                        approval.FindControl<StackPanel>("ChoicePanel")!.Children.OfType<RadioButton>().Single().IsChecked = true;
                        var submit = approval.FindControl<Button>("SubmitAnswer")!;
                        submit.IsEnabled.Should().BeTrue();
                        submit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    }
                }
                await controller.ActiveOperation.WaitAsync(TimeSpan.FromSeconds(30), native.Token);
                adapter.Calls.Should().Be(0);
                var retained = await f.Store.ReadConversationPageAsync(f.Request.SessionId, null, 25, f.Token);
                if (available)
                {
                    opened.Status.Text.Should().Contain(deny ? "Denied" : "Succeeded");
                    var message = retained.Messages.Should().ContainSingle().Subject;
                    message.Request.SessionId.Should().Be(f.Request.SessionId);
                    message.Body.Should().Be(body);
                    message.Outcome.Should().Be(deny ? ModelTurnOutcome.Denied : ModelTurnOutcome.Succeeded);
                    (message.AssistantResponse?.Body).Should().Be(deny ? null : "Genuine local summary of the approved public page.");
                    transport.Requests.Should().HaveCount(deny ? 1 : 2);
                    transport.Requests[0].Context.Evidence[0].Content.Should().Be(reader.Text);
                    network.Sends.Should().Be(deny ? 0 : 1);
                    var grants = await f.Store.ReadGrantsAsync(f.Token);
                    if (deny) { grants.Should().BeEmpty(); }
                    else { grants.Should().ContainSingle().Which.Status.Should().Be(OperationGrantStatus.Consumed); }
                }
                else
                {
                    opened.Status.Text.Should().Contain("Unavailable");
                    retained.Messages.Should().BeEmpty();
                    transport.Requests.Should().BeEmpty();
                    network.Sends.Should().Be(0);
                }
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
