using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using AwesomeAssertions;
using Kora.Application.Skills;
using Kora.Core.Auditing;
using Kora.Core.Skills;
using Kora.NativeUxFixture;
using Kora.Windows.Skills;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

public sealed partial class OwnedSkillAuthoringNativeTests
{
    [Theory]
    [InlineData("save", "current")]
    [InlineData("save", "review")]
    [InlineData("save", "confirm")]
    [InlineData("save", "after-commit")]
    [InlineData("create", "current")]
    [InlineData("create", "dirty")]
    [InlineData("create", "review")]
    [InlineData("create", "confirm")]
    [InlineData("generate", "current")]
    [InlineData("generate", "dirty")]
    [InlineData("generate", "confirm")]
    [InlineData("reopen", "current")]
    [InlineData("reopen", "dirty")]
    [InlineData("reopen", "confirm")]
    [InlineData("delete", "current")]
    [InlineData("delete", "dirty")]
    [InlineData("delete", "confirm")]
    public async Task SeparateSaveInputOwnsPublicationAndDirtySaveContinuations(string operation, string withdrawal)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var parent = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".net-test-artifacts"));
            Directory.CreateDirectory(parent);
            using var f = new NativeUxFixtureSession(parent, syntheticVoice: true);
            await f.InitializeAsync();
            await using var admission = new OwnedSkillControlAdmission(f.Interactions, f.Interactions, new(f.Tasks));
            var store = new WindowsOwnedDeclarativeSkillStore(f);
            var audit = new SaveAuthorityAudit(f.Audit);
            var existing = OwnedDeclarativeSkillDocument.Create("existing", "1.0.0", "Existing", "Description", "Existing source");
            var original = await store.Publish(existing, static () => true, action => Task.FromResult(action()), f.Token);
            await using var controller = new OwnedSkillEditorController(f.Main, new(store, admission, audit), admission,
                static () => true, static () => 1, NullLogger<OwnedSkillEditorController>.Instance,
                deletion: new(store, admission, f.Audit, TimeProvider.System));
            controller.Bind();
            OwnedSkillEditorWindow? editor = null;
            var next = new TaskCompletionSource<Window>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var opened = Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) =>
            {
                if (window is OwnedSkillEditorWindow skill) { editor = skill; }
                else { next.TrySetResult(window); }
            });
            async Task Run(string text) { f.Main.CommandText = text; await f.Main.RunTypedCommand.ExecuteAsync(); }
            async Task<Window> Review(Task pending)
            {
                (await Task.WhenAny(next.Task, pending)).Should().BeSameAs(next.Task,
                    "the exact native review must be reached before completion");
                var dialog = await next.Task;
                next = new(TaskCreationOptions.RunContinuationsAsynchronously);
                return dialog;
            }
            static void Choose(Window dialog, string caption) => dialog.GetLogicalDescendants().OfType<Button>()
                .Single(button => Equals(button.Content, caption)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            f.Main.BindVoiceOwnershipGate(static () => true);
            await f.Main.SetVoiceConsentAsync(true);
            await f.Main.BeginPushToTalkCommand.ExecuteAsync();
            await f.Voice.Started.Task.WaitAsync(f.Token);
            await f.Main.EndPushToTalkCommand.ExecuteAsync();
            await Run("owned skill create example 1.0.0");
            await Run("owned skill name: Example");
            await Run("owned skill description: Description");
            await Run("owned skill guidance: Complete literal guidance");
            await Run("owned skill validate");
            var draft = editor!.Document();
            if (withdrawal is "after-commit")
            {
                store.Checkpoint = point =>
                {
                    if (string.Equals(point, "after-commit", StringComparison.Ordinal)) { f.Voice.InvalidateCapture(); }
                };
            }
            var command = operation switch
            {
                "create" => "owned skill create next-draft 1.0.0",
                "generate" => "create a skill that explains a deployment failure",
                "reopen" => "owned skill reopen existing 1.0.0",
                "delete" => "owned skill delete existing 1.0.0",
                _ => "owned skill save",
            };
            f.Voice.PublishTranscript(f.Main.AssistantName + ", " + command);
            await next.Task.WaitAsync(TimeSpan.FromSeconds(20), f.Token);
            var saving = controller.ActiveOperation;
            if (operation is not "save")
            {
                var dirty = await Review(saving);
                dirty.Title.Should().Be("Unsaved skill draft");
                if (withdrawal is "dirty") { f.Voice.InvalidateCapture(); }
                Choose(dirty, "Save");
            }
            var saveReviews = new List<string>();
            while (await Task.WhenAny(next.Task, saving) == next.Task)
            {
                var review = await Review(saving);
                if (string.Equals(review.Title, "Review complete skill package", StringComparison.Ordinal))
                {
                    saveReviews.Add(review.Title!);
                    review.GetLogicalDescendants().OfType<TextBox>().Select(box => box.Text)
                        .Should().Equal(draft.ManifestText, draft.Guidance);
                    if (withdrawal is "review") { f.Voice.InvalidateCapture(); }
                    Choose(review, "Continue to separate native publication confirmation");
                }
                else if (string.Equals(review.Title, "Confirm exact new skill publication", StringComparison.Ordinal))
                {
                    saveReviews.Add(review.Title!);
                    if (withdrawal is "confirm") { f.Voice.InvalidateCapture(); }
                    Choose(review, "Publish exact new version");
                }
                else if (withdrawal is "current" && operation is "delete")
                {
                    Choose(review, string.Equals(review.Title, "Review exact owned skill source deletion", StringComparison.Ordinal)
                        ? "Continue to separate deletion confirmation" : "Delete only this exact owned source");
                }
                else { Choose(review, "Cancel"); }
            }
            await saving;
            var saved = await store.Read("example", "1.0.0", f.Token);
            var createAudit = audit.Events.Where(entry => string.Equals(entry.ActionId, "definition.owned-skill.create", StringComparison.Ordinal)).ToArray();
            var retainedDraft = editor.Guidance.Text;
            var retainedId = editor.IdInput.Text;
            var retainedPurpose = editor.Purpose.Text;
            var status = string.Join("\n", editor.GetLogicalDescendants().OfType<TextBlock>().Select(block => block.Text));
            if (withdrawal is "after-commit")
            {
                await Run("owned skill save");
                next.Task.IsCompleted.Should().BeFalse("unconfirmed publication holds further mutation without retry");
            }
            using var cleanup = Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) =>
            {
                if (string.Equals(window.Title, "Unsaved skill draft", StringComparison.Ordinal))
                { Avalonia.Threading.Dispatcher.UIThread.Post(() => Choose(window, "Discard")); }
            });
            (await controller.PrepareClose()).Should().BeTrue();
            saveReviews.Should().HaveCount(withdrawal switch { "dirty" => 0, "review" => 1, _ => 2 });
            if (withdrawal is "current")
            {
                saved.Should().NotBeNull();
                saved!.Document.RevisionDigest.Should().Be(draft.RevisionDigest);
                saved.Document.IsAvailableForInvocation.Should().BeFalse();
                createAudit.Select(entry => entry.Outcome).Should().Equal(SecurityAuditOutcome.Requested, SecurityAuditOutcome.Succeeded);
                createAudit.Should().OnlyContain(entry => entry.Initiator == SecurityAuditInitiator.VoiceCommand);
                if (operation is "create") { retainedId.Should().Be("next-draft"); }
                if (operation is "generate") { retainedPurpose.Should().Be("explains a deployment failure"); }
                if (operation is "reopen") { retainedId.Should().Be("existing"); }
            }
            else if (withdrawal is "after-commit")
            {
                saved.Should().NotBeNull("authority withdrawal after the native commit is not rollback");
                saved!.Document.RevisionDigest.Should().Be(draft.RevisionDigest);
                createAudit.Select(entry => entry.Outcome).Should().Equal(SecurityAuditOutcome.Requested, SecurityAuditOutcome.Unknown);
                status.Should().Contain("Publication may have occurred").And.Contain("Do not retry Save");
                retainedDraft.Should().Be(draft.Guidance);
            }
            else
            {
                saved.Should().BeNull("native assent cannot replace the withdrawn initiating Save input");
                createAudit.Should().BeEmpty();
                retainedDraft.Should().Be(draft.Guidance);
                retainedId.Should().Be(draft.Id);
                status.Should().ContainAny("authority", "authoring failed");
            }
            var retained = await store.Read("existing", "1.0.0", f.Token);
            if (operation is "delete" && withdrawal is "current") { retained.Should().BeNull(); }
            else { retained!.IsSameRevision(original).Should().BeTrue(); }
        });
    }

    private sealed class SaveAuthorityAudit(ISecurityAuditLog inner) : ISecurityAuditLog
    {
        internal List<SecurityAuditEvent> Events { get; } = [];
        public void Write(SecurityAuditEvent entry) { inner.Write(entry); Events.Add(entry); }
    }
}
