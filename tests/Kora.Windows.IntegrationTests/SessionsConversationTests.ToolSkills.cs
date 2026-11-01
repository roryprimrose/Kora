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
    private static readonly string[] ToolSkillInputFields = ["request", "first", "second", "target"];
    [Theory]
    [InlineData(false, false, false, false)]
    [InlineData(true, false, false, false)]
    [InlineData(true, true, false, false)]
    [InlineData(true, false, true, false)]
    [InlineData(true, true, true, false)]
    [InlineData(true, false, true, true)]
    public async Task NativeToolRevisionReviewRequiresSeparateEnablementAndExactActionApprovalBeforeRetention(bool available, bool deny, bool twoSources, bool stop)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            f.Time.UseFrozenTimers = true;
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            var adapter = new HistoryAdapter();
            var pendingSecond = new TaskCompletionSource<ModelToolTransportReply>(TaskCreationOptions.RunContinuationsAsynchronously);
            var secondEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            await using var transport = new PageModelTransport { TwoSources = twoSources,
                PendingSecond = stop ? pendingSecond.Task : null, SecondEntered = secondEntered };
            await using var bounded = new BoundedModelTurnAdapter(transport, f.Time, NullLogger<BoundedModelTurnAdapter>.Instance);
            var network = new PageNetworkTransport();
            await using var composed = new Composition(f, adapter: available ? bounded : adapter,
                pageTransport: available ? network : null);
            var parent = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".net-test-artifacts"));
            Directory.CreateDirectory(parent);
            using var native = new NativeUxFixtureSession(parent, timeProvider: f.Time);
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
                Avalonia.Automation.AutomationProperties.GetName(opened.SecondAddress).Should().Contain("Second explicit");
                Avalonia.Automation.AutomationProperties.GetLiveSetting(opened.Status)
                    .Should().Be(Avalonia.Automation.AutomationLiveSetting.Polite);
                var statusPeer = Avalonia.Automation.Peers.ControlAutomationPeer.CreatePeerForElement(opened.Status)!;
                statusPeer.GetName().Should().Contain("Reviewed skill status and recovery").And.Contain(opened.Status.Text);
                var buttons = ((StackPanel)((ScrollViewer)opened.Content!).Content!).Children.OfType<Button>().ToArray();
                async Task Click(string label)
                {
                    buttons.Single(button => Equals(button.Content, label)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await controller.ActiveOperation;
                }
                async Task RequireEnabled(Button button)
                {
                    if (button.IsEnabled) { return; }
                    var enabled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    void Changed(object? sender, Avalonia.AvaloniaPropertyChangedEventArgs args)
                    {
                        if (args.Property == Avalonia.Input.InputElement.IsEnabledProperty && button.IsEnabled)
                        { enabled.TrySetResult(); }
                    }
                    button.PropertyChanged += Changed;
                    try
                    {
                        if (button.IsEnabled) { enabled.TrySetResult(); }
                        await Task.WhenAny(enabled.Task, controller.ActiveOperation).WaitAsync(native.Token);
                        enabled.Task.IsCompleted.Should().BeTrue(opened.Status.Text);
                    }
                    finally { button.PropertyChanged -= Changed; }
                }
                await Click("Refresh existing Active conversations");
                opened.Targets.SelectedIndex = 0;
                const string body = "  original tool request e\u0301\r\n unchanged ";
                opened.Request.Text = body;
                opened.Address.Text = "https://example.com/";
                if (twoSources) { opened.SecondAddress.Text = "https://second.example/"; }
                await Click("Review exact target and tool revision");
                adapter.Calls.Should().Be(0);
                await Click("Enable exact reviewed revision");
                opened.Status.Text.Should().Contain("authorization");
                await Click("Review exact invocation");
                opened.Confirm.IsEnabled.Should().BeTrue();
                opened.Confirmation.Text.Should().Contain(body).And.Contain(twoSources ? "TWO host WebPageGet call" : "ONE host WebPageGet call");
                if (twoSources) { opened.Confirmation.Text.Should().Contain("https://second.example/").And.Contain("3 model rounds"); }
                if (twoSources && !deny)
                {
                    foreach (var changed in ToolSkillInputFields)
                    {
                        var selected = opened.Targets.SelectedItem;
                        if (changed is "request") { opened.Request.Text = "changed comparison"; }
                        if (changed is "first") { opened.Address.Text = "https://changed.example/"; }
                        if (changed is "second") { opened.SecondAddress.Text = "https://changed.example/"; }
                        if (changed is "target") { opened.Targets.SelectedItem = null; }
                        opened.Confirm.IsEnabled.Should().BeFalse();
                        opened.Confirmation.Text.Should().BeEmpty();
                        opened.Request.Text = body;
                        opened.Address.Text = "https://example.com/";
                        opened.SecondAddress.Text = "https://second.example/";
                        opened.Targets.SelectedItem = selected;
                        await Click("Review exact target and tool revision");
                        await Click("Enable exact reviewed revision");
                        await Click("Review exact invocation");
                        opened.Confirm.IsEnabled.Should().BeTrue(opened.Status.Text);
                        network.Sends.Should().Be(0);
                    }
                }
                opened.Confirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if (available)
                {
                    for (var page = 0; page < (twoSources ? 2 : 1); page++)
                    {
                    if (stop && page == 1)
                    {
                        await secondEntered.Task.WaitAsync(native.Token);
                        var running = controller.ActiveOperation;
                        buttons.Single(button => Equals(button.Content, "Stop and close reviewed use"))
                            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        running.IsCompleted.Should().BeFalse();
                        bounded.IsQuiescent.Should().BeFalse();
                        pendingSecond.SetResult(new(ModelTurnOutcome.Succeeded, transport.Requests[1].Provenance,
                            ModelToolProtocol.ParseAssistant("""{"kind":"answer","answer":"late forbidden answer"}"""u8)));
                        await running.WaitAsync(native.Token);
                        bounded.IsQuiescent.Should().BeTrue();
                        opened.IsVisible.Should().BeFalse();
                        break;
                    }
                    await Task.WhenAny(approvalOpened.Task, controller.ActiveOperation).WaitAsync(TimeSpan.FromSeconds(60), native.Token);
                    approvalOpened.Task.IsCompleted.Should().BeTrue(opened.Status.Text);
                    var approval = await approvalOpened.Task;
                    approvalOpened = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    approval.FindControl<TextBlock>("PromptLabel")!.Text.Should().Contain(page == 0 ? "https://example.com/" : "https://second.example/");
                    approval.FindControl<TextBlock>("TargetLabel")!.Text.Should().Contain(f.Request.SessionId.Value.ToString("D"));
                    if (deny && page == (twoSources ? 1 : 0)) { approval.FindControl<Button>("CancelQuestion")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
                    else
                    {
                        var review = approval.FindControl<Button>("ReviewQuestion")!;
                        await RequireEnabled(review);
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
                        await RequireEnabled(review);
                        approval.FindControl<StackPanel>("ChoicePanel")!.Children.OfType<RadioButton>()
                            .Single(choice => Equals(choice.Content, "Once")).IsChecked = true;
                        var submit = approval.FindControl<Button>("SubmitAnswer")!;
                        await RequireEnabled(submit);
                        submit.IsEnabled.Should().BeTrue();
                        submit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    }
                    }
                }
                await controller.ActiveOperation.WaitAsync(TimeSpan.FromSeconds(30), native.Token);
                adapter.Calls.Should().Be(0);
                var retained = await f.Store.ReadConversationPageAsync(f.Request.SessionId, null, 25, f.Token);
                if (stop)
                {
                    var message = retained.Messages.Should().ContainSingle().Subject;
                    message.AssistantResponse.Should().BeNull();
                    message.Outcome.Should().NotBe(ModelTurnOutcome.Succeeded);
                    network.Sends.Should().Be(1);
                    transport.Requests.Should().HaveCount(2);
                    opened.Confirmation.Text.Should().BeEmpty();
                    return;
                }
                if (available)
                {
                    opened.Status.Text.Should().Contain(deny ? "Denied" : "Succeeded");
                    var message = retained.Messages.Should().ContainSingle().Subject;
                    message.Request.SessionId.Should().Be(f.Request.SessionId);
                    message.Body.Should().Be(body);
                    message.Outcome.Should().Be(deny ? ModelTurnOutcome.Denied : ModelTurnOutcome.Succeeded);
                    (message.AssistantResponse?.Body).Should().Be(deny ? null : twoSources
                        ? "Genuine local comparison of both approved public pages." : "Genuine local summary of the approved public page.");
                    transport.Requests.Should().HaveCount(twoSources ? deny ? 2 : 3 : deny ? 1 : 2);
                    transport.Requests[0].Context.Evidence[0].Content.Should().Be(reader.Text);
                    network.Sends.Should().Be(twoSources ? deny ? 1 : 2 : deny ? 0 : 1);
                    var grants = await f.Store.ReadGrantsAsync(f.Token);
                    grants.Should().HaveCount(twoSources ? deny ? 1 : 2 : deny ? 0 : 1);
                    grants.Should().OnlyContain(grant => grant.Status == OperationGrantStatus.Consumed);
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
