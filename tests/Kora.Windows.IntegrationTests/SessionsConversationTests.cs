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

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task DirtySessionSwitchUsesSaveDiscardCancelAndNeverPostsToAnotherSession(int choice)
    {
        var decision = (ComposerDraftDecision)choice;
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
        model.DecideComposerDraft = () => Task.FromResult(decision);
        await model.SelectAsync(model.Sessions.Single(entry => entry.Authority.SessionId != original));
        if (decision == ComposerDraftDecision.Cancel)
        {
            model.SelectedSessionRecord!.Authority.SessionId.Should().Be(original);
            model.ComposerBody.Should().Be("unsaved original-session buffer");
        }
        else
        {
            model.SelectedSessionRecord!.Authority.SessionId.Should().NotBe(original);
            model.ComposerBody.Should().BeEmpty();
        }
        var saved = await f.Store.ReadConversationDraftAsync(original, f.Token);
        saved.Draft.Body.Should().Be(decision == ComposerDraftDecision.Save ? "unsaved original-session buffer" : null);
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
    public async Task ExplicitDraftSaveClearAndReloadUseExactNativeRevisionAndCannotAnswerQuestionsOrImplicitlySubmit()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        await using var composed = new Composition(f);
        var model = composed.Model;
        await model.RefreshAsync();
        await model.SelectAsync(model.Sessions.Single());
        await model.ReadConversationAsync();
        model.ComposerBody = "only explicitly saved draft";
        await model.SaveConversationDraftAsync();
        model.ComposerDirty.Should().BeFalse();
        model.DiscardComposerBuffer();
        await model.ReloadConversationDraftAsync();
        model.ComposerBody.Should().Be("only explicitly saved draft");
        model.CanClearConversationDraft.Should().BeFalse();
        model.ReviewClearConversationDraft();
        model.SavedConversationDraftReview.Should().Be("only explicitly saved draft");
        await model.ClearSavedConversationDraftAsync();
        (await f.Store.ReadConversationDraftAsync(f.Request.SessionId, f.Token)).Draft.Body.Should().BeNull();
        model.ComposerBody.Should().Be("only explicitly saved draft", "clearing saved storage never discards current memory edits");
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
        await model.SaveConversationDraftAsync();
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
        f.Reopen(new SelectionCheckpoint(() => subject!.SelectAsync(other).GetAwaiter().GetResult()));
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
        internal WindowsSqliteSessionWorkspaceTests.Access Access { get; } = new();
        internal AuditLog Audit { get; } = new();
        internal SessionsViewModel Model { get; }
        internal SessionWorkspaceService Workspace { get; }
        internal SessionConversationService Conversation { get; }
        internal NewRequestComposer NewComposer() => new(Workspace, Conversation, Access, NullLogger<NewRequestComposer>.Instance);

        internal Composition(InteractionStorageFixture f,
            Func<Kora.Core.Presentation.AdmittedDetailContent, Func<bool>,
                Func<CancellationToken, Task<Kora.Core.Presentation.AdmittedDetailContent>>, CancellationToken, Task<string>>? openDetail = null)
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
            turns = new(f.Store, Access, boundary, registry, Audit, NullLogger<ModelTurnHost>.Instance,
                f.Time, mode, profiles, f.Store);
            var conversation = new SessionConversationService(f.Store, f.Store, workspace, Access, turns, f.Time,
                NullLogger<SessionConversationService>.Instance);
            Conversation = conversation;
            var sink = new WindowsSqliteEvidenceSink(f.Paths);
            sink.Initialize();
            Model = new(workspace, new DurableEvidenceQuery(new WindowsSqliteEvidenceReader(sink), Access, f.Time,
                NullLogger<DurableEvidenceQuery>.Instance), Access, NullLogger<SessionsViewModel>.Instance,
                conversation: conversation, openConversationDetail: openDetail);
        }
        public async ValueTask DisposeAsync()
        {
            Model.Close();
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
