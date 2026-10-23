using Avalonia.Controls;
using Avalonia.Interactivity;

using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Application.Dependencies;
using Kora.Application.Diagnostics;
using Kora.Application.Hosting;
using Kora.Application.Tools;
using Kora.Application.Voice;
using Kora.Core.Auditing;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Providers;
using Kora.Core.Storage;
using Kora.Core.Tools;
using Kora.Windows.IntegrationTests.Storage;
using Kora.Windows.Storage;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed partial class SessionsConversationTests
{
    [Fact]
    public async Task NativeComposerActualProductionHostRetainsExactMessageAndReportsUnavailableWithoutLegacyRuntimeFallback()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            await using var composed = new Composition(f);
            var model = composed.Model;
            var window = new SessionsWindow(model);
            window.Show();
            try
            {
                await model.RefreshAsync();
                await model.SelectAsync(model.Sessions.Single());
                await model.ReadConversationAsync();
                const string body = "  exact native user message e\u0301🙂\r\n ending  ";
                window.FindControl<TextBox>("ConversationComposer")!.Text = body;
                model.ComposerBody.Should().Be(body);
                model.CanSubmitConversation.Should().BeTrue();
                window.FindControl<Button>("SubmitConversation")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await window.ConversationOperation;
                model.ConversationStatus.Should().Contain("Unavailable").And.Contain("UnregisteredModel");
                model.ComposerBody.Should().BeEmpty();
                (await f.Store.ReadConversationPageAsync(f.Request.SessionId, null, 25, f.Token)).Messages.Single().Body.Should().Be(body);
                composed.Audit.Events.Should().NotBeEmpty();
                f.Count("host_questions").Should().Be(0);
                f.Count("scoped_grants").Should().Be(0);
                model.ConversationStatus.Should().NotContain("answer:").And.NotContain("Succeeded");
            }
            finally { window.ClosePrivate(); }
        });
    }

    [Fact]
    public async Task DirtySessionSwitchRetainsSeparateExactBuffersWithoutDecisionSaveOrSubmission()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        var original = f.Request.SessionId;
        f.Request = InteractionStorageFixture.NewRequest();
        await f.AdmitAsync(newSession: true);
        await WindowsSqliteConversationTests.FinishInitial(f);
        await using var composed = new Composition(f);
        var model = composed.Model;
        await model.RefreshAsync();
        await model.SelectAsync(model.Sessions.Single(entry => entry.Authority.SessionId == original));
        await model.ReadConversationAsync();
        model.ComposerBody = "unsaved original-session buffer";
        model.DecideComposerDraft = () => throw new InvalidOperationException("Switching topics must not prompt.");
        await model.SelectAsync(model.Sessions.Single(entry => entry.Authority.SessionId != original));
        model.SelectedSessionRecord!.Authority.SessionId.Should().NotBe(original);
        model.ComposerBody.Should().BeEmpty();
        model.ComposerBody = "second topic input";
        await model.SelectAsync(model.Sessions.Single(entry => entry.Authority.SessionId == original));
        model.ComposerBody.Should().Be("unsaved original-session buffer");
        await model.SelectAsync(model.Sessions.Single(entry => entry.Authority.SessionId != original));
        model.ComposerBody.Should().Be("second topic input");
        var saved = await f.Store.ReadConversationDraftAsync(original, f.Token);
        saved.Draft.Body.Should().BeNull();
        f.Count("conversation_messages").Should().Be(0);
        model.Close();
    }

    [Fact]
    public async Task PrivacyClosingClearsPrivateDraftAndLateControlsWithoutImplicitSaveOrSubmission()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            await using var composed = new Composition(f);
            var model = composed.Model;
            var window = new SessionsWindow(model);
            window.Show();
            await model.RefreshAsync();
            await model.SelectAsync(model.Sessions.Single());
            model.ComposerBody = "private unsaved buffer";
            window.ClosePrivate();
            model.ComposerBody.Should().BeEmpty();
            model.ConversationMessages.Should().BeEmpty();
            model.CanSubmitConversation.Should().BeFalse();
            (await f.Store.ReadConversationDraftAsync(f.Request.SessionId, f.Token)).Draft.Revision.Should().Be(0);
            f.Count("conversation_messages").Should().Be(0);
        });
    }

    [Fact]
    public async Task LegacyRecoveryReadsOnlyExactExistingTextIntoEmptyInputWithoutWritesOrSubmission()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        var legacy = await WindowsSqliteConversationTests.Save(f,
            await f.Store.ReadConversationDraftAsync(f.Request.SessionId, f.Token), "existing legacy text");
        var audits = WindowsSqliteConversationTests.AuditText(f);
        var retention = await f.Store.ReadRetentionAsync(f.Request.SessionId, f.Token);
        await using var composed = new Composition(f);
        var model = composed.Model;
        await model.RefreshAsync();
        await model.SelectAsync(model.Sessions.Single());
        await model.ReadConversationAsync();
        model.ComposerBody.Should().BeEmpty("reading history never imports legacy input");
        model.CanRecoverConversationDraft.Should().BeTrue();
        await model.RecoverLegacyUnsentAsync();
        model.ComposerBody.Should().Be("existing legacy text");
        model.ComposerDirty.Should().BeTrue();
        model.CanRecoverConversationDraft.Should().BeFalse();
        model.ComposerBody = "new unsent input";
        await model.RecoverLegacyUnsentAsync();
        model.ComposerBody.Should().Be("new unsent input", "legacy recovery must never overwrite dirty text");
        (await f.Store.ReadConversationDraftAsync(f.Request.SessionId, f.Token)).Should().Be(legacy);
        (await f.Store.ReadRetentionAsync(f.Request.SessionId, f.Token)).Should().Be(retention);
        WindowsSqliteConversationTests.AuditText(f).Should().Be(audits);
        f.Count("conversation_messages").Should().Be(0);
        f.Count("host_questions").Should().Be(0);
        model.Close();
    }

    [Fact]
    public async Task InvalidBodyRefusalLeavesMemoryEditableAndNeverCreatesARequestOrImplicitSave()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        await using var composed = new Composition(f);
        var model = composed.Model;
        await model.RefreshAsync();
        await model.SelectAsync(model.Sessions.Single());
        model.ComposerBody = new string('x', 4097);
        await model.SubmitConversationAsync();
        model.CanCompose.Should().BeTrue();
        model.ComposerBody.Length.Should().Be(4097);
        f.Count("conversation_messages").Should().Be(0);
        model.ComposerBody = "corrected original message";
        await model.SubmitConversationAsync();
        f.Count("conversation_messages").Should().Be(1);
        model.ConversationStatus.Should().Contain("Unavailable");
    }

    [Fact]
    public async Task DetectedCredentialRefusalKeepsOnlyMemoryDraftForCorrectionAndNoSaveAnywayRoute()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        await using var composed = new Composition(f);
        var model = composed.Model;
        await model.RefreshAsync();
        await model.SelectAsync(model.Sessions.Single());
        await model.ReadConversationAsync();
        const string candidate = "password=synthetic-native-correction-only";
        model.ComposerBody = candidate;
        model.ComposerBody.AsSpan().SequenceEqual(candidate.AsSpan()).Should().BeTrue();
        model.CanCompose.Should().BeTrue();
        f.Count("conversation_drafts").Should().Be(0);
        await model.SubmitConversationAsync();
        f.Count("conversation_messages").Should().Be(0);
        model.ComposerBody.AsSpan().SequenceEqual(candidate.AsSpan()).Should().BeTrue();
        model.ConversationStatus.Contains(candidate, StringComparison.Ordinal).Should().BeFalse();
        model.ComposerBody = "corrected ordinary message";
        await model.SubmitConversationAsync();
        f.Count("conversation_messages").Should().Be(1);
    }

    [Fact]
    public async Task SelectedSessionChangeAtPhysicalSubmissionCommitCannotRetargetBodyOrLoseOriginalMemoryBuffer()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        var original = f.Request.SessionId;
        f.Request = InteractionStorageFixture.NewRequest();
        await f.AdmitAsync(newSession: true);
        await WindowsSqliteConversationTests.FinishInitial(f);
        SessionsViewModel? subject = null;
        Kora.Core.Storage.SessionWorkspaceEntry? other = null;
        f.Reopen(new SelectionCheckpoint(() => subject!.SelectAsync(other).IsCompletedSuccessfully.Should().BeTrue()));
        await f.Store.InitializeAsync(f.Token);
        await using var composed = new Composition(f);
        subject = composed.Model;
        await subject.RefreshAsync();
        other = subject.Sessions.Single(entry => entry.Authority.SessionId != original);
        await subject.SelectAsync(subject.Sessions.Single(entry => entry.Authority.SessionId == original));
        subject.ComposerBody = "original addressed memory-only buffer";
        await subject.SubmitConversationAsync();
        f.Count("conversation_messages").Should().Be(0);
        subject.SelectedSessionRecord!.Authority.SessionId.Should().Be(original);
        subject.ComposerBody.Should().Be("original addressed memory-only buffer");
        subject.CanSubmitConversation.Should().BeFalse();
    }

    private sealed class SelectionCheckpoint(Action changed) : IHostInteractionTransactionCheckpoint
    {
        public void BeforeAudit(Microsoft.Data.Sqlite.SqliteConnection connection, Microsoft.Data.Sqlite.SqliteTransaction transaction) { }
        public void BeforeCommit(Microsoft.Data.Sqlite.SqliteConnection connection, Microsoft.Data.Sqlite.SqliteTransaction transaction) => changed();
    }

    private sealed class Composition : IAsyncDisposable
    {
        private readonly AudioControlAdmission admission;
        private readonly ModelTurnHost turns;
        private readonly Kora.Application.Memory.MemoryManagementService? memories;
        internal WindowsSqliteSessionWorkspaceTests.Access Access { get; } = new();
        internal AuditLog Audit { get; } = new();
        internal SessionsViewModel Model { get; }
        internal SessionWorkspaceService Workspace { get; }
        internal SessionConversationService Conversation { get; }
        internal ModelTurnHost Turns => turns;
        internal Kora.Application.Memory.MemoryManagementService Memories =>
            memories ?? throw new InvalidOperationException("Memory context was not composed for this fixture.");
        internal NewRequestComposer NewComposer() => new(Workspace, Conversation, Access, NullLogger<NewRequestComposer>.Instance);

        internal Composition(InteractionStorageFixture f,
            Func<Kora.Core.Presentation.AdmittedDetailContent, Func<bool>,
                Func<CancellationToken, Task<Kora.Core.Presentation.AdmittedDetailContent>>, CancellationToken, Task<string>>? openDetail = null,
            ISessionWorkspaceStore? conversationReader = null,
            Action<HostId<SessionIdentity>>? revokeDetails = null,
            Func<SessionWorkspaceEntry, Kora.Core.Conversation.ConversationMessage, Func<bool>, CancellationToken, Task<string>>? reuseOriginal = null,
            IModelTurnAdapter? adapter = null, AppearanceConfigurationService? appearance = null,
            Func<ISessionWorkspaceAccess, ScopedInstructionUseBroker>? instructionFactory = null, bool memoryContext = false,
            ISessionAnswerPlayback? answerPlayback = null)
        {
            var workspace = WindowsSqliteSessionWorkspaceTests.Service(f, Access);
            Workspace = workspace;
            admission = new(f.Store, f.Store, new(f.Tasks));
            var mode = new ProviderModeConfigurationService(new LocalModelProviderModePreferences(f.Paths), admission, Audit);
            mode.Observe();
            var profiles = new ProviderProfileConfigurationService(new LocalProviderProfilePreferences(f.Paths),
                new UnavailableProviderCapabilityCatalogue(new Installed(), f.Time), admission, Audit, f.Time);
            var dependencies = new DependencyBootstrapper([], NullLogger<DependencyBootstrapper>.Instance);
            var boundary = new HostBoundary();
            var runtimes = new Kora.Tools.Runtime.RecordedRuntimeObservation(dependencies);
            var registry = new ReadOnlyCapabilityRegistry(boundary, new(), new(), new(boundary), new(dependencies),
                new(runtimes), new(runtimes), NullLogger<ReadOnlyCapabilityRegistry>.Instance);
            turns = adapter is null
                ? new(f.Store, Access, boundary, registry, Audit, NullLogger<ModelTurnHost>.Instance, f.Time, mode, profiles, f.Store)
                : new(f.Store, Access, boundary, registry, Audit, NullLogger<ModelTurnHost>.Instance, f.Time,
                    [new(ModelProviderSelection.OllamaCandidate,
                        ModelQualificationGate.LocalCandidateSelection | ModelQualificationGate.IntegratedHost,
                        f.Time.Now.AddMinutes(5), adapter)], mode);
            var conversationWorkspace = conversationReader is null ? workspace
                : new SessionWorkspaceService(conversationReader, new(f.Tasks), Access, NullLogger<SessionWorkspaceService>.Instance);
            var conversation = new SessionConversationService(f.Store, f.Store, conversationWorkspace, Access, turns, f.Time,
                NullLogger<SessionConversationService>.Instance, execution: turns, cancellationAudit: Audit, interactions: f.Store);
            Conversation = conversation;
            memories = memoryContext ? new(workspace, f.Store, Access, boundary, f.Store, Audit,
                NullLogger<Kora.Application.Memory.MemoryAdmissionService>.Instance, f.Time) : null;
            var sink = new WindowsSqliteEvidenceSink(f.Paths);
            sink.Initialize();
            Model = new(workspace, new DurableEvidenceQuery(new WindowsSqliteEvidenceReader(sink), Access, f.Time,
                NullLogger<DurableEvidenceQuery>.Instance), Access, NullLogger<SessionsViewModel>.Instance,
                memories: memories, conversation: conversation, openConversationDetail: openDetail, revokeConversationDetails: revokeDetails,
                reviewOriginalMessageReuse: reuseOriginal,
                reviewAppearanceSuggestion: appearance is null ? null : (message, eligible, validate, token) =>
                    AppearanceSuggestionReview.ReviewAsync(appearance, message, eligible, validate, token),
                confirmAppearanceSuggestion: appearance is null ? null : (review, token) => review.ConfirmAsync(token),
                instructionUse: instructionFactory?.Invoke(Access),
                answerReads: answerPlayback is null ? null : new(workspace, answerPlayback));
        }
        public async ValueTask DisposeAsync()
        {
            Model.Close();
            if (memories is not null) { await memories.DisposeAsync(); }
            await turns.DisposeAsync();
            await admission.DisposeAsync();
        }
    }

    private sealed class HostBoundary : ICapabilityHostAccess, IApplicationInfo
    {
        public bool IsCurrentHost => true;
        public string Version => "1.0.0";
    }
    private sealed class Installed : ILocalProviderInstallationObservation
    {
        public Task<LocalProviderInstallation> ObserveAsync(CancellationToken cancellationToken) =>
            Task.FromResult(LocalProviderInstallation.Installed);
    }
    private sealed class AuditLog : ISecurityAuditLog
    {
        internal List<SecurityAuditEvent> Events { get; } = [];
        public void Write(SecurityAuditEvent auditEvent)
        {
            HostActivity.RequireCurrent().Request.Origin.Should().Be(RequestOrigin.LocalUi);
            Events.Add(auditEvent);
        }
    }
}
