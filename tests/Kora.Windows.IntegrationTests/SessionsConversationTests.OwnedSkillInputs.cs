using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using AwesomeAssertions;
using Kora.Application.Skills;
using Kora.Core.Skills;
using Kora.NativeUxFixture;
using Kora.Windows.IntegrationTests.Storage;
using Kora.Windows.Skills;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RequiredLiteralSkillNativeCreateSaveReopenReviewAndConfirmCompletesCanonicalAnswer(bool editAfterPreview)
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
            var store = new WindowsOwnedDeclarativeSkillStore(native);
            await using var admission = new OwnedSkillControlAdmission(native.Interactions, native.Interactions, new(native.Tasks));
            var workflow = new OwnedSkillInvocationWorkflow(store, admission, composed.Access, f.Store, composed.Workspace,
                composed.Conversation, native.Audit, f.Time);
            await using var use = new OwnedSkillInvocationController(native.Main, workflow, static () => true, static () => 1,
                NullLogger<OwnedSkillInvocationController>.Instance);
            use.Bind();
            await using var editor = new OwnedSkillEditorController(native.Main, new(store, admission, native.Audit), admission,
                static () => true, static () => 1, NullLogger<OwnedSkillEditorController>.Instance, use);
            editor.Bind();
            OwnedSkillEditorWindow? editing = null;
            OwnedSkillInvocationWindow? invoking = null;
            using var editOpened = Window.WindowOpenedEvent.AddClassHandler<OwnedSkillEditorWindow>((window, _) => editing = window);
            using var useOpened = Window.WindowOpenedEvent.AddClassHandler<OwnedSkillInvocationWindow>((window, _) => invoking = window);
            var publicationReviews = new List<string>();
            using var publication = Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) =>
            {
                if (window.Title is not ("Review complete skill package" or "Confirm exact new skill publication")) { return; }
                publicationReviews.Add(window.Title);
                var controls = window.GetLogicalDescendants().ToArray();
                var sources = controls.OfType<TextBox>().ToArray();
                sources[0].Text.Should().Contain("kora-skill-format: 3\n").And.Contain("inputs: required-literal\n");
                sources[1].Text.Should().Be("Compare ${input:topic} and ${input:topic}.");
                var accept = controls.OfType<Button>().First();
                Avalonia.Threading.Dispatcher.UIThread.Post(() => accept.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));
            });
            async Task Command(string command)
            { native.Main.CommandText = command; await native.Main.RunTypedCommand.ExecuteAsync(); }
            await Command("owned skill create example 1.0.0");
            await Command("owned skill name: Example");
            await Command("owned skill description: Description");
            await Command("owned skill guidance: Compare ${input:topic} and ${input:topic}.");
            await Command("owned skill inputs required-literal");
            await Command("owned skill validate");
            await Command("owned skill save");
            publicationReviews.Should().Equal("Review complete skill package", "Confirm exact new skill publication");
            var saved = (await store.Read("example", "1.0.0", native.Token))!;
            saved.Document.InputTemplate!.RequiredNames.Should().Equal("topic");
            saved.Document.IsAvailableForInvocation.Should().BeFalse();
            await Command("owned skill reopen example 1.0.0");
            editing!.InputFormat.SelectedIndex.Should().Be(1);
            editing.Document().RevisionDigest.Should().Be(saved.Document.RevisionDigest);
            await Command("owned skill use example 1.0.0");
            var opened = invoking ?? throw new InvalidOperationException("No canonical owned skill use window.");
            opened.LiteralInputs.Inputs.Keys.Should().Equal("topic");
            opened.LiteralInputs.Inputs["topic"].IsUndoEnabled.Should().BeFalse();
            await Command("owned skill enable");
            await Command("owned skill targets");
            opened.Targets.SelectedIndex = 0;
            const string original = "  Original user task e\u0301  ";
            const string value = "  literal ${input:never-expanded} e\u0301  ";
            await Command("owned skill request: " + original);
            await Command("owned skill input topic: " + value);
            opened.LiteralInputs.Inputs["topic"].Text.Should().Be(value);
            await Command("owned skill review-use");
            opened.Confirm.IsEnabled.Should().BeTrue();
            using (var preview = JsonDocument.Parse(opened.Confirmation.Text![opened.Confirmation.Text!.IndexOf('{')..]))
            {
                preview.RootElement.GetProperty("UserRequest").GetString().Should().Be(original);
                var evidence = preview.RootElement.GetProperty("Evidence");
                evidence.GetArrayLength().Should().Be(3);
                evidence[1].GetProperty("Content").GetString().Should().Be(saved.Document.Guidance);
                using var inputs = JsonDocument.Parse(evidence[2].GetProperty("Content").GetString()!);
                inputs.RootElement.GetProperty("RequiredLiteralInputs").GetProperty("topic").GetString().Should().Be(value);
                inputs.RootElement.GetProperty("ExpandedGuidance").GetString().Should().Be($"Compare {value} and {value}.");
            }
            adapter.Calls.Should().Be(0);
            f.Count("conversation_messages").Should().Be(0);
            if (editAfterPreview)
            {
                opened.LiteralInputs.Inputs["topic"].Text = "new unreviewed value";
                opened.Confirm.IsEnabled.Should().BeFalse();
                await Command("owned skill invoke");
                adapter.Calls.Should().Be(0);
                f.Count("conversation_messages").Should().Be(0);
            }
            else
            {
                opened.Confirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await use.ActiveOperation;
                adapter.Calls.Should().Be(1);
                var retained = (await f.Store.ReadConversationPageAsync(f.Request.SessionId, null, 25, f.Token)).Messages.Single();
                retained.Body.Should().Be(original);
                retained.AssistantResponse!.Body.Should().Be("earlier ASSISTANT");
                retained.Request.SessionId.Should().Be(f.Request.SessionId);
            }
            await Command("owned skill close-use");
            opened.LiteralInputs.Inputs.Should().BeEmpty();
            opened.Instructions.Text.Should().BeEmpty();
            opened.Confirmation.Text.Should().BeEmpty();
            (await editor.PrepareClose()).Should().BeTrue();
        });
    }
}
