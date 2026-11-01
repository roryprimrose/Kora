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
    [InlineData("v1", "list", "draft")]
    [InlineData("v1", "pending", "draft")]
    [InlineData("v2", "list", "draft")]
    [InlineData("v2", "pending", "draft")]
    [InlineData("v3", "list", "draft")]
    [InlineData("v3", "pending", "draft")]
    [InlineData("v4", "list", "draft")]
    [InlineData("v4", "pending", "draft")]
    [InlineData("v1", "list", "fixture")]
    [InlineData("v1", "pending", "fixture")]
    [InlineData("v4", "list", "fixture")]
    [InlineData("v4", "pending", "fixture")]
    [InlineData("v1", "list", "owner")]
    [InlineData("v1", "pending", "privacy")]
    [InlineData("v1", "list", "cancel")]
    [InlineData("v1", "pending", "target")]
    [InlineData("v1", "list", "failure")]
    [InlineData("v1", "pending", "failure")]
    [InlineData("v1", "list", "unchanged")]
    [InlineData("v4", "pending", "unchanged")]
    public async Task PostRenameRefreshAdoptsOnlyTheExactCurrentEditorAndNeverDiscardsNewUnsavedText(
        string format, string boundary, string change)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var parent = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".net-test-artifacts"));
            Directory.CreateDirectory(parent);
            using var f = new NativeUxFixtureSession(parent);
            await f.InitializeAsync();
            await using var admission = new OwnedSkillControlAdmission(f.Interactions, f.Interactions, new(f.Tasks));
            var native = new WindowsOwnedDeclarativeSkillStore(f);
            var document = format is "v4"
                ? OwnedDeclarativeSkillDocument.CreateObservationWorkflow("example", "1.0.0", "Readable", "Description",
                    "{\"steps\":[{\"tool\":\"application.get_version\",\"input\":{}}]}")
                : OwnedDeclarativeSkillDocument.Create("example", "1.0.0", "Readable", "Description",
                    format is "v3" ? "Literal ${input:topic}" : "Exact original guidance",
                    format is "v2" ? OwnedSkillToolSelection.WebPageGet : OwnedSkillToolSelection.None,
                    format is "v3" ? OwnedSkillGuidanceFormat.RequiredLiteralInputs : OwnedSkillGuidanceFormat.Literal);
            var saved = await native.Publish(document, static () => true, operation => Task.FromResult(operation()), f.Token);
            var ordinary = OwnedDeclarativeSkillDocument.Create("ordinary", "1.0.0", "Ordinary", "Description", "Retain this open ordinary draft");
            if (format is "v4")
            { await native.Publish(ordinary, static () => true, operation => Task.FromResult(operation()), f.Token); }
            var store = new RenameRefreshStore(native, boundary) { FailRefresh = change is "failure" };
            var audit = new RenameAudit(f.Audit);
            var save = new OwnedSkillSaveWorkflow(store, admission, audit);
            var renames = new OwnedSkillRenameWorkflow(store, store, admission, audit, TimeProvider.System);
            var owner = 1L;
            await using var controller = new OwnedSkillEditorController(f.Main, save, admission, static () => true,
                () => owner, NullLogger<OwnedSkillEditorController>.Instance, renames: renames);
            controller.Bind();
            OwnedSkillEditorWindow? editor = null;
            using var observed = Window.WindowOpenedEvent.AddClassHandler<OwnedSkillEditorWindow>((window, _) => editor = window);
            async Task Run(string text) { f.Main.CommandText = text; await f.Main.RunTypedCommand.ExecuteAsync(); }
            await Run("owned skill list");
            var window = editor ?? throw new InvalidOperationException("The owned editor did not open.");
            await Run(format is "v4" ? "owned skill reopen ordinary 1.0.0" : "owned skill reopen example 1.0.0");
            var openId = window.IdInput.Text;
            var openGuidance = window.Guidance.Text;
            var oldRows = window.Saved.ItemsSource;
            var oldPending = window.PendingRenames.ItemsSource;
            var dirtyReviews = 0;
            var dirtyChoice = "Cancel";
            using var dialogs = Window.WindowOpenedEvent.AddClassHandler<Window>((dialog, _) =>
            {
                if (ReferenceEquals(dialog, window)) { return; }
                var buttons = dialog.GetLogicalDescendants().OfType<Button>().ToArray();
                if (dialog.Title is "Unsaved skill draft")
                {
                    dirtyReviews++;
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                        buttons.Single(button => Equals(button.Content, dirtyChoice)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));
                }
                else
                {
                    dialog.Title.Should().BeOneOf("Review complete owned package rename", "Confirm exact package rename");
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => buttons[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));
                }
            });
            var operation = Run("owned skill rename example 1.0.0 renamed");
            try
            {
                (await Task.WhenAny(store.Entered.Task, operation)).Should().BeSameAs(store.Entered.Task);
                await store.Entered.Task;
                operation.IsCompleted.Should().BeFalse();
                switch (change)
                {
                    case "draft":
                        window.Guidance.Text = "New unsaved private guidance";
                        window.DescriptionInput.Text = "New unsaved metadata";
                        break;
                    case "owner": owner++; break;
                    case "privacy": f.Main.CloseForObservedPrivacyEvent("Synthetic rename refresh closure", hidePresentation: true); break;
                    case "cancel":
                        window.GetLogicalDescendants().OfType<Button>().Single(button => Equals(button.Content, "Cancel"))
                            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        operation.IsCompleted.Should().BeFalse("cancellation must not abandon the held catalogue read");
                        break;
                    case "target": window.RenameId.Text = "other-target"; break;
                    case "fixture": window.Fixture.Text = "{\"input\":\"New private example\",\"assertions\":[{\"kind\":\"equals\",\"value\":\"private\"}]}"; break;
                }
                store.Release.TrySetResult();
                await operation;
                var status = window.GetLogicalDescendants().OfType<TextBlock>()
                    .Single(block => block.Text?.StartsWith(change is "unchanged" ? "Complete package rename" : "Rename is unconfirmed.", StringComparison.Ordinal) == true);
                status.Text.Should().NotBeNullOrEmpty();
                var outcomes = audit.Events.Where(entry => string.Equals(entry.ActionId,
                    "definition.owned-skill.rename", StringComparison.Ordinal)).Select(entry => entry.Outcome);
                outcomes.Should().Equal(SecurityAuditOutcome.Requested, SecurityAuditOutcome.Succeeded);
                (await native.PendingRenames(f.Token)).Should().BeEmpty();
                (await native.Read("example", "1.0.0", f.Token)).Should().BeNull();
                var renamed = (await native.Read("renamed", "1.0.0", f.Token))!;
                renamed.Document.InstructionBytes.ToArray().Should().Equal(saved.Document.InstructionBytes.ToArray());
                renamed.Document.IsAvailableForInvocation.Should().BeFalse();
                if (change is not "unchanged")
                {
                    window.Saved.ItemsSource.Should().BeSameAs(oldRows);
                    window.PendingRenames.ItemsSource.Should().BeSameAs(oldPending);
                    window.IdInput.Text.Should().Be(openId);
                    window.Guidance.Text.Should().Be(change is "draft" ? "New unsaved private guidance" : openGuidance);
                }
                if (change is "draft")
                {
                    window.DescriptionInput.Text.Should().Be("New unsaved metadata");
                    (await controller.PrepareClose()).Should().BeFalse();
                    dirtyReviews.Should().Be(1, "the retained unsaved draft must still require an explicit discard decision");
                    window.Guidance.Text.Should().Be("New unsaved private guidance");
                    dirtyChoice = "Discard";
                }
                if (change is "fixture")
                { window.Fixture.Text.Should().Be("{\"input\":\"New private example\",\"assertions\":[{\"kind\":\"equals\",\"value\":\"private\"}]}"); }
                if (change is "unchanged" && format is not "v4")
                { window.IdInput.Text.Should().Be("renamed"); window.Guidance.Text.Should().Be(document.Guidance); }
                (await controller.PrepareClose()).Should().BeTrue();
            }
            finally
            {
                store.Release.TrySetResult();
                await operation;
            }
        });
    }

    [Fact]
    public async Task RevocationAtHeldNativeRetirementKeepsBothNamesUnavailableAndEmitsUnknownNotSuccess()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var parent = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".net-test-artifacts"));
            Directory.CreateDirectory(parent);
            using var f = new NativeUxFixtureSession(parent);
            await f.InitializeAsync();
            await using var admission = new OwnedSkillControlAdmission(f.Interactions, f.Interactions, new(f.Tasks));
            var store = new WindowsOwnedDeclarativeSkillStore(f);
            var document = OwnedDeclarativeSkillDocument.Create("example", "1.0.0", "Readable", "Description", "Exact original guidance");
            var saved = await store.Publish(document, static () => true, operation => Task.FromResult(operation()), f.Token);
            var audit = new RenameAudit(f.Audit);
            var renames = new OwnedSkillRenameWorkflow(store, store, admission, audit, TimeProvider.System);
            var current = true;
            var heldRetirementReached = false;
            store.Checkpoint = point =>
            {
                if (point is "rename-before-retirement") { heldRetirementReached = true; current = false; }
            };
            var review = new OwnedSkillEditorWindow(_ => { }, static () => true);
            review.Show();
            using var dialogs = Window.WindowOpenedEvent.AddClassHandler<Window>((dialog, _) =>
            {
                if (ReferenceEquals(dialog, review)) { return; }
                var confirm = dialog.GetLogicalDescendants().OfType<Button>().First();
                Avalonia.Threading.Dispatcher.UIThread.Post(() => confirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));
            });
            var run = () => renames.Rename("example", "1.0.0", "renamed", false,
                new(Kora.Core.Hosting.RequestOrigin.LocalUi, static () => true), () => current, review, f.Token);
            var failure = await run.Should().ThrowAsync<OwnedSkillRenameUnconfirmedException>();
            heldRetirementReached.Should().BeTrue();
            failure.Which.Effect.Should().Be(OwnedSkillRenameEffect.SourceRetirementStarted);
            failure.Which.InnerException.Should().BeOfType<InvalidOperationException>();
            audit.Events.Select(entry => entry.Outcome).Should().Equal(SecurityAuditOutcome.Requested, SecurityAuditOutcome.Unknown);
            audit.Events[^1].ReasonCode.Should().Be("rename-unconfirmed");
            var pending = (await store.PendingRenames(f.Token)).Single();
            pending.Original.IsSameRevision(saved).Should().BeTrue();
            foreach (var documentToRetain in new[] { document, pending.Renamed })
            {
                File.ReadAllBytes(Path.Combine(store.Destination(documentToRetain), "manifest.yaml")).Should().Equal(documentToRetain.ManifestBytes.ToArray());
                File.ReadAllBytes(Path.Combine(store.Destination(documentToRetain), "instructions.md")).Should().Equal(documentToRetain.InstructionBytes.ToArray());
                await store.Invoking(value => value.Read(documentToRetain.Id, documentToRetain.Version, f.Token)).Should().ThrowAsync<InvalidOperationException>();
            }
            review.Close();
        });
    }

    private sealed class RenameAudit(ISecurityAuditLog inner) : ISecurityAuditLog
    {
        internal List<SecurityAuditEvent> Events { get; } = [];
        public void Write(SecurityAuditEvent entry) { inner.Write(entry); Events.Add(entry); }
    }

    private sealed class RenameRefreshStore(WindowsOwnedDeclarativeSkillStore inner, string boundary) : IOwnedDeclarativeSkillStore, IOwnedSkillRenameStore
    {
        private bool renamed;
        internal bool FailRefresh { get; init; }
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string Destination(OwnedDeclarativeSkillDocument document) => inner.Destination(document);
        public Task<OwnedDeclarativeSkillSnapshot?> Read(string id, string version, CancellationToken token,
            OwnedSkillReadScope scope = OwnedSkillReadScope.CompletePackage, OwnedDeclarativeSkillSnapshot? expectedPrimary = null) =>
            inner.Read(id, version, token, scope, expectedPrimary);
        public Task<OwnedDeclarativeSkillSnapshot> Publish(OwnedDeclarativeSkillDocument document, Func<bool> eligible,
            Func<Func<OwnedDeclarativeSkillSnapshot>, Task<OwnedDeclarativeSkillSnapshot>> commit, CancellationToken token,
            OwnedDeclarativeSkillSnapshot? expectedSource = null) =>
            inner.Publish(document, eligible, commit, token, expectedSource);
        public async Task<IReadOnlyList<OwnedDeclarativeSkillSnapshot>> List(CancellationToken token)
        {
            var result = await inner.List(token).ConfigureAwait(false);
            await Hold("list").ConfigureAwait(false);
            return result;
        }
        public async Task<IReadOnlyList<OwnedSkillRename>> PendingRenames(CancellationToken token)
        {
            var result = await inner.PendingRenames(token).ConfigureAwait(false);
            await Hold("pending").ConfigureAwait(false);
            return result;
        }
        public async Task<OwnedDeclarativeSkillSnapshot> Rename(OwnedSkillRename proposal, bool recovery, Func<bool> eligible,
            Func<Func<OwnedDeclarativeSkillSnapshot>, Task<OwnedDeclarativeSkillSnapshot>> commit, CancellationToken token)
        {
            var result = await inner.Rename(proposal, recovery, eligible, commit, token).ConfigureAwait(false);
            renamed = true;
            return result;
        }
        private async Task Hold(string point)
        {
            if (!renamed || !string.Equals(point, boundary, StringComparison.Ordinal)) { return; }
            Entered.TrySetResult();
            // Deliberately ignore cancellation until the admitted read physically joins.
            await Release.Task.ConfigureAwait(false);
            if (FailRefresh) { throw new IOException("Synthetic catalogue refresh failure after verified native rename."); }
        }
    }
}
