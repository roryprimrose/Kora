using System.Collections.Immutable;
using System.Text.Json;

using AwesomeAssertions;

using Kora.Application.Hosting;
using Kora.Core.Commands;
using Kora.Core.Communication;
using Kora.Core.Configuration;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Voice;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TypedAndCurrentNameVoiceSearchShowOnlyInertExactProvenanceNeverSpeakOrReason(bool voice)
    {
        var fixture = await Fixture.CreateInitializedAsync();
        var store = BindConversationSearch(fixture);
        fixture.ViewModel.TaskResponseMode = ResponseOutputMode.VoiceOnly;
        var command = "conversation search " + store.Session.Authority.SessionId.Value.ToString("D") + " --   MiXeD e\u0301  ";
        if (voice) { await fixture.RaiseActivatedTranscriptAsync("Kora, " + command, 1); }
        else { await fixture.RunAsync(command); }
        fixture.ViewModel.ResponseTitle.Should().Be("Exact conversation search.");
        using var page = JsonDocument.Parse(fixture.ViewModel.ResponseBody);
        page.RootElement.GetProperty("Messages")[0].GetProperty("Body").GetString().Should().Be("original MiXeD e\u0301");
        fixture.ViewModel.IsSpeechResponseEnabled.Should().BeFalse();
        fixture.ViewModel.IsVisualResponseVisible.Should().BeTrue();
        fixture.TextToSpeech.SpokenText.Should().BeNull();
        fixture.Reasoner.Requests.Should().BeEmpty();
        fixture.HostStore.Records.Should().BeEmpty();
        store.SearchOrigins.Should().OnlyContain(origin => origin == (voice ? RequestOrigin.ActivatedVoice : RequestOrigin.LocalUi));
        fixture.ViewModel.Transcript.Should().NotContain("MiXeD");
        await fixture.RunAsync("session help");
        fixture.ViewModel.ResponseTitle.Should().Be("Session command observed.");
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("unbound")]
    [InlineData("unprefixed")]
    [InlineData("late-source")]
    [InlineData("late-disposed")]
    public async Task SearchFailureNeverFallsBackOrPublishesPrivateRows(string fault)
    {
        var fixture = await Fixture.CreateInitializedAsync();
        var store = BindConversationSearch(fixture);
        if (fault is "unbound") { BindSessions(fixture); }
        if (fault is "late-source") { store.BeforeSearchRead = () => fixture.Session.IsUnlocked = false; }
        if (fault is "late-disposed") { store.BeforeSearchRead = fixture.ViewModel.Dispose; }
        var command = "conversation search " + store.Session.Authority.SessionId.Value.ToString("D") + " -- MiXeD";
        if (fault is "invalid") { command += new string('x', 256); }
        if (fault is "unprefixed") { await fixture.RaiseActivatedTranscriptAsync(command, 1); }
        else { await fixture.RunAsync(command); }
        fixture.ViewModel.ResponseBody.Should().NotContain("original MiXeD");
        fixture.Reasoner.Requests.Should().BeEmpty();
        fixture.HostStore.Records.Should().BeEmpty();
        fixture.TextToSpeech.SpokenText.Should().BeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PendingQuestionAndBothApprovalsKeepTheirExactDecisionTargets(bool voice)
    {
        var fixture = new Fixture();
        fixture.Probe.Status = new("local.inference", "Local", DependencyReadiness.Ready, "Ready");
        fixture.Reasoner.Question = new("Which target?", ["One", "Two"]);
        await fixture.ViewModel.InitializeAsync();
        var store = BindConversationSearch(fixture);
        var command = "conversation search " + store.Session.Authority.SessionId.Value.ToString("D") + " -- One";
        Task Search() => voice ? fixture.RaiseActivatedTranscriptAsync("Kora, " + command, 1) : fixture.RunAsync(command);
        await fixture.RunAsync("choose");
        await fixture.ViewModel.ActiveReasoningTask!;
        await Search();
        fixture.ViewModel.IsModelQuestionPending.Should().BeTrue();
        fixture.ViewModel.ResponseTitle.Should().Be("Session command blocked.");
        await fixture.ViewModel.CancelModelQuestionAsync();
        await fixture.ViewModel.PrepareGrantChangeAsync(new(GrantChangeOperation.Add,
            BuiltInAction.LockMachine, ModelApprovalScope.Always));
        await Search();
        fixture.ViewModel.IsGrantChangePending.Should().BeTrue();
        await fixture.ViewModel.RejectPendingGrantChangeAsync();
        fixture.Reasoner.Question = null;
        fixture.Reasoner.Action = BuiltInAction.LockMachine;
        await fixture.RunAsync("please lock");
        await fixture.ViewModel.ActiveReasoningTask!;
        await Search();
        fixture.ViewModel.IsModelActionApprovalPending.Should().BeTrue();
        store.SearchOrigins.Should().BeEmpty();
    }

    [Fact]
    public async Task ProtectedCallReadUsesInspectionNotMutationAndPrivacyClearsQueryAndContinuation()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        var store = BindConversationSearch(fixture);
        fixture.CallState.SetState(CallState.Active);
        await fixture.Dispatcher.LastInvocation;
        var command = "conversation search " + store.Session.Authority.SessionId.Value.ToString("D") + " -- MiXeD";
        await fixture.RaiseActivatedTranscriptAsync("Kora, " + command, 1);
        fixture.ViewModel.ResponseTitle.Should().Be("Exact conversation search.");
        fixture.ViewModel.CommandText = command;
        fixture.ViewModel.CloseForObservedPrivacyEvent("locked", true);
        await fixture.ViewModel.PrivacyClosureTask;
        fixture.ViewModel.CommandText.Should().BeEmpty();
        fixture.ViewModel.ResponseBody.Should().NotContain("original MiXeD");
    }

    [Fact]
    public async Task ExplicitCancelRebindNameChangeAndRetentionRetireOnlyHostHeldSearchState()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        var store = BindConversationSearch(fixture);
        store.SearchMessageCount = 30;
        var session = store.Session.Authority.SessionId.Value.ToString("D");
        await fixture.RunAsync("conversation search " + session + " -- MiXeD");
        await fixture.RunAsync("conversation search cancel " + session);
        fixture.ViewModel.ResponseBody.Should().StartWith("Host-held search cancelled.");
        await fixture.RunAsync("conversation search " + session + " -- MiXeD");
        fixture.ViewModel.AssistantNameInput = "Nova";
        await fixture.ViewModel.ApplyAssistantNameCommand.ExecuteAsync();
        await fixture.RunAsync("conversation search next " + session);
        fixture.ViewModel.ResponseTitle.Should().Be("Session command not confirmed.");
        await fixture.RunAsync("conversation search " + session + " -- MiXeD");
        fixture.ViewModel.RevokeSessionPresentation(store.Session.Authority.SessionId);
        fixture.ViewModel.ResponseBody.Should().StartWith("Session presentation revoked");
        BindSessions(fixture);
    }

    [Fact]
    public async Task FinalPresenterSourceFenceRejectsLateLossAfterActionCompletion()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        var store = BindConversationSearch(fixture, new SearchPublishTime(() => fixture.Session.IsUnlocked = false));
        store.SearchMessageCount = 30;
        await fixture.RunAsync("conversation search " + store.Session.Authority.SessionId.Value.ToString("D") + " -- MiXeD");
        fixture.ViewModel.ResponseBody.Should().NotContain("original MiXeD");
        fixture.Reasoner.Requests.Should().BeEmpty();
    }

    private static SessionCommandStore BindConversationSearch(Fixture fixture, TimeProvider? time = null)
    {
        var store = new SessionCommandStore(fixture);
        var workspace = new SessionWorkspaceService(store, new(fixture.HostStore), store, NullLogger<SessionWorkspaceService>.Instance);
        var conversation = new SessionConversationService(store, store, workspace, store, new RequestAdmission(),
            TimeProvider.System, NullLogger<SessionConversationService>.Instance);
        fixture.ViewModel.BindSessionCommands(workspace, searches: new(workspace, conversation, store, time ?? TimeProvider.System));
        return store;
    }

    private sealed partial class SessionCommandStore
    {
        internal Action? BeforeSearchRead { get; set; }
        internal int SearchMessageCount { get; set; } = 1;
        internal List<RequestOrigin> SearchOrigins { get; } = [];
        private ImmutableArray<ConversationMessage> searchMessages;
        private ValueTask<ConversationPage> ReadSearchPage(HostId<SessionIdentity> session, ConversationCursor? cursor,
            int limit, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            BeforeSearchRead?.Invoke();
            SearchOrigins.Add(Kora.Core.Diagnostics.HostActivity.RequireCurrent().Request.Origin);
            if (searchMessages.IsDefault)
            {
                searchMessages = Enumerable.Range(1, SearchMessageCount).Select(index =>
                    new ConversationMessage(new(new(Guid.NewGuid()), session, new(Guid.NewGuid()), RequestOrigin.LocalUi),
                        new(1), index, 2, DateTimeOffset.UnixEpoch, "original MiXeD e\u0301",
                        ModelTurnOutcome.Unavailable, ModelTurnReason.UnregisteredModel)).ToImmutableArray();
            }
            var rows = searchMessages.Where(message => message.Sequence > (cursor?.After ?? 0)).Take(limit).ToImmutableArray();
            var last = rows.LastOrDefault()?.Sequence ?? 0;
            return ValueTask.FromResult(new ConversationPage(Session.Authority, SearchMessageCount * 2, SearchMessageCount, rows,
                last < SearchMessageCount ? new(session, new(1), SearchMessageCount, last, SearchMessageCount * 2) : null));
        }
    }

    private sealed class SearchPublishTime(Action beforeTimestamp) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            beforeTimestamp();
            return DateTimeOffset.UnixEpoch;
        }
    }
}
