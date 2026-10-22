using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Application.Configuration;
using Kora.Application.Definitions;
using Kora.Application.Hosting;
using Kora.Core.Artifacts;
using Kora.Core.Commands;
using Kora.Core.Context;
using Kora.Definitions.Artifacts;
using Kora.NativeUxFixture;
using Kora.Windows.Artifacts;
using Kora.Windows.Context;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests.ArtifactDiscovery;

public sealed partial class OwnedDefinitionEditorWindowTests
{
    [Theory]
    [InlineData("confirmed")]
    [InlineData("cancel-read")]
    [InlineData("cancel-copy")]
    [InlineData("withdraw")]
    [InlineData("draft-changed")]
    [InlineData("privacy")]
    public async Task NativeProfileForkCompletesTwoSeparateReviewsWithoutReplacingEditorDraftAndRetiresPrivateSource(string condition)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var parent = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".net-test-artifacts"));
            Directory.CreateDirectory(parent);
            using var f = new NativeUxFixtureSession(parent);
            await f.InitializeAsync();
            var profile = Path.Combine(parent, "pf-" + Guid.NewGuid().ToString("N")[..8]);
            var sourcePath = Path.Combine(profile, "Code", "User", "prompts", "example.instructions.md");
            Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
            var sourceBytes = Document(ArtifactKind.Instruction, "Exact personal original").CopyBytes();
            File.WriteAllBytes(sourcePath, sourceBytes);
            try
            {
                await using var admission = new InstructionControlAdmission(f.Interactions, f.Interactions, new HostTaskCoordinator(f.Tasks));
                var store = new WindowsOwnedInstructionStore(f);
                var revisions = new InstructionRevisionCatalogue(f);
                var catalogue = EmbeddedArtifactCatalogue.Load();
                var actions = new OwnedInstructionEditorActions(store, admission, revisions, catalogue, f.Audit,
                    new BuiltInCommandRouter(new BuiltInCommandCatalog()));
                var preferences = new LocalArtifactDirectoryPreferences(f);
                using var reload = new ArtifactCatalogueReloadService(catalogue, f, preferences,
                    new ArtifactDirectoryConfigurationService(preferences, f.Audit),
                    new McpServerConfigurationService(f, preferences, Path.Combine(f.LocalRoot, "no-profile")),
                    NullLogger<ArtifactCatalogueReloadService>.Instance, revisions, store, Path.Combine(f.LocalRoot, "no-profile"));
                using var reader = new TrackingProfileReader(new WindowsProfileInstructionReader(new WindowsLocalFileInspector(f), profile));
                var forks = new ProfileInstructionForkWorkflow(reader, admission, actions, f.Audit);
                using var controller = new OwnedInstructionEditorController(f.Main, actions, new(actions), reload, f,
                    () => true, () => 1, NullLogger<OwnedInstructionEditorController>.Instance,
                    profileForks: forks, profilePicker: new ProfilePicker(sourcePath));
                OwnedInstructionEditorWindow? editor = null;
                var nextReview = new TaskCompletionSource<Window>(TaskCreationOptions.RunContinuationsAsynchronously);
                using var opened = Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) =>
                {
                    if (window is OwnedInstructionEditorWindow selected) { editor = selected; }
                    else { nextReview.TrySetResult(window); }
                });
                controller.Bind();
                f.Main.CommandText = "instructions create draft";
                await f.Main.RunTypedCommand.ExecuteAsync();
                editor.Should().NotBeNull();
                var draft = editor!.Source.Text + "\r\nUnsaved editor text";
                editor.Source.Text = draft;
                editor.CopyNameInput.Text = "separate";
                Click(editor, "Create Kora copy from profile instruction");
                var readReview = await AwaitProfileReview(nextReview.Task, editor, f.Token);
                readReview.Title.Should().Contain("Allow this exact");
                readReview.GetVisualDescendants().OfType<TextBox>().Select(box => box.Text).Should()
                    .NotContain(text => text != null && text.Contains("Exact personal original", StringComparison.Ordinal));
                nextReview = new(TaskCreationOptions.RunContinuationsAsynchronously);
                Click(readReview, condition is "cancel-read" ? "Cancel" : "Allow bounded local source read");
                Window? copyReview = null;
                TextBox[] privateText = [];
                if (condition is not "cancel-read")
                {
                    copyReview = await AwaitProfileReview(nextReview.Task, editor, f.Token);
                    privateText = copyReview.GetVisualDescendants().OfType<TextBox>().ToArray();
                    privateText.Select(box => box.Text).Should().Contain(text => text != null
                        && text.Contains("Exact personal original", StringComparison.Ordinal)
                        && text.Contains("attribution:", StringComparison.Ordinal));
                    if (condition is "draft-changed") { draft += " Changed."; editor.Source.Text = draft; }
                    if (condition is "withdraw") { Click(editor, "Cancel profile copy"); }
                    else if (condition is "privacy")
                    { f.Main.CloseForObservedPrivacyEvent("Synthetic profile fork privacy closure", hidePresentation: true); }
                    else { Click(copyReview, condition is "cancel-copy" ? "Cancel" : "Confirm disabled Kora copy"); }
                }
                await reader.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(15), f.Token);
                // Lease disposal is in the awaited workflow finally; flush its controller continuation too.
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { }, Avalonia.Threading.DispatcherPriority.Background);
                privateText.Should().OnlyContain(box => string.IsNullOrEmpty(box.Text));
                readReview.IsVisible.Should().BeFalse();
                if (copyReview is not null) { copyReview.IsVisible.Should().BeFalse(); }
                if (condition is "privacy") { editor.Source.Text.Should().BeEmpty(); }
                else { editor.Source.Text.Should().Be(draft); }
                var destination = Path.Combine(f.RoamingRoot, "Instructions", "separate.instructions.md");
                File.Exists(destination).Should().Be(condition is "confirmed");
                File.ReadAllBytes(sourcePath).Should().Equal(sourceBytes);
                File.WriteAllBytes(sourcePath, sourceBytes);
                if (condition is "confirmed")
                {
                    catalogue.Candidates.Single(item => item.OwnedInstructionName is "separate").IsUseEnabled.Should().BeFalse();
                    editor.GetVisualDescendants().OfType<TextBlock>().Should().Contain(block => block.Text != null
                        && block.Text.StartsWith("Created disabled Kora instruction separate", StringComparison.Ordinal));
                }
            }
            finally { Directory.Delete(profile, recursive: true); }
        });
    }

    private static void Click(Window window, string action) =>
        window.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, action))
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static async Task<Window> AwaitProfileReview(Task<Window> review, OwnedInstructionEditorWindow editor, CancellationToken token)
    {
        try { return await review.WaitAsync(TimeSpan.FromSeconds(15), token); }
        catch (TimeoutException exception)
        {
            throw new InvalidOperationException("Expected profile review did not open. Native status: "
                + string.Join("\n", editor.GetVisualDescendants().OfType<TextBlock>().Select(block => block.Text)), exception);
        }
    }

    private sealed class ProfilePicker(string path) : IUserFilePicker
    {
        public Task<string?> SelectAsync(CancellationToken cancellationToken) => Task.FromResult<string?>(path);
    }

    private sealed class TrackingProfileReader(IProfileInstructionReader reader) : IProfileInstructionReader, IDisposable
    {
        private TrackingSelection? selection;
        internal TaskCompletionSource Disposed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<IProfileInstructionSelection> OpenSelection(string selectedPath, CancellationToken token)
        {
            selection = new(await reader.OpenSelection(selectedPath, token), Disposed);
            return selection;
        }
        public void Dispose() => selection?.Dispose();

        private sealed class TrackingSelection(IProfileInstructionSelection selected, TaskCompletionSource disposed) : IProfileInstructionSelection
        {
            public Guid Id => selected.Id;
            public LocalFileMetadata Metadata => selected.Metadata;
            public Task<byte[]> Read(CancellationToken token) => selected.Read(token);
            public Task<InstructionWriteResult> WithCurrent(ProfileInstructionSnapshot expected, Func<bool> consentCurrent,
                Func<Func<bool>, Task<InstructionWriteResult>> publish, CancellationToken token) => selected.WithCurrent(expected, consentCurrent, publish, token);
            public void Dispose() { selected.Dispose(); disposed.TrySetResult(); }
        }
    }
}
