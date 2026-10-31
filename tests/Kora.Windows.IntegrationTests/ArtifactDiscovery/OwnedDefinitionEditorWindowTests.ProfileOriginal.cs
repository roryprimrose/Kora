using System.Security.Principal;

using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Application;
using Kora.Application.Configuration;
using Kora.Application.Definitions;
using Kora.Application.Hosting;
using Kora.Application.Interaction;
using Kora.Core.Artifacts;
using Kora.Core.Auditing;
using Kora.Core.Commands;
using Kora.Core.Communication;
using Kora.Core.Customizations;
using Kora.Core.Dependencies;
using Kora.Definitions.Artifacts;
using Kora.NativeUxFixture;
using Kora.Windows.Artifacts;
using Kora.Windows.Storage;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests.ArtifactDiscovery;

public sealed partial class OwnedDefinitionEditorWindowTests
{
    private static readonly string[] ProfileOriginalForbiddenCommands = ["instructions refine", "instructions restore"];

    [Theory]
    [InlineData("complete", "Code", false)]
    [InlineData("complete", "Code - Insiders", true)]
    [InlineData("unregistered", "Code", false)]
    [InlineData("privacy", "Code", false)]
    [InlineData("call", "Code", false)]
    [InlineData("owner", "Code", false)]
    [InlineData("input", "Code", false)]
    [InlineData("withdraw", "Code", false)]
    [InlineData("replaced", "Code", false)]
    [InlineData("missing", "Code", false)]
    [InlineData("uncertain", "Code", false)]
    [InlineData("reload-failure", "Code", false)]
    [InlineData("dictation", "Code", false)]
    [InlineData("owned-routes", "Code", false)]
    [InlineData("owned-routes", "Code - Insiders", true)]
    public Task RegisteredOriginalUsesSameEditorSeparateExactSaveConflictRecoveryAndNoNewAuthority(string state, string editorName, bool namedProfile) =>
        HeadlessSession.RunAsync(async () =>
        {
            var parent = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".net-test-artifacts"));
            Directory.CreateDirectory(parent);
            using var f = new NativeUxFixtureSession(parent, syntheticVoice: state is "dictation" or "owned-routes");
            var calls = new ProfileOriginalCallState();
            await f.InitializeAsync(null, callState: calls);
            if (state is "dictation")
            {
                f.Main.BindVoiceOwnershipGate(static () => true);
                await f.Main.SetVoiceConsentAsync(true);
            }
            var dictation = state is "dictation"
                ? f.Main.BindComposerDictation(NullLogger<Kora.Application.Voice.ComposerDictationService>.Instance) : null;
            await using var admission = new InstructionControlAdmission(f.Interactions, f.Interactions, new HostTaskCoordinator(f.Tasks));
            var profile = Path.Combine(f.LocalRoot, "selected-profile");
            var user = Path.Combine(profile, editorName, "User");
            var directory = namedProfile ? Path.Combine(user, "profiles", "fixture", "prompts") : Path.Combine(user, "prompts");
            Directory.CreateDirectory(Path.GetDirectoryName(user)!);
            using var identity = WindowsIdentity.GetCurrent();
            foreach (var path in namedProfile ? new[] { user, Path.Combine(user, "profiles"), Path.Combine(user, "profiles", "fixture"), directory }
                : new[] { user, directory })
            { RestrictedStorageDirectory.CreateRestrictedDirectory(path, identity.User!); }
            var sourcePath = Path.Combine(directory, "example.instructions.md");
            var text = "---\r\nkora-format: 1\r\nname: 'example'\r\ndescription: \"Profile original\"\r\nattribution: 'Author'\r\n---\r\nExact original.\r\n";
            var bytes = ArtifactFileDocument.Encode(text, true);
            WindowsOwnedInstructionStoreTests.WriteOwnedFile(sourcePath, bytes);
            var originals = new WindowsCustomizationStore(f, profile, Path.Combine(f.LocalRoot, "no-personal-root"));
            var source = (await originals.List(f.Token)).Single(item => item.Kind == CustomizationKind.Instruction && item.Scope == CustomizationScope.User);
            var use = new ArtifactUsePreferences(f);
            using var catalogue = new ArtifactCatalogue(EmbeddedArtifactCatalogue.Load().Artifacts, use);
            var management = new CustomizationManagementService(originals, admission, use, catalogue, f.Audit);
            var profileWorkflow = new ProfileInstructionEditorWorkflow(management);
            if (state is not "unregistered")
            { (await management.RegisterInstruction(source, (_, _) => Task.FromResult(true), () => true, f.Token)).Should().BeTrue(); }
            var ownedStore = new WindowsOwnedInstructionStore(f);
            var revisions = new InstructionRevisionCatalogue(f);
            var actions = new OwnedInstructionEditorActions(ownedStore, admission, revisions, catalogue, f.Audit,
                new BuiltInCommandRouter(new BuiltInCommandCatalog()));
            var preferences = new LocalArtifactDirectoryPreferences(f);
            using var reload = new ArtifactCatalogueReloadService(catalogue, f, preferences,
                new ArtifactDirectoryConfigurationService(preferences, f.Audit), new McpServerConfigurationService(f, preferences, profile),
                NullLogger<ArtifactCatalogueReloadService>.Instance, revisions, ownedStore, profile, use: use);
            var ownsHost = true;
            long ownerRevision = 1;
            using var questionHost = new NativeQuestionHost(f.Interactions, TimeProvider.System,
                NullLogger<NativeQuestionViewModel>.Instance);
            using var questions = new QuestionWindowController(f.Main, questionHost, f.VersionQuery, new AssemblyApplicationInfo(),
                new NativeDetailRenderer(NullLogger<NativeDetailRenderer>.Instance), () => ownsHost,
                NullLogger<QuestionWindowController>.Instance, () => ownerRevision);
            var model = new ProfileOriginalForbiddenModelRunner();
            var refinement = new OwnedInstructionRefinementService(actions, ownedStore, admission, model, TimeProvider.System);
            using var controller = new OwnedInstructionEditorController(f.Main, actions, new(actions), reload, f,
                () => ownsHost, () => ownerRevision, NullLogger<OwnedInstructionEditorController>.Instance,
                saveQuestions: questions, profileOriginals: profileWorkflow, dictation: dictation, refinement: refinement);
            OwnedInstructionEditorWindow? editor = null;
            var next = new TaskCompletionSource<Window>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var opened = Window.WindowOpenedEvent.AddClassHandler<Window>((view, _) =>
            {
                if (view is OwnedInstructionEditorWindow selected) { editor = selected; }
                else { next.TrySetResult(view); }
            });
            controller.Bind();
            f.Main.CommandText = "instructions editor";
            await f.Main.RunTypedCommand.ExecuteAsync();
            editor.Should().NotBeNull();
            await controller.WaitForOperation();
            Click(editor!, "Open registered profile original");
            if (state is "unregistered")
            {
                await controller.WaitForOperation();
                editor!.Source.Text.Should().BeNullOrEmpty();
                editor.GetVisualDescendants().OfType<TextBlock>().Should().Contain(block => block.Text != null
                    && block.Text.Contains("No exact profile instruction is registered", StringComparison.Ordinal));
                originals.RegisteredInstruction.Should().BeNull();
                File.ReadAllBytes(sourcePath).Should().Equal(bytes);
                return;
            }
            var selection = await AwaitProfileReview(next.Task, editor!, f.Token);
            selection.GetVisualDescendants().OfType<TextBox>().Select(box => box.Text).Should()
                .NotContain(value => value != null && value.Contains("Exact original.", StringComparison.Ordinal));
            Click(selection, "Cancel");
            await controller.WaitForOperation();
            editor!.Source.Text.Should().BeNullOrEmpty();
            next = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Click(editor, "Open registered profile original");
            selection = await AwaitProfileReview(next.Task, editor, f.Token);
            Click(selection, "Open exact registered original");
            await controller.WaitForOperation();
            editor.Source.Text.Should().Be(text);
            editor.Source.IsReadOnly.Should().BeFalse();
            editor.ReviewUse.IsEnabled.Should().BeFalse();
            File.ReadAllBytes(sourcePath).Should().Equal(bytes);
            if (state is "dictation")
            {
                editor.Activate();
                Dispatcher.UIThread.RunJobs();
                editor.Source.CaretIndex = text.Length;
                editor.Source.SelectionStart = text.Length;
                editor.Source.SelectionEnd = text.Length;
                var hold = editor.GetVisualDescendants().OfType<Button>().Single(button => button.Name is "DictateInstructionBody");
                hold.Focus().Should().BeTrue();
                editor.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
                var attempt = () => editor.BodyDictation!.Operation;
                await attempt.Should().ThrowAsync<InvalidOperationException>();
                editor.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
                f.Voice.StartCalls.Should().Be(0);
                editor.Source.Text.Should().Be(text);
                File.ReadAllBytes(sourcePath).Should().Equal(bytes);
                originals.RegisteredInstruction.Should().NotBeNull();
                Directory.EnumerateFiles(Path.Combine(f.RoamingRoot, "Instructions"), "*", SearchOption.AllDirectories)
                    .Should().BeEmpty();
                return;
            }
            var draft = text.Replace("Exact original.", "Exact edited draft.", StringComparison.Ordinal);
            editor.Source.Text = draft;
            next = new(TaskCreationOptions.RunContinuationsAsynchronously);
            f.Main.CommandText = "instructions save";
            next = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var save = f.Main.RunTypedCommand.ExecuteAsync();
            var review = await AwaitProfileReview(next.Task, editor, f.Token);
            review.Title.Should().Be("Save exact registered profile instruction original");
            review.GetVisualDescendants().OfType<Button>().Should().NotContain(button =>
                Equals(button.Content, "Reviewed complete source: exact save question"));
            questions.IsOwnerInUse.Should().BeFalse("registered originals cannot acquire owned-instruction spoken Save authority");
            var privatePanes = review.GetVisualDescendants().OfType<TextBox>().ToArray();
            privatePanes.Select(box => box.Text).Should().Equal(text, draft);
            if (state is "privacy" or "call")
            {
                if (state is "call") { calls.Set(CallState.Active); }
                else { f.Main.CloseForObservedPrivacyEvent("Synthetic original editor privacy closure", hidePresentation: true); }
                await save;
                await f.Main.PrivacyClosureTask;
                if (state is "call")
                {
                    editor.Source.Text.Should().Be(draft);
                    editor.IsVisible.Should().BeTrue();
                    editor.ReviewUse.IsEnabled.Should().BeFalse();
                }
                else
                {
                    editor.Source.Text.Should().BeEmpty();
                    editor.IsVisible.Should().BeFalse();
                }
                privatePanes.Should().OnlyContain(box => string.IsNullOrEmpty(box.Text));
                File.ReadAllBytes(sourcePath).Should().Equal(bytes);
                return;
            }
            if (state is "owner") { ownsHost = false; ownerRevision++; }
            if (state is "input") { f.Main.CommandText = "different original request"; }
            if (state is "withdraw") { management.RetireInstructionRegistration(source); }
            if (state is "replaced")
            {
                File.Move(sourcePath, sourcePath + ".previous");
                WindowsOwnedInstructionStoreTests.WriteOwnedFile(sourcePath, bytes);
            }
            if (state is "missing") { File.Delete(sourcePath); }
            if (state is "reload-failure") { catalogue.ReportReloadFailure(new IOException("Synthetic catalogue failure")); }
            if (state is "uncertain") { originals.AfterComponentPublished = _ => throw new IOException("Synthetic readback unavailable"); }
            if (state is not ("complete" or "owned-routes"))
            {
                Click(review, "Confirm save original");
                await save;
                if (state is "uncertain")
                {
                    File.ReadAllBytes(sourcePath).Should().Equal(ArtifactFileDocument.Encode(draft, true));
                    originals.RegisteredInstruction.Should().BeNull();
                    use.IsDisabled(source.Id).Should().BeTrue();
                    originals.AfterComponentPublished = null;
                    next = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    var held = f.Main.RunTypedCommand.ExecuteAsync();
                    await held;
                    next.Task.IsCompleted.Should().BeFalse("unconfirmed publication blocks another Save review");
                    (await management.RegisterInstruction(source, (_, _) => Task.FromResult(true), () => true, f.Token)).Should().BeTrue();
                    f.Main.CommandText = "instructions reload";
                    next = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    var reconciliation = f.Main.RunTypedCommand.ExecuteAsync();
                    var discard = await AwaitProfileReview(next.Task, editor, f.Token);
                    Click(discard, "Discard draft and continue");
                    await reconciliation;
                    editor.Source.Text.Should().Be(draft);
                    use.IsDisabled(source.Id).Should().BeTrue();
                }
                else if (state is "missing") { File.Exists(sourcePath).Should().BeFalse(); }
                else { File.ReadAllBytes(sourcePath).Should().Equal(bytes); }
                return;
            }
            Click(review, "Cancel");
            await save;
            File.ReadAllBytes(sourcePath).Should().Equal(bytes);
            editor.Source.Text.Should().Be(draft);
            var external = text + "\r\nExternal change.";
            File.WriteAllBytes(sourcePath, ArtifactFileDocument.Encode(external, true));
            next = new(TaskCreationOptions.RunContinuationsAsynchronously);
            save = f.Main.RunTypedCommand.ExecuteAsync();
            review = await AwaitProfileReview(next.Task, editor, f.Token);
            next = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Click(review, "Confirm save original");
            var conflict = await AwaitProfileReview(next.Task, editor, f.Token);
            conflict.Title.Should().Be("Profile instruction original conflict");
            conflict.GetVisualDescendants().OfType<TextBox>().Select(box => box.Text).Should().Equal(external, draft);
            catalogue.Publish(catalogue.SourceCandidates.Select(item => item.Kind == ArtifactKind.Instruction
                ? item with { IsUseEnabled = false } : item).ToArray());
            conflict.IsVisible.Should().BeTrue("owned-inventory refresh cannot approve or dismiss a registered-original conflict");
            next = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Click(conflict, "Overwrite with Kora changes");
            var overwrite = await AwaitProfileReview(next.Task, editor, f.Token);
            overwrite.GetVisualDescendants().OfType<TextBox>().Select(box => box.Text).Should().Equal(external, draft);
            File.ReadAllBytes(sourcePath).Should().Equal(ArtifactFileDocument.Encode(external, true));
            Click(overwrite, "Confirm save original");
            await save;
            File.ReadAllBytes(sourcePath).Should().Equal(ArtifactFileDocument.Encode(draft, true));
            editor.Source.Text.Should().Be(draft);
            use.IsDisabled(source.Id).Should().BeTrue();
            catalogue.Candidates.Single(item => item.Kind == ArtifactKind.Instruction
                && string.Equals(item.UseControlId, source.Id, StringComparison.Ordinal)).IsUseEnabled.Should().BeFalse();
            editor.ReviewUse.IsEnabled.Should().BeFalse();
            privatePanes.Should().OnlyContain(box => string.IsNullOrEmpty(box.Text));
            using var database = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = Path.Combine(f.LocalRoot, WindowsSqliteEvidenceSink.PartitionName, "evidence.db"),
                Mode = SqliteOpenMode.ReadOnly, Pooling = false,
            }.ToString());
            await database.OpenAsync(f.Token);
            using var receipt = database.CreateCommand();
            receipt.CommandText = "SELECT count(*) FROM security_audit_events WHERE action_id='definition.source.save' AND target_id=$source AND audit_outcome=$outcome;";
            receipt.Parameters.AddWithValue("$source", source.Id);
            receipt.Parameters.AddWithValue("$outcome", (int)SecurityAuditOutcome.Succeeded);
            ((long)(await receipt.ExecuteScalarAsync(f.Token))!).Should().Be(1);
            if (state is "owned-routes")
            {
                foreach (var command in ProfileOriginalForbiddenCommands)
                {
                    next = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    f.Main.CommandText = command;
                    await f.Main.RunTypedCommand.ExecuteAsync();
                    next.Task.IsCompleted.Should().BeFalse("owned operations cannot review a registered profile original");
                    editor.Source.Text.Should().Be(draft);
                    File.ReadAllBytes(sourcePath).Should().Equal(ArtifactFileDocument.Encode(draft, true));
                    editor.RestorePrevious.IsEnabled.Should().BeFalse("a confirmed profile-original Save cannot establish an owned restore point");
                    editor.ReviewUse.IsEnabled.Should().BeFalse();
                    originals.RegisteredInstruction.Should().NotBeNull();
                    use.IsDisabled(source.Id).Should().BeTrue();
                }
                model.Calls.Should().Be(0);
                f.Voice.StartCalls.Should().Be(0);
                questions.IsOwnerInUse.Should().BeFalse();
                Directory.EnumerateFiles(directory).Should().ContainSingle().Which.Should().Be(sourcePath);
                Directory.EnumerateFiles(Path.Combine(f.RoamingRoot, "Instructions"), "*", SearchOption.AllDirectories)
                    .Should().BeEmpty();
                ((long)(await receipt.ExecuteScalarAsync(f.Token))!).Should().Be(1, "denied owned routes cannot publish another original Save receipt");
                return;
            }
            editor.Source.Text += "\r\nUnsent.";
            next = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var closing = f.Main.TryPrepareHandoffAsync();
            var dirty = await AwaitProfileReview(next.Task, editor, f.Token);
            Click(dirty, "Cancel");
            (await closing).Should().BeFalse();
            editor.Source.Text.Should().EndWith("Unsent.");
            next = new(TaskCreationOptions.RunContinuationsAsynchronously);
            closing = f.Main.TryPrepareHandoffAsync();
            dirty = await AwaitProfileReview(next.Task, editor, f.Token);
            next = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Click(dirty, "Save");
            var closingSave = await AwaitProfileReview(next.Task, editor, f.Token);
            var closingDraft = draft + "\r\nUnsent.";
            closingSave.GetVisualDescendants().OfType<TextBox>().Select(box => box.Text).Should().Equal(draft, closingDraft);
            Click(closingSave, "Confirm save original");
            (await closing).Should().BeTrue();
            File.ReadAllBytes(sourcePath).Should().Equal(ArtifactFileDocument.Encode(closingDraft, true));
            use.IsDisabled(source.Id).Should().BeTrue();
            f.Main.AbandonHandoffPreparation();
            editor.Source.Text += "\r\nDiscarded.";
            next = new(TaskCreationOptions.RunContinuationsAsynchronously);
            closing = f.Main.TryPrepareHandoffAsync();
            dirty = await AwaitProfileReview(next.Task, editor, f.Token);
            Click(dirty, "Discard");
            (await closing).Should().BeTrue();
            File.ReadAllBytes(sourcePath).Should().Equal(ArtifactFileDocument.Encode(closingDraft, true));
        });

    private sealed class ProfileOriginalForbiddenModelRunner : IModelContextRunner
    {
        internal int Calls;

        public Task<ModelContextAdmission> AdmitAsync(LocalContextSessionTarget target, Func<bool> sourceCurrent,
            CancellationToken token, Func<CancellationToken, Task>? validateSource = null)
        {
            Calls++;
            throw new InvalidOperationException("A profile original must not reach owned instruction model admission.");
        }
    }

    private sealed class ProfileOriginalCallState : ICallStateService
    {
        public event EventHandler<CallStateChangedEventArgs>? StateChanged;
        public CallState CurrentState { get; private set; } = CallState.Clear;
        internal void Set(CallState state) { CurrentState = state; StateChanged?.Invoke(this, new(state)); }
    }
}
