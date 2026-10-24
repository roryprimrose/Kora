using Avalonia.Controls;
using Avalonia.Interactivity;
using AwesomeAssertions;
using Kora.Application.Agents;
using Kora.NativeUxFixture;
using Kora.Windows.IntegrationTests.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeSharedSkillAgentCompletesDistinctReadEnableAndInvocationAndClearsOnStop(bool typed)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var storage = new InteractionStorageFixture();
            await storage.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(storage);
            await using var f = new SharedAgentFixture(storage);
            var parent = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".net-test-artifacts"));
            Directory.CreateDirectory(parent);
            using var native = new NativeUxFixtureSession(parent);
            await native.InitializeAsync();
            var controller = new AgentRunController(native.Main, f.Workflow, f.Composed.Workspace,
                () => true, () => 1, NullLogger<AgentRunController>.Instance);
            controller.Bind();
            AgentRunWindow? opened = null;
            using var observation = Window.WindowOpenedEvent.AddClassHandler<AgentRunWindow>((window, _) => opened = window);
            native.Main.CommandText = "review agent";
            await native.Main.RunTypedCommand.ExecuteAsync();
            var window = opened ?? throw new InvalidOperationException("No original-input agent window.");
            try
            {
                var buttons = ((StackPanel)((ScrollViewer)window.Content!).Content!).Children.OfType<Button>().ToArray();
                async Task Act(string phrase, string label)
                {
                    if (typed)
                    {
                        native.Main.CommandText = phrase;
                        await native.Main.RunTypedCommand.ExecuteAsync();
                    }
                    else
                    {
                        buttons.Single(button => Equals(button.Content, label)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    }
                    await controller.ActiveOperation;
                }
                await Act("agent targets", "Refresh existing Active conversations");
                window.Targets.SelectedIndex = 0;
                await Act("agent skill sources", "Refresh registered shared skill sources");
                window.SkillSources.SelectedIndex = 0;
                await Act("agent skill catalogue", "Read selected source catalogue");
                window.Skills.SelectedIndex = 0;
                window.Source.Text = @"C:\Users\owner\existing.agent.md";
                window.Input.Text = "Original native request.";
                await Act("agent review invocation", "Read source and review exact invocation");
                window.SkillDeclaration.IsEnabled.Should().BeTrue();
                window.Confirm.IsEnabled.Should().BeFalse();
                await Act("agent skill review declaration", "Review selected shared skill declaration");
                f.Reader.Reads.Should().Be(0);
                window.Review.Text.Should().Contain("Exact primary evidence.").And.NotContain(f.Reader.Reference);
                window.SkillConsent.IsEnabled.Should().BeTrue();
                await Act("agent skill consent references", "Consent to read declared skill Markdown");
                window.Review.Text.Should().Contain(f.Reader.Primary).And.Contain(f.Reader.Reference).And.Contain(f.Profile.Text);
                window.SkillEnable.IsEnabled.Should().BeTrue();
                window.Confirm.IsEnabled.Should().BeFalse();
                f.Adapter.Calls.Should().Be(0);
                await Act("agent skill enable", "Enable this complete skill review once");
                window.Confirm.IsEnabled.Should().BeTrue();
                f.Adapter.Calls.Should().Be(0);
                await Act("agent confirm invocation", "Consent to this exact bounded invocation");
                f.Adapter.Calls.Should().Be(1);
                window.Result.Text.Should().Be("earlier ASSISTANT");
                window.Status.Text.Should().Contain("Succeeded");
                await Act("agent stop", "Stop / revoke and join owned execution");
                window.Review.Text.Should().BeEmpty();
                window.Input.Text.Should().BeEmpty();
                window.SkillSources.Items.Should().BeEmpty();
                window.Skills.Items.Should().BeEmpty();
                window.SkillEnable.IsEnabled.Should().BeFalse();
                f.Profile.Released.Should().BeTrue();
            }
            finally { await native.Main.ExitAsync(); }
        });
    }

    [Theory]
    [InlineData("agent skill source .agents\\skills", "SkillSource", ".agents\\skills")]
    [InlineData("agent skill select example 1.0.0", "SkillSelect", "example 1.0.0")]
    [InlineData("agent conversation Active", "SkillTarget", "Active")]
    [InlineData("agent skill sources", "SkillSources", "")]
    [InlineData("agent skill catalogue", "SkillCatalogue", "")]
    [InlineData("agent skill review declaration", "SkillDeclaration", "")]
    [InlineData("agent skill consent references", "SkillConsent", "")]
    [InlineData("agent skill enable", "SkillEnable", "")]
    public void SharedSkillAgentPhrasesRemainDisjointAndActivationNameAware(string phrase, string action, string argument)
    {
        AgentRunCommand.Parse("Kora " + phrase, "Kora").Should().Be(new AgentRunCommand(action, argument));
    }
}
