using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Application;
using Kora.Application.Configuration;
using Kora.Application.Definitions;
using Kora.Application.Hosting;
using Kora.Core.Artifacts;
using Kora.Core.Authorization;
using Kora.Core.Commands;
using Kora.Core.Hosting;
using Kora.Core.Storage;
using Kora.Definitions.Artifacts;
using Kora.NativeUxFixture;
using Kora.Windows.Artifacts;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests.ArtifactDiscovery;

public sealed partial class OwnedDefinitionEditorWindowTests
{
    [Theory]
    [InlineData(ArtifactKind.Instruction, "clean")]
    [InlineData(ArtifactKind.Prompt, "dirty")]
    [InlineData(ArtifactKind.Instruction, "new")]
    [InlineData(ArtifactKind.Prompt, "new")]
    [InlineData(ArtifactKind.Instruction, "invalid")]
    [InlineData(ArtifactKind.Prompt, "unavailable")]
    [InlineData(ArtifactKind.Instruction, "uncertain")]
    [InlineData(ArtifactKind.Prompt, "enabled")]
    public async Task NativeAndExactValidateObserveOnlyTheCapturedDraftAndPreserveAllEditorAndSourceState(ArtifactKind kind, string state)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            await using var f = await DraftValidationFixture.Create(kind, state is "new");
            var editor = f.Editor!;
            if (state is not ("clean" or "new")) { editor.Source.Text += "\r\nPRIVATE_UNSAVED_BODY"; }
            if (state is "invalid") { editor.Source.Text = editor.Source.Text!.Replace("kora-format: 1", "kora-format: 2", StringComparison.Ordinal); }
            if (state is "uncertain")
            {
                var retained = editor.Source.Text;
                editor.Source.Text = editor.Source.Text!.Replace("kora-format: 1", "kora-format: 2", StringComparison.Ordinal);
                await f.Run(f.Prefix + " save");
                editor.Source.Text = retained;
            }
            if (state is "enabled")
            { await f.Actions.Activate(f.Source!, _ => Task.FromResult(true), () => true, f.Session.Token); }
            if (state is "unavailable") { f.Catalogue.ReportReloadFailure(new IOException("Synthetic unavailable source")); }
            var text = editor.Source.Text;
            var catalogue = f.Catalogue.Candidates;
            var items = editor.Files.ItemsSource;
            var selected = editor.Files.SelectedItem;
            var query = editor.QueryInput.Text;
            var sourceBytes = f.Source?.Document.CopyBytes();
            var preference = Path.Combine(f.Session.LocalRoot, "Preferences", kind == ArtifactKind.Instruction ? "instruction-revisions.json" : "prompt-revisions.json");
            var preferenceBytes = File.Exists(preference) ? File.ReadAllBytes(preference) : null;
            f.Store.Reads = 0; f.Store.Lists = 0; f.Store.Writes = 0;
            f.Store.DenyAccess = true;
            await f.Run("Kora, " + f.Prefix + " validate");
            f.Status.Should().Contain(state is "invalid" ? "Prepared Save proposal rejected" : "Supported structural draft observed");
            f.Status.Should().ContainEquivalentOf("nothing saved/enabled").And.NotContain("PRIVATE_UNSAVED_BODY");
            if (state is "unavailable") { f.Status.Should().Contain("Source/catalogue unavailable"); }
            if (state is "uncertain") { f.Status.Should().Contain("Existing uncertain publication/source hold is unchanged"); }
            editor.Source.Text.Should().Be(text);
            editor.Files.ItemsSource.Should().BeSameAs(items);
            editor.Files.SelectedItem.Should().BeSameAs(selected);
            editor.QueryInput.Text.Should().Be(query);
            f.Catalogue.Candidates.Should().BeSameAs(catalogue);
            f.Store.Reads.Should().Be(0);
            f.Store.Lists.Should().Be(0);
            f.Store.Writes.Should().Be(0);
            if (sourceBytes is not null) { File.ReadAllBytes(f.SourcePath).Should().Equal(sourceBytes); }
            else { File.Exists(f.SourcePath).Should().BeFalse(); }
            if (preferenceBytes is not null) { File.ReadAllBytes(preference).Should().Equal(preferenceBytes); }
            if (state is "enabled")
            { f.Catalogue.Candidates.Single(item => item.Kind == kind && item.OwnedInstructionName is "example").IsUseEnabled.Should().BeTrue(); }
            var delivered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            foreach (var block in editor.GetVisualDescendants().OfType<TextBlock>())
            {
                block.PropertyChanged += (_, change) =>
                {
                    if (change.Property == TextBlock.TextProperty && block.Text?.Contains("Historical observation only.", StringComparison.Ordinal) == true)
                    { delivered.TrySetResult(); }
                };
            }
            editor.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "Validate"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await delivered.Task.WaitAsync(TestContext.Current.CancellationToken);
            editor.Source.Text.Should().Be(text);
            editor.Source.Text += "\r\nLater draft.";
            f.Status.Should().NotContain("Supported structural draft observed").And.NotContain("Prepared Save proposal rejected");
            if (state is "unavailable" or "uncertain")
            {
                await f.Run(f.Prefix + " save");
                f.Status.Should().Contain("Reload/reconcile first");
                f.Store.Reads.Should().Be(0);
                f.Store.Writes.Should().Be(0);
            }
        });
    }

    [Theory]
    [InlineData("draft")]
    [InlineData("name")]
    [InlineData("kind")]
    [InlineData("selection")]
    [InlineData("catalogue")]
    [InlineData("owner")]
    [InlineData("privacy")]
    [InlineData("input")]
    public async Task LateValidationCannotPublishAfterCapturedDraftOrPrivateEpochChanges(string change)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            await using var f = await DraftValidationFixture.Create(ArtifactKind.Instruction, false);
            var editor = f.Editor!;
            f.Store.DenyAccess = true;
            f.Sessions.PauseNext = true;
            var validation = f.Run("instructions validate");
            await f.Sessions.Started.Task.WaitAsync(TestContext.Current.CancellationToken);
            switch (change)
            {
                case "draft": editor.Source.Text += "\nChanged while validation admitted."; break;
                case "name": editor.NameInput.Text = "different"; break;
                case "kind": editor.SelectKind(ArtifactKind.Prompt); break;
                case "selection": editor.Files.SelectedItem = null; break;
                case "catalogue": f.Catalogue.Publish(f.Catalogue.Candidates); break;
                case "owner": f.Ownership++; break;
                case "privacy": f.Session.Main.CloseForObservedPrivacyEvent("Synthetic validation closure", hidePresentation: true); break;
                case "input": f.Session.Main.CommandText = "instructions save"; break;
            }
            f.Sessions.Release.TrySetResult();
            await validation;
            f.Status.Should().NotContain("Supported structural draft observed").And.NotContain("Historical observation only");
            f.Store.Reads.Should().Be(0);
            f.Store.Lists.Should().Be(0);
            f.Store.Writes.Should().Be(0);
            if (change is "privacy")
            {
                await f.Session.Main.PrivacyClosureTask;
                editor.Source.Text.Should().BeEmpty();
                editor.IsVisible.Should().BeFalse();
            }
        });
    }

    [Theory]
    [InlineData("different-kind")]
    [InlineData("name")]
    [InlineData("readonly")]
    [InlineData("missing-baseline")]
    [InlineData("closed")]
    [InlineData("owner")]
    [InlineData("host")]
    public async Task ValidateCannotOpenSwitchRetargetRepairOrInventAnOwnedDraft(string condition)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            await using var f = await DraftValidationFixture.Create(ArtifactKind.Instruction, false);
            var editor = f.Editor!;
            var original = editor.Source.Text;
            if (condition is "name") { editor.NameInput.Text = "../untrusted-path"; }
            if (condition is "readonly") { editor.Source.IsReadOnly = true; }
            if (condition is "missing-baseline") { await f.Run("instructions inspect missing"); original = editor.Source.Text; }
            if (condition is "closed") { editor.Close(); original = editor.Source.Text; }
            if (condition is "owner") { f.Ownership++; original = string.Empty; }
            if (condition is "host") { f.Owned = false; original = string.Empty; }
            f.Store.Reads = 0;
            f.Store.DenyAccess = true;
            await f.Run(condition is "different-kind" ? "prompts validate" : "instructions validate");
            f.Status.Should().NotContain("Supported structural draft observed");
            editor.Source.Text.Should().Be(original);
            f.Store.Reads.Should().Be(0);
            f.Store.Lists.Should().Be(0);
            f.Store.Writes.Should().Be(0);
            if (condition is "closed" or "owner" or "host") { editor.IsVisible.Should().BeFalse(); }
            else { editor.KindInput.SelectedItem.Should().Be(ArtifactKind.Instruction); }
        });
    }

    private sealed class DraftValidationFixture : IAsyncDisposable
    {
        internal NativeUxFixtureSession Session { get; }
        internal ValidationSessionFence Sessions { get; }
        internal InstructionControlAdmission Admission { get; }
        internal ArtifactCatalogue Catalogue { get; } = EmbeddedArtifactCatalogue.Load();
        internal ValidationStore Store { get; }
        internal OwnedInstructionEditorActions Actions { get; }
        internal OwnedInstructionSnapshot? Source { get; private set; }
        internal OwnedInstructionEditorWindow? Editor { get; private set; }
        internal long Ownership = 1;
        internal bool Owned = true;
        internal string Prefix { get; }
        private readonly RestoreTestTime restoreClock = new();
        internal void ExpireRestore() => restoreClock.Now += TimeSpan.FromMinutes(2);
        internal string SourcePath => Path.Combine(Session.RoamingRoot, Prefix is "instructions" ? "Instructions" : "Prompts",
            "example" + (Prefix is "instructions" ? ".instructions.md" : ".prompt.md"));
        internal string Status => Editor is null ? string.Empty : string.Join("\n", Editor.GetVisualDescendants().OfType<TextBlock>().Select(block => block.Text));
        private readonly WindowsOwnedInstructionStore nativeInstructions;
        private readonly WindowsOwnedInstructionStore nativePrompts;
        private readonly ArtifactCatalogueReloadService reload;
        private readonly OwnedInstructionEditorController controller;
        private readonly IDisposable opened;

        private DraftValidationFixture(NativeUxFixtureSession session, ArtifactKind kind)
        {
            Session = session;
            Sessions = new(session.Interactions);
            Admission = new(session.Interactions, Sessions, new HostTaskCoordinator(session.Tasks));
            nativeInstructions = new(session);
            nativePrompts = new(session, ArtifactKind.Prompt);
            Store = new(kind == ArtifactKind.Instruction ? nativeInstructions : nativePrompts);
            var instructions = new InstructionRevisionCatalogue(session);
            var prompts = new InstructionRevisionCatalogue(session, ArtifactKind.Prompt);
            var controls = new BuiltInCommandRouter(new BuiltInCommandCatalog());
            var instructionActions = new OwnedInstructionEditorActions(kind == ArtifactKind.Instruction ? Store : nativeInstructions,
                Admission, instructions, Catalogue, session.Audit, controls);
            var promptActions = new OwnedInstructionEditorActions(kind == ArtifactKind.Prompt ? Store : nativePrompts,
                Admission, prompts, Catalogue, session.Audit, controls, ArtifactKind.Prompt);
            Actions = kind == ArtifactKind.Instruction ? instructionActions : promptActions;
            var preferences = new LocalArtifactDirectoryPreferences(session);
            reload = new(Catalogue, session, preferences, new ArtifactDirectoryConfigurationService(preferences, session.Audit),
                new McpServerConfigurationService(session, preferences, Path.Combine(session.LocalRoot, "no-profile")),
                NullLogger<ArtifactCatalogueReloadService>.Instance, instructions, nativeInstructions, Path.Combine(session.LocalRoot, "no-profile"), prompts, nativePrompts);
            controller = new(session.Main, instructionActions, new(instructionActions), reload, session,
                () => Owned, () => Ownership, session.Loggers.CreateLogger<OwnedInstructionEditorController>(), promptActions, new(promptActions),
                restoreTime: restoreClock);
            Prefix = kind == ArtifactKind.Instruction ? "instructions" : "prompts";
            opened = Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) =>
            { if (window is OwnedInstructionEditorWindow editor) { Editor = editor; } });
            controller.Bind();
        }

        private sealed class RestoreTestTime : TimeProvider
        {
            internal DateTimeOffset Now = DateTimeOffset.UtcNow;
            public override DateTimeOffset GetUtcNow() => Now;
        }

        internal static async Task<DraftValidationFixture> Create(ArtifactKind kind, bool newDraft,
            IUiDispatcher? dispatcher = null)
        {
            var parent = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".net-test-artifacts"));
            Directory.CreateDirectory(parent);
            var session = new NativeUxFixtureSession(parent);
            await session.InitializeAsync(null, dispatcher: dispatcher);
            var fixture = new DraftValidationFixture(session, kind);
            if (newDraft) { await fixture.Run(fixture.Prefix + " create example"); }
            else
            {
                var store = kind == ArtifactKind.Instruction ? fixture.nativeInstructions : fixture.nativePrompts;
                fixture.Source = (await store.Write("example", null, Document(kind, "PRIVATE_ORIGINAL_BODY"), () => true, session.Token)).Current!;
                fixture.reload.ReconcileSavedCopy(fixture.Source).Should().BeTrue();
                await fixture.Run(fixture.Prefix + " list");
                await fixture.Run(fixture.Prefix + " inspect example");
                fixture.Editor!.Files.SelectedItem = fixture.Editor.Files.ItemsSource!.Cast<OwnedDefinitionInventoryRow>().Single(row => row.Name is "example");
            }
            fixture.Store.Reads = 0; fixture.Store.Writes = 0; fixture.Store.Lists = 0;
            return fixture;
        }

        internal async Task Run(string command)
        { Session.Main.CommandText = command; await Session.Main.RunTypedCommand.ExecuteAsync(); }
        internal void DisposeController() => controller.Dispose();
        internal Task WaitForEditorOperation() => controller.WaitForOperation();
        internal void FailAfterInstructionPublication() => nativeInstructions.AfterPublication = () => throw new IOException("Synthetic readback unavailable");
        public async ValueTask DisposeAsync()
        {
            controller.Dispose();
            opened.Dispose();
            reload.Dispose();
            await Admission.DisposeAsync();
            await Session.Main.PrivacyClosureTask;
            Session.Dispose();
        }
    }

    private sealed class ValidationSessionFence(IInstructionControlSessionStore inner) : IInstructionControlSessionStore
    {
        internal bool PauseNext;
        internal readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ValueTask<WorkSessionAuthorization> CreateInstructionSession(HostRequest request, Func<bool> admitted, CancellationToken token) =>
            inner.CreateInstructionSession(request, admitted, token);
        public async ValueTask<T> WithInstructionSession<T>(HostRequest request, HostRevision generation, Func<T> operation, CancellationToken token)
        {
            if (PauseNext)
            {
                PauseNext = false;
                Started.TrySetResult();
                await Release.Task.WaitAsync(token);
            }
            return await inner.WithInstructionSession(request, generation, operation, token);
        }
    }

    private sealed class ValidationStore(IOwnedInstructionStore inner) : IOwnedInstructionStore
    {
        internal Func<string, CancellationToken, Task<OwnedInstructionSnapshot?>>? ReadOverride;
        internal bool DenyAccess;
        internal Action? AfterWrite;
        internal int Reads;
        internal int Lists;
        internal int Writes;
        private void RequireAccess()
        { if (DenyAccess) { throw new InvalidOperationException("Validation must not access the owned store."); } }
        public Task<IReadOnlyList<string>> List(CancellationToken token)
        { Lists++; RequireAccess(); return inner.List(token); }
        public Task<OwnedInstructionSnapshot?> Read(string name, CancellationToken token)
        { Reads++; RequireAccess(); return ReadOverride is { } read ? read(name, token) : inner.Read(name, token); }
        public async Task<InstructionWriteResult> Write(string name, OwnedInstructionSnapshot? expected, ArtifactFileDocument? replacement,
            Func<bool> eligible, Func<Func<InstructionWriteResult>, Task<InstructionWriteResult>> commit, CancellationToken token)
        {
            Writes++; RequireAccess();
            var result = await inner.Write(name, expected, replacement, eligible, commit, token);
            AfterWrite?.Invoke();
            return result;
        }
    }
}