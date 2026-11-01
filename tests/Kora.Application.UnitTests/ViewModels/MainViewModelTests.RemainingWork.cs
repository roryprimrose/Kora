using AwesomeAssertions;

using Kora.Application.Hosting;
using Kora.Core.Commands;
using Kora.Core.Communication;
using Kora.Core.Conversation;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Core.Storage;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TypedAndActivatedRemainingWorkCompleteWithFactualOutputAndZeroDispatch(bool voice)
    {
        var f = await Fixture.CreateInitializedAsync();
        var (store, conversation) = BindRemainingWork(f);
        await using var owned = conversation;
        var command = "session remaining " + store.Session.Authority.SessionId.Value.ToString("D");
        if (voice) { await f.RaiseActivatedTranscriptAsync("Kora, " + command, 1); }
        else { await f.RunAsync(command); }
        f.ViewModel.ResponseTitle.Should().Be("Remaining work.", f.ViewModel.Transcript);
        f.ViewModel.ResponseBody.Should().Contain("Unknown").And.Contain("Interrupted")
            .And.Contain("Pending questions: 1").And.Contain("3 task records")
            .And.Contain("Durable ledger observed").And.Contain("ordinary lane observed")
            .And.NotContain("Actual question").And.NotContain(store.Session.Authority.SessionId.Value.ToString("D"));
        f.Reasoner.Requests.Should().BeEmpty();
        f.HostStore.Records.Should().BeEmpty();
        store.RequestRetentions.Should().Be(0);
        f.ViewModel.CanReadCurrentResponse.Should().BeFalse("the status identity must not become an unfenced ordinary response");
        await f.RunAsync("status");
        f.ViewModel.ResponseTitle.Should().NotBe("Remaining work.");
    }

    [Theory]
    [InlineData("privacy")]
    [InlineData("call")]
    [InlineData("input")]
    [InlineData("name")]
    [InlineData("disposed")]
    [InlineData("source")]
    [InlineData("unavailable")]
    [InlineData("unbound")]
    [InlineData("invalid")]
    [InlineData("unprefixed")]
    public async Task ChangedOriginalInputChannelOrSourceNeverPublishesLateRemainingWork(string fault)
    {
        var f = await Fixture.CreateInitializedAsync();
        var (store, conversation) = BindRemainingWork(f);
        await using var owned = conversation;
        store.BeforeRemainingRead = () =>
        {
            switch (fault)
            {
                case "privacy": f.Session.IsUnlocked = false; break;
                case "call": f.CallState.SetState(CallState.Active); break;
                case "input": f.ViewModel.CommandText = "new unrelated input"; break;
                case "name": f.ViewModel.BindRemainingWork(new(null!, conversation, store, TimeProvider.System), conversation); break;
                case "disposed": f.ViewModel.Dispose(); break;
                case "source": store.RemainingHead++; break;
                case "unavailable": throw new IOException("unavailable");
            }
        };
        if (fault is "unbound") { f.ViewModel.BindSessionCommands(new(store, new(f.HostStore), store, NullLogger<SessionWorkspaceService>.Instance)); }
        var command = fault is "invalid" ? "session remaining active"
            : "session remaining " + store.Session.Authority.SessionId.Value.ToString("D");
        if (fault is "unprefixed") { await f.RaiseActivatedTranscriptAsync(command, 1); }
        else { await f.RunAsync(command); }
        f.ViewModel.ResponseBody.Should().NotContain("Durable ledger observed");
        f.TextToSpeech.SpokenText.Should().BeNull();
        f.Reasoner.Requests.Should().BeEmpty();
        f.HostStore.Records.Should().BeEmpty();
    }

    [Fact]
    public async Task RemainingWorkDoesNotReplaceCurrentQuestionOrApproval()
    {
        var f = new Fixture();
        f.Probe.Status = new("local.inference", "Local", Kora.Core.Dependencies.DependencyReadiness.Ready, "Ready");
        f.Reasoner.Question = new("Which target?", ["One", "Two"]);
        await f.ViewModel.InitializeAsync();
        var (store, conversation) = BindRemainingWork(f);
        await using var owned = conversation;
        var command = "session remaining " + store.Session.Authority.SessionId.Value.ToString("D");
        await f.RunAsync("choose");
        await f.ViewModel.ActiveReasoningTask!;
        var title = f.ViewModel.ResponseTitle;
        var body = f.ViewModel.ResponseBody;
        await f.RunAsync(command);
        f.ViewModel.ResponseTitle.Should().Be(title);
        f.ViewModel.ResponseBody.Should().Be(body);
        f.ViewModel.IsModelQuestionPending.Should().BeTrue();
        await f.ViewModel.CancelModelQuestionAsync();
        await f.ViewModel.PrepareGrantChangeAsync(new(GrantChangeOperation.Add, BuiltInAction.LockMachine, ModelApprovalScope.Always));
        body = f.ViewModel.ResponseBody;
        await f.RunAsync(command);
        f.ViewModel.IsGrantChangePending.Should().BeTrue();
        f.ViewModel.ResponseBody.Should().Be(body);
        store.RemainingReads.Should().Be(0);
    }

    [Theory]
    [InlineData("native")]
    [InlineData("speech")]
    [InlineData("unadmitted")]
    [InlineData("unbound")]
    [InlineData("rename")]
    [InlineData("retired")]
    [InlineData("standalone")]
    [InlineData("late-speech")]
    [InlineData("final")]
    public async Task NativeRemainingWorkUsesSameFactualAnswerAndFinalSourceAdmission(string mode)
    {
        var f = await Fixture.CreateInitializedAsync();
        var (store, conversation) = BindRemainingWork(f, bindWorkspace: mode is not "standalone");
        await using var owned = conversation;
        if (mode is "unbound") { f.ViewModel.BindSessionCommands(new(store, new(f.HostStore), store, NullLogger<SessionWorkspaceService>.Instance)); }
        if (mode is "speech" or "late-speech") { f.ViewModel.DefaultResponseMode = Kora.Core.Voice.ResponseOutputMode.Hybrid; }
        if (mode is "late-speech") { f.TextToSpeech.BeforeSpeak = () => f.ViewModel.CommandText = "replacement input"; }
        var finalCalls = 0;
        bool Admitted() => mode is not "unadmitted" && (mode is not "final" || store.RemainingReads < 3 || ++finalCalls != 3);
        var operation = () => f.ViewModel.AnswerRemainingWorkAsync(store.Session, Admitted, TestContext.Current.CancellationToken);
        if (mode is "unadmitted" or "unbound" or "final")
        { await operation.Should().ThrowAsync<InvalidOperationException>(); }
        else
        {
            var body = await operation();
            if (mode is "late-speech") { body.Should().BeEmpty(); }
            else { body.Should().Contain("Unknown").And.Contain("Durable ledger observed"); }
            if (mode is "speech") { f.TextToSpeech.SpokenText.Should().Contain("Unknown"); }
            if (mode is "rename")
            {
                await f.RunAsync("session rename " + store.Session.Authority.SessionId.Value.ToString("D") + " 1 0 \"New name\"");
                f.ViewModel.ResponseBody.Should().NotContain("Durable ledger observed");
            }
            if (mode is "retired")
            {
                f.ViewModel.RevokeSessionPresentation(store.Session.Authority.SessionId);
                f.ViewModel.ResponseBody.Should().NotContain("Durable ledger observed");
            }
        }
        f.Reasoner.Requests.Should().BeEmpty();
        store.RequestRetentions.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RemainingWorkReadableDuplicateChoiceCompletesOnItsOriginalTypedOrActivatedChannel(bool voice)
    {
        var f = await Fixture.CreateInitializedAsync();
        var (store, conversation) = BindRemainingWork(f);
        await using var owned = conversation;
        var request = new HostRequest(new(Guid.NewGuid()), store.Session.Authority.SessionId, new(Guid.NewGuid()), RequestOrigin.LocalUi);
        await store.RenameSessionAsync(request, new(1), 0, new("Build"), () => true, TestContext.Current.CancellationToken);
        var secondId = new HostId<SessionIdentity>(Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"));
        var other = new SessionWorkspaceEntry(new(secondId, new(1), false), new(secondId, new(1), new("Build")));
        store.ClaudePageRead = (_, _) => new([store.Session, other], null);
        Task Run(string text) => voice ? f.RaiseActivatedTranscriptAsync("Kora, " + text, 1) : f.RunAsync(text);
        await Run("what work remains in \"Build\"");
        f.ViewModel.ResponseTitle.Should().Be("Remaining-work target choice.");
        f.ViewModel.ResponseBody.Should().Contain("1. Build").And.Contain("2. Build");
        await Run("session remaining choose 1");
        f.ViewModel.ResponseTitle.Should().Be("Remaining work.");
        f.ViewModel.ResponseBody.Should().Contain("Conversation: Build (Active)");
        await Run("session remaining cancel");
        f.ViewModel.ResponseBody.Should().Contain("choice cancelled");
        f.Reasoner.Requests.Should().BeEmpty();
        f.HostStore.Records.Should().BeEmpty();
    }

    [Fact]
    public async Task RemainingWorkMetadataEventAlreadyBeingDeliveredCannotReviveRetiredBinding()
    {
        var f = await Fixture.CreateInitializedAsync();
        var store = new SessionCommandStore(f);
        var workspace = new SessionWorkspaceService(store, new(f.HostStore), store, NullLogger<SessionWorkspaceService>.Instance);
        await using var conversation = new SessionConversationService(store, store, workspace, store, new DefaultOnlyRequestAdmission(),
            TimeProvider.System, NullLogger<SessionConversationService>.Instance);
        workspace.SessionMetadataChanging += _ => f.ViewModel.BindSessionCommands(workspace);
        f.ViewModel.BindSessionCommands(workspace);
        f.ViewModel.BindRemainingWork(new(workspace, conversation, store, TimeProvider.System), conversation);
        await f.RunAsync("session rename " + store.Session.Authority.SessionId.Value.ToString("D") + " 1 0 \"New name\"");
        await f.RunAsync("session remaining " + store.Session.Authority.SessionId.Value.ToString("D"));
        f.ViewModel.ResponseTitle.Should().Be("Remaining-work answer unavailable/changed.");
        store.RemainingReads.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RemainingWorkRejectsNonOriginalOrUnavailableNameVoiceAuthority(bool nameUnavailable)
    {
        var f = new Fixture();
        if (nameUnavailable) { f.NamePreferences.Name = "Supported Commands"; }
        await f.ViewModel.InitializeAsync();
        var (store, conversation) = BindRemainingWork(f);
        await using var owned = conversation;
        await f.ViewModel.ExecuteSessionCommandAsync(new(SessionCommandOperation.RemainingWork, store.Session.Authority.SessionId.Value),
            nameUnavailable ? Kora.Core.Auditing.SecurityAuditInitiator.VoiceCommand : Kora.Core.Auditing.SecurityAuditInitiator.ModelSuggestion,
            scopeActivationPrefixed: true);
        store.RemainingReads.Should().Be(0);
        f.ViewModel.Transcript.Should().Contain("Remaining-work answer unavailable");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RemainingWorkDirectNativeCommandPreservesExplicitSourceOrDeniesProtectedCall(bool protectedCall)
    {
        var f = await Fixture.CreateInitializedAsync();
        var (store, conversation) = BindRemainingWork(f);
        await using var owned = conversation;
        if (protectedCall) { f.CallState.SetState(CallState.Active); await f.Dispatcher.LastInvocation; }
        await f.ViewModel.ExecuteSessionCommandAsync(new(SessionCommandOperation.RemainingWork, store.Session.Authority.SessionId.Value),
            Kora.Core.Auditing.SecurityAuditInitiator.LocalUser);
        if (protectedCall)
        {
            store.RemainingReads.Should().Be(0);
            f.ViewModel.Transcript.Should().Contain("Remaining-work answer unavailable");
        }
        else { f.ViewModel.ResponseTitle.Should().Be("Remaining work."); }
    }

    private static (SessionCommandStore Store, SessionConversationService Conversation) BindRemainingWork(Fixture f, bool bindWorkspace = true)
    {
        var store = new SessionCommandStore(f);
        var workspace = new SessionWorkspaceService(store, new(f.HostStore), store, NullLogger<SessionWorkspaceService>.Instance);
        var conversation = new SessionConversationService(store, store, workspace, store, new DefaultOnlyRequestAdmission(),
            TimeProvider.System, NullLogger<SessionConversationService>.Instance);
        if (bindWorkspace) { f.ViewModel.BindSessionCommands(workspace); }
        f.ViewModel.BindRemainingWork(new(workspace, conversation, store, TimeProvider.System), conversation);
        return (store, conversation);
    }

    private sealed partial class SessionCommandStore : ISessionWorkStore
    {
        internal Action? BeforeRemainingRead { get; set; }
        internal int RemainingReads { get; private set; }
        internal long RemainingHead { get; set; } = 1;
        private SessionWorkSnapshot? remainingSnapshot;
        public ValueTask<SessionWorkSnapshot> ReadWorkAsync(HostId<SessionIdentity> session, long admissionRevision,
            SessionQueueLimits limits, CancellationToken token)
        {
            RemainingReads++;
            var head = RemainingHead;
            BeforeRemainingRead?.Invoke();
            var request = new HostRequest(new(Guid.NewGuid()), Session.Authority.SessionId, new(Guid.NewGuid()), RequestOrigin.LocalUi);
            remainingSnapshot ??= new(Session, 1, DateTimeOffset.UtcNow, new(Session.Authority.SessionId, Session.Authority.Generation, 0, []),
                0, 10, 1, [], 0,
                [new(new(request, new(3), HostTaskState.Unknown), new(1), "source", false, null),
                new(new(new HostRequest(new(Guid.NewGuid()), request.SessionId, new(Guid.NewGuid()), RequestOrigin.LocalUi),
                    new(2), HostTaskState.Interrupted), new(1), "source", false, null)], 3,
                [new(new(request, new(Guid.NewGuid()), new(1)), new(1), QuestionStatus.Pending, QuestionKind.Text, null, DateTimeOffset.UtcNow.AddMinutes(1))], 0);
            return ValueTask.FromResult(remainingSnapshot with { AuthorityRevision = head });
        }
    }
}