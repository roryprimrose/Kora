using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using AwesomeAssertions;
using Kora.Application.Skills;
using Kora.Core.Skills;
using Kora.NativeUxFixture;
using Kora.Windows.Skills;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

public sealed partial class OwnedSkillAuthoringNativeTests
{
    [Theory]
    [InlineData("primary", "guidance")]
    [InlineData("primary", "metadata")]
    [InlineData("declaration", "guidance")]
    [InlineData("components", "guidance")]
    [InlineData("components", "component")]
    [InlineData("consent", "guidance")]
    [InlineData("primary", "unchanged")]
    [InlineData("components", "unchanged")]
    [InlineData("primary", "cancel")]
    [InlineData("components", "privacy")]
    public async Task ReopenAdoptsOnlyAnUnchangedDraftAndKeepsNewEditsUntilExplicitDiscard(string boundary, string change)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var parent = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".net-test-artifacts"));
            Directory.CreateDirectory(parent);
            using var f = new NativeUxFixtureSession(parent);
            await f.InitializeAsync();
            await using var admission = new OwnedSkillControlAdmission(f.Interactions, f.Interactions, new(f.Tasks));
            var native = new WindowsOwnedDeclarativeSkillStore(f);
            var original = OwnedDeclarativeSkillDocument.Create("original", "1.0.0", "Original", "Original description", "Original guidance");
            var target = boundary is "primary"
                ? OwnedDeclarativeSkillDocument.Create("example", "1.0.0", "Target", "Target description", "Saved target guidance")
                : OwnedDeclarativeSkillDocument.CreatePackage("example", "1.0.0", "Target", "Target description",
                    "Saved target guidance", new Dictionary<string, byte[]>(StringComparer.Ordinal)
                    {
                        ["notes.md"] = "Exact saved component"u8.ToArray(),
                    });
            await native.Publish(original, static () => true, operation => Task.FromResult(operation()), f.Token);
            var saved = await native.Publish(target, static () => true, operation => Task.FromResult(operation()), f.Token);
            var store = new ReopenReadStore(native, boundary);
            await using var controller = new OwnedSkillEditorController(f.Main, new(store, admission, f.Audit), admission,
                static () => true, static () => 1, NullLogger<OwnedSkillEditorController>.Instance);
            controller.Bind();
            OwnedSkillEditorWindow? editor = null;
            using var observed = Window.WindowOpenedEvent.AddClassHandler<OwnedSkillEditorWindow>((window, _) => editor = window);
            async Task Run(string text) { f.Main.CommandText = text; await f.Main.RunTypedCommand.ExecuteAsync(); }
            await Run("owned skill reopen original 1.0.0");
            var window = editor ?? throw new InvalidOperationException("The owned editor did not open.");
            var originalSource = window.BasePackage;
            var dirtyReviews = 0;
            var dirtyChoice = "Cancel";
            var componentReviews = 0;
            Window? consent = null;
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
                    dialog.Title.Should().Be("Review exact owned component declaration");
                    componentReviews++;
                    if (boundary is "consent" && !store.Release.Task.IsCompleted)
                    { consent = dialog; store.Entered.TrySetResult(); }
                    else
                    { Avalonia.Threading.Dispatcher.UIThread.Post(() => buttons[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent))); }
                }
            });
            var operation = Run("owned skill reopen example 1.0.0");
            try
            {
                (await Task.WhenAny(store.Entered.Task, operation)).Should().BeSameAs(store.Entered.Task);
                await store.Entered.Task;
                operation.IsCompleted.Should().BeFalse();
                switch (change)
                {
                    case "guidance": window.Guidance.Text = "New unsaved private guidance"; break;
                    case "metadata": window.DescriptionInput.Text = "New unsaved metadata"; break;
                    case "component":
                        window.ComponentPath.Text = "new-notes.md";
                        window.ComponentBody.Text = "New unsaved private component";
                        break;
                    case "cancel":
                        window.GetLogicalDescendants().OfType<Button>().Single(button => Equals(button.Content, "Cancel"))
                            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        break;
                    case "privacy":
                        f.Main.CloseForObservedPrivacyEvent("Synthetic Reopen closure", hidePresentation: true);
                        break;
                }
                operation.IsCompleted.Should().BeFalse("the actual source read remains owned until it joins");
                if (consent is not null)
                {
                    consent.GetLogicalDescendants().OfType<Button>().First()
                        .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }
                store.Release.TrySetResult();
                await operation;
                if (change is "unchanged")
                {
                    window.Document().ManifestText.Should().Be(target.ManifestText);
                    window.Document().Guidance.Should().Be(target.Guidance);
                    window.BasePackage!.IsSameRevision(saved).Should().BeTrue();
                    window.Document().References.Select(reference => reference.Text).Should()
                        .Equal(target.References.Select(reference => reference.Text));
                    componentReviews.Should().Be(boundary is "primary" ? 0 : 1);
                }
                else
                {
                    window.IdInput.Text.Should().Be(original.Id);
                    window.Guidance.Text.Should().Be(change is "guidance" ? "New unsaved private guidance" : original.Guidance);
                    window.DescriptionInput.Text.Should().Be(change is "metadata" ? "New unsaved metadata" : original.Description);
                    window.BasePackage.Should().BeSameAs(originalSource, "late reads must not replace the retained source binding");
                    if (change is "component")
                    {
                        window.ComponentPath.Text.Should().Be("new-notes.md");
                        window.ComponentBody.Text.Should().Be("New unsaved private component");
                    }
                    if (change is "guidance" or "metadata" or "component")
                    {
                        window.GetLogicalDescendants().OfType<TextBlock>().Select(block => block.Text).Should().Contain(text =>
                            text != null && text.Contains("draft changed while reopening", StringComparison.Ordinal));
                        (await controller.PrepareClose()).Should().BeFalse();
                        dirtyReviews.Should().Be(1, "new unsaved edits still require an explicit decision");
                        window.IdInput.Text.Should().Be(original.Id);
                        dirtyChoice = "Discard";
                    }
                    if (boundary is "declaration")
                    { componentReviews.Should().Be(0, "a stale draft must not proceed to component consent"); }
                    if (boundary is "consent")
                    { store.CompleteReads.Should().Be(0, "draft edits retire Reopen before any component body read"); }
                }
                if (change is "guidance" or "metadata" or "component")
                {
                    await Run("owned skill reopen example 1.0.0");
                    window.Document().ManifestText.Should().Be(target.ManifestText);
                    window.Document().Guidance.Should().Be(target.Guidance);
                    window.BasePackage!.IsSameRevision(saved).Should().BeTrue();
                    window.ComponentBody.Text.Should().BeEmpty();
                }
                (await native.Read(target.Id, target.Version, f.Token))!.IsSameRevision(saved).Should().BeTrue();
                (await native.Read(target.Id, target.Version, f.Token))!.Document.IsAvailableForInvocation.Should().BeFalse();
                (await controller.PrepareClose()).Should().BeTrue();
                dirtyReviews.Should().Be(change is "guidance" or "metadata" or "component" ? 2 : 0);
            }
            finally
            {
                store.Release.TrySetResult();
                consent?.Close();
                await operation;
            }
        });
    }

    private sealed class ReopenReadStore(WindowsOwnedDeclarativeSkillStore inner, string boundary) : IOwnedDeclarativeSkillStore
    {
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int CompleteReads { get; private set; }
        public string Destination(OwnedDeclarativeSkillDocument document) => inner.Destination(document);
        public Task<IReadOnlyList<OwnedDeclarativeSkillSnapshot>> List(CancellationToken token) => inner.List(token);
        public Task<OwnedDeclarativeSkillSnapshot> Publish(OwnedDeclarativeSkillDocument document, Func<bool> eligible,
            Func<Func<OwnedDeclarativeSkillSnapshot>, Task<OwnedDeclarativeSkillSnapshot>> commit, CancellationToken token,
            OwnedDeclarativeSkillSnapshot? expectedSource = null) =>
            inner.Publish(document, eligible, commit, token, expectedSource);
        public async Task<OwnedDeclarativeSkillSnapshot?> Read(string id, string version, CancellationToken token,
            OwnedSkillReadScope scope = OwnedSkillReadScope.CompletePackage, OwnedDeclarativeSkillSnapshot? expectedPrimary = null)
        {
            var result = await inner.Read(id, version, token, scope, expectedPrimary).ConfigureAwait(false);
            if (!string.Equals(id, "example", StringComparison.Ordinal)) { return result; }
            if (scope is OwnedSkillReadScope.CompletePackage) { CompleteReads++; }
            if (boundary is "primary" or "declaration" && scope is OwnedSkillReadScope.PrimaryDeclaration
                || boundary is "components" && scope is OwnedSkillReadScope.CompletePackage)
            {
                Entered.TrySetResult();
                // Deliberately retain a completed source read despite cancellation until the barrier releases it.
                await Release.Task.ConfigureAwait(false);
            }
            return result;
        }
    }
}
