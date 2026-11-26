using AwesomeAssertions;

using Kora.Application.Hosting;
using Kora.Core.Communication;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Fact]
    public async Task DispatchCommandCannotStealTheCurrentQuestionAnswer()
    {
        var fixture = new Fixture();
        await using var pendingDetails = new PendingDetailsQuestion(fixture);
        fixture.Probe.Status = new("local.inference", "Local", Kora.Core.Dependencies.DependencyReadiness.Ready, "Ready");
        await fixture.ViewModel.InitializeAsync();
        var store = BindManualDispatchCommands(fixture);
        await pendingDetails.PresentAsync();
        var title = fixture.ViewModel.ResponseTitle;
        var question = fixture.ViewModel.CurrentDetailsOffer;
        await fixture.RunAsync("session dispatch pause " + store.Session.Authority.SessionId.Value.ToString("D"));
        fixture.ViewModel.HasDetailsQuestion.Should().BeTrue();
        fixture.ViewModel.ResponseTitle.Should().Be(title);
        fixture.ViewModel.CurrentDetailsOffer.Should().BeSameAs(question);
        fixture.Audit.Events.Should().NotContain(item => string.Equals(item.ActionId, "conversation.manual-dispatch-hold", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("unprefixed")]
    [InlineData("call")]
    [InlineData("input")]
    [InlineData("input-restored")]
    [InlineData("private")]
    [InlineData("name")]
    [InlineData("unbound")]
    public async Task DispatchControlRetiresStaleInputChannelCallAndPrivateAuthorityWithoutModelFallback(string fault)
    {
        var fixture = await Fixture.CreateInitializedAsync();
        var store = BindManualDispatchCommands(fixture);
        if (fault is "unbound") { BindSessions(fixture); }
        store.BeforeDispatchMetadataRead = () =>
        {
            store.BeforeDispatchMetadataRead = null;
            if (fault is "input") { fixture.ViewModel.CommandText = "replacement input"; }
            if (fault is "input-restored")
            {
                var previous = fixture.ViewModel.CommandText;
                fixture.ViewModel.CommandText = "replacement input";
                fixture.ViewModel.CommandText = previous;
            }
            if (fault is "call") { fixture.CallState.SetState(CallState.Active); }
            if (fault is "private") { fixture.ViewModel.CloseForObservedPrivacyEvent("locked", true); }
            if (fault is "name")
            {
                fixture.ViewModel.AssistantNameInput = "Nova";
                fixture.ViewModel.ApplyAssistantNameCommand.ExecuteAsync().IsCompletedSuccessfully.Should().BeTrue();
            }
        };
        await fixture.RaiseActivatedTranscriptAsync((fault is "unprefixed" ? "" : "Kora, ")
            + "session dispatch pause " + store.Session.Authority.SessionId.Value.ToString("D"), 1);
        fixture.ViewModel.ResponseTitle.Should().NotBe("Session command observed.");

        store.RequestRetentions.Should().Be(0);
        fixture.Audit.Events.Should().NotContain(item => string.Equals(item.ActionId, "conversation.manual-dispatch-hold", StringComparison.Ordinal));
    }

    private static SessionCommandStore BindManualDispatchCommands(Fixture fixture)
    {
        var store = new SessionCommandStore(fixture);
        var workspace = new SessionWorkspaceService(store, new(fixture.HostStore), store, NullLogger<SessionWorkspaceService>.Instance);
        var conversation = new SessionConversationService(store, store, workspace, store, new RequestAdmission(),
            TimeProvider.System, NullLogger<SessionConversationService>.Instance, cancellationAudit: fixture.Audit);
        fixture.ViewModel.BindSessionCommands(workspace, new(workspace, conversation, store, TimeProvider.System));
        return store;
    }

    private sealed partial class SessionCommandStore
    {
        internal Action? BeforeDispatchMetadataRead { get; set; }
    }
}
