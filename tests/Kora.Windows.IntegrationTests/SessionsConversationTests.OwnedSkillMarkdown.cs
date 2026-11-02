using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using AwesomeAssertions;
using Kora.Application.Skills;
using Kora.Core.Artifacts;
using Kora.Core.Skills;
using Kora.NativeUxFixture;
using Kora.Windows.IntegrationTests.Storage;
using Kora.Windows.Skills;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
    [Fact]
    public async Task ActualComponentEditorWholePackageReviewSaveRestartAndSeparateToolFreeUseReturnGenuineAnswer()
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
            var useWorkflow = new OwnedSkillInvocationWorkflow(store, admission, composed.Access, f.Store, composed.Workspace,
                composed.Conversation, native.Audit, f.Time, new MarkdownUsePolicy());
            await using var use = new OwnedSkillInvocationController(native.Main, useWorkflow, static () => true, static () => 1,
                NullLogger<OwnedSkillInvocationController>.Instance);
            use.Bind();
            await using var editor = new OwnedSkillEditorController(native.Main, new(store, admission, native.Audit), admission,
                static () => true, static () => 1, NullLogger<OwnedSkillEditorController>.Instance, use);
            editor.Bind();
            OwnedSkillEditorWindow? editing = null;
            OwnedSkillInvocationWindow? invoking = null;
            var publications = 0;
            var reviewedSources = new List<string?[]>();
            using var opened = Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) =>
            {
                if (window is OwnedSkillEditorWindow authoring) { editing = authoring; }
                else if (window is OwnedSkillInvocationWindow invocation) { invoking = invocation; }
                else if (window.Title is "Review complete skill package" or "Confirm exact new skill publication")
                {
                    publications++;
                    var source = window.GetLogicalDescendants().OfType<TextBox>().Select(box => box.Text).ToArray();
                    reviewedSources.Add(source);
                    var confirm = window.GetLogicalDescendants().OfType<Button>().First();
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => confirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));
                }
            });
            async Task Command(string text) { native.Main.CommandText = text; await native.Main.RunTypedCommand.ExecuteAsync(); }
            await Command("owned skill create example 1.0.0");
            await Command("owned skill name: Example");
            await Command("owned skill description: Description");
            await Command("owned skill guidance: Review original prose.");
            await Command("owned skill format markdown-package");
            await Command(@"owned skill reference references\guide.md: Initial reference");
            await Command(@"owned skill reference references\guide.md: Exact reference" + "\r\n");
            await Command("owned skill reference removed.md: Removed content");
            await Command("owned skill remove-reference removed.md");
            await Command("owned skill reference rubric.md: Plain rubric.");
            editing!.Document().References.Should().HaveCount(2);
            await Command("owned skill validate");
            await Command("owned skill save");
            publications.Should().Be(2);
            foreach (var source in reviewedSources)
            {
                source[1].Should().Contain(@"references\guide.md").And.Contain("Exact reference\r\n").And.NotContain("Removed content");
                source[1].Should().Contain("ADDED").And.Contain("Whole destination package digest");
            }
            adapter.Calls.Should().Be(0);
            f.Count("conversation_messages").Should().Be(0);
            var reopened = (await new WindowsOwnedDeclarativeSkillStore(native).Read("example", "1.0.0", native.Token))!;
            reopened.Document.References[0].Text.Should().Be("Exact reference\r\n");
            reopened.Document.IsAvailableForInvocation.Should().BeFalse();
            await Command("owned skill use example 1.0.0");
            invoking!.Instructions.Text.Should().NotContain("Exact reference");
            await Command("owned skill read-references");
            invoking.Instructions.Text.Should().Contain("Exact reference\r\n").And.Contain("Plain rubric.");
            await Command("owned skill enable");
            await Command("owned skill targets");
            invoking.Targets.SelectedIndex = 0;
            await Command("owned skill request: Original unchanged prose");
            await Command("owned skill review-use");
            adapter.Calls.Should().Be(0);
            await Command("owned skill invoke");
            adapter.Calls.Should().Be(1);
            var retained = (await f.Store.ReadConversationPageAsync(f.Request.SessionId, null, 25, f.Token)).Messages.Single();
            retained.Body.Should().Be("Original unchanged prose");
            retained.AssistantResponse!.Body.Should().Be("earlier ASSISTANT");
            using var wire = JsonDocument.Parse(adapter.Wire!);
            wire.RootElement.GetProperty("Evidence").GetArrayLength().Should().Be(4);
            wire.RootElement.GetProperty("Tools").GetArrayLength().Should().Be(0);
            await Command("owned skill close-use");
            (await editor.PrepareClose()).Should().BeTrue();
        });
    }

    private sealed class MarkdownUsePolicy : IArtifactUsePolicy
    {
        public event EventHandler? Changed { add { } remove { } }
        public long Revision => 0;
        public bool IsDisabled(string identity) => false;
        public bool IsRevisionAllowed(string identity, string digest) => true;
    }
}
