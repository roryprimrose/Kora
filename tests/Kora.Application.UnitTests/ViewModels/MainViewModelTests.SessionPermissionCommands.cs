using AwesomeAssertions;

using Kora.Application.Hosting;
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
        fixture.ViewModel.ResponseBody.Should().Contain("No grants will be saved");
        store.PermissionPolicy.Should().BeNull();
        fixture.HostStore.Records.Should().BeEmpty();
        await Run("session permission confirm " + id);
        fixture.ViewModel.ResponseTitle.Should().Be("Session command committed.", fixture.ViewModel.ResponseBody);
        store.PermissionPolicy!.Level.Should().Be(SessionPermissionLevel.ApproveAll);
        fixture.HostStore.Records.Should().HaveCount(2).And.OnlyContain(record =>
            record.Request.Origin == (voice ? RequestOrigin.ActivatedVoice : RequestOrigin.LocalUi)
            && record.Request.SessionId == store.Session.Authority.SessionId);
        fixture.Reasoner.Requests.Should().BeEmpty();
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
        fixture.Reasoner.Requests.Should().BeEmpty();
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
        fixture.Reasoner.Requests.Should().BeEmpty();
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

        public ValueTask<SessionPermissionPolicy> ReadSessionPermissionAsync(HostId<SessionIdentity> session, CancellationToken token) =>
            ValueTask.FromResult(PermissionPolicy ?? SessionPermissionPolicy.Default(Session.Authority));

        public ValueTask<SessionPermissionPolicy> SetSessionPermissionAsync(HostRequest request, SessionPermissionPolicy expected,
            SessionPermissionLevel desired, Func<bool> admitted, CancellationToken token)
        {
            BeforePermissionCommit?.Invoke();
            if (!admitted() || expected != (PermissionPolicy ?? SessionPermissionPolicy.Default(Session.Authority)))
            { throw new InvalidOperationException("Permission source changed."); }
            PermissionPolicy = expected with { Level = desired, Revision = new(expected.Revision.Value + 1) };
            return ValueTask.FromResult(PermissionPolicy);
        }
    }
}
