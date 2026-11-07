using System.Security.Cryptography;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Application.Dependencies;
using Kora.Application.Mcp;
using Kora.Application.Skills;
using Kora.Application.UnitTests.Configuration;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Mcp;
using Kora.Core.Skills;
using Kora.NativeUxFixture;
using Kora.Windows.IntegrationTests.Storage;
using Kora.Windows.Skills;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
    [Theory]
    [InlineData("success")]
    [InlineData("cancel")]
    [InlineData("source")]
    [InlineData("edit")]
    public async Task OwnedMcpNativeUseReviewsExactReopenedSourceAndJoinsOneInvocation(string fault)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            f.Time.UseFrozenTimers = true;
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            var file = Path.Combine(f.Paths.RoamingRoot, "Mcp", "mcp.json");
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, """{"servers":{"reference":{"url":"https://example.com/mcp"}}}""");
            var preferences = new NativeMcpPreferences();
            var configuration = new McpServerConfigurationService(f.Paths, preferences, Path.Combine(f.Paths.RoamingRoot, "test-profile"));
            var server = configuration.Load().Single();
            preferences.Value = new([], [], [], [], [], [server.Id]);
            using var broker = new McpTransportFixture();
            var revision = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(file)));
            var policy = new McpReadOnlyPolicy([new(broker.Account.Destination, broker.Account, revision,
                "search", new McpToolSchema(NativeMcpSchema).Digest)]);
            await using var model = new NativeMcpModel();
            await using var bounded = new BoundedModelTurnAdapter(model, f.Time, NullLogger<BoundedModelTurnAdapter>.Instance);
            await using var composed = new Composition(f, adapter: bounded, mcpConfiguration: configuration, mcpBroker: broker.Broker, mcpEffects: policy);
            var parent = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".net-test-artifacts"));
            Directory.CreateDirectory(parent);
            using var native = new NativeUxFixtureSession(parent, timeProvider: f.Time);
            await native.InitializeAsync();
            using var questionHost = new NativeQuestionHost(f.Store, f.Time, NullLogger<NativeQuestionViewModel>.Instance);
            using var questions = new QuestionWindowController(native.Main, questionHost, native.VersionQuery, new HostBoundary(),
                new(NullLogger<NativeDetailRenderer>.Instance), static () => true, NullLogger<QuestionWindowController>.Instance, static () => 1);
            var reader = new McpSkillReader(server.Id, revision);
            var discovery = new SharedSkillDiscoveryService(new LocalSharedSkillPreferences(native), reader, native.SharedAdmission, native.Audit,
                NullLogger<SharedSkillDiscoveryService>.Instance);
            await using var sharedAdmission = new SharedSkillUseAdmission(native.Interactions, native.Interactions, new(native.Tasks));
            var tools = new ReviewedToolSkillInvocationWorkflow(discovery, sharedAdmission, composed.Access, f.Store, composed.Conversation, composed.Audit, f.Time);
            var store = new WindowsOwnedDeclarativeSkillStore(native);
            var document = OwnedDeclarativeSkillDocument.CreateMcp("reference", "1.0.0", "Reference", "Review reference", "Owned inert guidance",
                new(server.Id, revision, "search", new McpToolSchema(NativeMcpSchema).Digest));
            await store.Publish(document, static () => true, commit => Task.FromResult(commit()), native.Token);
            await using var ownedAdmission = new OwnedSkillControlAdmission(native.Interactions, native.Interactions, new(native.Tasks));
            var workflow = new OwnedSkillInvocationWorkflow(store, ownedAdmission, composed.Access, f.Store, composed.Workspace,
                composed.Conversation, native.Audit, f.Time, new MarkdownUsePolicy());
            await using var controller = new OwnedSkillInvocationController(native.Main, workflow, static () => true, static () => 1,
                NullLogger<OwnedSkillInvocationController>.Instance, tools, questions);
            controller.Bind();
            SharedSkillInvocationWindow? opened = null;
            var nextQuestion = new TaskCompletionSource<QuestionWindow>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var useOpened = Window.WindowOpenedEvent.AddClassHandler<SharedSkillInvocationWindow>((window, _) => opened = window);
            using var questionOpened = Window.WindowOpenedEvent.AddClassHandler<QuestionWindow>((window, _) =>
            {
                var signal = nextQuestion;
                window.Activated += (_, _) => signal.TrySetResult(window);
            });
            async Task Command(string operation, string? literal = null) => await controller.Handle(
                new(operation, Literal: literal), new(RequestOrigin.LocalUi, static () => true), static () => true);
            async Task Ready(Control control)
            {
                if (control.IsEnabled) { return; }
                var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                void Changed(object? sender, Avalonia.AvaloniaPropertyChangedEventArgs args)
                { if (args.Property == Avalonia.Input.InputElement.IsEnabledProperty && control.IsEnabled) { signal.TrySetResult(); } }
                control.PropertyChanged += Changed;
                try
                {
                    if (control.IsEnabled) { return; }
                    await Task.WhenAny(signal.Task, controller.ActiveOperation).WaitAsync(native.Token);
                    control.IsEnabled.Should().BeTrue(opened?.Status.Text);
                }
                finally { control.PropertyChanged -= Changed; }
            }
            await controller.Handle(new("use", document.Id, document.Version),
                new(RequestOrigin.LocalUi, static () => true), static () => true);
            var window = opened ?? throw new InvalidOperationException("Owned MCP review did not open.");
            window.Instructions.Text.Should().Be(document.CompleteText);
            await Command("targets");
            window.Targets.SelectedIndex = 0;
            await Command("mcp-options");
            window.McpServers.SelectedItem.Should().BeNull();
            window.McpAccounts.SelectedItem.Should().BeNull();
            await Command("mcp-server", server.Id);
            await Command("mcp-account", broker.Account.AccountId.ToString("D"));
            await Command("request", "Summarize the exact reference with this skill.");
            await Command("mcp-arguments", """{"query":"reference"}""");
            await Command("review-tool");
            await Command("enable");
            await Command("review-use");
            window.Confirm.IsEnabled.Should().BeTrue(window.Status.Text);
            window.Confirmation.Text.Should().Contain(document.ManifestText);
            broker.Connected.Should().BeFalse();
            if (fault is "edit")
            {
                window.McpArguments.Text = """{"query":"changed"}""";
                window.Confirm.IsEnabled.Should().BeFalse();
                window.Confirmation.Text.Should().BeEmpty();
                await controller.CloseAndWait();
                broker.Connected.Should().BeFalse();
                return;
            }
            if (fault is "source")
            {
                File.AppendAllText(Path.Combine(store.Destination(document), "instructions.md"), "changed");
                await Command("invoke");
                broker.Connected.Should().BeFalse();
                window.Status.Text.Should().Contain("unavailable or unconfirmed");
                return;
            }
            var invoke = Command("invoke");
            for (var stage = 0; stage < 5; stage++)
            {
                await Task.WhenAny(nextQuestion.Task, invoke).WaitAsync(native.Token);
                nextQuestion.Task.IsCompleted.Should().BeTrue(window.Status.Text);
                var question = await nextQuestion.Task;
                nextQuestion = new(TaskCreationOptions.RunContinuationsAsynchronously);
                if (fault is "cancel" && stage == 3)
                {
                    var cancel = question.FindControl<Button>("CancelQuestion")!;
                    await Ready(cancel);
                    cancel.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    break;
                }
                if (stage != 1)
                {
                    var review = question.FindControl<Button>("ReviewQuestion")!;
                    await Ready(review);
                    var exact = question.FindControl<TextBox>("ExactReview")!;
                    var displayed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    exact.PropertyChanged += (_, args) =>
                    { if (args.Property == TextBox.TextProperty && !string.IsNullOrEmpty(exact.Text)) { displayed.TrySetResult(); } };
                    review.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await Task.WhenAny(displayed.Task, invoke).WaitAsync(native.Token);
                    displayed.Task.IsCompleted.Should().BeTrue(window.Status.Text);
                    exact.Text.Should().Contain("owned:reference@1.0.0").And.Contain(document.RevisionDigest);
                }
                var choice = question.FindControl<StackPanel>("ChoicePanel")!.Children.OfType<RadioButton>().Single(button =>
                    Equals(button.Content, stage == 1 ? "search" : stage == 4 ? "Consent to one local continuation" : "Once"));
                await Ready(choice);
                choice.IsChecked = true;
                var submit = question.FindControl<Button>("SubmitAnswer")!;
                await Ready(submit);
                var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                question.Closed += (_, _) => closed.TrySetResult();
                submit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Task.WhenAny(closed.Task, invoke).WaitAsync(native.Token);
                closed.Task.IsCompleted.Should().BeTrue(window.Status.Text);
            }
            await invoke;
            if (fault is "success")
            {
                window.Status.Text.Should().Contain("Retained exact request");
                window.Status.Text.Should().Contain(nameof(ModelTurnOutcome.Succeeded));
                window.McpResult.Text.Should().Be($"Actual inert result: mcp.search\nSource: {revision}\nreference 42\n");
                broker.Calls.Should().Be(1);
            }
            else
            {
                window.Status.Text.Should().Contain("unavailable or unconfirmed");
                window.McpResult.Text.Should().BeEmpty();
                broker.Calls.Should().Be(0);
            }
            await controller.CloseAndWait();
            window.IsVisible.Should().BeFalse();
            window.McpResult.Text.Should().BeEmpty();
        });
    }
}
