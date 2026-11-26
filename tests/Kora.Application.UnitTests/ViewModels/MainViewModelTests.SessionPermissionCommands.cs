using AwesomeAssertions;

using Kora.Application.Hosting;
using Kora.Application.Configuration;
using Kora.Core.Auditing;
using Kora.Core.Authorization.SessionPermission;
using Kora.Core.Communication;
using Kora.Core.Hosting;
using Kora.Core.Storage;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NewSessionDefaultSelectionPersistsWithoutChangingAnExistingSession(bool failWrite)
    {
        var fixture = await Fixture.CreateInitializedAsync();
        var session = BindPermissionCommands(fixture);
        var memory = new PermissionPreferenceMemory { FailWrite = failWrite };
        var preferences = new SessionPermissionDefaultPreferences(memory);
        fixture.ViewModel.BindSessionPermissionDefaults(preferences);
        fixture.ViewModel.NewSessionPermission = (int)SessionPermissionLevel.ReviewAll;
        fixture.ViewModel.NewSessionPermission = (int)SessionPermissionLevel.ApproveAll;
        await fixture.ViewModel.PreferenceUpdate;
        preferences.Load().Should().Be(failWrite ? SessionPermissionLevel.ReviewAll : SessionPermissionLevel.ApproveAll);
        fixture.ViewModel.NewSessionPermission.Should().Be(failWrite ? 0 : 2);
        session.PermissionPolicy.Should().BeNull("the new-session default cannot change an existing session");
        FluentActions.Invoking(() => fixture.ViewModel.NewSessionPermission = 99).Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => fixture.ViewModel.BindSessionPermissionDefaults(preferences)).Should().Throw<InvalidOperationException>();
        FluentActions.Invoking(() => fixture.ViewModel.BindSessionPermissionDefaults(null!)).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task UnboundNewSessionDefaultCannotPersistAnUnverifiedSelection()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        fixture.ViewModel.NewSessionPermission = (int)SessionPermissionLevel.ApproveAll;
        await fixture.ViewModel.PreferenceUpdate;
        fixture.ViewModel.NewSessionPermission.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Permission_typed_and_activated_voice_complete_one_exact_change_without_reasoning(bool voice)
    {
        var fixture = await Fixture.CreateInitializedAsync();
        var store = BindPermissionCommands(fixture);
        var id = store.Session.Authority.SessionId.Value.ToString("D");
        async Task Run(string text)
        {
            if (voice) { await fixture.RaiseActivatedTranscriptAsync("Kora, " + text, 1); }
            else { await fixture.RunAsync(text); }
        }
        await Run("session permission status " + id);
        fixture.ViewModel.ResponseTitle.Should().Be("Session command observed.");
        await Run("session permission review " + id + " approve-all");
        fixture.ViewModel.ResponseTitle.Should().Be("Session command review.");
        fixture.ViewModel.ResponseBody.Should().Contain("Grants are not evaluated, consumed or created");
        store.PermissionPolicy.Should().BeNull();
        fixture.HostStore.Records.Should().BeEmpty();
        await Run("session permission confirm " + id);
        fixture.ViewModel.ResponseTitle.Should().Be("Session command committed.", fixture.ViewModel.ResponseBody);
        store.PermissionPolicy!.Level.Should().Be(SessionPermissionLevel.ApproveAll);
        fixture.HostStore.Records.Should().HaveCount(2).And.OnlyContain(record =>
            record.Request.Origin == (voice ? RequestOrigin.ActivatedVoice : RequestOrigin.LocalUi)
            && record.Request.SessionId == store.Session.Authority.SessionId);

        await Run("session permission confirm " + id);
        fixture.ViewModel.ResponseTitle.Should().Be("Session command not confirmed.");
        fixture.HostStore.Records.Should().HaveCount(2);
    }

    [Theory]
    [InlineData("unprefixed")]
    [InlineData("old-name")]
    [InlineData("protected-call")]
    [InlineData("unbound")]
    [InlineData("invalid")]
    public async Task Permission_voice_commands_preserve_activation_call_and_binding_boundaries(string failure)
    {
        var fixture = await Fixture.CreateInitializedAsync();
        var store = BindPermissionCommands(fixture);
        if (failure is "protected-call")
        {
            fixture.CallState.SetState(CallState.Active);
            await fixture.Dispatcher.LastInvocation;
        }
        if (failure is "unbound") { BindSessions(fixture); }
        var command = failure is "invalid" ? "session permission review selected approve-all"
            : "session permission review " + store.Session.Authority.SessionId.Value.ToString("D") + " approve-all";
        await fixture.RaiseActivatedTranscriptAsync((failure is "unprefixed" ? ""
            : failure is "old-name" ? "Nova, " : "Kora, ") + command, 1);
        fixture.ViewModel.ResponseTitle.Should().NotBe("Session command review.");
        store.PermissionPolicy.Should().BeNull();
        fixture.HostStore.Records.Should().BeEmpty();

    }

    [Theory]
    [InlineData("name")]
    [InlineData("privacy")]
    [InlineData("invalid")]
    [InlineData("revoked")]
    [InlineData("rebound")]
    [InlineData("channel")]
    [InlineData("call")]
    [InlineData("commit-call")]
    [InlineData("ownership")]
    [InlineData("owner-binding")]
    public async Task Permission_review_cannot_survive_source_or_control_changes(string transition)
    {
        var fixture = await Fixture.CreateInitializedAsync();
        var owner = 0L;
        fixture.ViewModel.BindCallOwnershipGate(() => true, () => owner);
        var store = BindPermissionCommands(fixture);
        var id = store.Session.Authority.SessionId.Value.ToString("D");
        await fixture.RunAsync("session permission review " + id + " approve-all");
        fixture.ViewModel.ResponseTitle.Should().Be("Session command review.");
        if (transition is "name")
        {
            fixture.ViewModel.AssistantNameInput = "Nova";
            await fixture.ViewModel.ApplyAssistantNameCommand.ExecuteAsync();
        }
        if (transition is "privacy")
        {
            fixture.ViewModel.CommandText = "session permission review " + id + " approve-all";
            fixture.ViewModel.CloseForObservedPrivacyEvent("locked", true);
            await fixture.ViewModel.PrivacyClosureTask;
            fixture.ViewModel.CommandText.Should().BeEmpty();
        }
        if (transition is "invalid") { await fixture.RunAsync("session permission nonsense"); }
        if (transition is "revoked") { fixture.ViewModel.RevokeSessionPresentation(store.Session.Authority.SessionId); }
        if (transition is "rebound") { BindPermissionCommands(fixture); }
        if (transition is "call")
        {
            fixture.CallState.SetState(CallState.Active);
            await fixture.Dispatcher.LastInvocation;
        }
        if (transition is "commit-call") { store.BeforePermissionCommit = () => fixture.CallState.SetState(CallState.Active); }
        if (transition is "ownership") { owner++; }
        if (transition is "owner-binding") { fixture.ViewModel.BindCallOwnershipGate(() => true, () => owner); }
        if (transition is "channel")
        { await fixture.RaiseActivatedTranscriptAsync("Kora, session permission confirm " + id, 1); }
        else if (transition is not "privacy") { await fixture.RunAsync("session permission confirm " + id); }
        if (transition is not "privacy") { fixture.ViewModel.ResponseTitle.Should().Be("Session command not confirmed."); }
        store.PermissionPolicy.Should().BeNull();
        fixture.HostStore.Records.Should().NotContain(record => record.State == HostTaskState.Succeeded);

    }

    private static SessionCommandStore BindPermissionCommands(Fixture fixture)
    {
        var store = new SessionCommandStore(fixture);
        var workspace = new SessionWorkspaceService(store, new(fixture.HostStore), store, NullLogger<SessionWorkspaceService>.Instance);
        fixture.ViewModel.BindSessionCommands(workspace, permissions: new(workspace, store, TimeProvider.System));
        return store;
    }

    private sealed partial class SessionCommandStore : ISessionPermissionStore
    {
        internal SessionPermissionPolicy? PermissionPolicy { get; private set; }
        internal Action? BeforePermissionCommit { get; set; }
        internal List<AutomaticApprovalAudit> AutomaticAudits { get; } = [];
        internal bool FailAutomaticAudit { get; set; }
        internal bool RetireAfterAutomaticAudit { get; set; }
        internal Action? AfterPermissionRead { get; set; }

        public ValueTask<SessionPermissionPolicy> ReadSessionPermissionAsync(HostId<SessionIdentity> session, CancellationToken token)
        {
            var policy = PermissionPolicy ?? SessionPermissionPolicy.Default(Session.Authority);
            AfterPermissionRead?.Invoke();
            return ValueTask.FromResult(policy);
        }

        public ValueTask<SessionPermissionPolicy> SetSessionPermissionAsync(HostRequest request, SessionPermissionPolicy expected,
            SessionPermissionLevel desired, Func<bool> admitted, CancellationToken token)
        {
            BeforePermissionCommit?.Invoke();
            if (!admitted() || expected != (PermissionPolicy ?? SessionPermissionPolicy.Default(Session.Authority)))
            { throw new InvalidOperationException("Permission source changed."); }
            PermissionPolicy = expected with { Level = desired, Revision = new(expected.Revision.Value + 1) };
            return ValueTask.FromResult(PermissionPolicy);
        }

        public ValueTask RecordAutomaticApprovalAsync(HostRequest request, SessionPermissionPolicy expected,
            AutomaticApprovalAudit details, Func<bool> admitted, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (FailAutomaticAudit) { throw new IOException("Synthetic automatic control audit unavailable."); }
            if (!admitted() || expected != PermissionPolicy || request.SessionId != expected.SessionId)
            { throw new InvalidOperationException("Automatic control authority changed."); }
            details.Validate();
            AutomaticAudits.Add(details);
            if (RetireAfterAutomaticAudit) { PermissionPolicy = expected with { Revision = new(expected.Revision.Value + 1) }; }
            return ValueTask.CompletedTask;
        }
    }

    private sealed class PermissionPreferenceMemory : IPreferenceStore
    {
        internal string? Content { get; private set; }
        internal bool FailWrite { get; init; }
        public string? ReadText(string fileName) => Content;
        public string[]? ReadLines(string fileName) => throw new NotSupportedException();
        public void WriteText(string fileName, string contents)
        {
            if (FailWrite) { throw new IOException("Synthetic preference write unavailable."); }
            Content = contents;
        }
        public void WriteLines(string fileName, IEnumerable<string> contents) => throw new NotSupportedException();
        public void Delete(string fileName) => throw new NotSupportedException();
    }
}
